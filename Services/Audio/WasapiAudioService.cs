using System;
using System.Collections.Generic;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Ouro.Models;

namespace Ouro.Services.Audio
{
    /// <summary>
    /// Captures independent dual-track audio (System Loopback & Microphone) using NAudio.Wasapi.
    /// Manages thread-safe circular RAM buffers matching the video replay buffer duration.
    /// </summary>
    public sealed class WasapiAudioService : IAudioService
    {
        private readonly object _syncLock = new();

        private WasapiLoopbackCapture? _systemCapture;
        private WasapiCapture? _micCapture;

        private AudioRamBuffer? _systemBuffer;
        private AudioRamBuffer? _micBuffer;

        private long _lastSystemDataTick;
        private bool _isCapturing;
        private bool _isDisposed;

        public bool IsCapturing
        {
            get
            {
                lock (_syncLock)
                {
                    return _isCapturing;
                }
            }
        }

        public WaveFormat? SystemAudioFormat => _systemCapture?.WaveFormat;
        public WaveFormat? MicrophoneAudioFormat => _micCapture?.WaveFormat;

        public List<AudioDeviceInfo> GetOutputDevices()
        {
            var list = new List<AudioDeviceInfo>
            {
                new AudioDeviceInfo { Id = "", Name = "Dispositivo Predeterminado de Salida" }
            };
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var ep in endpoints)
                {
                    list.Add(new AudioDeviceInfo { Id = ep.ID, Name = ep.FriendlyName });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WasapiAudioService] Error enumerating render devices: {ex.Message}");
            }
            return list;
        }

        public List<AudioDeviceInfo> GetInputDevices()
        {
            var list = new List<AudioDeviceInfo>
            {
                new AudioDeviceInfo { Id = "", Name = "Dispositivo Predeterminado (Micrófono)" }
            };
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (var ep in endpoints)
                {
                    list.Add(new AudioDeviceInfo { Id = ep.ID, Name = ep.FriendlyName });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WasapiAudioService] Error enumerating capture devices: {ex.Message}");
            }
            return list;
        }

        public void Start(int bufferDurationSeconds = 60)
        {
            Start(bufferDurationSeconds, null, null);
        }

        public void Start(int bufferDurationSeconds = 60, string? outputDeviceId = null, string? inputDeviceId = null)
        {
            lock (_syncLock)
            {
                ThrowIfDisposed();

                if (_isCapturing)
                {
                    StopInternal();
                }

                // 1. Initialize System Loopback Capture (Desktop / Game audio)
                try
                {
                    MMDevice? renderDevice = null;
                    if (!string.IsNullOrEmpty(outputDeviceId))
                    {
                        try
                        {
                            using var enumerator = new MMDeviceEnumerator();
                            renderDevice = enumerator.GetDevice(outputDeviceId);
                        }
                        catch { }
                    }

                    // Provide headroom in audio buffer so oldest audio is never dropped before matching video keyframe
                    int audioCapacity = Math.Max(bufferDurationSeconds + 15, 60);
                    _systemCapture = renderDevice != null ? new WasapiLoopbackCapture(renderDevice) : new WasapiLoopbackCapture();
                    _systemBuffer = new AudioRamBuffer(_systemCapture.WaveFormat, audioCapacity);
                    _systemCapture.DataAvailable += OnSystemDataAvailable;
                    _systemCapture.RecordingStopped += OnSystemRecordingStopped;
                    _systemCapture.StartRecording();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WasapiAudioService] Failed to initialize System Audio Loopback: {ex.Message}");
                }

                // 2. Initialize Microphone Capture (Default or selected recording device)
                try
                {
                    MMDevice? captureDevice = null;
                    if (!string.IsNullOrEmpty(inputDeviceId))
                    {
                        try
                        {
                            using var enumerator = new MMDeviceEnumerator();
                            captureDevice = enumerator.GetDevice(inputDeviceId);
                        }
                        catch { }
                    }

                    int audioCapacity = Math.Max(bufferDurationSeconds + 15, 60);
                    _micCapture = captureDevice != null ? new WasapiCapture(captureDevice) : new WasapiCapture();
                    _micBuffer = new AudioRamBuffer(_micCapture.WaveFormat, audioCapacity);
                    _micCapture.DataAvailable += OnMicrophoneDataAvailable;
                    _micCapture.RecordingStopped += OnMicrophoneRecordingStopped;
                    _micCapture.StartRecording();
                }
                catch (Exception ex)
                {
                    // Microphone may not be connected on all systems; continue system capture gracefully
                    System.Diagnostics.Debug.WriteLine($"[WasapiAudioService] Microphone endpoint unavailable or disconnected: {ex.Message}");
                }

                _lastSystemDataTick = Stopwatch.GetTimestamp();
                _isCapturing = true;
            }
        }

        private void FillSystemLoopbackSilenceGap(AudioRamBuffer buf, long nowTick, int incomingBytes)
        {
            int bytesPerSec = buf.WaveFormat.AverageBytesPerSecond;
            int blockAlign = Math.Max(1, buf.WaveFormat.BlockAlign);
            if (bytesPerSec <= 0) return;

            long prevTick = _lastSystemDataTick;
            _lastSystemDataTick = nowTick;
            if (prevTick <= 0) return;

            double elapsedSec = Stopwatch.GetElapsedTime(prevTick, nowTick).TotalSeconds;
            double incomingSec = (double)incomingBytes / bytesPerSec;
            double gapSec = elapsedSec - incomingSec;

            // Windows WASAPI Loopback stops firing DataAvailable when the audio endpoint is completely silent.
            // Preserve true chronological timing by writing zeroed PCM frames for the exact real-time pause.
            double maxGapSec = Math.Max(120.0, buf.DurationSeconds);
            if (gapSec > 0.045 && gapSec <= maxGapSec)
            {
                int silentBytes = (int)Math.Round(gapSec * bytesPerSec);
                silentBytes -= silentBytes % blockAlign;
                if (silentBytes > 0)
                {
                    byte[] silenceChunk = new byte[Math.Min(silentBytes, bytesPerSec)];
                    int remaining = silentBytes;
                    while (remaining > 0)
                    {
                        int toWrite = Math.Min(remaining, silenceChunk.Length);
                        toWrite -= toWrite % blockAlign;
                        if (toWrite <= 0) break;
                        buf.Write(silenceChunk, 0, toWrite);
                        remaining -= toWrite;
                    }
                }
            }
        }

        private void OnSystemDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || !_isCapturing) return;

            long nowTick = Stopwatch.GetTimestamp();
            lock (_syncLock)
            {
                if (!_isCapturing) return;
                if (_systemBuffer != null)
                {
                    FillSystemLoopbackSilenceGap(_systemBuffer, nowTick, e.BytesRecorded);
                    _systemBuffer.Write(e.Buffer, 0, e.BytesRecorded);
                }
            }
        }

        private void OnMicrophoneDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || !_isCapturing) return;

            AudioRamBuffer? buf;
            lock (_syncLock)
            {
                if (!_isCapturing) return;
                buf = _micBuffer;
            }

            buf?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        private void OnSystemRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"[WasapiAudioService] System Audio recording stopped unexpectedly: {e.Exception.Message}");
            }
        }

        private void OnMicrophoneRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"[WasapiAudioService] Microphone recording stopped unexpectedly: {e.Exception.Message}");
            }
        }

        public byte[] GetSystemAudioSnapshot()
        {
            lock (_syncLock)
            {
                if (_systemBuffer != null && _isCapturing)
                {
                    FillSystemLoopbackSilenceGap(_systemBuffer, Stopwatch.GetTimestamp(), 0);
                }
                return _systemBuffer?.ExtractSnapshot() ?? Array.Empty<byte>();
            }
        }

        public byte[] GetMicrophoneAudioSnapshot()
        {
            lock (_syncLock)
            {
                return _micBuffer?.ExtractSnapshot() ?? Array.Empty<byte>();
            }
        }

        public void ClearBuffers()
        {
            lock (_syncLock)
            {
                _systemBuffer?.Clear();
                _micBuffer?.Clear();
                _lastSystemDataTick = Stopwatch.GetTimestamp();
            }
        }

        public void Stop()
        {
            StopInternal();
        }

        private void StopInternal()
        {
            WasapiLoopbackCapture? sys;
            WasapiCapture? mic;

            lock (_syncLock)
            {
                if (!_isCapturing) return;
                _isCapturing = false;

                sys = _systemCapture;
                mic = _micCapture;
                _systemCapture = null;
                _micCapture = null;

                _systemBuffer = null;
                _micBuffer = null;
            }

            // Perform hardware teardown OUTSIDE lock to avoid audio callback deadlock
            if (sys != null)
            {
                sys.DataAvailable -= OnSystemDataAvailable;
                sys.RecordingStopped -= OnSystemRecordingStopped;
                try { sys.StopRecording(); } catch { }
                try { sys.Dispose(); } catch { }
            }

            if (mic != null)
            {
                mic.DataAvailable -= OnMicrophoneDataAvailable;
                mic.RecordingStopped -= OnMicrophoneRecordingStopped;
                try { mic.StopRecording(); } catch { }
                try { mic.Dispose(); } catch { }
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

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(WasapiAudioService));
            }
        }
    }
}
