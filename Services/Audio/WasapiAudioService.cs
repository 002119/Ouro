using System;
using System.Collections.Generic;
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

        public AudioRamBuffer? SystemAudioBuffer
        {
            get
            {
                lock (_syncLock)
                {
                    return _systemBuffer;
                }
            }
        }

        public AudioRamBuffer? MicrophoneAudioBuffer
        {
            get
            {
                lock (_syncLock)
                {
                    return _micBuffer;
                }
            }
        }

        public event EventHandler<byte[]>? SystemAudioDataAvailable;
        public event EventHandler<byte[]>? MicrophoneAudioDataAvailable;
        public event EventHandler<string>? AudioErrorOccurred;

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

                    _systemCapture = renderDevice != null ? new WasapiLoopbackCapture(renderDevice) : new WasapiLoopbackCapture();
                    _systemBuffer = new AudioRamBuffer(_systemCapture.WaveFormat, bufferDurationSeconds);
                    _systemCapture.DataAvailable += OnSystemDataAvailable;
                    _systemCapture.RecordingStopped += OnSystemRecordingStopped;
                    _systemCapture.StartRecording();
                }
                catch (Exception ex)
                {
                    AudioErrorOccurred?.Invoke(this, $"Failed to initialize System Audio Loopback: {ex.Message}");
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

                    _micCapture = captureDevice != null ? new WasapiCapture(captureDevice) : new WasapiCapture();
                    _micBuffer = new AudioRamBuffer(_micCapture.WaveFormat, bufferDurationSeconds);
                    _micCapture.DataAvailable += OnMicrophoneDataAvailable;
                    _micCapture.RecordingStopped += OnMicrophoneRecordingStopped;
                    _micCapture.StartRecording();
                }
                catch (Exception ex)
                {
                    // Microphone may not be connected on all systems; continue system capture gracefully
                    AudioErrorOccurred?.Invoke(this, $"Microphone endpoint unavailable or disconnected: {ex.Message}");
                }

                _isCapturing = true;
            }
        }

        private void OnSystemDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || !_isCapturing) return;

            AudioRamBuffer? buf;
            lock (_syncLock)
            {
                if (!_isCapturing) return;
                buf = _systemBuffer;
            }

            buf?.Write(e.Buffer, 0, e.BytesRecorded);

            byte[] chunk = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
            SystemAudioDataAvailable?.Invoke(this, chunk);
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

            byte[] chunk = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
            MicrophoneAudioDataAvailable?.Invoke(this, chunk);
        }

        private void OnSystemRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                AudioErrorOccurred?.Invoke(this, $"System Audio recording stopped unexpectedly: {e.Exception.Message}");
            }
        }

        private void OnMicrophoneRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                AudioErrorOccurred?.Invoke(this, $"Microphone recording stopped unexpectedly: {e.Exception.Message}");
            }
        }

        public byte[] GetSystemAudioSnapshot()
        {
            lock (_syncLock)
            {
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
