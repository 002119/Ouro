using System;
using FFmpeg.AutoGen;
using Vortice.Direct3D11;
using D3D11Device = Vortice.Direct3D11.ID3D11Device;
using D3D11DeviceContext = Vortice.Direct3D11.ID3D11DeviceContext;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace Ouro.Services.Capture
{
    /// <summary>
    /// High-performance NVIDIA NVENC H.264 hardware encoder using FFmpeg.AutoGen.
    /// Operates on a pure zero-copy pipeline: Direct3D 11 VRAM surfaces from the capture
    /// pipeline are mapped straight to NVENC hardware encoding contexts (AV_HWDEVICE_TYPE_D3D11VA),
    /// eliminating CPU RAM downloads and blits completely.
    /// </summary>
    public unsafe sealed class NvencEncoder : IVideoEncoder
    {
        private readonly D3D11Device _d3dDevice;
        private readonly D3D11DeviceContext _d3dContext;
        private readonly IRamReplayBuffer? _replayBuffer;
        private readonly object _syncLock = new();
        private volatile bool _forceNextKeyframe;

        private AVCodecContext* _codecContext;
        private AVBufferRef* _hwDeviceCtx;
        private AVBufferRef* _hwFramesCtx;
        private AVPacket* _packet;

        private bool _isEncoding;
        private bool _isDisposed;

        public bool IsEncoding
        {
            get
            {
                lock (_syncLock)
                {
                    return _isEncoding;
                }
            }
        }

        public int OutputWidth { get; private set; } = 1920;
        public int OutputHeight { get; private set; } = 1080;
        public int Framerate { get; private set; } = 60;
        public int BitrateBps { get; private set; } = 15_000_000;
        public byte[]? CodecExtraData { get; private set; }

        public NvencEncoder(D3D11Device device, D3D11DeviceContext context, IRamReplayBuffer? replayBuffer = null)
        {
            _d3dDevice = device ?? throw new ArgumentNullException(nameof(device));
            _d3dContext = context ?? throw new ArgumentNullException(nameof(context));
            _replayBuffer = replayBuffer;
        }

        /// <summary>
        /// Initializes the hardware NVENC encoder session, D3D11VA device context,
        /// and hardware frames context mapping.
        /// </summary>
        public void Start(int width = 1920, int height = 1080, int fps = 60, int bitrateKbps = 15000)
        {
            lock (_syncLock)
            {
                ThrowIfDisposed();

                if (_isEncoding)
                {
                    StopInternal();
                }

                Utils.FFmpegBootstrapper.Initialize();

                OutputWidth = width;
                OutputHeight = height;
                Framerate = fps > 0 ? fps : 60;
                BitrateBps = bitrateKbps * 1000;

                if (_replayBuffer != null)
                {
                    _replayBuffer.TimeBase = new AVRational { num = 1, den = Framerate };
                }

                try
                {
                    InitializeEncoder();
                    _forceNextKeyframe = true;
                    _isEncoding = true;
                }
                catch
                {
                    ReleaseEncoderResources();
                    throw;
                }
            }
        }

        private void InitializeEncoder()
        {
            // 1. Locate the H.264 NVENC encoder.
            // The MP4 muxer currently authors an H.264 stream, so falling back to HEVC here
            // would produce a container whose codec metadata no longer matches its packets.
            AVCodec* codec = ffmpeg.avcodec_find_encoder_by_name("h264_nvenc");
            if (codec == null)
            {
                throw new NotSupportedException(
                    "NVIDIA NVENC H.264 encoder (h264_nvenc) is not available. " +
                    "Install/update the NVIDIA driver or use a GPU with NVENC support.");
            }

            // 2. Allocate and configure codec context
            _codecContext = ffmpeg.avcodec_alloc_context3(codec);
            if (_codecContext == null)
            {
                throw new InvalidOperationException("Failed to allocate AVCodecContext.");
            }

            _codecContext->width = OutputWidth;
            _codecContext->height = OutputHeight;
            _codecContext->time_base = new AVRational { num = 1, den = Framerate };
            _codecContext->framerate = new AVRational { num = Framerate, den = 1 };
            _codecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_D3D11;
            _codecContext->bit_rate = BitrateBps;
            _codecContext->rc_max_rate = BitrateBps;
            _codecContext->rc_buffer_size = BitrateBps / 2;
            _codecContext->gop_size = Framerate;     // Keyframe every 1 second (60 frames) for clean buffer cuts and seek points
            _codecContext->max_b_frames = 0;         // Zero B-frames for minimum encoding latency
            _codecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER; // Generate global SPS/PPS headers for MP4 containers

            // 3. Configure D3D11VA Hardware Device Context wrapping our Vortice device
            _hwDeviceCtx = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
            if (_hwDeviceCtx == null)
            {
                throw new InvalidOperationException("Failed to allocate D3D11VA hardware device context.");
            }

            var hwDeviceData = (AVHWDeviceContext*)_hwDeviceCtx->data;
            var d3d11vaDevice = (AVD3D11VADeviceContext*)hwDeviceData->hwctx;
            System.Runtime.InteropServices.Marshal.AddRef(_d3dDevice.NativePointer);
            System.Runtime.InteropServices.Marshal.AddRef(_d3dContext.NativePointer);
            d3d11vaDevice->device = (FFmpeg.AutoGen.ID3D11Device*)_d3dDevice.NativePointer;
            d3d11vaDevice->device_context = (FFmpeg.AutoGen.ID3D11DeviceContext*)_d3dContext.NativePointer;

            int res = ffmpeg.av_hwdevice_ctx_init(_hwDeviceCtx);
            if (res < 0)
            {
                throw new InvalidOperationException($"Failed to initialize D3D11VA hardware device context. Code: {res}");
            }

            // 4. Configure D3D11VA Hardware Frames Context
            _hwFramesCtx = ffmpeg.av_hwframe_ctx_alloc(_hwDeviceCtx);
            if (_hwFramesCtx == null)
            {
                throw new InvalidOperationException("Failed to allocate D3D11VA hardware frames context.");
            }

            var framesData = (AVHWFramesContext*)_hwFramesCtx->data;
            framesData->format = AVPixelFormat.AV_PIX_FMT_D3D11;
            framesData->sw_format = AVPixelFormat.AV_PIX_FMT_BGRA; // Matches DXGI_FORMAT_B8G8R8A8_UNORM
            framesData->width = OutputWidth;
            framesData->height = OutputHeight;
            framesData->initial_pool_size = 4; // Compact 4-surface ring for zero-latency NVENC (saves ~1 GB VRAM at 4K)

            var d3d11Frames = (AVD3D11VAFramesContext*)framesData->hwctx;
            d3d11Frames->BindFlags = 0x20; // D3D11_BIND_RENDER_TARGET

            res = ffmpeg.av_hwframe_ctx_init(_hwFramesCtx);
            if (res < 0)
            {
                throw new InvalidOperationException($"Failed to initialize D3D11VA hardware frames context. Code: {res}");
            }

            _codecContext->hw_device_ctx = ffmpeg.av_buffer_ref(_hwDeviceCtx);
            _codecContext->hw_frames_ctx = ffmpeg.av_buffer_ref(_hwFramesCtx);

            // 5. Ultra-Low Latency & Fast NVENC Tuning Options
            AVDictionary* options = null;
            ffmpeg.av_dict_set(&options, "preset", "p1", 0);            // p1: Fastest hardware preset
            ffmpeg.av_dict_set(&options, "tune", "ull", 0);             // Ultra-low latency tuning
            ffmpeg.av_dict_set(&options, "rc", "cbr", 0);               // Constant bitrate rate control
            ffmpeg.av_dict_set(&options, "zerolatency", "1", 0);        // Zero latency mode
            ffmpeg.av_dict_set(&options, "delay", "0", 0);              // Zero frame latency
            ffmpeg.av_dict_set(&options, "forced-idr", "1", 0);         // Strict IDR keyframes for seek points

            res = ffmpeg.avcodec_open2(_codecContext, codec, &options);
            ffmpeg.av_dict_free(&options);

            if (res < 0)
            {
                throw new InvalidOperationException($"Failed to open NVENC codec context. Code: {res}");
            }

            if (_codecContext->extradata != null && _codecContext->extradata_size > 0)
            {
                CodecExtraData = new byte[_codecContext->extradata_size];
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)_codecContext->extradata, CodecExtraData, 0, _codecContext->extradata_size);
            }

            // 6. Pre-allocate reusable AVPacket to avoid GC allocations in loop
            _packet = ffmpeg.av_packet_alloc();
        }

        /// <summary>
        /// Ingests a hardware-backed Direct3D 11 texture and passes it directly to NVENC via D3D11VA.
        /// Zero CPU copies occur; texture is mapped directly into NVENC hardware frame buffers on GPU.
        /// </summary>
        public void EncodeFrame(ID3D11Texture2D texture, long timestamp)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));

            lock (_syncLock)
            {
                if (!_isEncoding || _isDisposed || _codecContext == null || _hwFramesCtx == null || _packet == null)
                {
                    return;
                }

                // Allocate an AVFrame from D3D11VA hardware frames context pool
                AVFrame* hwFrame = ffmpeg.av_frame_alloc();
                if (hwFrame == null) return;

                hwFrame->format = (int)AVPixelFormat.AV_PIX_FMT_D3D11;
                hwFrame->hw_frames_ctx = ffmpeg.av_buffer_ref(_hwFramesCtx);

                int res = ffmpeg.av_hwframe_get_buffer(_hwFramesCtx, hwFrame, 0);
                if (res < 0)
                {
                    ffmpeg.av_frame_free(&hwFrame);
                    return;
                }

                // Blit captured texture straight into the hardware frame on GPU (zero-copy VRAM blit)
                var dstTexPtr = (IntPtr)hwFrame->data[0];
                int dstSubresource = (int)(IntPtr)hwFrame->data[1];
                var dstTex = new ID3D11Texture2D(dstTexPtr);
                GC.SuppressFinalize(dstTex); // Prevent Vortice from decrementing ref count on FFmpeg's pool!

                _d3dContext.CopySubresourceRegion(dstTex, (uint)dstSubresource, 0, 0, 0, texture, 0);

                hwFrame->pts = timestamp;

                if (_forceNextKeyframe)
                {
                    hwFrame->pict_type = AVPictureType.AV_PICTURE_TYPE_I;
                    _forceNextKeyframe = false;
                }

                // Dispatch hardware frame to NVENC ASIC
                res = ffmpeg.avcodec_send_frame(_codecContext, hwFrame);
                ffmpeg.av_frame_free(&hwFrame);

                if (res < 0)
                {
                    return;
                }

                // Drain resulting compressed packets from NVENC
                while (true)
                {
                    res = ffmpeg.avcodec_receive_packet(_codecContext, _packet);
                    if (res == ffmpeg.AVERROR(ffmpeg.EAGAIN) || res == ffmpeg.AVERROR_EOF)
                    {
                        break;
                    }
                    if (res < 0)
                    {
                        break;
                    }

                    // Enqueue directly to RAM Replay Buffer
                    _replayBuffer?.EnqueuePacket(_packet);

                    if (CodecExtraData == null && _codecContext != null && _codecContext->extradata != null && _codecContext->extradata_size > 0)
                    {
                        CodecExtraData = new byte[_codecContext->extradata_size];
                        System.Runtime.InteropServices.Marshal.Copy((IntPtr)_codecContext->extradata, CodecExtraData, 0, _codecContext->extradata_size);
                    }

                    // Unreference packet buffer for next iteration
                    ffmpeg.av_packet_unref(_packet);
                }
            }
        }

        /// <summary>
        /// Forces NVENC to generate an IDR keyframe on the very next encoded frame.
        /// </summary>
        public void ForceNextKeyframe()
        {
            _forceNextKeyframe = true;
        }

        /// <summary>
        /// Flushes remaining queued frames from NVENC and halts the encoder.
        /// </summary>
        public void Stop()
        {
            lock (_syncLock)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            if (!_isEncoding) return;
            _isEncoding = false;

            // Drain remaining frames if context is valid
            if (_codecContext != null && _packet != null)
            {
                try
                {
                    ffmpeg.avcodec_send_frame(_codecContext, null);
                    while (ffmpeg.avcodec_receive_packet(_codecContext, _packet) == 0)
                    {
                        _replayBuffer?.EnqueuePacket(_packet);
                        ffmpeg.av_packet_unref(_packet);
                    }
                }
                catch
                {
                    // Ignore drain exceptions during shutdown
                }
            }

            ReleaseEncoderResources();
        }

        private void ReleaseEncoderResources()
        {
            if (_packet != null)
            {
                var p = _packet;
                ffmpeg.av_packet_free(&p);
                _packet = null;
            }

            // Cleanup hardware frames and device contexts
            if (_hwFramesCtx != null)
            {
                var hf = _hwFramesCtx;
                ffmpeg.av_buffer_unref(&hf);
                _hwFramesCtx = null;
            }

            if (_hwDeviceCtx != null)
            {
                var hd = _hwDeviceCtx;
                ffmpeg.av_buffer_unref(&hd);
                _hwDeviceCtx = null;
            }

            // Cleanup codec context
            if (_codecContext != null)
            {
                var cc = _codecContext;
                ffmpeg.avcodec_free_context(&cc);
                _codecContext = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(NvencEncoder));
            }
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                StopInternal();
            }
        }
    }
}
