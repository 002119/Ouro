using System;
using System.Threading.Tasks;
using Ouro.Models;

namespace Ouro.Services.Orchestration
{
    /// <summary>
    /// Central coordination brain of the Ouro retrospective capture utility.
    /// Manages the replay buffer lifecycle, listens for global hotkey save triggers,
    /// coordinates snapshot extraction from RAM, and dispatches to the MP4 muxer.
    /// </summary>
    public interface IClipOrchestrator : IDisposable
    {
        event Action? OnClipSaveStarted;
        event Action<string>? OnClipSaved;
        event Action<Exception>? OnClipSaveFailed;

        /// <summary>
        /// True if the replay engine and capture pipeline are actively running.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Exact duration in seconds of the most recently saved clip.
        /// </summary>
        double LastClipDurationSeconds { get; }

        /// <summary>
        /// Explicit window handle to capture when CaptureTargetMode is SpecificWindow.
        /// </summary>
        IntPtr TargetWindowHandle { get; set; }

        /// <summary>
        /// Ensures the underlying Direct3D 11 screen capture engine is active for live viewport preview.
        /// </summary>
        void EnsureCaptureStarted();

        /// <summary>
        /// Starts active monitoring and subscribes to global hotkey save triggers.
        /// </summary>
        void Start();

        /// <summary>
        /// Halts active monitoring and unsubscribes from hotkeys.
        /// </summary>
        void Stop();

        /// <summary>
        /// Updates the running orchestrator preferences (FPS, bitrate, directory).
        /// </summary>
        void UpdateSettings(OuroSettings settings);

        /// <summary>
        /// Live-updates independent volume gain multipliers for game audio and microphone.
        /// </summary>
        void UpdateVolumes(double gameVolume, double micVolume);

        /// <summary>
        /// Programmatically triggers the extraction and persistence of the active RAM replay buffers to disk.
        /// Protected by concurrency guards to prevent duplicate saves during active I/O flushes.
        /// </summary>
        /// <returns>Path of the written MP4 file, or empty string if debounced.</returns>
        Task<string> TriggerSaveClipAsync();
    }
}
