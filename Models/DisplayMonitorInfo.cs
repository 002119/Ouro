using System;

namespace Ouro.Models
{
    public class DisplayMonitorInfo
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }
        public IntPtr MonitorHandle { get; set; }
        public IntPtr HMonitor => MonitorHandle;

        public override string ToString() => DisplayName;
    }
}
