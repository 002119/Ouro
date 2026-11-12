using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ouro.Models;
using Ouro.Services.Audio;
using Ouro.Services.Capture;
using Ouro.Services.Export;
using Ouro.Services.Input;

namespace Ouro.Services.Orchestration
{
    /// <summary>
    /// Central orchestrator connecting input triggers, memory replay buffers,
    /// audio endpoints, and disk persistence muxers.
    /// Supports Smart Folder Routing based on active foreground process.
    /// </summary>
    public sealed class ClipOrchestrator : IClipOrchestrator
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private readonly IInputService _inputService;
        private readonly IRamReplayBuffer _replayBuffer;
        private readonly IAudioService _audioService;
        private readonly IMp4Muxer _mp4Muxer;
        private readonly CaptureEngine? _captureEngine;
        private readonly IVideoEncoder? _videoEncoder;
        private OuroSettings? _settings;

        private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
        private readonly object _syncLock = new();

        private string _clipsDirectory;
        private bool _isRunning;
        private bool _isDisposed;
        private readonly Stopwatch _recordingStopwatch = new();
        private long _lastPts = -1;

        public event Action? OnClipSaveStarted;
        public event Action<string>? OnClipSaved;
        public event Action<Exception>? OnClipSaveFailed;

        public bool IsRunning
        {
            get
            {
                lock (_syncLock)
                {
                    return _isRunning;
                }
            }
        }

        public double LastClipDurationSeconds { get; private set; }

        public IntPtr TargetWindowHandle { get; set; } = IntPtr.Zero;

        public string ClipsDirectory
        {
            get
            {
                lock (_syncLock)
                {
                    return _clipsDirectory;
                }
            }
            set
            {
                lock (_syncLock)
                {
                    _clipsDirectory = string.IsNullOrWhiteSpace(value)
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Ouro", "Clips")
                        : Environment.ExpandEnvironmentVariables(value);
                }
            }
        }

        [ActivatorUtilitiesConstructor]
        public ClipOrchestrator(
            IInputService inputService,
            IRamReplayBuffer replayBuffer,
            IAudioService audioService,
            IMp4Muxer mp4Muxer,
            IOptions<OuroSettings> options,
            CaptureEngine? captureEngine = null,
            IVideoEncoder? videoEncoder = null)
            : this(inputService, replayBuffer, audioService, mp4Muxer, options?.Value?.OutputDirectoryPath, captureEngine, videoEncoder, options?.Value)
        {
        }

        public ClipOrchestrator(
            IInputService inputService,
            IRamReplayBuffer replayBuffer,
            IAudioService audioService,
            IMp4Muxer mp4Muxer,
            string? clipsDirectory = null,
            CaptureEngine? captureEngine = null,
            IVideoEncoder? videoEncoder = null,
            OuroSettings? settings = null)
        {
            _inputService = inputService ?? throw new ArgumentNullException(nameof(inputService));
            _replayBuffer = replayBuffer ?? throw new ArgumentNullException(nameof(replayBuffer));
            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _mp4Muxer = mp4Muxer ?? throw new ArgumentNullException(nameof(mp4Muxer));
            _captureEngine = captureEngine;
            _videoEncoder = videoEncoder;
            _settings = settings;
            if (_settings?.BufferDurationSeconds > 0)
            {
                _replayBuffer.MaxDurationSeconds = _settings.BufferDurationSeconds;
            }

            string rawPath = string.IsNullOrWhiteSpace(clipsDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Ouro", "Clips")
                : clipsDirectory;

            _clipsDirectory = Environment.ExpandEnvironmentVariables(rawPath);
        }

        public void EnsureCaptureStarted()
        {
            lock (_syncLock)
            {
                if (_captureEngine != null && !_captureEngine.IsCapturing)
                {
                    StartCaptureEngineTarget();
                }
            }
        }

        private IntPtr ResolveTargetMonitorHandle()
        {
            try
            {
                int targetIdx = _settings?.SelectedDisplayIndex ?? 0;
                var monitors = Utils.DisplayMonitorHelper.GetMonitors();
                var targetMon = monitors.FirstOrDefault(m => m.Index == targetIdx);
                return targetMon?.HMonitor ?? IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private void StartCaptureEngineTarget()
        {
            if (_captureEngine == null) return;
            try
            {
                int width = _settings?.OutputWidth > 0 ? _settings.OutputWidth : 1920;
                int height = _settings?.OutputHeight > 0 ? _settings.OutputHeight : 1080;
                _captureEngine.SetResolution(width, height);

                if (_settings?.CaptureTargetMode == "SpecificWindow" && TargetWindowHandle != IntPtr.Zero)
                {
                    _captureEngine.StartForWindow(TargetWindowHandle, _settings?.SelectedWindowTitle ?? "Target Window");
                }
                else if (_settings?.CaptureTargetMode == "GameOnly")
                {
                    IntPtr hwnd = GetForegroundWindow();
                    string procName = GetForegroundProcessName();
                    if (hwnd != IntPtr.Zero && procName != "Desktop")
                    {
                        _captureEngine.StartForWindow(hwnd, procName);
                    }
                    else
                    {
                        IntPtr hMon = ResolveTargetMonitorHandle();
                        _captureEngine.StartForMonitor(hMon);
                    }
                }
                else
                {
                    IntPtr hMon = ResolveTargetMonitorHandle();
                    _captureEngine.StartForMonitor(hMon);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClipOrchestrator] Error starting capture engine target: {ex.Message}");
            }
        }

        public void Start()
        {
            lock (_syncLock)
            {
                ThrowIfDisposed();

                if (_isRunning) return;

                _inputService.OnSaveClipRequested += HandleSaveClipRequested;

                // Clear replay buffer & start fresh high-precision timestamping
                _replayBuffer.Clear();
                _recordingStopwatch.Restart();
                _lastPts = -1;

                // 1. Start dual-track WASAPI Audio capture
                try
                {
                    int duration = _settings?.BufferDurationSeconds > 0 ? _settings.BufferDurationSeconds : 60;
                    _replayBuffer.MaxDurationSeconds = duration; // keep video buffer horizon identical to audio
                    _audioService.Start(duration, _settings?.SelectedAudioOutputDeviceId, _settings?.SelectedAudioInputDeviceId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClipOrchestrator] Error starting audio service: {ex.Message}");
                }

                // 2. Start hardware video capture and NVENC encoding pipeline
                if (_captureEngine != null && _videoEncoder != null)
                {
                    try
                    {
                        int fps = _settings?.TargetFPS > 0 ? _settings.TargetFPS : 60;
                        int bitrateMbps = _settings?.VideoBitrateMbps > 0 ? _settings.VideoBitrateMbps : 15;
                        int width = _settings?.OutputWidth > 0 ? _settings.OutputWidth : 1920;
                        int height = _settings?.OutputHeight > 0 ? _settings.OutputHeight : 1080;

                        _captureEngine.SetResolution(width, height);
                        _captureEngine.SetTargetFramerate(fps);
                        _captureEngine.ConfigureOverlay(
                            _settings?.EnableVideoOverlay ?? false,
                            _settings?.OverlayCustomText ?? "OURO",
                            _settings?.OverlayIncludeTelemetry ?? true);
                        _videoEncoder.Start(width, height, fps, bitrateMbps * 1000);

                        _captureEngine.FrameCaptured -= OnFrameCaptured;
                        _captureEngine.FrameCaptured += OnFrameCaptured;

                        StartCaptureEngineTarget();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ClipOrchestrator] Failed to start video capture pipeline: {ex.Message}");
                        OnClipSaveFailed?.Invoke(new InvalidOperationException($"Error al iniciar captura de video: {ex.Message}", ex));
                    }
                }

                _isRunning = true;
            }
        }

        public void UpdateSettings(OuroSettings settings)
        {
            if (settings == null) return;
            lock (_syncLock)
            {
                _settings = settings;
                if (!string.IsNullOrWhiteSpace(settings.OutputDirectoryPath))
                {
                    _clipsDirectory = Environment.ExpandEnvironmentVariables(settings.OutputDirectoryPath);
                }

                int duration = _settings?.BufferDurationSeconds > 0 ? _settings.BufferDurationSeconds : 60;
                _replayBuffer.MaxDurationSeconds = duration;

                // If currently capturing replay, seamlessly apply new audio devices
                if (_isRunning)
                {
                    try
                    {
                        _audioService.Stop();
                        _audioService.Start(duration, _settings?.SelectedAudioOutputDeviceId, _settings?.SelectedAudioInputDeviceId);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ClipOrchestrator] Audio hot-reload error: {ex.Message}");
                    }
                }
                else if (_captureEngine != null && _captureEngine.IsCapturing)
                {
                    int fps = _settings?.TargetFPS > 0 ? _settings.TargetFPS : 60;
                    _captureEngine.SetTargetFramerate(fps);
                    _captureEngine.ConfigureOverlay(
                        _settings?.EnableVideoOverlay ?? false,
                        _settings?.OverlayCustomText ?? "OURO",
                        _settings?.OverlayIncludeTelemetry ?? true);
                    StartCaptureEngineTarget();
                }
            }
        }

        public void UpdateVolumes(double gameVolume, double micVolume)
        {
            lock (_syncLock)
            {
                if (_settings != null)
                {
                    _settings.GameVolume = gameVolume;
                    _settings.MicVolume = micVolume;
                }
            }
        }

        public void Stop()
        {
            lock (_syncLock)
            {
                if (!_isRunning) return;

                _inputService.OnSaveClipRequested -= HandleSaveClipRequested;

                try
                {
                    _audioService.Stop();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClipOrchestrator] Error stopping audio service: {ex.Message}");
                }

                if (_captureEngine != null)
                {
                    _captureEngine.FrameCaptured -= OnFrameCaptured;
                }

                try
                {
                    _videoEncoder?.Stop();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClipOrchestrator] Error stopping video encoder: {ex.Message}");
                }

                _replayBuffer.Clear();
                _recordingStopwatch.Reset();
                _lastPts = -1;
                _isRunning = false;
            }
        }

        private static string GetForegroundProcessName()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    return "Desktop";
                }

                GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId == 0)
                {
                    return "Desktop";
                }

                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                string name = process.ProcessName;
                string title = string.Empty;
                try
                {
                    title = process.MainWindowTitle;
                }
                catch
                {
                    // Ignore security exception for elevated system processes
                }

                return Utils.GameProcessResolver.ResolveGameName(name, title);
            }
            catch
            {
                return "Desktop";
            }
        }

        private void OnFrameCaptured(Vortice.Direct3D11.ID3D11Texture2D texture)
        {
            if (!_isRunning || _isDisposed || _videoEncoder == null) return;

            try
            {
                int fps = _settings?.TargetFPS > 0 ? _settings.TargetFPS : 60;
                long pts = (long)Math.Round(_recordingStopwatch.Elapsed.TotalSeconds * fps);
                if (pts <= _lastPts)
                {
                    return;
                }
                _lastPts = pts;

                _videoEncoder.EncodeFrame(texture, pts);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClipOrchestrator] Frame encode error: {ex.Message}");
            }
        }

        private void HandleSaveClipRequested(object? sender, EventArgs e)
        {
            // Offload to background worker thread immediately to keep input dispatch completely non-blocking
            Task.Run(async () =>
            {
                try
                {
                    await TriggerSaveClipAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Exceptions are caught and reported via OnClipSaveFailed inside TriggerSaveClipAsync
                }
            });
        }

        public async Task<string> TriggerSaveClipAsync()
        {
            ThrowIfDisposed();

            // Re-entrancy guard: debounces rapid F12 spam while a save operation is in-flight
            if (!await _saveSemaphore.WaitAsync(0).ConfigureAwait(false))
            {
                return string.Empty;
            }

            string destinationPath = string.Empty;
            List<IntPtr>? videoPackets = null;

            try
            {
                OnClipSaveStarted?.Invoke();

                // 1. Smart Folder Routing: Detect active game / foreground process
                string gameFolder = GetForegroundProcessName();

                string baseDir;
                lock (_syncLock)
                {
                    baseDir = _clipsDirectory;
                }

                string outputDir = Path.Combine(baseDir, gameFolder);
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                string fileName = $"{gameFolder}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";
                destinationPath = Path.Combine(outputDir, fileName);

                // 2. Synchronize snapshot retrieval across memory buffers
                // Allow a brief grace period (up to 2.5s) if replay buffer was just initialized
                int waitAttempts = 0;
                while (_replayBuffer.PacketCount == 0 && waitAttempts < 25)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    waitAttempts++;
                }

                videoPackets = _replayBuffer.ExtractSnapshot();
                if (videoPackets.Count == 0)
                {
                    throw new InvalidOperationException("El buffer de repetición aún no tiene cuadros acumulados. Espera un momento tras iniciar el replay.");
                }

                // Compute exact duration of extracted video packets
                unsafe
                {
                    if (videoPackets.Count > 1)
                    {
                        var first = (FFmpeg.AutoGen.AVPacket*)videoPackets[0];
                        var last = (FFmpeg.AutoGen.AVPacket*)videoPackets[videoPackets.Count - 1];
                        if (first != null && last != null &&
                            first->pts != FFmpeg.AutoGen.ffmpeg.AV_NOPTS_VALUE &&
                            last->pts != FFmpeg.AutoGen.ffmpeg.AV_NOPTS_VALUE &&
                            last->pts >= first->pts)
                        {
                            var tb = _replayBuffer.TimeBase;
                            double tbSec = (tb.den > 0) ? ((double)tb.num / tb.den) : (1.0 / 60.0);
                            LastClipDurationSeconds = (last->pts - first->pts + 1) * tbSec;
                        }
                        else
                        {
                            LastClipDurationSeconds = _replayBuffer.DurationSeconds;
                        }
                    }
                    else
                    {
                        LastClipDurationSeconds = _replayBuffer.DurationSeconds;
                    }
                }

                byte[] systemAudioPcm = _audioService.GetSystemAudioSnapshot();
                byte[] micAudioPcm = _audioService.GetMicrophoneAudioSnapshot();

                // 2b. Reset buffers & force NVENC IDR keyframe so consecutive clips do NOT duplicate
                // earlier footage and immediately accumulate only fresh gameplay from this point forward.
                // Note: Do NOT restart _recordingStopwatch or reset _lastPts here to keep capture timestamps
                // strictly monotonic and avoid backwards timestamp race conditions with concurrent frame capture.
                _replayBuffer.Clear();
                _audioService.ClearBuffers();
                _videoEncoder?.ForceNextKeyframe();

                if (_settings?.AudioMode == "GameOnly")
                {
                    micAudioPcm = Array.Empty<byte>();
                }
                else if (_settings?.AudioMode == "MicOnly")
                {
                    systemAudioPcm = Array.Empty<byte>();
                }

                // 3. Resolve audio format metadata for both System and Microphone independently
                var sysFormat = _audioService.SystemAudioFormat;
                var micFormat = _audioService.MicrophoneAudioFormat;

                var sysFormatInfo = AudioFormatInfo.FromWaveFormat(sysFormat);
                var micFormatInfo = AudioFormatInfo.FromWaveFormat(micFormat);

                // 4. Dispatch to background MP4 muxer
                int clipWidth = _settings?.OutputWidth > 0 ? _settings.OutputWidth : 1920;
                int clipHeight = _settings?.OutputHeight > 0 ? _settings.OutputHeight : 1080;
                float gameVol = (float)(_settings?.GameVolume ?? 1.0);
                float micVol = (float)(_settings?.MicVolume ?? 1.0);

                bool separateAudio = _settings?.SeparateAudioTracksInMp4 ?? true;

                await _mp4Muxer.ExportClipAsync(
                    destinationPath,
                    videoPackets,
                    systemAudioPcm,
                    micAudioPcm,
                    sysFormatInfo,
                    micFormatInfo,
                    _replayBuffer.TimeBase,
                    clipWidth,
                    clipHeight,
                    gameVol,
                    micVol,
                    _videoEncoder?.CodecExtraData,
                    separateAudio).ConfigureAwait(false);

                OnClipSaved?.Invoke(destinationPath);
                return destinationPath;
            }
            catch (Exception ex)
            {
                OnClipSaveFailed?.Invoke(ex);
                throw;
            }
            finally
            {
                FreeSnapshotPackets(videoPackets);
                _saveSemaphore.Release();
            }
        }

        private static unsafe void FreeSnapshotPackets(List<IntPtr>? packets)
        {
            if (packets == null) return;
            foreach (var ptr in packets)
            {
                if (ptr != IntPtr.Zero)
                {
                    var pkt = (FFmpeg.AutoGen.AVPacket*)ptr;
                    FFmpeg.AutoGen.ffmpeg.av_packet_free(&pkt);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(ClipOrchestrator));
            }
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                Stop();
                _saveSemaphore.Dispose();
            }
        }
    }
}
