using System;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Ouro.Services.Capture
{
    /// <summary>
    /// Direct3D 11 hardware-accelerated aspect ratio scaler.
    /// Detects non-16:9 / 4:3 input textures from games and stretches them to 16:9 (1920x1080)
    /// using GPU hardware bilinear / bicubic filtering with 0 CPU RAM overhead.
    /// </summary>
    public sealed class StretchFilter : IDisposable
    {
        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private readonly object _syncLock = new();

        private ID3D11Texture2D? _targetTexture;
        private ID3D11RenderTargetView? _targetRtv;
        private ID3D11VertexShader? _vertexShader;
        private ID3D11PixelShader? _pixelShaderLinear;
        private ID3D11SamplerState? _linearSampler;
        private ID3D11RasterizerState? _rasterizerState;
        private ID3D11BlendState? _alphaBlendState;

        private Viewport _viewport;
        private bool _isDisposed;

        public int TargetWidth { get; }
        public int TargetHeight { get; }

        public ID3D11Texture2D? TargetTexture
        {
            get
            {
                lock (_syncLock)
                {
                    return _targetTexture;
                }
            }
        }

        public StretchFilter(ID3D11Device device, ID3D11DeviceContext context, int targetWidth = 1920, int targetHeight = 1080)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            TargetWidth = targetWidth;
            TargetHeight = targetHeight;

            InitializeResources();
        }

        private void InitializeResources()
        {
            lock (_syncLock)
            {
                // Target 16:9 NVENC-compatible texture
                var targetDesc = new Texture2DDescription
                {
                    Width = (uint)TargetWidth,
                    Height = (uint)TargetHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.None,
                    MiscFlags = ResourceOptionFlags.None
                };

                _targetTexture = _device.CreateTexture2D(targetDesc);
                _targetRtv = _device.CreateRenderTargetView(_targetTexture);

                _viewport = new Viewport(0, 0, TargetWidth, TargetHeight);

                // Compile shaders from embedded pre-compiled bytecode
                _vertexShader = _device.CreateVertexShader(StretchShaderBytecode.VertexShader);
                _pixelShaderLinear = _device.CreatePixelShader(StretchShaderBytecode.PixelShaderLinear);

                // Hardware linear clamp sampler
                var samplerDesc = new SamplerDescription
                {
                    Filter = Filter.MinMagMipLinear,
                    AddressU = TextureAddressMode.Clamp,
                    AddressV = TextureAddressMode.Clamp,
                    AddressW = TextureAddressMode.Clamp,
                    ComparisonFunc = ComparisonFunction.Never,
                    MinLOD = 0,
                    MaxLOD = float.MaxValue
                };
                _linearSampler = _device.CreateSamplerState(samplerDesc);

                var rasterDesc = new RasterizerDescription
                {
                    CullMode = CullMode.None,
                    FillMode = FillMode.Solid,
                    DepthClipEnable = false
                };
                _rasterizerState = _device.CreateRasterizerState(rasterDesc);

                // Alpha blending for seamless HUD overlay composition
                var blendDesc = new BlendDescription
                {
                    AlphaToCoverageEnable = false,
                    IndependentBlendEnable = false
                };
                blendDesc.RenderTarget[0] = new RenderTargetBlendDescription
                {
                    BlendEnable = true,
                    SourceBlend = Blend.SourceAlpha,
                    DestinationBlend = Blend.InverseSourceAlpha,
                    BlendOperation = BlendOperation.Add,
                    SourceBlendAlpha = Blend.One,
                    DestinationBlendAlpha = Blend.InverseSourceAlpha,
                    BlendOperationAlpha = BlendOperation.Add,
                    RenderTargetWriteMask = ColorWriteEnable.All
                };
                _alphaBlendState = _device.CreateBlendState(blendDesc);
            }
        }

        /// <summary>
        /// Processes a captured Direct3D11 frame.
        /// If the source is in 4:3 (or forced), scales it to 16:9 on the GPU using hardware sampling.
        /// Optionally composes an in-VRAM HUD watermark overlay with zero CPU overhead.
        /// </summary>
        public ID3D11Texture2D ProcessFrame(ID3D11Texture2D sourceTexture, bool forceStretch = false, ID3D11Texture2D? overlayTexture = null, int overlayW = 0, int overlayH = 0)
        {
            if (sourceTexture == null) throw new ArgumentNullException(nameof(sourceTexture));

            lock (_syncLock)
            {
                ThrowIfDisposed();

                var desc = sourceTexture.Description;
                double aspect = desc.Height > 0 ? (double)desc.Width / desc.Height : 16.0 / 9.0;
                bool is4By3 = Math.Abs(aspect - (4.0 / 3.0)) < 0.05;

                // If identical 16:9 1080p without stretch override and no overlay, 1:1 hardware copy
                if (!is4By3 && !forceStretch && overlayTexture == null && desc.Width == TargetWidth && desc.Height == TargetHeight && desc.Format == Format.B8G8R8A8_UNorm)
                {
                    _context.CopyResource(_targetTexture!, sourceTexture);
                    return _targetTexture!;
                }

                // Create temporary SRV for source texture
                using var srv = _device.CreateShaderResourceView(sourceTexture);

                // Bind render target and viewport
                _context.OMSetRenderTargets(_targetRtv!);
                _context.RSSetViewport(_viewport);
                _context.RSSetState(_rasterizerState);

                _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                _context.IASetInputLayout(null);

                _context.VSSetShader(_vertexShader);
                _context.PSSetShader(_pixelShaderLinear);
                _context.PSSetSampler(0, _linearSampler);
                _context.PSSetShaderResource(0, srv);

                // Fullscreen procedural triangle (3 vertices via SV_VertexID)
                _context.Draw(3, 0);

                // Render overlay badge in bottom-right corner if requested
                if (overlayTexture != null && overlayW > 0 && overlayH > 0)
                {
                    using var overlaySrv = _device.CreateShaderResourceView(overlayTexture);
                    _context.OMSetBlendState(_alphaBlendState);

                    // Viewport targeted strictly to bottom-right corner with 24px margins
                    float x = Math.Max(0, TargetWidth - overlayW - 24);
                    float y = Math.Max(0, TargetHeight - overlayH - 24);
                    var overlayViewport = new Viewport(x, y, overlayW, overlayH);
                    _context.RSSetViewport(overlayViewport);

                    _context.PSSetShaderResource(0, overlaySrv);
                    _context.Draw(3, 0);

                    // Restore default blend state and viewport
                    _context.OMSetBlendState(null);
                    _context.RSSetViewport(_viewport);
                }

                // Unbind resources to prevent pipeline hazard in downstream encoders
#pragma warning disable CS8625
                _context.PSSetShaderResource(0, null);
                _context.OMSetRenderTargets((ID3D11RenderTargetView?)null);
#pragma warning restore CS8625

                return _targetTexture!;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(StretchFilter));
            }
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                _targetRtv?.Dispose();
                _targetRtv = null;

                _targetTexture?.Dispose();
                _targetTexture = null;

                _vertexShader?.Dispose();
                _vertexShader = null;

                _pixelShaderLinear?.Dispose();
                _pixelShaderLinear = null;

                _linearSampler?.Dispose();
                _linearSampler = null;

                _alphaBlendState?.Dispose();
                _alphaBlendState = null;

                _rasterizerState?.Dispose();
                _rasterizerState = null;
            }
        }
    }
}
