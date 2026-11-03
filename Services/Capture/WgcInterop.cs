using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Ouro.Services.Capture
{
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    public interface IGraphicsCaptureItemInterop
    {
        int CreateForWindow([In] IntPtr hWnd, [In] ref Guid riid, out IntPtr result);
        int CreateForMonitor([In] IntPtr hMonitor, [In] ref Guid riid, out IntPtr result);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    public interface IDirect3DDxgiInterfaceAccess
    {
        int GetInterface([In] ref Guid iid, out IntPtr p);
    }

    /// <summary>
    /// Interop bindings for Windows Graphics Capture (WGC) and Direct3D 11 device bridging.
    /// Uses native WindowsCreateString and IGraphicsCaptureItemInterop to avoid .NET 8 HString marshaling issues.
    /// </summary>
    public static class WgcInterop
    {
        private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        private static readonly Guid D3D11Texture2DGuid = typeof(ID3D11Texture2D).GUID;

        private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, ExactSpelling = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsCreateString(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
            int length,
            out IntPtr hstring);

        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("api-ms-win-core-winrt-l1-1-0.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int RoGetActivationFactory(
            IntPtr activatableClassId,
            [In] ref Guid iid,
            out IntPtr factory);

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        /// <summary>
        /// Bridges a Vortice ID3D11Device to a WinRT Direct3D11 device required by Direct3D11CaptureFramePool.
        /// </summary>
        public static IDirect3DDevice CreateWinRtDirect3DDevice(ID3D11Device d3d11Device)
        {
            if (d3d11Device == null) throw new ArgumentNullException(nameof(d3d11Device));

            using var dxgiDevice = d3d11Device.QueryInterface<IDXGIDevice>();
            int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr pUnknown);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            try
            {
                return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
            }
            finally
            {
                Marshal.Release(pUnknown);
            }
        }

        /// <summary>
        /// Extracts the underlying hardware ID3D11Texture2D from a WGC Direct3D11CaptureFrame surface.
        /// </summary>
        public static ID3D11Texture2D GetTextureFromSurface(IDirect3DSurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            var access = WinRT.CastExtensions.As<IDirect3DDxgiInterfaceAccess>(surface);
            var guid = D3D11Texture2DGuid;
            int hr = access.GetInterface(ref guid, out IntPtr pTexture);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            return new ID3D11Texture2D(pTexture);
        }

        private static IGraphicsCaptureItemInterop GetCaptureItemInterop()
        {
            const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
            int hr = WindowsCreateString(className, className.Length, out IntPtr hStr);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            try
            {
                var interopGuid = typeof(IGraphicsCaptureItemInterop).GUID;
                hr = RoGetActivationFactory(hStr, ref interopGuid, out IntPtr factoryPtr);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);

                return (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
            }
            finally
            {
                WindowsDeleteString(hStr);
            }
        }

        /// <summary>
        /// Creates a GraphicsCaptureItem for an isolated application window (HWND).
        /// </summary>
        public static GraphicsCaptureItem CreateItemForWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) throw new ArgumentException("Invalid window handle.", nameof(hWnd));

            var interop = GetCaptureItemInterop();
            var itemGuid = GraphicsCaptureItemGuid;
            int hr = interop.CreateForWindow(hWnd, ref itemGuid, out IntPtr itemPtr);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
        }

        /// <summary>
        /// Creates a GraphicsCaptureItem for a specific physical monitor handle, defaulting to primary.
        /// </summary>
        public static GraphicsCaptureItem CreateItemForMonitor(IntPtr? hMonitor = null)
        {
            IntPtr monitorHandle = hMonitor ?? MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);
            if (monitorHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not retrieve primary monitor handle.");
            }

            var interop = GetCaptureItemInterop();
            var itemGuid = GraphicsCaptureItemGuid;
            int hr = interop.CreateForMonitor(monitorHandle, ref itemGuid, out IntPtr itemPtr);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
        }

        public static GraphicsCaptureItem CreateItemForPrimaryMonitor()
        {
            return CreateItemForMonitor(null);
        }
    }
}
