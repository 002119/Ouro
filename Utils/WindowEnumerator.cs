using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Ouro.Utils
{
    public class WindowCaptureItem
    {
        public IntPtr Hwnd { get; set; }
        public string Title { get; set; } = "";
        public string ProcessName { get; set; } = "";

        public string DisplayName => string.IsNullOrWhiteSpace(Title)
            ? $"[{ProcessName}]"
            : $"[{ProcessName}] {Title}";

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Enumerates open top-level application windows (similar to OBS Studio Window Capture).
    /// </summary>
    public static class WindowEnumerator
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out bool pvAttribute, int cbAttribute);

        private const int DWMWA_CLOAKED = 14;

        public static List<WindowCaptureItem> GetCaptureableWindows()
        {
            var windows = new List<WindowCaptureItem>();
            IntPtr shellHwnd = GetShellWindow();
            int currentPid = Environment.ProcessId;

            EnumWindows((hWnd, lParam) =>
            {
                if (hWnd == shellHwnd) return true;
                if (!IsWindowVisible(hWnd)) return true;

                // Check if cloaked by DWM (e.g. suspended UWP apps or virtual desktops)
                if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out bool isCloaked, sizeof(bool)) == 0 && isCloaked)
                {
                    return true;
                }

                int length = GetWindowTextLength(hWnd);
                if (length == 0) return true;

                var builder = new StringBuilder(length + 1);
                GetWindowText(hWnd, builder, builder.Capacity);
                string title = builder.ToString().Trim();

                if (string.IsNullOrWhiteSpace(title)) return true;

                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0 || pid == currentPid) return true;

                string procName = "";
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    procName = proc.ProcessName;
                }
                catch
                {
                    return true;
                }

                // Filter system/shell noise
                if (procName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                    procName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) ||
                    procName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                    procName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) && title == "Calculadora" ? false :
                    procName.Equals("SystemSettings", StringComparison.OrdinalIgnoreCase) ||
                    title == "Program Manager" ||
                    title == "Settings")
                {
                    if (procName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return true;
                    if (procName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase)) return true;
                    if (procName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)) return true;
                }

                windows.Add(new WindowCaptureItem
                {
                    Hwnd = hWnd,
                    Title = title,
                    ProcessName = procName
                });

                return true;
            }, IntPtr.Zero);

            return windows;
        }
    }
}
