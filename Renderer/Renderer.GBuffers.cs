using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using Device = SharpDX.Direct3D11.Device;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    public class GBuffer : IDisposable
    {
        public BloomBuffers Bloom { get; private set; }

        public RenderTarget2D RT0 { get; private set; }
        public RenderTarget2D RT1 { get; private set; }
        public RenderTarget2D RT1_Clone { get; private set; }

        public RenderTarget2D RT2 { get; private set; }
        public RenderTarget2D RT2_Clone { get; private set; }

        public RenderTarget2D LightDiffuse { get; private set; }
        public RenderTarget2D LightSpecular { get; private set; }
        public RenderTarget2D LightIBL { get; private set; }

        public RenderTarget2D Shading { get; private set; }
        public RenderTarget2D Shading_Clone { get; private set; }

        public RenderTarget2D PostProcessResult { get; private set; }
        public RenderTarget2D FXAA { get; private set; }
        public RenderTarget2D HDAO { get; private set; }

        public DepthTarget Depth { get; private set; }
        public DepthTarget Depth_Clone { get; private set; }
        public DepthTarget DepthHalf { get; private set; }

        public RenderTarget2D UberDepthHalf { get; private set; }
        public RenderTarget2D UberDepthQuarter { get; private set; }
        public RenderTarget2D UberDepth8th { get; private set; }

        public RenderTarget2D SkyGenerateMask { get; private set; }
        public RenderTarget2D SkyGenerateMaskHalf { get; private set; }
        public RenderTarget2D SkyGenerateFar { get; private set; }
        public RenderTarget2D SkyGenerateNear { get; private set; }
        public RenderTarget2D FullHemisphereSkyColor { get; private set; }
        public RenderTarget2D FullHemisphereSkyColorTemp { get; private set; }
        public RenderTarget2D DepthAngleDensityLookup { get; private set; }

        public RenderTarget2D SkyBlur1 { get; private set; }
        public RenderTarget2D SkyBlur2 { get; private set; }

        public RenderTarget2D SkyHemiSeedInscatter { get; private set; }
        public RenderTarget2D SkyHemiBlur { get; private set; }

        public RenderTarget2D ColorGradingLUT { get; private set; }
        public UAVTarget3D LUTVolume { get; private set; }

        public RenderTarget2D Luminance { get; private set; }
        public Texture2D LuminanceStaging { get; private set; }

        public int Width { get; }
        public int Height { get; }

        public GBuffer(Device device, int width, int height)
        {
            Width = width;
            Height = height;

            RT0 = new RenderTarget2D(device, width, height, Format.R8G8B8A8_UNorm, debugName: "RT0: Albedo");

            RT1 = new RenderTarget2D(device, width, height, Format.R10G10B10A2_UNorm, debugName: "RT1: Normal");
            RT1_Clone = new RenderTarget2D(device, width, height, Format.R10G10B10A2_UNorm, debugName: "RT1 Clone: Normal");

            RT2 = new RenderTarget2D(device, width, height, Format.R8G8B8A8_UNorm, debugName: "RT2: Stack");
            RT2_Clone = new RenderTarget2D(device, width, height, Format.R8G8B8A8_UNorm, debugName: "RT2 Clone: Stack");

            LightDiffuse = new RenderTarget2D(device, width, height, Format.R11G11B10_Float, debugName: "Light Diffuse");
            LightSpecular = new RenderTarget2D(device, width, height, Format.R11G11B10_Float, debugName: "Light Specular");
            LightIBL = new RenderTarget2D(device, width, height, Format.R11G11B10_Float, debugName: "Light IBL");

            Shading = new RenderTarget2D(device, width, height, Format.R11G11B10_Float, debugName: "Shading");
            Shading_Clone = new RenderTarget2D(device, width, height, Format.R11G11B10_Float, debugName: "Shading Clone");

            PostProcessResult = new RenderTarget2D(device, width, height, Format.R16G16B16A16_Float, debugName: "Post Process Result");
            FXAA = new RenderTarget2D(device, width, height, Format.R16G16B16A16_Float, debugName: "FXAA Result");
            HDAO = new RenderTarget2D(device, width, height, Format.R8G8_UNorm, debugName: "HDAO");

            Depth = new DepthTarget(device, width, height, Format.R24G8_Typeless, debugName: "RT Depth");
            Depth_Clone = new DepthTarget(device, width, height, Format.R24G8_Typeless, debugName: "RT Depth Clone");
            DepthHalf = new DepthTarget(device, width / 2, height / 2, Format.R24G8_Typeless, debugName: "Depth Half");

            UberDepthHalf = new RenderTarget2D(device, width / 2, height / 2, Format.R16G16_Float, createUAV: true, debugName: "Uber Depth (Half)");
            UberDepthQuarter = new RenderTarget2D(device, width / 4, height / 4, Format.R16G16B16A16_Float, createUAV: true, debugName: "Uber Depth (Quarter)");
            UberDepth8th = new RenderTarget2D(device, width / 8, height / 8, Format.R16G16_Float, createUAV: true, debugName: "Uber Depth (8th)");

            SkyGenerateMask = new RenderTarget2D(device, width / 4, height / 4, Format.R8_UNorm, debugName: "Sky Generate Mask");
            SkyGenerateMaskHalf = new RenderTarget2D(device, width / 8, height / 8, Format.R16G16B16A16_Float, debugName: "Sky Generate Mask (Half)");
            SkyGenerateFar = new RenderTarget2D(device, width / 4, height / 4, Format.R16G16B16A16_Float, debugName: "Sky Generate Far");
            SkyGenerateNear = new RenderTarget2D(device, width / 4, height / 4, Format.R16G16B16A16_Float, debugName: "Sky Generate Near");

            SkyBlur1 = new RenderTarget2D(device, width / 8, height / 8, Format.R16G16B16A16_Float, debugName: "Sky Mask Blur 1");
            SkyBlur2 = new RenderTarget2D(device, width / 8, height / 8, Format.R16G16B16A16_Float, debugName: "Sky Mask Blur 2");

            FullHemisphereSkyColorTemp = new RenderTarget2D(device, 512, 512, Format.R16G16B16A16_Float, debugName: "Full Hemisphere Sky Color Temp");
            FullHemisphereSkyColor = new RenderTarget2D(device, 512, 512, Format.R16G16B16A16_Float, debugName: "Full Hemisphere Sky Color", resourceOptionFlags: ResourceOptionFlags.GenerateMipMaps, mipLevels: 0);
            DepthAngleDensityLookup = new RenderTarget2D(device, 512, 512, Format.R16G16B16A16_Float, debugName: "Depth Angle Density Lookup");

            SkyHemiSeedInscatter = new RenderTarget2D(device, 512, 512, Format.R16G16_Float, debugName: "Hemisphere Seed Inscattering");
            SkyHemiBlur = new RenderTarget2D(device, 512, 512, Format.R16G16_Float, debugName: "Hemisphere Blur");

            ColorGradingLUT = new RenderTarget2D(device, 1024, 32, Format.R16G16B16A16_Float, debugName: "Color Grading LUT");
            LUTVolume = new UAVTarget3D(device, 32, 32, 32, Format.R11G11B10_Float, "LUT Volume");

            Luminance = new RenderTarget2D(device, width, height, Format.R32_Float, debugName: "Luminance");
            LuminanceStaging = RenderHelpers.CreateStagingTexture(device, 1, 1, Luminance.Texture.Description.Format, "Luminance Staging");

            Bloom = new BloomBuffers(device, width, height);
        }

        public void SetRenderTargets(DeviceContext ctx)
        {
            ctx.OutputMerger.SetTargets(Depth.DSV, RT0.RTV, RT1.RTV, RT2.RTV);
            ctx.ClearRenderTargetView(RT0.RTV, new RawColor4(0.0f, 0.0f, 0.0f, 0f));
            ctx.ClearRenderTargetView(RT1.RTV, new RawColor4(0f, 0f, 0f, 0f));
            ctx.ClearRenderTargetView(RT2.RTV, new RawColor4(1f, 0.5f, 1f, 1f));
            ctx.ClearDepthStencilView(Depth.DSV, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 0f, 0);
        }

        public void Dispose()
        {
            RT0?.Dispose();
            RT0 = null;
            RT1?.Dispose();
            RT1 = null;
            RT1_Clone?.Dispose();
            RT1_Clone = null;
            RT2?.Dispose();
            RT2 = null;
            RT2_Clone?.Dispose();
            RT2_Clone = null;

            LightDiffuse?.Dispose();
            LightDiffuse = null;
            LightSpecular?.Dispose();
            LightSpecular = null;
            LightIBL?.Dispose();
            LightIBL = null;

            Shading?.Dispose();
            Shading = null;
            Shading_Clone?.Dispose();
            Shading_Clone = null;

            PostProcessResult?.Dispose();
            PostProcessResult = null;
            FXAA?.Dispose();
            FXAA = null;
            HDAO?.Dispose();
            HDAO = null;

            Depth?.Dispose();
            Depth = null;
            Depth_Clone?.Dispose();
            Depth_Clone = null;
            DepthHalf?.Dispose();
            DepthHalf = null;

            UberDepthHalf?.Dispose();
            UberDepthHalf = null;
            UberDepthQuarter?.Dispose();
            UberDepthQuarter = null;
            UberDepth8th?.Dispose();
            UberDepth8th = null;

            SkyGenerateMask?.Dispose();
            SkyGenerateMask = null;
            SkyGenerateMaskHalf?.Dispose();
            SkyGenerateMaskHalf = null;
            SkyGenerateFar?.Dispose();
            SkyGenerateFar = null;
            SkyGenerateNear?.Dispose();
            SkyGenerateNear = null;

            SkyBlur1?.Dispose();
            SkyBlur1 = null;
            SkyBlur2?.Dispose();
            SkyBlur2 = null;

            SkyHemiSeedInscatter?.Dispose();
            SkyHemiSeedInscatter = null;
            SkyHemiBlur?.Dispose();
            SkyHemiBlur = null;

            FullHemisphereSkyColorTemp?.Dispose();
            FullHemisphereSkyColorTemp = null;
            FullHemisphereSkyColor?.Dispose();
            FullHemisphereSkyColor = null;
            DepthAngleDensityLookup?.Dispose();
            DepthAngleDensityLookup = null;
            LUTVolume?.Dispose();
            LUTVolume = null;
            LuminanceStaging?.Dispose();
            LuminanceStaging = null;
            ColorGradingLUT?.Dispose();
            ColorGradingLUT = null;
            Bloom?.Dispose();
            Bloom = null;
        }
    }

    public class BloomBuffers : IDisposable
    {
        public RenderTarget2D Bloom3rd { get; private set; }
        public RenderTarget2D Bloom3rdTemp { get; private set; }
        public RenderTarget2D Bloom3rdComb { get; private set; }

        public RenderTarget2D Bloom6th { get; private set; }
        public RenderTarget2D Bloom6thTemp { get; private set; }
        public RenderTarget2D Bloom6thComb { get; private set; }

        public RenderTarget2D Bloom12th { get; private set; }
        public RenderTarget2D Bloom12thTemp { get; private set; }
        public RenderTarget2D Bloom12thComb { get; private set; }

        public RenderTarget2D Bloom12thHalfW { get; private set; }
        public RenderTarget2D Bloom12thQuarterW { get; private set; }
        public RenderTarget2D Bloom12thQuarterWTemp { get; private set; }

        public RenderTarget2D Bloom24th { get; private set; }
        public RenderTarget2D Bloom24thTemp { get; private set; }

        public RenderTarget2D BloomFinal { get; private set; }

        public RenderTarget2D AutoExposureColumns { get; private set; }
        public Texture2D AutoExposureColumnsStaging { get; private set; }

        public BloomBuffers(Device device, int width, int height)
        {
            Bloom3rd = new RenderTarget2D(device, width / 3, height / 3, Format.R16G16B16A16_Float, debugName: "Bloom 3rd");
            Bloom3rdTemp = new RenderTarget2D(device, width / 3, height / 3, Format.R16G16B16A16_Float, debugName: "Bloom 3rd Temp");
            Bloom3rdComb = new RenderTarget2D(device, width / 3, height / 3, Format.R16G16B16A16_Float, debugName: "Bloom 3rd Combined");

            Bloom6th = new RenderTarget2D(device, width / 6, height / 6, Format.R16G16B16A16_Float, debugName: "Bloom 6th");
            Bloom6thTemp = new RenderTarget2D(device, width / 6, height / 6, Format.R16G16B16A16_Float, debugName: "Bloom 6th Temp");
            Bloom6thComb = new RenderTarget2D(device, width / 6, height / 6, Format.R16G16B16A16_Float, debugName: "Bloom 6th Comb Combined");

            Bloom12th = new RenderTarget2D(device, width / 12, height / 12, Format.R16G16B16A16_Float, debugName: "Bloom 12th");
            Bloom12thTemp = new RenderTarget2D(device, width / 12, height / 12, Format.R16G16B16A16_Float, debugName: "Bloom 12th Temp");
            Bloom12thComb = new RenderTarget2D(device, width / 12, height / 12, Format.R16G16B16A16_Float, debugName: "Bloom 12th Combined");

            Bloom12thHalfW = new RenderTarget2D(device, width / (12 * 2), height / 12, Format.R16G16B16A16_Float, debugName: "Bloom 12th Half");
            Bloom12thQuarterW = new RenderTarget2D(device, width / (12 * 4), height / 12, Format.R16G16B16A16_Float, debugName: "Bloom 12th Quarter");
            Bloom12thQuarterWTemp = new RenderTarget2D(device, width / (12 * 4), height / 12, Format.R16G16B16A16_Float, debugName: "Bloom 12th Quarter Temp");

            Bloom24th = new RenderTarget2D(device, width / 24, height / 24, Format.R16G16B16A16_Float, debugName: "Bloom 24th");
            Bloom24thTemp = new RenderTarget2D(device, width / 24, height / 24, Format.R16G16B16A16_Float, debugName: "Bloom 24th Temp");

            BloomFinal = new RenderTarget2D(device, width / 2, height / 2, Format.R16G16B16A16_Float, debugName: "Bloom Final");

            AutoExposureColumns = new RenderTarget2D(device, width / 48, 1, Format.R32G32B32A32_Float, debugName: "AutoExposureColumns");
            //AutoExposureColumnsStaging = RenderHelpers.CreateStagingTexture(device,
            //    AutoExposureColumns.Width,
            //    AutoExposureColumns.Height,
            //    AutoExposureColumns.Texture.Description.Format,
            //    "AutoExposureColumns Staging");

            // improves auto exposure performance by buffering 3 column textures
            CreateAutoExposureTextures(device, width, height);
        }

        public const int ExposureBufferCount = 3;
        public Texture2D[] ExposureStagings = new Texture2D[ExposureBufferCount];
        public void CreateAutoExposureTextures(Device device, int width, int height)
        {
            for (int i = 0; i < ExposureBufferCount; i++)
            {
                ExposureStagings[i] = RenderHelpers.CreateStagingTexture(device, width / 48, 1, Format.R32G32B32A32_Float, $"AutoExposure Columns Staging {i}");
            }
        }

        public void Dispose()
        {
            Bloom3rd?.Dispose();
            Bloom3rd = null;
            Bloom3rdComb?.Dispose();
            Bloom3rdComb = null;
            Bloom3rdTemp?.Dispose();
            Bloom3rdTemp = null;

            Bloom6th?.Dispose();
            Bloom6th = null;
            Bloom6thComb?.Dispose();
            Bloom6thComb = null;
            Bloom6thTemp?.Dispose();
            Bloom6thTemp = null;

            Bloom12th?.Dispose();
            Bloom12th = null;
            Bloom12thComb?.Dispose();
            Bloom12thComb = null;
            Bloom12thTemp?.Dispose();
            Bloom12thTemp = null;

            Bloom12thHalfW?.Dispose();
            Bloom12thHalfW = null;

            Bloom12thQuarterW?.Dispose();
            Bloom12thQuarterW = null;
            Bloom12thQuarterWTemp?.Dispose();
            Bloom12thQuarterWTemp = null;

            Bloom24th?.Dispose();
            Bloom24th = null;
            Bloom24thTemp?.Dispose();
            Bloom24thTemp = null;

            BloomFinal?.Dispose();
            BloomFinal = null;

            AutoExposureColumns?.Dispose();
            AutoExposureColumns = null;
            //AutoExposureColumnsStaging?.Dispose();
            //AutoExposureColumnsStaging = null;

            foreach (var tex in ExposureStagings)
            {
                tex?.Dispose();
            }
            ExposureStagings = null;
        }
    }

    public class RenderTarget2D : IDisposable
    {
        public Texture2D Texture { get; private set; }
        public RenderTargetView RTV { get; private set; }
        public ShaderResourceView SRV { get; private set; }
        public UnorderedAccessView UAV { get; private set; }

        public int Width { get; }
        public int Height { get; }

        public RenderTarget2D(
            Device device,
            int width,
            int height,
            Format format = Format.R8G8B8A8_UNorm,
            ResourceOptionFlags resourceOptionFlags = ResourceOptionFlags.None,
            bool createUAV = false,
            int mipLevels = 1,
            string debugName = "")
        {
            Width = width;
            Height = height;

            BindFlags flags = BindFlags.RenderTarget;
            if (createUAV)
                flags |= BindFlags.UnorderedAccess;

            // Texture description
            var texDesc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = mipLevels,
                ArraySize = 1,
                Format = format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = flags | BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = resourceOptionFlags
            };

            Texture = new Texture2D(device, texDesc);
            if (debugName != string.Empty)
                Texture.DebugName = debugName;

            RTV = new RenderTargetView(device, Texture);
            SRV = new ShaderResourceView(device, Texture);
            SRV.DebugName = $"{Texture.DebugName} SRV";

            if (createUAV)
            {
                var uavDesc = new UnorderedAccessViewDescription
                {
                    Format = format,
                    Dimension = UnorderedAccessViewDimension.Texture2D,
                    Texture2D = new UnorderedAccessViewDescription.Texture2DResource
                    {
                        MipSlice = 0,
                    }
                };
                UAV = new UnorderedAccessView(device, Texture, uavDesc);
                UAV.DebugName = $"{Texture.DebugName} UAV";
            }
        }

        public void Clear(DeviceContext context, RawColor4? clearColor = null)
        {
            if (clearColor == null)
                clearColor = new RawColor4(0, 0, 0, 0);

            context.ClearRenderTargetView(RTV, clearColor.Value);
        }

        public void SetRenderTarget(DeviceContext context, bool clear = true)
        {
            context.OutputMerger.SetRenderTargets(RTV);
            if (clear)
                context.ClearRenderTargetView(RTV, new Color4(0, 0, 0, 0));
        }

        /// <summary>
        /// Sets the render target AND viewport to the render targets size. Does not set dsv
        /// </summary>
        /// <param name="context"></param>
        public void Bind(DeviceContext context)
        {
            context.OutputMerger.SetRenderTargets(null, RTV);
            context.Rasterizer.SetViewport(GetViewport());
        }

        public void SetViewport(DeviceContext context)
        {
            context.Rasterizer.SetViewport(GetViewport());
        }

        public void CopyTo(DeviceContext context, RenderTarget2D target)
        {
            context.CopyResource(this.Texture, target.Texture);
        }

        public Vector4 GetResolutionInverse()
        {
            int width = Texture.Description.Width;
            int height = Texture.Description.Height;
            return new(width, height, 1f / width, 1f / height);
        }

        public (int width, int height) GetResolution()
        {
            int width = Texture.Description.Width;
            int height = Texture.Description.Height;
            return (width, height);
        }

        public Viewport GetViewport()
        {
            return new Viewport
            {
                X = 0,
                Y = 0,
                Width = Width,
                Height = Height,
                MinDepth = 0,
                MaxDepth = 1,
            };
        }

        public void SetShaderResource(DeviceContext context, int slot, ShaderStage stage)
        {
            switch (stage)
            {
                case ShaderStage.Pixel:
                    context.PixelShader.SetShaderResource(slot, SRV);
                    break;
                case ShaderStage.Vertex:
                    context.VertexShader.SetShaderResource(slot, SRV);
                    break;
            }
        }

        public void Dispose()
        {
            Texture?.Dispose();
            SRV?.Dispose();
            RTV?.Dispose();
            UAV?.Dispose();

            SRV = null;
            RTV = null;
            UAV = null;
            Texture = null;
        }
    }

    public class DepthTarget : IDisposable
    {
        public Texture2D Texture { get; private set; }
        public DepthStencilView DSV { get; private set; }
        public ShaderResourceView DepthSRV { get; private set; }

        public int Width { get; }
        public int Height { get; }

        public DepthTarget(
            Device device,
            int width,
            int height,
            Format format = Format.R8G8B8A8_UNorm,
            ResourceOptionFlags resourceOptionFlags = ResourceOptionFlags.None,
            string debugName = "")
        {
            Width = width;
            Height = height;

            var depthDesc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = format,
                SampleDescription = new SampleDescription(1, 0),
                BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.None,
                Usage = ResourceUsage.Default,
                OptionFlags = ResourceOptionFlags.None
            };

            Texture = new Texture2D(device, depthDesc);
            if (debugName != string.Empty)
                Texture.DebugName = debugName;

            var dsvDesc = new DepthStencilViewDescription
            {
                Format = Format.D24_UNorm_S8_UInt,
                Dimension = DepthStencilViewDimension.Texture2D
            };
            DSV = new DepthStencilView(device, Texture, dsvDesc);

            var srvDesc = new ShaderResourceViewDescription
            {
                Format = Format.R24_UNorm_X8_Typeless,
                Dimension = ShaderResourceViewDimension.Texture2D,
                Texture2D = { MipLevels = 1, MostDetailedMip = 0 }
            };
            DepthSRV = new ShaderResourceView(device, Texture, srvDesc);
            DepthSRV.DebugName = $"{Texture.DebugName} SRV";
        }

        public void CopyTo(DeviceContext context, DepthTarget target)
        {
            context.CopyResource(this.Texture, target.Texture);
        }

        public void Clear(DeviceContext context, float depth, byte stencilRef)
        {
            context.ClearDepthStencilView(DSV, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, depth, stencilRef);
        }

        public void Set(DeviceContext context)
        {
            context.OutputMerger.SetRenderTargets(DSV, [null]);
            //if (clear)
            //    context.ClearDepthStencilView(DSV, DepthStencilClearFlags.Depth);
        }

        public void SetShaderResource(DeviceContext context, int slot, ShaderStage stage)
        {
            switch (stage)
            {
                case ShaderStage.Pixel:
                    context.PixelShader.SetShaderResource(slot, DepthSRV);
                    break;
                case ShaderStage.Vertex:
                    context.VertexShader.SetShaderResource(slot, DepthSRV);
                    break;
            }
        }

        public Vector4 GetResolutionInverse()
        {
            int width = Texture.Description.Width;
            int height = Texture.Description.Height;
            return new(width, height, 1f / width, 1f / height);
        }

        public (int width, int height) GetResolution()
        {
            int width = Texture.Description.Width;
            int height = Texture.Description.Height;
            return (width, height);
        }

        public void Dispose()
        {
            Texture?.Dispose();
            DepthSRV?.Dispose();
            DSV?.Dispose();

            DepthSRV = null;
            DSV = null;
            Texture = null;
        }
    }

    public class UAVTarget3D : IDisposable
    {
        public Texture3D Texture { get; private set; }
        public ShaderResourceView SRV { get; private set; }
        public UnorderedAccessView UAV { get; private set; }

        public int Width { get; }
        public int Height { get; }
        public int Depth { get; }

        public UAVTarget3D(
            Device device,
            int width,
            int height,
            int depth,
            Format format = Format.R8G8B8A8_UNorm,
            string debugName = "")
        {
            Width = width;
            Height = height;
            Depth = depth;

            var texDesc = new Texture3DDescription
            {
                Width = width,
                Height = height,
                Depth = depth,
                MipLevels = 1,
                Format = format,
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource | BindFlags.RenderTarget,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None
            };

            Texture = new Texture3D(device, texDesc);
            var srvDesc = new ShaderResourceViewDescription
            {
                Format = format,
                Dimension = ShaderResourceViewDimension.Texture3D,
                Texture3D = new ShaderResourceViewDescription.Texture3DResource
                {
                    MostDetailedMip = 0,
                    MipLevels = 1
                }
            };
            SRV = new ShaderResourceView(device, Texture, srvDesc);

            var uavDesc = new UnorderedAccessViewDescription
            {
                Format = format,
                Dimension = UnorderedAccessViewDimension.Texture3D,
                Texture3D = new UnorderedAccessViewDescription.Texture3DResource
                {
                    MipSlice = 0,
                    FirstWSlice = 0,
                    WSize = depth
                }
            };
            UAV = new UnorderedAccessView(device, Texture, uavDesc);

            if (!string.IsNullOrEmpty(debugName))
            {
                Texture.DebugName = debugName;
                SRV.DebugName = $"{debugName} SRV";
                UAV.DebugName = $"{debugName} UAV";
            }
        }

        public void Dispose()
        {
            UAV?.Dispose();
            SRV?.Dispose();
            Texture?.Dispose();

            UAV = null;
            SRV = null;
            Texture = null;
        }
    }
}
