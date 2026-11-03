using System;
using System.IO;
using System.Media;

namespace Ouro.Utils
{
    /// <summary>
    /// Generates and plays a sleek, low-latency synthesized audio chime
    /// providing immediate acoustic confirmation whenever a clip is saved.
    /// Operates completely in-memory with zero disk reads.
    /// </summary>
    public static class ClipFeedbackSound
    {
        private static readonly SoundPlayer? _player;

        static ClipFeedbackSound()
        {
            try
            {
                byte[] wavBytes = GenerateChimeWav();
                var ms = new MemoryStream(wavBytes);
                _player = new SoundPlayer(ms);
                _player.Load();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClipFeedbackSound] Failed to init sound player: {ex.Message}");
            }
        }

        public static void Play()
        {
            try
            {
                _player?.Play();
            }
            catch
            {
                try
                {
                    SystemSounds.Asterisk.Play();
                }
                catch { }
            }
        }

        private static byte[] GenerateChimeWav()
        {
            int sampleRate = 44100;
            double durationSeconds = 0.16; // 160 ms
            int totalSamples = (int)(sampleRate * durationSeconds);
            short[] pcmSamples = new short[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;

                // Two-tone rising futuristic chime: E5 (659 Hz) -> B5 (987 Hz)
                double freq = t < 0.06 ? 659.25 : 987.77;

                // Exponential decay envelope
                double envelope = Math.Exp(-12.0 * t);
                if (t < 0.005)
                {
                    envelope *= (t / 0.005); // 5ms attack to avoid clicking
                }

                double sample = Math.Sin(2.0 * Math.PI * freq * t) * envelope;
                pcmSamples[i] = (short)Math.Clamp((int)(sample * 24000.0), short.MinValue, short.MaxValue);
            }

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // RIFF header
            bw.Write(new char[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + totalSamples * 2);
            bw.Write(new char[] { 'W', 'A', 'V', 'E' });

            // fmt subchunk
            bw.Write(new char[] { 'f', 'm', 't', ' ' });
            bw.Write(16); // Subchunk1Size (16 for PCM)
            bw.Write((short)1); // AudioFormat (1 = PCM)
            bw.Write((short)1); // NumChannels (1 = Mono)
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2); // ByteRate
            bw.Write((short)2); // BlockAlign
            bw.Write((short)16); // BitsPerSample

            // data subchunk
            bw.Write(new char[] { 'd', 'a', 't', 'a' });
            bw.Write(totalSamples * 2);

            for (int i = 0; i < totalSamples; i++)
            {
                bw.Write(pcmSamples[i]);
            }

            bw.Flush();
            return ms.ToArray();
        }
    }
}
