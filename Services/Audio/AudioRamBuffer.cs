using System;
using NAudio.Wave;

namespace Ouro.Services.Audio
{
    /// <summary>
    /// Thread-safe circular in-memory buffer designed to retain raw PCM audio bytes
    /// for a configurable retrospective duration (e.g. 60-120 seconds).
    /// Overwrites the oldest audio data once capacity is reached.
    /// </summary>
    public sealed class AudioRamBuffer
    {
        private readonly byte[] _buffer;
        private readonly int _capacity;
        private readonly object _syncLock = new();

        private int _head;
        private int _tail;
        private int _count;
        private long _totalBytesWritten;

        public WaveFormat WaveFormat { get; }
        public int DurationSeconds { get; }
        public int Capacity => _capacity;

        public int CurrentBytes
        {
            get
            {
                lock (_syncLock)
                {
                    return _count;
                }
            }
        }

        public double CurrentSeconds
        {
            get
            {
                lock (_syncLock)
                {
                    return WaveFormat.AverageBytesPerSecond > 0
                        ? (double)_count / WaveFormat.AverageBytesPerSecond
                        : 0.0;
                }
            }
        }

        public long TotalBytesWritten
        {
            get
            {
                lock (_syncLock)
                {
                    return _totalBytesWritten;
                }
            }
        }

        public AudioRamBuffer(WaveFormat waveFormat, int durationSeconds = 60)
        {
            WaveFormat = waveFormat ?? throw new ArgumentNullException(nameof(waveFormat));
            DurationSeconds = durationSeconds > 0 ? durationSeconds : 60;

            // Ensure alignment to block/sample boundary
            int bytesPerSec = waveFormat.AverageBytesPerSecond;
            int rawCapacity = bytesPerSec * DurationSeconds;
            int blockAlign = waveFormat.BlockAlign > 0 ? waveFormat.BlockAlign : 4;
            _capacity = rawCapacity - (rawCapacity % blockAlign);

            _buffer = new byte[_capacity];
        }

        /// <summary>
        /// Writes raw PCM bytes into the circular buffer. Overwrites oldest bytes if full.
        /// </summary>
        public void Write(byte[] data, int offset, int count)
        {
            if (data == null || count <= 0) return;

            lock (_syncLock)
            {
                _totalBytesWritten += count;

                if (count >= _capacity)
                {
                    // If incoming chunk exceeds capacity, keep only the latest bytes
                    offset += count - _capacity;
                    count = _capacity;
                    Buffer.BlockCopy(data, offset, _buffer, 0, count);
                    _head = 0;
                    _tail = 0;
                    _count = count;
                    return;
                }

                int firstChunk = Math.Min(count, _capacity - _head);
                Buffer.BlockCopy(data, offset, _buffer, _head, firstChunk);

                int remaining = count - firstChunk;
                if (remaining > 0)
                {
                    Buffer.BlockCopy(data, offset + firstChunk, _buffer, 0, remaining);
                }

                _head = (_head + count) % _capacity;

                if (_count + count > _capacity)
                {
                    _tail = _head;
                    _count = _capacity;
                }
                else
                {
                    _count += count;
                }
            }
        }

        /// <summary>
        /// Extracts a chronological snapshot copy of all valid PCM bytes currently buffered.
        /// </summary>
        public byte[] ExtractSnapshot()
        {
            lock (_syncLock)
            {
                if (_count == 0) return Array.Empty<byte>();

                byte[] snapshot = new byte[_count];

                if (_tail < _head)
                {
                    Buffer.BlockCopy(_buffer, _tail, snapshot, 0, _count);
                }
                else
                {
                    int firstPart = _capacity - _tail;
                    Buffer.BlockCopy(_buffer, _tail, snapshot, 0, firstPart);
                    int secondPart = _head;
                    if (secondPart > 0)
                    {
                        Buffer.BlockCopy(_buffer, 0, snapshot, firstPart, secondPart);
                    }
                }

                return snapshot;
            }
        }

        /// <summary>
        /// Clears the circular buffer.
        /// </summary>
        public void Clear()
        {
            lock (_syncLock)
            {
                _head = 0;
                _tail = 0;
                _count = 0;
                Array.Clear(_buffer, 0, _buffer.Length);
            }
        }
    }
}
