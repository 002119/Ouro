using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FFmpeg.AutoGen;

namespace Ouro.Services.Export
{
    /// <summary>
    /// High-performance MP4 multiplexer using FFmpeg.AutoGen.
    /// Interleaves pre-encoded H.264/HEVC video packets and encodes a single synchronized AAC Master Stereo
    /// audio track (Desktop Loopback + Microphone combined with independent volume controls) in an MP4 container.
    /// Single-track authoring guarantees 100% compatibility with Windows Media Player, Movies & TV, Discord, and WhatsApp.
    /// </summary>
    public unsafe sealed class Mp4Muxer : IMp4Muxer
    {
        private readonly object _syncLock = new();

        public Task ExportClipAsync(
            string destinationPath,
            List<IntPtr> videoPackets,
            byte[] systemAudioPcm,
            byte[] micAudioPcm,
            AudioFormatInfo systemAudioFormat,
            AudioFormatInfo? micAudioFormat,
            AVRational videoTimeBase,
            int videoWidth = 1920,
            int videoHeight = 1080,
            float gameVolume = 1.0f,
            float micVolume = 1.0f,
            byte[]? videoExtraData = null)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException("Destination path cannot be null or empty.", nameof(destinationPath));
            }

            if (videoPackets == null || videoPackets.Count == 0)
            {
                throw new ArgumentException("Video packets list cannot be null or empty.", nameof(videoPackets));
            }

            systemAudioFormat ??= AudioFormatInfo.DefaultStereo48kHz;
            micAudioFormat ??= AudioFormatInfo.DefaultStereo48kHz;

            return Task.Run(() =>
            {
                lock (_syncLock)
                {
                    Utils.FFmpegBootstrapper.Initialize();
                    ExportInternal(
                        destinationPath,
                        videoPackets,
                        systemAudioPcm,
                        micAudioPcm,
                        systemAudioFormat,
                        micAudioFormat,
                        videoTimeBase,
                        videoWidth,
                        videoHeight,
                        gameVolume,
                        micVolume,
                        videoExtraData);
                }
            });
        }

        private void ExportInternal(
            string destinationPath,
            List<IntPtr> videoPackets,
            byte[] systemAudioPcm,
            byte[] micAudioPcm,
            AudioFormatInfo systemAudioFormat,
            AudioFormatInfo micAudioFormat,
            AVRational videoTimeBase,
            int videoWidth,
            int videoHeight,
            float gameVolume,
            float micVolume,
            byte[]? videoExtraData = null)
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            AVFormatContext* oc = null;
            AVCodecContext* masterAudioCodecCtx = null;

            bool isFileOpened = false;

            try
            {
                // 1. Resample input PCM buffers into uniform 48kHz Stereo Float arrays
                float[] sysFloats = ConvertPcmToStereoFloat48k(systemAudioPcm, systemAudioFormat);
                float[] micFloats = ConvertPcmToStereoFloat48k(micAudioPcm, micAudioFormat);

                bool hasSystem = sysFloats.Length > 0;
                bool hasMic = micFloats.Length > 0;

                // 2. Blend Game Audio + Microphone into Single Master Stereo Track with user volume multipliers
                float[] masterFloats = Array.Empty<float>();
                float gameGain = gameVolume;
                // Perceptual gain boost: 1.0 (100%) = 1.0x, 2.0 (200%) = 3.0x (+10 dB)
                float micGain = micVolume <= 1.0f ? micVolume : (1.0f + (micVolume - 1.0f) * 2.0f);

                if (hasSystem && hasMic)
                {
                    int maxLen = Math.Max(sysFloats.Length, micFloats.Length);
                    int sysOffset = maxLen - sysFloats.Length;
                    int micOffset = maxLen - micFloats.Length;
                    masterFloats = new float[maxLen];

                    for (int i = 0; i < maxLen; i++)
                    {
                        float s = (i >= sysOffset && (i - sysOffset) < sysFloats.Length ? sysFloats[i - sysOffset] : 0f) * gameGain;
                        float m = (i >= micOffset && (i - micOffset) < micFloats.Length ? micFloats[i - micOffset] : 0f) * micGain;
                        float mixed = s + m;
                        if (float.IsNaN(mixed) || float.IsInfinity(mixed)) mixed = 0f;
                        masterFloats[i] = Math.Clamp(mixed, -1.0f, 1.0f);
                    }
                }
                else if (hasSystem)
                {
                    masterFloats = new float[sysFloats.Length];
                    for (int i = 0; i < sysFloats.Length; i++)
                    {
                        float s = sysFloats[i] * gameGain;
                        if (float.IsNaN(s) || float.IsInfinity(s)) s = 0f;
                        masterFloats[i] = Math.Clamp(s, -1.0f, 1.0f);
                    }
                }
                else if (hasMic)
                {
                    masterFloats = new float[micFloats.Length];
                    for (int i = 0; i < micFloats.Length; i++)
                    {
                        float m = micFloats[i] * micGain;
                        if (float.IsNaN(m) || float.IsInfinity(m)) m = 0f;
                        masterFloats[i] = Math.Clamp(m, -1.0f, 1.0f);
                    }
                }

                // 3. Allocate output format context with MP4 hint
                int allocRes = ffmpeg.avformat_alloc_output_context2(&oc, null, "mp4", destinationPath);
                if (allocRes < 0 || oc == null)
                {
                    throw new InvalidOperationException($"Failed to allocate MP4 output context. Code: {allocRes}");
                }

                // 4. Configure Video Stream (Stream 0)
                AVStream* videoStream = ffmpeg.avformat_new_stream(oc, null);
                if (videoStream == null)
                {
                    throw new InvalidOperationException("Failed to allocate video output stream.");
                }

                videoStream->id = 0;
                videoStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
                videoStream->codecpar->codec_id = AVCodecID.AV_CODEC_ID_H264;
                videoStream->codecpar->width = videoWidth > 0 ? videoWidth : 1920;
                videoStream->codecpar->height = videoHeight > 0 ? videoHeight : 1080;
                videoStream->codecpar->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
                videoStream->codecpar->profile = ffmpeg.AV_PROFILE_H264_HIGH;
                videoStream->codecpar->level = 41;
                videoStream->time_base = videoTimeBase;

                int fps = videoTimeBase.num > 0 ? (videoTimeBase.den / videoTimeBase.num) : 60;
                videoStream->r_frame_rate = new AVRational { num = fps, den = 1 };
                videoStream->avg_frame_rate = new AVRational { num = fps, den = 1 };

                // Extract or assign SPS / PPS Extradata for the MP4 avcC box
                byte[]? extradataBytes = videoExtraData ?? ExtractH264Extradata(videoPackets);
                if (extradataBytes != null && extradataBytes.Length > 0)
                {
                    videoStream->codecpar->extradata = (byte*)ffmpeg.av_malloc((ulong)extradataBytes.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE);
                    System.Runtime.InteropServices.Marshal.Copy(extradataBytes, 0, (IntPtr)videoStream->codecpar->extradata, extradataBytes.Length);
                    videoStream->codecpar->extradata_size = extradataBytes.Length;
                }

                // 5. Configure Single Master AAC Audio Stream (Stream 1) - ONLY IF SAMPLES ARE PRESENT
                AVCodec* aacCodec = null;
                AVStream* masterAudioStream = null;

                if (masterFloats.Length > 0)
                {
                    aacCodec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);
                    if (aacCodec == null)
                    {
                        throw new NotSupportedException("AAC audio encoder is not available in FFmpeg.");
                    }

                    masterAudioStream = ffmpeg.avformat_new_stream(oc, null);
                    masterAudioStream->id = 1;
                    masterAudioCodecCtx = CreateAudioEncoderContext(aacCodec, 48000, 2, 192_000);
                    ffmpeg.avcodec_parameters_from_context(masterAudioStream->codecpar, masterAudioCodecCtx);
                    masterAudioStream->time_base = masterAudioCodecCtx->time_base;
                }

                // 6. Open destination file handle
                if ((oc->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
                {
                    int openRes = ffmpeg.avio_open(&oc->pb, destinationPath, ffmpeg.AVIO_FLAG_WRITE);
                    if (openRes < 0)
                    {
                        throw new IOException($"Failed to open output file '{destinationPath}' via avio_open. Code: {openRes}");
                    }
                    isFileOpened = true;
                }

                // 7. Write MP4 Container Header with 'faststart' for instant playback
                AVDictionary* muxOpts = null;
                ffmpeg.av_dict_set(&muxOpts, "movflags", "faststart", 0);
                int headerRes = ffmpeg.avformat_write_header(oc, &muxOpts);
                ffmpeg.av_dict_free(&muxOpts);

                if (headerRes < 0)
                {
                    throw new InvalidOperationException($"Failed to write MP4 container header. Code: {headerRes}");
                }

                // 8. Prepare Video Packets with normalized timestamps starting at 0
                var normVideoPackets = new List<IntPtr>();
                long startVideoPts = -1;
                long lastVideoPts = 0;
                foreach (var packetPtr in videoPackets)
                {
                    if (packetPtr == IntPtr.Zero) continue;

                    var srcPkt = (AVPacket*)packetPtr;
                    if (startVideoPts < 0 && srcPkt->pts != ffmpeg.AV_NOPTS_VALUE)
                    {
                        startVideoPts = srcPkt->pts;
                    }

                    AVPacket* vPkt = ffmpeg.av_packet_alloc();
                    ffmpeg.av_packet_ref(vPkt, srcPkt);

                    if (startVideoPts >= 0 && vPkt->pts != ffmpeg.AV_NOPTS_VALUE)
                    {
                        vPkt->pts -= startVideoPts;
                        if (vPkt->dts != ffmpeg.AV_NOPTS_VALUE)
                        {
                            vPkt->dts -= startVideoPts;
                        }
                    }
                    else
                    {
                        vPkt->pts = 0;
                        vPkt->dts = 0;
                    }

                    lastVideoPts = Math.Max(lastVideoPts, vPkt->pts);
                    vPkt->stream_index = videoStream->index;
                    ffmpeg.av_packet_rescale_ts(vPkt, videoTimeBase, videoStream->time_base);
                    normVideoPackets.Add((IntPtr)vPkt);
                }

                // 9. Encode Audio Packets into Stream 1
                var normAudioPackets = new List<IntPtr>();
                if (masterAudioStream != null && masterAudioCodecCtx != null && masterFloats.Length > 0)
                {
                    double vTbSec = (videoTimeBase.den > 0) ? ((double)videoTimeBase.num / videoTimeBase.den) : (1.0 / 60.0);
                    // Video duration includes the final frame's own duration.
                    double videoDurSec = (lastVideoPts + 1) * vTbSec;
                    if (videoDurSec > 0.5)
                    {
                        // Audio and video are END-aligned (both snapshots are taken at the same instant).
                        // 48000 Hz * 2 channels = 96000 floats/sec.
                        int targetFloats = (int)Math.Round(videoDurSec * 48000) * 2;
                        if (masterFloats.Length > targetFloats)
                        {
                            // Audio older than the first video keyframe: trim from the FRONT.
                            var trimmed = new float[targetFloats];
                            Array.Copy(masterFloats, masterFloats.Length - targetFloats, trimmed, 0, targetFloats);
                            masterFloats = trimmed;
                        }
                    }
                    normAudioPackets = EncodeAudioPackets(masterAudioCodecCtx, masterAudioStream, masterFloats);
                }

                // 10. Interleave and write both streams chronologically into MP4 container
                int vIdx = 0;
                int aIdx = 0;
                double vTb = (double)videoStream->time_base.num / (videoStream->time_base.den > 0 ? videoStream->time_base.den : 1);
                double aTb = (masterAudioStream != null && masterAudioStream->time_base.den > 0)
                    ? ((double)masterAudioStream->time_base.num / masterAudioStream->time_base.den)
                    : 1.0;

                while (vIdx < normVideoPackets.Count || aIdx < normAudioPackets.Count)
                {
                    bool chooseVideo;
                    if (vIdx < normVideoPackets.Count && aIdx < normAudioPackets.Count)
                    {
                        var vp = (AVPacket*)normVideoPackets[vIdx];
                        var ap = (AVPacket*)normAudioPackets[aIdx];
                        double vTime = vp->pts * vTb;
                        double aTime = ap->pts * aTb;
                        chooseVideo = vTime <= aTime;
                    }
                    else if (vIdx < normVideoPackets.Count)
                    {
                        chooseVideo = true;
                    }
                    else
                    {
                        chooseVideo = false;
                    }

                    if (chooseVideo)
                    {
                        var vp = (AVPacket*)normVideoPackets[vIdx++];
                        ffmpeg.av_interleaved_write_frame(oc, vp);
                        ffmpeg.av_packet_free(&vp);
                    }
                    else
                    {
                        var ap = (AVPacket*)normAudioPackets[aIdx++];
                        ffmpeg.av_interleaved_write_frame(oc, ap);
                        ffmpeg.av_packet_free(&ap);
                    }
                }

                // 11. Finalize container trailer
                ffmpeg.av_write_trailer(oc);
            }
            finally
            {
                if (masterAudioCodecCtx != null)
                {
                    var ctx = masterAudioCodecCtx;
                    ffmpeg.avcodec_free_context(&ctx);
                }

                if (oc != null)
                {
                    if (isFileOpened && oc->pb != null)
                    {
                        ffmpeg.avio_closep(&oc->pb);
                    }
                    ffmpeg.avformat_free_context(oc);
                }
            }
        }

        private static float[] ConvertPcmToStereoFloat48k(byte[] pcmData, AudioFormatInfo format)
        {
            if (pcmData == null || pcmData.Length == 0) return Array.Empty<float>();

            AVSampleFormat inSampleFmt = format.IsFloatingPoint
                ? AVSampleFormat.AV_SAMPLE_FMT_FLT
                : (format.BitsPerSample == 16 ? AVSampleFormat.AV_SAMPLE_FMT_S16 : AVSampleFormat.AV_SAMPLE_FMT_S32);

            AVChannelLayout inLayout;
            ffmpeg.av_channel_layout_default(&inLayout, format.Channels > 0 ? format.Channels : 2);

            AVChannelLayout outLayout;
            ffmpeg.av_channel_layout_default(&outLayout, 2); // Stereo

            SwrContext* swrCtx = null;
            int res = ffmpeg.swr_alloc_set_opts2(
                &swrCtx,
                &outLayout,
                AVSampleFormat.AV_SAMPLE_FMT_FLT, // Interleaved float
                48000,
                &inLayout,
                inSampleFmt,
                format.SampleRate > 0 ? format.SampleRate : 48000,
                0, null);

            if (res < 0 || swrCtx == null || ffmpeg.swr_init(swrCtx) < 0)
            {
                if (swrCtx != null) ffmpeg.swr_free(&swrCtx);
                return FallbackConvertPcmToStereoFloat48k(pcmData, format);
            }

            try
            {
                int inBytesPerSample = (format.BitsPerSample / 8) * format.Channels;
                int inSamples = inBytesPerSample > 0 ? pcmData.Length / inBytesPerSample : 0;
                if (inSamples <= 0) return FallbackConvertPcmToStereoFloat48k(pcmData, format);

                long maxOutSamples = ffmpeg.swr_get_out_samples(swrCtx, inSamples);
                int outBufferSize = (int)maxOutSamples * 2;
                float[] outFloats = new float[outBufferSize];

                fixed (byte* inPtr = pcmData)
                fixed (float* outPtr = outFloats)
                {
                    byte** inArray = stackalloc byte*[1];
                    inArray[0] = inPtr;

                    byte** outArray = stackalloc byte*[1];
                    outArray[0] = (byte*)outPtr;

                    int actualOutSamples = ffmpeg.swr_convert(
                        swrCtx,
                        outArray,
                        (int)maxOutSamples,
                        inArray,
                        inSamples);

                    if (actualOutSamples > 0)
                    {
                        int totalFloatElements = actualOutSamples * 2;
                        float[] result = new float[totalFloatElements];
                        Array.Copy(outFloats, result, totalFloatElements);
                        return result;
                    }
                }

                return FallbackConvertPcmToStereoFloat48k(pcmData, format);
            }
            finally
            {
                if (swrCtx != null) ffmpeg.swr_free(&swrCtx);
            }
        }

        private static float[] FallbackConvertPcmToStereoFloat48k(byte[] pcmData, AudioFormatInfo format)
        {
            if (pcmData == null || pcmData.Length == 0) return Array.Empty<float>();

            int channels = format.Channels > 0 ? format.Channels : 2;
            int bytesPerSample = format.BitsPerSample / 8;
            if (bytesPerSample <= 0) bytesPerSample = 4;
            int frameBytes = bytesPerSample * channels;
            int totalFrames = pcmData.Length / frameBytes;
            if (totalFrames <= 0) return Array.Empty<float>();

            float[] stereoFloats = new float[totalFrames * 2];

            for (int f = 0; f < totalFrames; f++)
            {
                int frameOffset = f * frameBytes;
                float left = 0f;
                float right = 0f;

                if (format.IsFloatingPoint && bytesPerSample == 4)
                {
                    if (channels == 1)
                    {
                        float val = BitConverter.ToSingle(pcmData, frameOffset);
                        left = val;
                        right = val;
                    }
                    else if (channels == 2)
                    {
                        left = BitConverter.ToSingle(pcmData, frameOffset);
                        right = BitConverter.ToSingle(pcmData, frameOffset + 4);
                    }
                    else if (channels >= 8)
                    {
                        float fl = BitConverter.ToSingle(pcmData, frameOffset);
                        float fr = BitConverter.ToSingle(pcmData, frameOffset + 4);
                        float fc = BitConverter.ToSingle(pcmData, frameOffset + 8);
                        float lfe = BitConverter.ToSingle(pcmData, frameOffset + 12);
                        float bl = BitConverter.ToSingle(pcmData, frameOffset + 16);
                        float br = BitConverter.ToSingle(pcmData, frameOffset + 20);
                        float sl = BitConverter.ToSingle(pcmData, frameOffset + 24);
                        float sr = BitConverter.ToSingle(pcmData, frameOffset + 28);

                        left = fl + 0.707f * fc + 0.5f * lfe + 0.707f * bl + 0.707f * sl;
                        right = fr + 0.707f * fc + 0.5f * lfe + 0.707f * br + 0.707f * sr;
                    }
                    else
                    {
                        left = BitConverter.ToSingle(pcmData, frameOffset);
                        right = BitConverter.ToSingle(pcmData, frameOffset + 4);
                    }
                }
                else if (bytesPerSample == 2)
                {
                    if (channels == 1)
                    {
                        short s = BitConverter.ToInt16(pcmData, frameOffset);
                        float val = s / 32768.0f;
                        left = val;
                        right = val;
                    }
                    else
                    {
                        left = BitConverter.ToInt16(pcmData, frameOffset) / 32768.0f;
                        right = BitConverter.ToInt16(pcmData, frameOffset + 2) / 32768.0f;
                    }
                }
                else if (bytesPerSample == 3)
                {
                    int s1 = (pcmData[frameOffset] | (pcmData[frameOffset + 1] << 8) | ((sbyte)pcmData[frameOffset + 2] << 16));
                    float val1 = s1 / 8388608.0f;
                    if (channels == 1)
                    {
                        left = val1;
                        right = val1;
                    }
                    else
                    {
                        int s2 = (pcmData[frameOffset + 3] | (pcmData[frameOffset + 4] << 8) | ((sbyte)pcmData[frameOffset + 5] << 16));
                        left = val1;
                        right = s2 / 8388608.0f;
                    }
                }
                else
                {
                    int s1 = BitConverter.ToInt32(pcmData, frameOffset);
                    float val1 = s1 / 2147483648.0f;
                    if (channels == 1)
                    {
                        left = val1;
                        right = val1;
                    }
                    else
                    {
                        int s2 = BitConverter.ToInt32(pcmData, frameOffset + 4);
                        left = val1;
                        right = s2 / 2147483648.0f;
                    }
                }

                if (float.IsNaN(left) || float.IsInfinity(left)) left = 0f;
                if (float.IsNaN(right) || float.IsInfinity(right)) right = 0f;

                stereoFloats[f * 2] = Math.Clamp(left, -1.0f, 1.0f);
                stereoFloats[f * 2 + 1] = Math.Clamp(right, -1.0f, 1.0f);
            }

            return stereoFloats;
        }

        private static AVCodecContext* CreateAudioEncoderContext(AVCodec* aacCodec, int sampleRate, int channels, int bitrate)
        {
            var ctx = ffmpeg.avcodec_alloc_context3(aacCodec);
            if (ctx == null)
            {
                throw new InvalidOperationException("Failed to allocate audio AVCodecContext.");
            }

            ctx->sample_rate = sampleRate;
            ffmpeg.av_channel_layout_default(&ctx->ch_layout, channels);
            ctx->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLTP; // AAC planar float
            ctx->bit_rate = bitrate;
            ctx->time_base = new AVRational { num = 1, den = sampleRate };

            int res = ffmpeg.avcodec_open2(ctx, aacCodec, null);
            if (res < 0)
            {
                ffmpeg.avcodec_free_context(&ctx);
                throw new InvalidOperationException($"Failed to open AAC encoder context. Code: {res}");
            }

            return ctx;
        }

        private static List<IntPtr> EncodeAudioPackets(
            AVCodecContext* aacCtx,
            AVStream* stream,
            float[] stereoFloats)
        {
            var packets = new List<IntPtr>();
            int totalStereoSamples = stereoFloats.Length / 2;
            int frameSize = aacCtx->frame_size > 0 ? aacCtx->frame_size : 1024;

            AVFrame* frame = ffmpeg.av_frame_alloc();
            frame->nb_samples = frameSize;
            frame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLTP;
            ffmpeg.av_channel_layout_copy(&frame->ch_layout, &aacCtx->ch_layout);
            frame->sample_rate = aacCtx->sample_rate;
            ffmpeg.av_frame_get_buffer(frame, 0);

            AVPacket* packet = ffmpeg.av_packet_alloc();

            try
            {
                long audioPts = 0;
                int sampleOffset = 0;

                while (sampleOffset < totalStereoSamples)
                {
                    int chunk = Math.Min(frameSize, totalStereoSamples - sampleOffset);
                    ffmpeg.av_frame_make_writable(frame);

                    float* leftChannel = (float*)frame->data[0];
                    float* rightChannel = (float*)frame->data[1];

                    for (int i = 0; i < chunk; i++)
                    {
                        int idx = (sampleOffset + i) * 2;
                        leftChannel[i] = stereoFloats[idx];
                        rightChannel[i] = stereoFloats[idx + 1];
                    }

                    // Pad with silence if last chunk is smaller than frameSize
                    for (int i = chunk; i < frameSize; i++)
                    {
                        leftChannel[i] = 0f;
                        rightChannel[i] = 0f;
                    }

                    frame->pts = audioPts;
                    audioPts += frameSize;

                    if (ffmpeg.avcodec_send_frame(aacCtx, frame) >= 0)
                    {
                        while (ffmpeg.avcodec_receive_packet(aacCtx, packet) == 0)
                        {
                            packet->stream_index = stream->index;
                            ffmpeg.av_packet_rescale_ts(packet, aacCtx->time_base, stream->time_base);
                            AVPacket* cloned = ffmpeg.av_packet_alloc();
                            ffmpeg.av_packet_ref(cloned, packet);
                            packets.Add((IntPtr)cloned);
                            ffmpeg.av_packet_unref(packet);
                        }
                    }

                    sampleOffset += chunk;
                }

                // Flush remaining packets from encoder
                if (ffmpeg.avcodec_send_frame(aacCtx, null) >= 0)
                {
                    while (ffmpeg.avcodec_receive_packet(aacCtx, packet) == 0)
                    {
                        packet->stream_index = stream->index;
                        ffmpeg.av_packet_rescale_ts(packet, aacCtx->time_base, stream->time_base);
                        AVPacket* cloned = ffmpeg.av_packet_alloc();
                        ffmpeg.av_packet_ref(cloned, packet);
                        packets.Add((IntPtr)cloned);
                        ffmpeg.av_packet_unref(packet);
                    }
                }
            }
            finally
            {
                if (packet != null)
                {
                    var p = packet;
                    ffmpeg.av_packet_free(&p);
                }
                if (frame != null)
                {
                    var f = frame;
                    ffmpeg.av_frame_free(&f);
                }
            }

            return packets;
        }

        private static byte[]? ExtractH264Extradata(List<IntPtr> videoPackets)
        {
            foreach (var ptr in videoPackets)
            {
                if (ptr == IntPtr.Zero) continue;
                var pkt = (AVPacket*)ptr;
                if (pkt->data == null || pkt->size < 16) continue;

                byte* data = pkt->data;
                int size = pkt->size;

                int spsStart = -1;
                int idrStart = -1;

                for (int i = 0; i < size - 4; i++)
                {
                    if (data[i] == 0 && data[i + 1] == 0 && (data[i + 2] == 1 || (data[i + 2] == 0 && data[i + 3] == 1)))
                    {
                        int nalHeaderIdx = (data[i + 2] == 1) ? i + 3 : i + 4;
                        byte nalType = (byte)(data[nalHeaderIdx] & 0x1F);

                        if (nalType == 7 && spsStart < 0)
                        {
                            spsStart = i;
                        }
                        else if (nalType == 5)
                        {
                            idrStart = i;
                            break;
                        }
                    }
                }

                if (spsStart >= 0 && idrStart > spsStart)
                {
                    int extradataLen = idrStart - spsStart;
                    byte[] extradata = new byte[extradataLen];
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)(data + spsStart), extradata, 0, extradataLen);
                    return extradata;
                }
            }

            return null;
        }
    }
}
