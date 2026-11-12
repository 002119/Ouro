using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Ouro.Models;

namespace Ouro.Services.Capture
{
    /// <summary>
    /// High-performance video capture engine combining Windows Graphics Capture (WGC),
    /// Direct3D 11 hardware scaling, and instant GDI baseline fallback.
    /// Guarantees continuous 60 FPS frame feed to NVENC and real-time live preview
    /// from the moment the application opens, whether on desktop or in 3D games.
    /// </summary>
    public sealed class CaptureEngine : IDisposable
    {
        #region Win32 GDI Interop for Immediate Desktop Previews
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool StretchBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidthDest, int nHeightDest,
            IntPtr hdcSrc, int nXSrc, int nYSrc, int nWidthSrc, int nHeightSrc, uint dwRop);

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);

        [DllImport("gdi32.dll")]
        private static extern int SetStretchBltMode(IntPtr hdc, int nStretchMode);

        [DllImport("gdi32.dll")]
        private static extern bool SetBrushOrgEx(IntPtr hdc, int nXOrg, int nYOrg, IntPtr lppt);

        private const int HALFTONE = 4;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, IntPtr lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);
        #endregion

        private readonly object _syncLock = new();

        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDirect3DDevice? _winrtDevice;
        private StretchFilter? _stretchFilter;
        private OverlayRenderer? _overlayRenderer;

        private GraphicsCaptureItem? _captureItem;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _captureSession;

        // Master GPU Texture holding the latest screen state
        private ID3D11Texture2D? _masterTexture;
        private ID3D11Texture2D? _uploadTexture;
        private ID3D11Texture2D? _stagingTexture;

        private byte[]? _previewCpuBuffer;
        private byte[]? _gdiCpuBuffer;
        private byte[]? _lastPreviewBytes;
        private int _lastPreviewPitch;
        private int _lastPreviewWidth;
        private int _lastPreviewHeight;

        private SizeInt32 _lastCapturedSize;
        private bool _isCapturing;
        private bool _isDisposed;
        private bool _isPreviewEnabled = true;
        private bool _isUiVisible = true;
        private bool _stagingCopyPending;

        // In-VRAM GPU HUD Overlay Settings
        private bool _isOverlayEnabled;
        private string _overlayText = "OURO";
        private bool _overlayIncludeTelemetry = true;

        // Dedicated high-precision frame pacer thread
        private Thread? _pacerThread;
        private CancellationTokenSource? _pacerCts;
        private int _targetFps = 60;
        private int _previewTickCounter;

        public event Action<ID3D11Texture2D>? FrameCaptured;
        public event Action<byte[], int, int, int>? PreviewFrameReady;

        public bool IsUiVisible
        {
            get
            {
                lock (_syncLock)
                {
                    return _isUiVisible;
                }
            }
            set
            {
                lock (_syncLock)
                {
                    _isUiVisible = value;
                }
            }
        }

        public bool IsPreviewEnabled
        {
            get
            {
                lock (_syncLock)
                {
                    return _isPreviewEnabled;
                }
            }
            set
            {
                lock (_syncLock)
                {
                    _isPreviewEnabled = value;
                    if (value && _lastPreviewBytes != null)
                    {
                        PreviewFrameReady?.Invoke(_lastPreviewBytes, _lastPreviewPitch, _lastPreviewWidth, _lastPreviewHeight);
                    }
                }
            }
        }

        public bool IsCapturing
        {
            get
            {
                lock (_syncLock)
                {
                    return _isCapturing;
                }
            }
        }

        public CaptureTargetInfo? CurrentTarget { get; private set; }

        public void ConfigureOverlay(bool enabled, string brandText, bool includeTelemetry)
        {
            lock (_syncLock)
            {
                _isOverlayEnabled = enabled;
                _overlayText = brandText;
                _overlayIncludeTelemetry = includeTelemetry;

                if (_isOverlayEnabled && _overlayRenderer != null)
                {
                    string resLabel = $"{OutputWidth}x{OutputHeight}";
                    _overlayRenderer.UpdateOverlay(_overlayText, resLabel, _targetFps, _overlayIncludeTelemetry);
                }
            }
        }
        public int OutputWidth { get; private set; } = 1920;
        public int OutputHeight { get; private set; } = 1080;

        public ID3D11Device? Device
        {
            get
            {
                lock (_syncLock)
                {
                    return _device;
                }
            }
        }

        public ID3D11DeviceContext? Context
        {
            get
            {
                lock (_syncLock)
                {
                    return _context;
                }
            }
        }

        public CaptureEngine(int outputWidth = 1920, int outputHeight = 1080)
        {
            OutputWidth = outputWidth;
            OutputHeight = outputHeight;

            InitializeDirect3D();
            CaptureBaselineScreen();
            StartContinuousTicker();
        }

        private void InitializeDirect3D()
        {
            lock (_syncLock)
            {
                var flags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;
                D3D11.D3D11CreateDevice(
                    (IDXGIAdapter?)null,
                    DriverType.Hardware,
                    flags,
                    new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                    out ID3D11Device device,
                    out ID3D11DeviceContext context).CheckError();

                _device = device;
                _context = context;

                try
                {
                    using var multithread = _device.QueryInterface<ID3D11Multithread>();
                    multithread?.SetMultithreadProtected(true);
                }
                catch { }

                _winrtDevice = WgcInterop.CreateWinRtDirect3DDevice(_device);
                _stretchFilter = new StretchFilter(_device, _context, OutputWidth, OutputHeight);
                _overlayRenderer = new OverlayRenderer(_device, _context);
                CreateTexturesLocked();
            }
        }

        private void CreateTexturesLocked()
        {
            if (_device == null) return;

            _masterTexture?.Dispose();
            _uploadTexture?.Dispose();
            _stagingTexture?.Dispose();

            // 1. Master Render Target holding the active GPU frame
            var masterDesc = new Texture2DDescription
            {
                Width = (uint)OutputWidth,
                Height = (uint)OutputHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
            };
            _masterTexture = _device.CreateTexture2D(masterDesc);

            // 2. Dynamic upload texture for high-speed GDI fallback
            var uploadDesc = new Texture2DDescription
            {
                Width = (uint)OutputWidth,
                Height = (uint)OutputHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.Write
            };
            _uploadTexture = _device.CreateTexture2D(uploadDesc);

            // 3. Staging read texture for WPF live preview
            var stagingDesc = new Texture2DDescription
            {
                Width = (uint)OutputWidth,
                Height = (uint)OutputHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CPUAccessFlags = CpuAccessFlags.Read
            };
            _stagingTexture = _device.CreateTexture2D(stagingDesc);
        }

        /// <summary>
        /// Captures an immediate baseline image of the primary screen via Win32 GDI
        /// and uploads it straight to the Direct3D 11 Master Texture.
        /// Guarantees that the preview screen is NEVER blank or in standby upon boot.
        /// </summary>
        public void CaptureBaselineScreen()
        {
            lock (_syncLock)
            {
                if (_device == null || _context == null || _uploadTexture == null || _masterTexture == null)
                {
                    return;
                }

                try
                {
                    int screenW = GetSystemMetrics(0); // SM_CXSCREEN
                    int screenH = GetSystemMetrics(1); // SM_CYSCREEN
                    if (screenW <= 0 || screenH <= 0) return;

                    IntPtr hdcScreen = GetDC(IntPtr.Zero);
                    IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
                    IntPtr hBmp = CreateCompatibleBitmap(hdcScreen, OutputWidth, OutputHeight);
                    IntPtr hOld = SelectObject(hdcMem, hBmp);

                    // High-quality Halftone interpolation scales the full screen to target 16:9 resolution proportionately
                    SetStretchBltMode(hdcMem, HALFTONE);
                    SetBrushOrgEx(hdcMem, 0, 0, IntPtr.Zero);
                    StretchBlt(hdcMem, 0, 0, OutputWidth, OutputHeight, hdcScreen, 0, 0, screenW, screenH, 0x00CC0020);

                    int totalBytes = OutputWidth * OutputHeight * 4;
                    if (_gdiCpuBuffer == null || _gdiCpuBuffer.Length != totalBytes)
                    {
                        _gdiCpuBuffer = new byte[totalBytes];
                    }

                    var bmi = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                        biWidth = OutputWidth,
                        biHeight = -OutputHeight, // Top-down
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = 0 // BI_RGB
                    };

                    GCHandle pin = GCHandle.Alloc(_gdiCpuBuffer, GCHandleType.Pinned);
                    try
                    {
                        GetDIBits(hdcMem, hBmp, 0, (uint)OutputHeight, pin.AddrOfPinnedObject(), ref bmi, 0);

                        var mapped = _context.Map(_uploadTexture, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                        Marshal.Copy(_gdiCpuBuffer, 0, mapped.DataPointer, totalBytes);
                        _context.Unmap(_uploadTexture, 0);

                        _context.CopyResource(_masterTexture, _uploadTexture);

                        // Broadcast immediate initial preview frame
                        _lastPreviewBytes = _gdiCpuBuffer;
                        _lastPreviewPitch = OutputWidth * 4;
                        _lastPreviewWidth = OutputWidth;
                        _lastPreviewHeight = OutputHeight;

                        if (_isPreviewEnabled)
                        {
                            PreviewFrameReady?.Invoke(_gdiCpuBuffer, OutputWidth * 4, OutputWidth, OutputHeight);
                        }
                    }
                    finally
                    {
                        pin.Free();
                        SelectObject(hdcMem, hOld);
                        DeleteObject(hBmp);
                        DeleteDC(hdcMem);
                        ReleaseDC(IntPtr.Zero, hdcScreen);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CaptureEngine] Baseline screen capture error: {ex.Message}");
                }
            }
        }

        private void StartContinuousTicker()
        {
            _pacerCts?.Cancel();
            _pacerCts?.Dispose();

            var cts = new CancellationTokenSource();
            _pacerCts = cts;
            int fps = _targetFps > 0 ? _targetFps : 60;

            _pacerThread = new Thread(() => RunHighPrecisionPacerLoop(fps, cts.Token))
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "Ouro_FramePacer"
            };
            _pacerThread.Start();
        }

        private void RunHighPrecisionPacerLoop(int fps, CancellationToken token)
        {
            try { timeBeginPeriod(1); } catch { }
            try
            {
                var sw = Stopwatch.StartNew();
                long frameIndex = 0;

                while (!token.IsCancellationRequested && !_isDisposed)
                {
                    frameIndex++;
                    double targetSec = (double)frameIndex / fps;

                    while (!token.IsCancellationRequested)
                    {
                        double remainingSec = targetSec - sw.Elapsed.TotalSeconds;
                        if (remainingSec <= 0)
                        {
                            break;
                        }

                        if (remainingSec > 0.002)
                        {
                            Thread.Sleep(1);
                        }
                        else
                        {
                            Thread.SpinWait(64);
                        }
                    }

                    if (token.IsCancellationRequested || _isDisposed)
                    {
                        break;
                    }

                    // If the system slept or froze for >100ms, resync frameIndex so we don't burst-fire catch-up ticks
                    double behindSec = sw.Elapsed.TotalSeconds - targetSec;
                    if (behindSec > 0.100)
                    {
                        frameIndex = (long)Math.Floor(sw.Elapsed.TotalSeconds * fps);
                    }

                    OnContinuousTick(null);
                }
            }
            finally
            {
                try { timeEndPeriod(1); } catch { }
            }
        }

        private void OnContinuousTick(object? state)
        {
            byte[]? previewToDeliver = null;
            int pitchToDeliver = 0;
            int widthToDeliver = 0;
            int heightToDeliver = 0;

            lock (_syncLock)
            {
                if (_isDisposed || _device == null || _context == null || _masterTexture == null)
                {
                    return;
                }

                // 1. If actively recording replay, continuously feed master GPU frame to NVENC at exact target FPS
                if (_isCapturing)
                {
                    try
                    {
                        FrameCaptured?.Invoke(_masterTexture);
                    }
                    catch { }
                }

                // 2. Zero-Stall Asynchronous Live Preview Readback:
                // Only run when UI is visible and preview is enabled.
                // Stage A (tick 0): Issue async GPU-to-Staging copy without mapping.
                // Stage B (tick 1): Map with DoNotWait so CPU never blocks waiting for GPU pipeline!
                if (_isPreviewEnabled && _isUiVisible && _stagingTexture != null)
                {
                    int previewPeriod = Math.Max(4, _targetFps / 15);
                    int step = _previewTickCounter++ % previewPeriod;
                    if (step == 0)
                    {
                        try
                        {
                            _context.CopyResource(_stagingTexture, _masterTexture);
                            _stagingCopyPending = true;
                        }
                        catch { }
                    }
                    else if (_stagingCopyPending)
                    {
                        try
                        {
                            var mapped = _context.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.DoNotWait);
                            if (mapped.DataPointer != IntPtr.Zero)
                            {
                                try
                                {
                                    int rowPitch = (int)mapped.RowPitch;
                                    int totalBytes = rowPitch * OutputHeight;
                                    if (_previewCpuBuffer == null || _previewCpuBuffer.Length != totalBytes)
                                    {
                                        _previewCpuBuffer = new byte[totalBytes];
                                    }
                                    Marshal.Copy(mapped.DataPointer, _previewCpuBuffer, 0, totalBytes);

                                    _lastPreviewBytes = _previewCpuBuffer;
                                    _lastPreviewPitch = rowPitch;
                                    _lastPreviewWidth = OutputWidth;
                                    _lastPreviewHeight = OutputHeight;

                                    previewToDeliver = _previewCpuBuffer;
                                    pitchToDeliver = rowPitch;
                                    widthToDeliver = OutputWidth;
                                    heightToDeliver = OutputHeight;
                                    _stagingCopyPending = false;
                                }
                                finally
                                {
                                    _context.Unmap(_stagingTexture, 0);
                                }
                            }
                        }
                        catch
                        {
                            // DoNotWait returns DXGI_ERROR_WAS_STILL_DRAWING if GPU is still busy; retry on next tick with zero stall!
                        }
                    }
                }
            }

            if (previewToDeliver != null)
            {
                PreviewFrameReady?.Invoke(previewToDeliver, pitchToDeliver, widthToDeliver, heightToDeliver);
            }
        }

        public void RequestPreviewRefresh()
        {
            lock (_syncLock)
            {
                if (_lastPreviewBytes != null && _isPreviewEnabled)
                {
                    PreviewFrameReady?.Invoke(_lastPreviewBytes, _lastPreviewPitch, _lastPreviewWidth, _lastPreviewHeight);
                }
                else
                {
                    CaptureBaselineScreen();
                }
            }
        }

        public void SetResolution(int width, int height)
        {
            lock (_syncLock)
            {
                if (width <= 0 || height <= 0) return;
                if (OutputWidth == width && OutputHeight == height) return;

                OutputWidth = width;
                OutputHeight = height;

                if (_device != null && _context != null)
                {
                    _stretchFilter?.Dispose();
                    _stretchFilter = new StretchFilter(_device, _context, OutputWidth, OutputHeight);
                    CreateTexturesLocked();
                    CaptureBaselineScreen();
                }
            }
        }

        public void SetTargetFramerate(int fps)
        {
            lock (_syncLock)
            {
                _targetFps = fps > 0 ? fps : 60;
                StartContinuousTicker();
            }
        }

        public void Start(CaptureTargetInfo target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            lock (_syncLock)
            {
                ThrowIfDisposed();

                if (_isCapturing)
                {
                    StopInternal();
                }

                CurrentTarget = target;

                try
                {
                    _captureItem = target.Type == CaptureTargetType.SpecificProcess && target.WindowHandle != IntPtr.Zero
                        ? WgcInterop.CreateItemForWindow(target.WindowHandle)
                        : WgcInterop.CreateItemForMonitor(target.MonitorHandle != IntPtr.Zero ? target.MonitorHandle : null);

                    _lastCapturedSize = _captureItem.Size;
                    _captureItem.Closed += OnCaptureItemClosed;

                    _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                        _winrtDevice,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        numberOfBuffers: 2,
                        _lastCapturedSize);

                    _framePool.FrameArrived += OnFrameArrived;

                    _captureSession = _framePool.CreateCaptureSession(_captureItem);
                    _captureSession.IsCursorCaptureEnabled = false;

                    try
                    {
                        Windows.Graphics.Capture.GraphicsCaptureAccess.RequestAccessAsync(
                            Windows.Graphics.Capture.GraphicsCaptureAccessKind.Borderless).AsTask().Wait(250);
                    }
                    catch { }

                    _captureSession.IsBorderRequired = false; // Completely removes yellow/green border on Windows 11
                    _captureSession.StartCapture();
                    _isCapturing = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CaptureEngine] WGC session start warning: {ex.Message}");
                    StopInternal();
                }
            }
        }

        public void StartForWindow(IntPtr hWnd, string processName = "")
        {
            Start(new CaptureTargetInfo
            {
                Type = CaptureTargetType.SpecificProcess,
                WindowHandle = hWnd,
                ProcessName = processName,
                DisplayName = string.IsNullOrEmpty(processName) ? "Target Window" : processName
            });
        }

        public void StartForMonitor(IntPtr? hMonitor = null, string displayName = "Primary Monitor")
        {
            Start(new CaptureTargetInfo
            {
                Type = CaptureTargetType.Monitor,
                DisplayName = displayName,
                MonitorHandle = hMonitor ?? IntPtr.Zero
            });
        }

        public void Stop()
        {
            lock (_syncLock)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            _isCapturing = false;

            if (_framePool != null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
            }

            if (_captureItem != null)
            {
                _captureItem.Closed -= OnCaptureItemClosed;
            }

            _captureSession?.Dispose();
            _captureSession = null;

            _framePool?.Dispose();
            _framePool = null;

            _captureItem = null;
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            lock (_syncLock)
            {
                if (_isDisposed || _framePool == null || _stretchFilter == null || _context == null || _masterTexture == null)
                {
                    return;
                }

                try
                {
                    using var frame = sender.TryGetNextFrame();
                    if (frame == null) return;

                    if (frame.ContentSize.Width != _lastCapturedSize.Width || frame.ContentSize.Height != _lastCapturedSize.Height)
                    {
                        _lastCapturedSize = frame.ContentSize;
                        _framePool.Recreate(
                            _winrtDevice,
                            DirectXPixelFormat.B8G8R8A8UIntNormalized,
                            2,
                            _lastCapturedSize);
                    }

                    using var sourceTexture = WgcInterop.GetTextureFromSurface(frame.Surface);

                    ID3D11Texture2D? overlayTex = null;
                    int overlayW = 0;
                    int overlayH = 0;

                    if (_isOverlayEnabled && _overlayRenderer != null)
                    {
                        string resLabel = $"{OutputWidth}x{OutputHeight}";
                        _overlayRenderer.UpdateOverlay(_overlayText, resLabel, _targetFps, _overlayIncludeTelemetry);
                        overlayTex = _overlayRenderer.OverlayTexture;
                        overlayW = _overlayRenderer.BadgeWidth;
                        overlayH = _overlayRenderer.BadgeHeight;
                    }

                    // Hardware GPU scaling to 16:9 target + optional overlay badge
                    var processedTexture = _stretchFilter.ProcessFrame(sourceTexture, false, overlayTex, overlayW, overlayH);

                    // Update master GPU texture with latest high-fps game/window frame
                    _context.CopyResource(_masterTexture, processedTexture);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CaptureEngine] Frame arrived exception: {ex.Message}");
                }
            }
        }

        private void OnCaptureItemClosed(GraphicsCaptureItem sender, object args)
        {
            lock (_syncLock)
            {
                StopInternal();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(CaptureEngine));
            }
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                _pacerCts?.Cancel();
                _pacerCts?.Dispose();
                _pacerCts = null;
                _pacerThread = null;

                StopInternal();

                _stretchFilter?.Dispose();
                _stretchFilter = null;

                _overlayRenderer?.Dispose();
                _overlayRenderer = null;

                _stagingTexture?.Dispose();
                _stagingTexture = null;

                _uploadTexture?.Dispose();
                _uploadTexture = null;

                _masterTexture?.Dispose();
                _masterTexture = null;

                _winrtDevice?.Dispose();
                _winrtDevice = null;

                _context?.Dispose();
                _context = null;

                _device?.Dispose();
                _device = null;
            }
        }
    }
}
