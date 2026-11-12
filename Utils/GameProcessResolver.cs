using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Ouro.Utils
{
    /// <summary>
    /// Utility to accurately identify foreground gaming processes and normalize their names
    /// into clean, readable display titles (e.g. "FortniteClient-Win64-Shipping" -> "Fortnite").
    /// </summary>
    public static class GameProcessResolver
    {
        /// <summary>
        /// Explicit mapping for popular PC games to their official, clean display names.
        /// </summary>
        private static readonly Dictionary<string, string> KnownGameProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            // Riot Games
            { "VALORANT-Win64-Shipping", "Valorant" },
            { "VALORANT", "Valorant" },
            { "League of Legends (TM) Client", "League of Legends" },
            { "League of Legends", "League of Legends" },
            { "LeagueClientUx", "League of Legends" },

            // Epic Games / Valve / EA
            { "FortniteClient-Win64-Shipping", "Fortnite" },
            { "FortniteClient", "Fortnite" },
            { "FortniteLauncher", "Fortnite" },
            { "cs2", "Counter-Strike 2" },
            { "csgo", "Counter-Strike GO" },
            { "dota2", "Dota 2" },
            { "r5apex", "Apex Legends" },
            { "ApexLegends", "Apex Legends" },

            // Blizzard / Activision
            { "Overwatch", "Overwatch 2" },
            { "Overwatch2", "Overwatch 2" },
            { "cod", "Call of Duty" },
            { "ModernWarfare", "Call of Duty" },
            { "BlackOpsColdWar", "Call of Duty Cold War" },
            { "BlackOps6", "Call of Duty Black Ops 6" },

            // Ubisoft & Rockstar
            { "RainbowSix", "Rainbow Six Siege" },
            { "RainbowSix_Vulkan", "Rainbow Six Siege" },
            { "GTA5", "GTA V" },
            { "PlayGTAV", "GTA V" },
            { "RDR2", "Red Dead Redemption 2" },

            // Popular Esports & Action
            { "RocketLeague", "Rocket League" },
            { "Cyberpunk2077", "Cyberpunk 2077" },
            { "RobloxPlayerBeta", "Roblox" },
            { "Roblox", "Roblox" },
            { "Minecraft.Windows", "Minecraft" },
            { "javaw", "Minecraft" },
            { "GenshinImpact", "Genshin Impact" },
            { "HonkaiStarRail", "Honkai Star Rail" },
            { "ZenlessZoneZero", "Zenless Zone Zero" },
            { "EldenRing", "Elden Ring" },
            { "RustClient", "Rust" },
            { "Rust", "Rust" },
            { "DeadByDaylight-Win64-Shipping", "Dead by Daylight" },
            { "DeadByDaylight", "Dead by Daylight" },
            { "EscapeFromTarkov", "Escape From Tarkov" },
            { "Warframe.x64", "Warframe" },
            { "Warframe", "Warframe" },
            { "TslGame", "PUBG" },
            { "PUBG", "PUBG" },
            { "FallGuys_client", "Fall Guys" },
            { "Smite", "Smite" },
            { "Palworld-Win64-Shipping", "Palworld" },
            { "Palworld", "Palworld" },
            { "Helldivers2", "Helldivers 2" },
            { "MarvelRivals-Win64-Shipping", "Marvel Rivals" },
            { "MarvelRivals", "Marvel Rivals" },
            { "Brawlhalla", "Brawlhalla" },
            { "MonsterHunterWorld", "Monster Hunter World" },
            { "MonsterHunterWilds", "Monster Hunter Wilds" }
        };

        /// <summary>
        /// System background processes, browsers, chat tools, and IDEs that must be treated as Desktop.
        /// </summary>
        public static readonly HashSet<string> NonGameProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            // Windows OS & System Components
            "explorer", "ouro", "taskmgr", "applicationframehost", "shellexperiencehost",
            "searchhost", "startmenuexperiencehost", "lockapp", "dwm", "csrss", "systemsettings",
            "cmd", "powershell", "pwsh", "windowsterminal", "conhost", "svchost", "rundll32",
            "ctfmon", "sihost", "fontdrvhost", "runtimebroker", "securityhealthservice",

            // Web Browsers
            "chrome", "msedge", "firefox", "brave", "opera", "operagx", "vivaldi", "tor",

            // Music & Streaming Media Players
            "spotify", "spotifyfree", "vlc", "wmplayer", "music.ui", "video.ui",
            "itunes", "foobar2000", "aimp", "netflix",

            // Social & Communication
            "discord", "slack", "teams", "telegram", "whatsapp", "signal", "element", "skype",

            // Gaming Store & Launcher Client UIs (Not the actual game)
            "steam", "steamwebhelper", "epicgameslauncher", "battle.net", "riotclientservices",
            "origin", "ea", "eadesktop", "gog", "galaxyclient", "ubisoftconnect", "upc",
            "xboxpcapp", "geforceexperience", "antigravity",

            // Development, Text & Utility Tools
            "code", "devenv", "notepad", "notepad++", "obs64", "obs", "nvidia share",
            "nvcontainer", "calculator", "snippingtool", "mspaint", "paint3d"
        };

        /// <summary>
        /// Resolves a raw process name into a human-readable game title.
        /// Returns "Desktop" if the process is a background app or non-game.
        /// </summary>
        public static string ResolveGameName(string rawProcessName, string? windowTitle = null)
        {
            if (string.IsNullOrWhiteSpace(rawProcessName))
            {
                return "Desktop";
            }

            // Remove any trailing .exe extension
            if (rawProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                rawProcessName = rawProcessName[..^4];
            }

            // 1. Check if it's explicitly a non-game system/background process
            if (NonGameProcesses.Contains(rawProcessName))
            {
                return "Desktop";
            }

            // 2. Direct dictionary lookup for known games
            if (KnownGameProcesses.TryGetValue(rawProcessName, out string? knownTitle))
            {
                return SanitizeSingleDirectoryName(knownTitle);
            }

            // 3. Heuristic cleanup: remove common Unreal/Unity/Shipping suffixes
            string cleaned = rawProcessName;
            string[] suffixesToRemove = {
                "-Win64-Shipping", "-Win32-Shipping", "-Shipping",
                "-Win64-Test", "-Win32-Test",
                "Client-Win64", "Client_Win64", "Client-Win32",
                "_client", "-client", "client",
                "_Release", "-Release",
                "_x64", "-x64", "x64", "_x86", "-x86", "x86",
                "_64", "-64", "_32", "-32"
            };

            foreach (var suffix in suffixesToRemove)
            {
                if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned[..^suffix.Length];
                    break;
                }
            }

            // Re-check dictionary with stripped name
            if (KnownGameProcesses.TryGetValue(cleaned, out string? strippedTitle))
            {
                return SanitizeSingleDirectoryName(strippedTitle);
            }

            // If a window title is provided and looks clean (short, without URLs or file paths), use it
            if (!string.IsNullOrWhiteSpace(windowTitle) &&
                windowTitle.Length < 40 &&
                !windowTitle.Contains(":\\") &&
                !windowTitle.Contains("http://") &&
                !windowTitle.Contains("https://"))
            {
                string cleanWindow = SanitizeSingleDirectoryName(windowTitle);
                if (cleanWindow != "Desktop" && !NonGameProcesses.Contains(cleanWindow))
                {
                    return cleanWindow;
                }
            }

            // Convert PascalCase or CamelCase to spaced words (e.g. "HollowKnight" -> "Hollow Knight")
            if (cleaned.Length > 3 && !cleaned.Contains(' '))
            {
                cleaned = Regex.Replace(cleaned, "([a-z])([A-Z])", "$1 $2");
            }

            string finalName = SanitizeSingleDirectoryName(cleaned);
            if (NonGameProcesses.Contains(finalName))
            {
                return "Desktop";
            }

            return finalName;
        }

        private static readonly HashSet<string> ReservedDosNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private static string SanitizeSingleDirectoryName(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return "Desktop";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(candidate
                .Where(c => !invalid.Contains(c) && c != '®' && c != '™' && c != '©' && c != '\uFFFD')
                .ToArray())
                .Trim()
                .Trim('.');

            if (string.IsNullOrWhiteSpace(cleaned) || ReservedDosNames.Contains(cleaned))
            {
                return "Desktop";
            }

            return cleaned;
        }
    }
}
