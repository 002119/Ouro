using System;
using System.Collections.Generic;
using FFmpeg.AutoGen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ouro.Models;

namespace Ouro.Services.Capture
{
    /// <summary>
    /// Thread-safe circular in-memory buffer storing pre-encoded video AVPackets.
    /// Retains packets up to the configured duration (e.g. 60s) completely in RAM.
    /// </summary>
    public unsafe sealed class RamReplayBuffer : IRamReplayBuffer
    {
        private readonly object _syncLock = new();
        private readonly Queue<IntPtr> _packets = new();
        private double _maxDurationSeconds;

        public double MaxDurationSeconds
        {
            get
            {
                lock (_syncLock)
                {
                    return _maxDurationSeconds;
                }
            }
            set
            {
                lock (_syncLock)
                {
                    _maxDurationSeconds = value > 0 ? value : 60.0;
                    PruneOverflow();
                }
            }
        }

        private long _firstPts = -1;
        private long _latestPts = -1;
        private long _totalBytesAllocated;
        private bool _isDisposed;

        public AVRational TimeBase { get; set; } = new AVRational { num = 1, den = 60 };

        public int PacketCount
        {
            get
            {
                lock (_syncLock)
                {
                    return _packets.Count;
                }
            }
        }

        public long TotalBytesAllocated
        {
            get
            {
                lock (_syncLock)
                {
                    return _totalBytesAllocated;
                }
            }
        }

        public double DurationSeconds
        {
            get
            {
                lock (_syncLock)
                {
                    if (_packets.Count <= 1 || TimeBase.den == 0)
                    {
                        return 0.0;
                    }

                    var oldestPkt = (AVPacket*)_packets.Peek();
                    long firstPts = oldestPkt != null ? oldestPkt->pts : -1;
                    double fps = TimeBase.den > 0 ? (double)TimeBase.den / (TimeBase.num > 0 ? TimeBase.num : 1) : 60.0;
                    if (fps <= 0) fps = 60.0;

                    // If timestamps are uninitialized or non-monotonic (e.g. clock reset/backwards jump),
                    // calculate duration reliably from frame count to prevent buffer freeze.
                    if (firstPts < 0 || _latestPts < 0 || _latestPts < firstPts)
                    {
                        return _packets.Count / fps;
                    }

                    long delta = _latestPts - firstPts;
                    return (double)delta * TimeBase.num / TimeBase.den;
                }
            }
        }

        [ActivatorUtilitiesConstructor]
        public RamReplayBuffer(IOptions<OuroSettings> options)
            : this(options?.Value?.BufferDurationSeconds ?? 60.0)
        {
        }

        public RamReplayBuffer(double maxDurationSeconds = 60.0)
        {
            _maxDurationSeconds = maxDurationSeconds > 0 ? maxDurationSeconds : 60.0;
        }

        public void EnqueuePacket(AVPacket* packet)
        {
            if (packet == null) return;

            lock (_syncLock)
            {
                if (_isDisposed) return;

                AVPacket* clone = ffmpeg.av_packet_alloc();
                ffmpeg.av_packet_ref(clone, packet);

                _packets.Enqueue((IntPtr)clone);
                _totalBytesAllocated += clone->size;

                if (clone->pts != ffmpeg.AV_NOPTS_VALUE)
                {
                    if (_firstPts < 0) _firstPts = clone->pts;
                    _latestPts = clone->pts;
                }

                // Evict older packets exceeding duration limit
                PruneOverflow();
            }
        }

        private void PruneOverflow()
        {
            double fps = TimeBase.den > 0 ? (double)TimeBase.den / (TimeBase.num > 0 ? TimeBase.num : 1) : 60.0;
            if (fps <= 0) fps = 60.0;
            // Keep 1.0s of GOP keyframe headroom in RAM so ExtractSnapshot can slice at the keyframe
            // closest to exact _maxDurationSeconds without losing ~0.8s to orphaned leading P-frames.
            double pruneDurationLimit = _maxDurationSeconds + 1.0;
            // Strict frame-count ceiling to guarantee RAM can NEVER leak indefinitely even if PTS fails
            int maxPacketSafetyLimit = (int)Math.Ceiling(pruneDurationLimit * fps * 1.5) + 60;

            while ((DurationSeconds > pruneDurationLimit || _packets.Count > maxPacketSafetyLimit) && _packets.Count > 1)
            {
                IntPtr oldestPtr = _packets.Dequeue();
                var oldestPkt = (AVPacket*)oldestPtr;
                if (oldestPkt != null)
                {
                    _totalBytesAllocated -= oldestPkt->size;
                    ffmpeg.av_packet_free(&oldestPkt);
                }
            }

            if (_packets.Count > 0)
            {
                var oldest = (AVPacket*)_packets.Peek();
                _firstPts = oldest != null ? oldest->pts : -1;
            }
            else
            {
                _firstPts = -1;
                _latestPts = -1;
            }
        }

        public List<IntPtr> ExtractSnapshot()
        {
            lock (_syncLock)
            {
                PruneOverflow();

                var snapshot = new List<IntPtr>(_packets.Count);
                if (_packets.Count == 0) return snapshot;

                double fps = TimeBase.den > 0 ? (double)TimeBase.den / (TimeBase.num > 0 ? TimeBase.num : 1) : 60.0;
                if (fps <= 0) fps = 60.0;

                var packetArray = _packets.ToArray();
                int keyframeIndex = -1;

                if (_latestPts >= 0 && _firstPts >= 0 && _latestPts >= _firstPts)
                {
                    long targetWindowTicks = (long)Math.Round(_maxDurationSeconds * fps);
                    long targetStartPts = _latestPts - targetWindowTicks + 1;

                    if (_firstPts >= targetStartPts)
                    {
                        // Short consecutive clip (e.g. 8s elapsed since last F12): start from the earliest keyframe in buffer
                        for (int i = 0; i < packetArray.Length; i++)
                        {
                            var pkt = (AVPacket*)packetArray[i];
                            if (pkt != null && (pkt->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0)
                            {
                                keyframeIndex = i;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Full buffer clip: pick the keyframe whose PTS is closest to targetStartPts
                        long bestDiff = long.MaxValue;
                        for (int i = 0; i < packetArray.Length; i++)
                        {
                            var pkt = (AVPacket*)packetArray[i];
                            if (pkt != null && (pkt->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0 && pkt->pts != ffmpeg.AV_NOPTS_VALUE)
                            {
                                long diff = Math.Abs(pkt->pts - targetStartPts);
                                if (diff < bestDiff)
                                {
                                    bestDiff = diff;
                                    keyframeIndex = i;
                                }
                            }
                        }
                    }
                }

                if (keyframeIndex < 0)
                {
                    for (int i = 0; i < packetArray.Length; i++)
                    {
                        var pkt = (AVPacket*)packetArray[i];
                        if (pkt != null && (pkt->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0)
                        {
                            keyframeIndex = i;
                            break;
                        }
                    }
                    if (keyframeIndex < 0) keyframeIndex = 0;
                }

                for (int i = keyframeIndex; i < packetArray.Length; i++)
                {
                    IntPtr ptr = packetArray[i];
                    if (ptr == IntPtr.Zero) continue;
                    var src = (AVPacket*)ptr;

                    AVPacket* clone = ffmpeg.av_packet_alloc();
                    ffmpeg.av_packet_ref(clone, src);
                    snapshot.Add((IntPtr)clone);
                }

                return snapshot;
            }
        }

        public void Clear()
        {
            lock (_syncLock)
            {
                while (_packets.Count > 0)
                {
                    var ptr = _packets.Dequeue();
                    var pkt = (AVPacket*)ptr;
                    ffmpeg.av_packet_free(&pkt);
                }
                _totalBytesAllocated = 0;
                _firstPts = -1;
                _latestPts = -1;
            }
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                Clear();
            }
        }
    }
}
