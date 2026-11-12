using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ouro.Models;

namespace Ouro.Utils
{
    public static class DisplayMonitorHelper
    {
        private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfoEx
        {
            public int cbSize;
            public Rect rcMonitor;
            public Rect rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        private const uint MONITORINFOF_PRIMARY = 1;

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

        public static List<DisplayMonitorInfo> GetMonitors()
        {
            var list = new List<DisplayMonitorInfo>();
            int index = 0;

            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref Rect rc, IntPtr data) =>
                {
                    var mi = new MonitorInfoEx();
                    mi.cbSize = Marshal.SizeOf<MonitorInfoEx>();
                    if (GetMonitorInfo(hMon, ref mi))
                    {
                        bool isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                        int w = mi.rcMonitor.Right - mi.rcMonitor.Left;
                        int h = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                        string name = mi.szDevice ?? $"Monitor {index + 1}";
                        string displayName = isPrimary
                            ? $"Pantalla {index + 1}: {w}x{h} [Principal]"
                            : $"Pantalla {index + 1}: {w}x{h}";

                        list.Add(new DisplayMonitorInfo
                        {
                            Index = index,
                            Name = name,
                            DisplayName = displayName,
                            Width = w,
                            Height = h,
                            IsPrimary = isPrimary,
                            MonitorHandle = hMon
                        });
                        index++;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DisplayMonitorHelper] EnumDisplayMonitors error: {ex.Message}");
            }

            if (list.Count == 0)
            {
                list.Add(new DisplayMonitorInfo
                {
                    Index = 0,
                    Name = "Primary",
                    DisplayName = "Pantalla Principal (1920x1080)",
                    Width = 1920,
                    Height = 1080,
                    IsPrimary = true,
                    MonitorHandle = IntPtr.Zero
                });
            }

            return list;
        }
    }
}
