using DirectXTex;
using DirectXTexNet;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Tiger;
using Tiger.Schema;
using static Tiger.Schema.DirectXSampler;
using Texture = Tiger.Schema.Texture;

namespace Charm.Renderer;

public class AssetManager : IDisposable
{
    public readonly Dictionary<uint, ShaderResourceView> _cache = new(); // used for mesh
    public readonly Dictionary<uint, ShaderResourceView> _globalCache = new(); // used for pipelines/externs
    public ShaderResourceView WhiteTexture;
    public ShaderResourceView BlackTexture;

    public VertexShader EntityOverrideVS_NoVC;
    public VertexShader EntityOverrideVS_VC;
    public VertexShader InvestmentOverrideVS_NoVC;
    public VertexShader InvestmentOverrideVS_VC;

    private static AssetManager _instance;
    public static AssetManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = new AssetManager();

            return _instance;
        }
    }

    public AssetManager()
    {
        if (GPU.Instance is null || GPU.Instance.Device is null)
            throw new Exception("GPU Device is not valid!");

        CreateDefaults();
    }

    public static AssetManager GetInstance()
    {
        if (Instance == null)
            throw new Exception("AssetManager Instance is not valid!");

        return Instance;
    }

    private void CreateDefaults()
    {
        if (WhiteTexture is null)
        {
            var whiteData = Enumerable.Repeat((byte)255, 1 * 1 * 4).ToArray();
            WhiteTexture = new ShaderResourceView(
                GPU.Instance.Device,
                SharpDX.Toolkit.Graphics.Texture2D.New(GPU.Instance.Device, 1, 1, Format.R8G8B8A8_UNorm, whiteData));
            WhiteTexture.DebugName = "Placeholder White";
        }

        if (BlackTexture is null)
        {
            var blackdata = Enumerable.Repeat((byte)0, 1 * 1 * 4).ToArray();
            BlackTexture = new ShaderResourceView(
                GPU.Instance.Device,
                SharpDX.Toolkit.Graphics.Texture2D.New(GPU.Instance.Device, 1, 1, Format.R8G8B8A8_UNorm, blackdata));
            BlackTexture.DebugName = "Placeholder Black";
        }

        var bytecode = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders/entity_vs_override.hlsl", "VSMain", "vs_5_0");
        EntityOverrideVS_NoVC = new SharpDX.Direct3D11.VertexShader(GPU.Instance.Device, bytecode)
        {
            DebugName = "Entity Override Vertex Shader"
        };

        bytecode = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders/entity_vs_override_vc.hlsl", "VSMain", "vs_5_0");
        EntityOverrideVS_VC = new SharpDX.Direct3D11.VertexShader(GPU.Instance.Device, bytecode)
        {
            DebugName = "Entity Override VC Vertex Shader"
        };

        bytecode = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders/investment_vs_override.hlsl", "VSMain", "vs_5_0");
        InvestmentOverrideVS_NoVC = new SharpDX.Direct3D11.VertexShader(GPU.Instance.Device, bytecode)
        {
            DebugName = "Investment Override Vertex Shader"
        };

        bytecode = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders/investment_vs_override_vc.hlsl", "VSMain", "vs_5_0");
        InvestmentOverrideVS_VC = new SharpDX.Direct3D11.VertexShader(GPU.Instance.Device, bytecode)
        {
            DebugName = "Investment Override VC Vertex Shader"
        };

        UpdateEntityOverride(true);
    }

    public void UpdateEntityOverride(bool useVC)
    {
        //EntityOverrideVS = useVC ? EntityOverrideVS_VC : EntityOverrideVS_NoVC;
    }

    // TODO
    public void UnregisterTexture(ShaderResourceView srv)
    {

    }

    public ShaderResourceView GetOrCreateTexture(DeviceContext context, Texture texture)
    {
        if (!_cache.TryGetValue(texture.Hash.Hash32, out var srv))
        {
            srv = CreateTexture(context, texture);
            _cache[texture.Hash.Hash32] = srv;
        }

        return srv;
    }

    public ShaderResourceView GetOrCreateGlobalTexture(DeviceContext context, Texture texture)
    {
        if (!_globalCache.TryGetValue(texture.Hash.Hash32, out var srv))
        {
            srv = CreateTexture(context, texture);
            _globalCache[texture.Hash.Hash32] = srv;
        }

        return srv;
    }

    public Dictionary<uint, ShaderResourceView> CreateTextures(DeviceContext context, SMaterialShader stage)
    {
        Dictionary<uint, ShaderResourceView> textures = new();

        foreach (var tex in stage.EnumerateTextures())
        {
            if (tex.Texture is null)
                continue;

            textures.TryAdd(tex.TextureIndex, GetOrCreateTexture(context, tex.Texture));
        }

        return textures;
    }

    public Dictionary<uint, ShaderResourceView> CreateTextures(DeviceContext context, List<STextureTag> tags)
    {
        Dictionary<uint, ShaderResourceView> textures = new();

        foreach (var tex in tags)
        {
            if (tex.Texture is null)
                continue;

            textures.TryAdd(tex.TextureIndex, GetOrCreateTexture(context, tex.Texture));
        }

        return textures;
    }

    public ShaderResourceView CreateTexture(DeviceContext context, Tiger.Schema.Texture tex)
    {
        byte[] pixelData = tex.GetRawBytes();

        if (tex.GetDimension() == Tiger.Schema.TextureDimension.D3)
        {
            var desc = new Texture3DDescription
            {
                Width = tex.Width,
                Height = tex.Height,
                Depth = tex.Depth,
                MipLevels = 1,
                Format = (Format)tex.TagData.Format,
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None
            };
            var texture = new SharpDX.Direct3D11.Texture3D(context.Device, desc);
            texture.DebugName = $"Texture{tex.GetDimension().GetEnumDescription()} {tex.Hash}";

            Tiger.Schema.Texture.ComputePitch((DXGI_FORMAT)tex.TagData.Format,
                tex.Width,
                tex.Height,
                out long rowPitch, out long slicePitch,
                DirectXTexUtility.CPFLAGS.NONE);

            Utilities.Pin(pixelData, ptr =>
            {
                var dataBox = new DataBox(ptr, (int)rowPitch, (int)slicePitch);
                context.UpdateSubresource(dataBox, texture, 0);
            });

            pixelData = null;
            return new ShaderResourceView(context.Device, texture);
        }
        else if (tex.GetDimension() == Tiger.Schema.TextureDimension.CUBE && tex.Depth == 6)
        {
            int mipCount = tex.TagData.MipCount;
            var desc = new Texture2DDescription
            {
                Width = tex.Width,
                Height = tex.Height,
                MipLevels = mipCount,
                ArraySize = tex.Depth,
                Format = (Format)tex.TagData.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.TextureCube
            };

            var texture = new Texture2D(context.Device, desc);
            texture.DebugName = $"Texture{tex.GetDimension().GetEnumDescription()} {tex.Hash}";

            int offset = 0;
            Utilities.Pin(pixelData, basePtr =>
            {
                for (int mip = 0; mip < mipCount; mip++)
                {
                    for (int slice = 0; slice < tex.Depth; slice++)
                    {
                        int width = Math.Max(1, tex.Width >> mip);
                        int height = Math.Max(1, tex.Height >> mip);

                        Tiger.Schema.Texture.ComputePitch(
                            (DXGI_FORMAT)tex.TagData.Format,
                            width,
                            height,
                            out long rowPitch,
                            out long slicePitch,
                            DirectXTexUtility.CPFLAGS.NONE
                        );

                        IntPtr ptr = basePtr + offset;
                        var dataBox = new DataBox(ptr, (int)rowPitch, 0);
                        int subresource = mip + slice * mipCount;

                        context.UpdateSubresource(dataBox, texture, subresource);
                        offset += (int)slicePitch;
                    }
                }
            });

            var srvDesc = new ShaderResourceViewDescription
            {
                Format = desc.Format,
                Dimension = ShaderResourceViewDimension.TextureCube,
                TextureCube = new ShaderResourceViewDescription.TextureCubeResource
                {
                    MipLevels = 1, //mipCount
                    MostDetailedMip = 0
                }
            };

            //pixelData = null;
            return new ShaderResourceView(context.Device, texture, srvDesc);
        }
        else
        {
            var desc = new Texture2DDescription
            {
                Width = tex.Width,
                Height = tex.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = (Format)tex.TagData.Format,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None
            };

            Tiger.Schema.Texture.ComputePitch((DXGI_FORMAT)desc.Format,
                desc.Width,
                desc.Height,
                out long rowPitch, out long slicePitch,
                DirectXTexUtility.CPFLAGS.NONE);

            var texture = new SharpDX.Direct3D11.Texture2D(context.Device, desc);
            texture.DebugName = $"Texture{tex.GetDimension().GetEnumDescription()} {tex.Hash}";
            Utilities.Pin(pixelData, ptr =>
            {
                var dataBox = new DataBox(ptr, (int)rowPitch, 0);
                context.UpdateSubresource(dataBox, texture, 0);
            });

            pixelData = null;
            return new ShaderResourceView(context.Device, texture);
        }
    }

    public List<SamplerState> CreateSamplers(DeviceContext context, SMaterialShader stage)
    {
        List<SamplerState> samplers = new();
        foreach (var sampler in stage.EnumerateSamplers())
        {
            if (sampler.Hash.GetFileMetadata().Type != 34)
                continue;

            samplers.Add(CreateSampler(context, sampler.Sampler));
        }

        return samplers;
    }

    public List<SamplerState> CreateSamplers(DeviceContext context, List<DirectXSampler> samplersStucts)
    {
        List<SamplerState> samplers = new();
        foreach (var sampler in samplersStucts)
        {
            if (sampler.Hash.GetFileMetadata().Type != 34)
                continue;

            samplers.Add(CreateSampler(context, sampler.Sampler));
        }
        return samplers;
    }

    public SamplerState CreateSampler(DeviceContext context, D3D11_SAMPLER_DESC sampler)
    {
        return new SharpDX.Direct3D11.SamplerState(context.Device,
        new SamplerStateDescription
        {
            Filter = sampler.Filter,
            AddressU = sampler.AddressU,
            AddressV = sampler.AddressV,
            AddressW = sampler.AddressW,

            MinimumLod = sampler.MinLOD,
            MaximumLod = sampler.MaxLOD,
            MipLodBias = sampler.MipLODBias,
            MaximumAnisotropy = (int)sampler.MaxAnisotropy,
            ComparisonFunction = sampler.ComparisonFunc,
            BorderColor = new(sampler.BorderColor[0], sampler.BorderColor[1], sampler.BorderColor[2], sampler.BorderColor[3]),
        });
    }

    public ShaderResourceView CreateFromPlate(DeviceContext context, TexturePlate plate)
    {
        using TigerReader reader = plate.GetReader();
        var hashes = plate.TagData.PlateTransforms.Enumerate(reader).Select(x => x.Texture.Hash.Hash32).ToArray();
        if (hashes.Length == 0)
            return null;

        uint outHash = Helpers.HashCombine(hashes);

        if (!_cache.TryGetValue(outHash, out var srv))
        {
            srv = CreateFromScratchImage(context, plate.MakePlatedTexture());
            if (srv is not null)
                srv.DebugName = $"Gear Plate {plate.Hash}";

            _cache[outHash] = srv;
        }
        return srv;
    }

    // Temp? Used for Investment
    public ShaderResourceView CreateFromScratchImage(DeviceContext context, ScratchImage scratch)
    {
        if (scratch is null)
            return null;

        var meta = scratch.GetMetadata();
        var desc = new Texture2DDescription
        {
            Width = meta.Width,
            Height = meta.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = (Format)meta.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None
        };

        int arraySize = (int)meta.ArraySize;
        int mipCount = (int)meta.MipLevels;
        var data = new DataBox[arraySize * mipCount];

        int index = 0;
        for (int item = 0; item < arraySize; item++)
        {
            for (int mip = 0; mip < mipCount; mip++)
            {
                var img = scratch.GetImage(mip, item, 0);

                data[index] = new DataBox(
                    img.Pixels,
                    (int)img.RowPitch,
                    (int)img.SlicePitch
                );

                index++;
            }
        }

        var texture = new Texture2D(context.Device, desc, data);
        var srv = new ShaderResourceView(context.Device, texture);
        scratch?.Dispose();
        return srv;
    }

    public void DisposeTextures()
    {
        foreach (var srv in _cache.Values)
        {
            srv?.Dispose();
        }
        _cache.Clear();
    }

    public void DisposeGlobalTextures()
    {
        foreach (var srv in _globalCache.Values)
        {
            srv?.Dispose();
        }
        _globalCache.Clear();
    }

    public void Dispose()
    {
        DisposeTextures();
        DisposeGlobalTextures();

        WhiteTexture?.Dispose();
        BlackTexture?.Dispose();
        EntityOverrideVS_VC?.Dispose();
        EntityOverrideVS_NoVC?.Dispose();
        InvestmentOverrideVS_NoVC?.Dispose();
        InvestmentOverrideVS_VC?.Dispose();

        WhiteTexture = null;
        BlackTexture = null;
        EntityOverrideVS_VC = null;
        EntityOverrideVS_NoVC = null;
        InvestmentOverrideVS_NoVC = null;
        InvestmentOverrideVS_VC = null;
        _instance = null;
    }
}

