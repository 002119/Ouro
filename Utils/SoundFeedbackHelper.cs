using System;
using System.IO;
using System.Media;
using System.Windows;

namespace Ouro.Utils
{
    /// <summary>
    /// Discrete audio feedback when a clip is saved.
    /// </summary>
    public static class SoundFeedbackHelper
    {
        private static byte[]? _cachedSoundBytes;

        public static void PlayClipChime()
        {
            try
            {
                if (_cachedSoundBytes == null)
                {
                    try
                    {
                        var uri = new Uri("pack://application:,,,/Resources/clip_chime.wav", UriKind.Absolute);
                        var sri = Application.GetResourceStream(uri);
                        if (sri != null)
                        {
                            using var ms = new MemoryStream();
                            sri.Stream.CopyTo(ms);
                            _cachedSoundBytes = ms.ToArray();
                        }
                    }
                    catch
                    {
                        // Fallback when pack:// scheme is not registered
                    }

                    _cachedSoundBytes ??= GenerateSynthesizedChimeWav();
                }

                if (_cachedSoundBytes != null)
                {
                    using var stream = new MemoryStream(_cachedSoundBytes);
                    using var player = new SoundPlayer(stream);
                    player.Play();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SoundFeedbackHelper] Could not play sound: {ex.Message}");
            }
        }

        private static byte[] GenerateSynthesizedChimeWav()
        {
            const int sampleRate = 44100;
            const double durationSeconds = 0.16;
            int totalSamples = (int)(sampleRate * durationSeconds);
            int dataSize = totalSamples * 2;

            using var ms = new MemoryStream(44 + dataSize);
            using var bw = new BinaryWriter(ms);

            bw.Write(new[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + dataSize);
            bw.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(new[] { 'd', 'a', 't', 'a' });
            bw.Write(dataSize);

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double freq = t < 0.06 ? 659.25 : 987.77;
                double env = Math.Exp(-t * 18.0);
                short sample = (short)(Math.Sin(2.0 * Math.PI * freq * t) * env * 6500);
                bw.Write(sample);
            }

            return ms.ToArray();
        }
    }
}
