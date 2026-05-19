using Arithmic;
using DirectXTex;
using DirectXTexNet;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Shaders;
using static Tiger.Schema.DirectXSampler;
using Texture = Tiger.Schema.Texture;

namespace Charm.Renderer;

public sealed class TextureAsset : IDisposable
{
	public uint Hash;
	public ShaderResourceView SRV;
	public int RefCount;

	public TextureAsset(uint hash, ShaderResourceView srv)
	{
		Hash = hash;
		SRV = srv;
	}

	public void AddRef()
	{
		RefCount++;
	}

	public bool Release()
	{
		RefCount--;
		return RefCount <= 0;
	}

	public void Dispose()
	{
		RefCount = 0;
		SRV?.Dispose();
		SRV = null;
	}
}

public class AssetManager : IDisposable
{
	public readonly Dictionary<uint, MaterialData> _materialCache = new();
	public readonly Dictionary<uint, TextureAsset> _cache = new(); // used for mesh
	public readonly Dictionary<uint, TextureAsset> _globalCache = new(); // used for pipelines/externs
	public ShaderResourceView WhiteTexture;
	public ShaderResourceView BlackTexture;
	public ShaderResourceView BlackTextureWAlpha;

	public VertexShader EntityOverrideVS_NoVC;
	public VertexShader EntityOverrideVS_VC;
	public VertexShader InvestmentOverrideVS_NoVC; // When o7 is SV_Position
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

	public static AssetManager Get()
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

		if (BlackTextureWAlpha is null)
		{
			var blackdata = new byte[] { 0, 0, 0, 255 };
			BlackTextureWAlpha = new ShaderResourceView(
				GPU.Instance.Device,
				SharpDX.Toolkit.Graphics.Texture2D.New(GPU.Instance.Device, 1, 1, Format.R8G8B8A8_UNorm, blackdata));
			BlackTextureWAlpha.DebugName = "Placeholder Black W Alpha";
		}

		var bytecode = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/entity_vs_override.hlsl", "VSMain", "vs_5_0");
		EntityOverrideVS_NoVC = new SharpDX.Direct3D11.VertexShader(GPU.Instance.Device, bytecode)
		{
			DebugName = "Entity Override Vertex Shader"
		};

		bytecode = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/entity_vs_override_vc.hlsl", "VSMain", "vs_5_0");
		EntityOverrideVS_VC = new SharpDX.Direct3D11.VertexShader(GPU.Instance.Device, bytecode)
		{
			DebugName = "Entity Override VC Vertex Shader"
		};

		bytecode = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/investment_vs_override.hlsl", "VSMain", "vs_5_0", ShaderFlags.Debug | ShaderFlags.SkipOptimization);
		InvestmentOverrideVS_NoVC = new SharpDX.Direct3D11.VertexShader(GPU.Instance.Device, bytecode)
		{
			DebugName = "Investment Override Vertex Shader"
		};

		bytecode = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/investment_vs_override_vc.hlsl", "VSMain", "vs_5_0", ShaderFlags.Debug | ShaderFlags.SkipOptimization);
		InvestmentOverrideVS_VC = new SharpDX.Direct3D11.VertexShader(GPU.Instance.Device, bytecode)
		{
			DebugName = "Investment Override VC Vertex Shader"
		};
	}

	public MaterialData GetOrCreateMaterial(Material material)
	{
		if (!_materialCache.TryGetValue(material.Hash.Hash32, out var mat))
		{
			mat = new MaterialData(GPU.Instance.ImmediateContext, material);
			_materialCache[material.Hash.Hash32] = mat;
		}

		mat.AddRef();
		return mat;
	}

	public void ReleaseMaterial(uint hash)
	{
		if (_materialCache.TryGetValue(hash, out var mat))
		{
			if (mat.Release())
			{
				mat.Dispose();
				_materialCache.Remove(hash);
			}
		}
	}

	public TextureAsset GetOrCreateTexture(Texture texture)
	{
		if (!_cache.TryGetValue(texture.Hash.Hash32, out var tex))
		{
			tex = new TextureAsset(texture.Hash.Hash32, CreateTexture(GPU.Instance.ImmediateContext, texture));
			_cache[texture.Hash.Hash32] = tex;
		}

		tex.AddRef();
		return tex;
	}

	public TextureAsset GetOrCreateGlobalTexture(Texture texture)
	{
		if (!_globalCache.TryGetValue(texture.Hash.Hash32, out var tex))
		{
			tex = new TextureAsset(texture.Hash.Hash32, CreateTexture(GPU.Instance.ImmediateContext, texture));
			_globalCache[texture.Hash.Hash32] = tex;
		}

		tex.AddRef();
		return tex;
	}

	public void ReleaseTexture(uint hash)
	{
		if (_cache.TryGetValue(hash, out var tex))
		{
			if (tex.Release())
			{
				tex.Dispose();
				_cache.Remove(hash);
			}
		}
	}

	public void ReleaseTexture(TextureAsset tex)
	{
		if (tex is null)
			return;

		ReleaseTexture(tex.Hash);
	}

	public Dictionary<uint, TextureAsset> CreateTextures(SMaterialShader stage)
	{
		Dictionary<uint, TextureAsset> textures = new();

		foreach (var tex in stage.EnumerateTextures())
		{
			if (tex.Texture is null)
				continue;

			textures.TryAdd(tex.TextureIndex, GetOrCreateTexture(tex.Texture));
		}

		return textures;
	}

	public Dictionary<uint, TextureAsset> CreateTextures(List<STextureTag> tags)
	{
		Dictionary<uint, TextureAsset> textures = new();

		foreach (var tex in tags)
		{
			if (tex.Texture is null)
				continue;

			textures.TryAdd(tex.TextureIndex, GetOrCreateTexture(tex.Texture));
		}

		return textures;
	}

	public ShaderResourceView CreateTexture(DeviceContext context, Tiger.Schema.Texture tex)
	{
		if (tex.Hash.CheckRedacted())
		{
			Log.Warning($"Texture {tex.Hash} is Redacted. Can not load.");
			return null;
		}

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
					MipLevels = mipCount,
					MostDetailedMip = 0
				}
			};

			//pixelData = null;
			return new ShaderResourceView(context.Device, texture, srvDesc);
		}
		else
		{
			int mipCount = tex.TagData.LargeTextureBuffer != null ? tex.TagData.MipCount : 1;

			var desc = new Texture2DDescription
			{
				Width = tex.Width,
				Height = tex.Height,
				MipLevels = mipCount,
				ArraySize = 1,
				Format = (Format)tex.TagData.Format,
				SampleDescription = new SampleDescription(1, 0),
				Usage = ResourceUsage.Default,
				BindFlags = BindFlags.ShaderResource,
				CpuAccessFlags = CpuAccessFlags.None,
				OptionFlags = ResourceOptionFlags.None
			};

			var texture = new SharpDX.Direct3D11.Texture2D(context.Device, desc);
			texture.DebugName = $"Texture{tex.GetDimension().GetEnumDescription()} {tex.Hash}";

			int offset = 0;
			Utilities.Pin(pixelData, basePtr =>
			{
				for (int mip = 0; mip < mipCount; mip++)
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
					int subresource = mip;

					context.UpdateSubresource(dataBox, texture, subresource);
					offset += (int)slicePitch;
				}
			});

			pixelData = null;
			return new ShaderResourceView(context.Device, texture);
		}
	}

	public List<SamplerState> CreateSamplers(SMaterialShader stage)
	{
		List<SamplerState> samplers = new();
		foreach (var sampler in stage.EnumerateSamplers())
		{
			if (sampler.Hash.GetFileMetadata().Type != 34)
				continue;

			samplers.Add(CreateSampler(GPU.Instance.ImmediateContext, sampler.Sampler));
		}

		return samplers;
	}

	public List<SamplerState> CreateSamplers(List<DirectXSampler> samplersStucts)
	{
		List<SamplerState> samplers = new();
		foreach (var sampler in samplersStucts)
		{
			if (sampler.Hash.GetFileMetadata().Type != 34)
				continue;

			samplers.Add(CreateSampler(GPU.Instance.ImmediateContext, sampler.Sampler));
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

	public TextureAsset CreateFromPlate(TexturePlate plate)
	{
		if (plate is null)
			return null;

		using TigerReader reader = plate.GetReader();
		var hashes = plate.TagData.PlateTransforms.Enumerate(reader).Select(x => x.Texture.Hash.Hash32).ToArray();
		if (hashes.Length == 0)
			return null;

		uint outHash = Helpers.HashCombine(hashes);

		if (!_cache.TryGetValue(outHash, out var tex))
		{
			tex = new(outHash, CreateFromScratchImage(GPU.Instance.ImmediateContext, plate.MakePlatedTexture()));
			if (tex.SRV is not null)
				tex.SRV.DebugName = $"Gear Plate {plate.Hash}";

			_cache[outHash] = tex;
		}
		tex.RefCount++;
		return tex;
	}

	// Temp? Used for Investment
	public ShaderResourceView CreateFromScratchImage(DeviceContext context, ScratchImage scratch)
	{
		if (scratch is null)
			return null;

		// turns out you can get a srv directly from ScratchImage, generating the mips takes up a chunk of time tho
		//scratch = scratch.GenerateMipMaps(TEX_FILTER_FLAGS.SEPARATE_ALPHA, 3);
		//var a = scratch.CreateShaderResourceView(context.Device.NativePointer);
		//var b = new ShaderResourceView(a);
		//return b;

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

	/// <summary>
	/// Disposes all cached textures, regardless of reference count.
	/// Should be used only when cleaning up the AssetManager.
	/// </summary>
	public void DisposeTextures()
	{
		Log.Debug($"{_cache.Count} Textures still registered.");
		foreach (var srv in _cache.Values)
		{
			srv?.Dispose();
		}
		_cache.Clear();
	}

	/// <summary>
	/// Disposes all global cached textures, regardless of reference count.
	/// Should be used only when cleaning up the AssetManager.
	/// </summary>
	public void DisposeGlobalTextures()
	{
		Log.Debug($"{_globalCache.Count} Global Textures still registered.");
		foreach (var srv in _globalCache.Values)
		{
			srv?.Dispose();
		}
		_globalCache.Clear();
	}

	public void DisposeMaterials()
	{
		Log.Debug($"{_materialCache.Count} Materials still registered.");
		foreach (var mat in _materialCache.Values)
		{
			mat?.Dispose();
		}
		_materialCache.Clear();
	}

	public void Dispose()
	{
		DisposeMaterials();
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

