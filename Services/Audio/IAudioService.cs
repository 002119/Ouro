using System;
using System.Collections.Generic;
using NAudio.Wave;
using Ouro.Models;

namespace Ouro.Services.Audio
{
    /// <summary>
    /// Contract for independent dual-track audio capture (System Desktop Loopback & Microphone).
    /// </summary>
    public interface IAudioService : IDisposable
    {
        bool IsCapturing { get; }

        WaveFormat? SystemAudioFormat { get; }
        WaveFormat? MicrophoneAudioFormat { get; }

        /// <summary>
        /// Starts capturing system loopback audio and microphone input into circular RAM buffers.
        /// </summary>
        /// <param name="bufferDurationSeconds">Duration of circular retrospective audio to hold in RAM.</param>
        /// <param name="outputDeviceId">Optional specific MMDevice ID for output loopback.</param>
        /// <param name="inputDeviceId">Optional specific MMDevice ID for microphone recording.</param>
        void Start(int bufferDurationSeconds = 60, string? outputDeviceId = null, string? inputDeviceId = null);

        /// <summary>
        /// Halts audio capture sessions and flushes capture endpoints cleanly.
        /// </summary>
        void Stop();

        /// <summary>
        /// Enumerates active playback/render devices (speakers, headphones).
        /// </summary>
        List<AudioDeviceInfo> GetOutputDevices();

        /// <summary>
        /// Enumerates active recording/capture devices (microphones).
        /// </summary>
        List<AudioDeviceInfo> GetInputDevices();

        /// <summary>
        /// Retrieves the current contiguous snapshot of system audio PCM bytes from RAM.
        /// </summary>
        byte[] GetSystemAudioSnapshot();

        /// <summary>
        /// Retrieves the current contiguous snapshot of microphone PCM bytes from RAM.
        /// </summary>
        byte[] GetMicrophoneAudioSnapshot();

        /// <summary>
        /// Clears circular audio buffers. Used when flushing replay buffers between consecutive clips.
        /// </summary>
        void ClearBuffers();
    }
}
