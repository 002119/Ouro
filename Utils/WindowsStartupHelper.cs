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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowsStartupHelper] Error setting startup: {ex.Message}");
            }
        }
    }
}
