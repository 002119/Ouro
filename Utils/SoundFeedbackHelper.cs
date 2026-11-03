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
                    var uri = new Uri("pack://application:,,,/Resources/clip_chime.wav", UriKind.Absolute);
                    var sri = Application.GetResourceStream(uri);
                    if (sri != null)
                    {
                        using var ms = new MemoryStream();
                        sri.Stream.CopyTo(ms);
                        _cachedSoundBytes = ms.ToArray();
                    }
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
    }
}
