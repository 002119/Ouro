using System;

namespace Ouro.Models
{
    /// <summary>
    /// Represents a game category folder containing recorded clips (e.g. VALORANT, FORTNITE).
    /// Used for organizing clips by game, identical to GeForce Experience gallery albums.
    /// </summary>
    public sealed class GameFolderInfo
    {
        public string GameName { get; set; } = string.Empty;
        public int ClipCount { get; set; }
        public long TotalSizeBytes { get; set; }
        public string TotalSizeFormatted { get; set; } = "0 MB";
        public DateTime LatestClipTime { get; set; }
        public string LatestClipRelative { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public bool IsAllFolder { get; set; }
    }
}
