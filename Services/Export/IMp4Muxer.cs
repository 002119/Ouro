using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFmpeg.AutoGen;

namespace Ouro.Services.Export
{
    /// <summary>
    /// Service contract for multiplexing video and multi-track audio snapshots from RAM
    /// into a finalized, high-performance MP4 file on disk.
    /// </summary>
    public interface IMp4Muxer
    {
        /// <summary>
        /// Asynchronously multiplexes pre-encoded video packets and raw audio streams
        /// into an MP4 container with zero frame drops on the capture pipeline.
        /// </summary>
        Task ExportClipAsync(
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
            byte[]? videoExtraData = null);
    }
}
