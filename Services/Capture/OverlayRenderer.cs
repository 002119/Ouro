using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Ouro.Services.Capture
{
    /// <summary>
    /// Generates and maintains a high-performance GPU Direct3D 11 texture
    /// containing a semi-transparent HUD telemetry & branding watermark.
    /// Operates with 0% CPU runtime cost during frame capture (static VRAM blit).
    /// </summary>
    public sealed class OverlayRenderer : IDisposable
    {
        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private readonly object _syncLock = new();

        private ID3D11Texture2D? _overlayTexture;
        private string _lastOverlayKey = string.Empty;
        private int _badgeWidth;
        private int _badgeHeight;
        private bool _isDisposed;

        public ID3D11Texture2D? OverlayTexture
        {
            get
            {
                lock (_syncLock)
                {
                    return _overlayTexture;
                }
            }
        }

        public int BadgeWidth => _badgeWidth;
        public int BadgeHeight => _badgeHeight;

        public OverlayRenderer(ID3D11Device device, ID3D11DeviceContext context)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public void UpdateOverlay(string text, string resolution, int fps, bool includeTelemetry)
        {
            string key = $"{text}|{resolution}|{fps}|{includeTelemetry}";
            lock (_syncLock)
            {
                if (_lastOverlayKey == key && _overlayTexture != null)
                {
                    return;
                }

                _lastOverlayKey = key;
                GenerateBadgeTextureLocked(text, resolution, fps, includeTelemetry);
            }
        }

        private void GenerateBadgeTextureLocked(string brandText, string res, int fps, bool includeTelemetry)
        {
            string brand = string.IsNullOrWhiteSpace(brandText) ? "OURO" : brandText.Trim().ToUpperInvariant();
            string subtext = includeTelemetry ? $"{res} • {fps} FPS" : "REPLAY";

            int w = 240;
            int h = 48;
            _badgeWidth = w;
            _badgeHeight = h;

            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);

                // Sleek pill-shaped background card (dark obsidian with subtle mint border)
                var rect = new Rectangle(2, 2, w - 4, h - 4);
                using (var path = GetRoundedRect(rect, 10))
                {
                    using var brush = new SolidBrush(Color.FromArgb(170, 9, 10, 13)); // #090A0D @ ~66% opacity
                    g.FillPath(brush, path);

                    using var borderPen = new Pen(Color.FromArgb(90, 0, 240, 181), 1.5f); // #00F0B5 Mint border
                    g.DrawPath(borderPen, path);
                }

                // Brand Tag Pill
                var tagRect = new Rectangle(10, 12, 54, 24);
                using (var tagPath = GetRoundedRect(tagRect, 6))
                {
                    using var tagBrush = new SolidBrush(Color.FromArgb(220, 0, 240, 181));
                    g.FillPath(tagBrush, tagPath);
                }

                // Tag Text
                using (var tagFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                using (var tagTextBrush = new SolidBrush(Color.FromArgb(255, 9, 10, 13)))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(brand.Length > 6 ? brand[..6] : brand, tagFont, tagTextBrush, new RectangleF(10, 12, 54, 24), sf);
                }

                // Telemetry / Details Text
                using (var font = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                using (var textBrush = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                    g.DrawString(subtext, font, textBrush, new RectangleF(72, 12, w - 78, 24), sf);
                }
            }

            // Convert System.Drawing ARGB to Direct3D 11 B8G8R8A8 texture
            var bmpData = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                _overlayTexture?.Dispose();

                var desc = new Texture2DDescription
                {
                    Width = (uint)w,
                    Height = (uint)h,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.None
                };

                var subresource = new SubresourceData(bmpData.Scan0, (uint)bmpData.Stride, 0);
                _overlayTexture = _device.CreateTexture2D(desc, new[] { subresource });
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }
        }

        private static GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                _overlayTexture?.Dispose();
                _overlayTexture = null;
            }
        }
    }
}
