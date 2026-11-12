using System;

namespace Ouro.Models
{
    public enum CaptureTargetType
    {
        Monitor,
        SpecificProcess
    }

    public sealed class CaptureTargetInfo
    {
        public CaptureTargetType Type { get; init; } = CaptureTargetType.Monitor;
        public IntPtr WindowHandle { get; init; } = IntPtr.Zero;
        public IntPtr MonitorHandle { get; init; } = IntPtr.Zero;
        public int ProcessId { get; init; }
        public string ProcessName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = "Primary Display";
        public int Width { get; init; } = 1920;
        public int Height { get; init; } = 1080;
        public double AspectRatio => Height > 0 ? (double)Width / Height : 16.0 / 9.0;
        public bool Is4By3 => Math.Abs(AspectRatio - (4.0 / 3.0)) < 0.05;
    }
}
