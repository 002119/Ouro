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
                    if (_firstPts < 0 || _latestPts < 0 || _latestPts < _firstPts || TimeBase.den == 0)
                    {
                        return 0.0;
                    }

                    long delta = _latestPts - _firstPts;
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
            while (DurationSeconds > _maxDurationSeconds && _packets.Count > 1)
            {
                IntPtr oldestPtr = _packets.Dequeue();
                var oldestPkt = (AVPacket*)oldestPtr;
                _totalBytesAllocated -= oldestPkt->size;
                ffmpeg.av_packet_free(&oldestPkt);

                // Update oldest PTS
                if (_packets.Count > 0)
                {
                    var nextOldest = (AVPacket*)_packets.Peek();
                    if (nextOldest->pts != ffmpeg.AV_NOPTS_VALUE)
                    {
                        _firstPts = nextOldest->pts;
                    }
                }
            }
        }

        public List<IntPtr> ExtractSnapshot()
        {
            lock (_syncLock)
            {
                var snapshot = new List<IntPtr>(_packets.Count);
                bool foundKeyframe = false;

                foreach (var ptr in _packets)
                {
                    if (ptr == IntPtr.Zero) continue;
                    var src = (AVPacket*)ptr;

                    if (!foundKeyframe)
                    {
                        if ((src->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0)
                        {
                            foundKeyframe = true;
                        }
                        else
                        {
                            // Skip orphaned P-frames that have no preceding keyframe in this buffer window
                            continue;
                        }
                    }

                    AVPacket* clone = ffmpeg.av_packet_alloc();
                    ffmpeg.av_packet_ref(clone, src);
                    snapshot.Add((IntPtr)clone);
                }

                // Fallback: If no packet had keyframe flag set, copy all
                if (snapshot.Count == 0 && _packets.Count > 0)
                {
                    foreach (var ptr in _packets)
                    {
                        var src = (AVPacket*)ptr;
                        AVPacket* clone = ffmpeg.av_packet_alloc();
                        ffmpeg.av_packet_ref(clone, src);
                        snapshot.Add((IntPtr)clone);
                    }
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
