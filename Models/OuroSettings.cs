namespace Ouro.Models
{
    /// <summary>
    /// Strongly-typed configuration options for Ouro user preferences.
    /// Binds to the "OuroSettings" section in appsettings.json.
    /// </summary>
    public sealed class OuroSettings
    {
        public const string SectionName = "OuroSettings";

        public int BufferDurationSeconds { get; set; } = 60;
        public string OutputDirectoryPath { get; set; } = "%USERPROFILE%\\Videos\\Ouro\\Clips";
        public int VideoBitrateMbps { get; set; } = 15;
        public int TargetFPS { get; set; } = 60;
        public string Resolution { get; set; } = "1080p";
        public int OutputWidth { get; set; } = 1920;
        public int OutputHeight { get; set; } = 1080;
        public string CaptureTargetMode { get; set; } = "Monitor"; // "Monitor", "GameOnly", "SpecificWindow"
        public string SelectedWindowTitle { get; set; } = "";
        public string AudioMode { get; set; } = "DualTrack"; // "DualTrack", "SystemOnly", "MicOnly"
        public string SelectedAudioOutputDeviceId { get; set; } = "";
        public string SelectedAudioInputDeviceId { get; set; } = "";
        public double GameVolume { get; set; } = 1.0;
        public double MicVolume { get; set; } = 1.0;
        public string SaveHotkey { get; set; } = "F12";
        public string DiscordWebhookUrl { get; set; } = "";
        public int DiscordMaxUploadMb { get; set; } = 48; // 24 or 48 MB limit for Discord upload
        public bool StartWithWindows { get; set; } = false;
        public bool PlaySoundOnClip { get; set; } = true;
        public int SelectedDisplayIndex { get; set; } = 0;
    }
}
