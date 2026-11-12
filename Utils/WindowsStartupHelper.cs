using System;
using System.IO;
using Microsoft.Win32;

namespace Ouro.Utils
{
    /// <summary>
    /// Manages auto-start with Windows via HKCU Run registry key with --tray argument.
    /// </summary>
    public static class WindowsStartupHelper
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "OuroReplayEngine";

        public static bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static void SetStartupEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key == null) return;

                if (enabled)
                {
                    string? exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath))
                    {
                        exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Ouro.exe");
                    }
                    key.SetValue(AppName, $"\"{exePath}\" --tray");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }

                // Also clean up any legacy shortcut in shell:startup to avoid duplicates
                CleanLegacyStartupShortcuts();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsStartupHelper] Error setting startup: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up any legacy startup shortcuts from the Windows Startup folder
        /// to ensure only the official HKCU Run registry entry exists and prevent duplicates.
        /// </summary>
        public static void CleanLegacyStartupShortcuts()
        {
            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startupFolder))
                {
                    string[] legacyNames = { "Ouro.lnk", "Ouro Replay.lnk", "Ouro Replay Engine.lnk" };
                    foreach (string name in legacyNames)
                    {
                        string path = Path.Combine(startupFolder, name);
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsStartupHelper] Error cleaning legacy shortcuts: {ex.Message}");
            }
        }
    }
}
