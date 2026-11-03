using System;
using System.IO;

namespace Ouro.Models
{
    /// <summary>
    /// Represents metadata for a recorded clip found in the clips directory.
    /// </summary>
    public sealed class ClipItemInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string GameName { get; set; } = "ESCRITORIO";
        public long FileSizeBytes { get; set; }
        public string FileSizeFormatted { get; set; } = "0 MB";
        public DateTime CreationTime { get; set; }
        public string CreationTimeFormatted { get; set; } = string.Empty;
        public string RelativeTime { get; set; } = string.Empty;

        public static ClipItemInfo FromFileInfo(FileInfo fi)
        {
            string dirName = fi.Directory?.Name ?? "Desktop";
            string resolvedGame = Utils.GameProcessResolver.ResolveGameName(dirName);
            string gameName = string.Equals(resolvedGame, "Desktop", StringComparison.OrdinalIgnoreCase)
                ? "ESCRITORIO"
                : resolvedGame.ToUpper();

            double mb = fi.Length / (1024.0 * 1024.0);
            string sizeStr = mb < 1.0 ? $"{mb * 1024.0:F0} KB" : $"{mb:F1} MB";

            string relative;
            var span = DateTime.Now - fi.CreationTime;
            if (span.TotalMinutes < 1)
                relative = "Justo ahora";
            else if (span.TotalMinutes < 60)
                relative = $"Hace {(int)span.TotalMinutes} min";
            else if (span.TotalHours < 24 && fi.CreationTime.Date == DateTime.Today)
                relative = $"Hoy a las {fi.CreationTime:HH:mm}";
            else if (span.TotalDays < 2 && fi.CreationTime.Date == DateTime.Today.AddDays(-1))
                relative = $"Ayer a las {fi.CreationTime:HH:mm}";
            else
                relative = fi.CreationTime.ToString("dd/MM/yyyy HH:mm");

            return new ClipItemInfo
            {
                FilePath = fi.FullName,
                FileName = fi.Name,
                GameName = gameName,
                FileSizeBytes = fi.Length,
                FileSizeFormatted = sizeStr,
                CreationTime = fi.CreationTime,
                CreationTimeFormatted = fi.CreationTime.ToString("dd/MM/yyyy HH:mm:ss"),
                RelativeTime = relative
            };
        }
    }
}
