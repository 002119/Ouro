using System;
using NAudio.Wave;

namespace Ouro.Services.Export
{
    /// <summary>
    /// Metadata describing the raw PCM audio format captured from WASAPI endpoints.
    /// Accurately preserves channel layout, bit depth, and sample encoding.
    /// </summary>
    public sealed class AudioFormatInfo
    {
        public int SampleRate { get; init; } = 48000;
        public int Channels { get; init; } = 2;
        public int BitsPerSample { get; init; } = 32;
        public bool IsFloatingPoint { get; init; } = true;

        public static AudioFormatInfo DefaultStereo48kHz => new()
        {
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 32,
            IsFloatingPoint = true
        };

        public static AudioFormatInfo FromWaveFormat(WaveFormat? wf)
        {
            if (wf == null) return DefaultStereo48kHz;

            bool isFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat;
            if (wf is WaveFormatExtensible wfe)
            {
                // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT: {00000003-0000-0010-8000-00aa00389b71}
                isFloat = wfe.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
            }

            return new AudioFormatInfo
            {
                SampleRate = wf.SampleRate > 0 ? wf.SampleRate : 48000,
                Channels = wf.Channels > 0 ? wf.Channels : 2,
                BitsPerSample = wf.BitsPerSample > 0 ? wf.BitsPerSample : 16,
                IsFloatingPoint = isFloat
            };
        }
    }
}
