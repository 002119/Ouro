using System;
using Vortice.Direct3D11;

namespace Ouro.Services.Capture
{
    /// <summary>
    /// Hardware-accelerated video encoder interface.
    /// Ingests Direct3D 11 VRAM textures directly and encodes them via GPU ASIC (NVENC),
    /// pushing packets to the circular RAM buffer with zero CPU copies.
    /// </summary>
    public interface IVideoEncoder : IDisposable
    {
        bool IsEncoding { get; }
        int OutputWidth { get; }
        int OutputHeight { get; }
        int Framerate { get; }
        int BitrateBps { get; }
        byte[]? CodecExtraData { get; }

        /// <summary>
        /// Starts the hardware encoder with the specified dimensions, target framerate, and bitrate.
        /// </summary>
        void Start(int width = 1920, int height = 1080, int fps = 60, int bitrateKbps = 15000);

        /// <summary>
        /// Ingests a hardware-backed Direct3D 11 texture and encodes it into the replay buffer.
        /// Zero CPU copies occur; texture is mapped directly into NVENC via D3D11VA hw_frames_ctx.
        /// </summary>
        /// <param name="texture">Direct3D 11 texture residing in GPU VRAM.</param>
        /// <param name="timestamp">Frame presentation timestamp (PTS).</param>
        void EncodeFrame(ID3D11Texture2D texture, long timestamp);

        /// <summary>
        /// Flushes remaining packets and halts the encoder.
        /// </summary>
        void Stop();

        /// <summary>
        /// Forces NVENC to generate an IDR keyframe on the very next encoded frame.
        /// Used after flushing the replay buffer so subsequent clips begin cleanly on an I-frame.
        /// </summary>
        void ForceNextKeyframe();
    }
}
