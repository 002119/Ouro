using System;
using System.Collections.Generic;
using FFmpeg.AutoGen;

namespace Ouro.Services.Capture
{
    /// <summary>
    /// Thread-safe circular in-memory replay buffer interface.
    /// Stores encoded AVPackets directly in RAM without writing to disk until triggered.
    /// </summary>
    public unsafe interface IRamReplayBuffer : IDisposable
    {
        /// <summary>
        /// Appends an encoded video/audio packet into the circular RAM buffer.
        /// Packets exceeding the maximum configured duration are pruned in FIFO order.
        /// </summary>
        /// <param name="packet">Pointer to the unmanaged AVPacket produced by the encoder.</param>
        void EnqueuePacket(AVPacket* packet);

        /// <summary>
        /// Extracts a thread-safe snapshot of cloned AVPacket pointers in chronological order.
        /// </summary>
        List<IntPtr> ExtractSnapshot();

        /// <summary>
        /// Timebase corresponding to the buffered video packets.
        /// </summary>
        AVRational TimeBase { get; set; }

        /// <summary>
        /// Total count of packets currently buffered in RAM.
        /// </summary>
        int PacketCount { get; }

        /// <summary>
        /// Estimated total memory consumption of buffered packets in bytes.
        /// </summary>
        long TotalBytesAllocated { get; }

        /// <summary>
        /// Maximum duration in seconds retained in RAM. Can be adjusted dynamically.
        /// </summary>
        double MaxDurationSeconds { get; set; }

        /// <summary>
        /// Current duration stored in seconds.
        /// </summary>
        double DurationSeconds { get; }

        /// <summary>
        /// Clears all buffered packets from memory.
        /// </summary>
        void Clear();
    }
}
