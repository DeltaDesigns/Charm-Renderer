using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Tiger;
using Device = SharpDX.Direct3D11.Device;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public partial class CharmRenderer
{
	public class GBuffer : IDisposable
	{
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

		public DepthTarget Depth { get; private set; }
		public DepthTarget Depth_Clone { get; private set; }

		public RenderTarget2D SkyGenerateFar { get; private set; }
		public RenderTarget2D SkyGenerateNear { get; private set; }
		public RenderTarget2D FullHemisphereSkyColor { get; private set; }
		public RenderTarget2D DepthAngleDensityLookup { get; private set; }

		public RenderTarget2D ColorGradingLUT { get; private set; }
		public UAVTarget3D LUTVolume { get; private set; }

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

			Shading = new RenderTarget2D(device, width, height, Format.R11G11B10_Float, debugName: "Staging");
			Shading_Clone = new RenderTarget2D(device, width, height, Format.R11G11B10_Float, debugName: "Staging Clone");

			PostProcessResult = new RenderTarget2D(device, width, height, Format.R16G16B16A16_Float, debugName: "Post Process Result");

			Depth = new DepthTarget(device, width, height, Format.R24G8_Typeless, debugName: "RT Depth");
			Depth_Clone = new DepthTarget(device, width, height, Format.R24G8_Typeless, debugName: "RT Depth Clone");

			SkyGenerateFar = new RenderTarget2D(device, width / 4, height / 4, Format.R16G16B16A16_Float, debugName: "Sky Generate Far");
			SkyGenerateNear = new RenderTarget2D(device, width / 4, height / 4, Format.R16G16B16A16_Float, debugName: "Sky Generate Near");
			FullHemisphereSkyColor = new RenderTarget2D(device, 512, 512, Format.R16G16B16A16_Float, debugName: "Full Hemisphere Sky Color Generate");
			DepthAngleDensityLookup = new RenderTarget2D(device, 512, 512, Format.R16G16B16A16_Float, debugName: "Depth Angle Density Lookup");

			ColorGradingLUT = new RenderTarget2D(device, 1024, 32, Format.R16G16B16A16_Float, debugName: "Color Grading LUT");
			LUTVolume = new UAVTarget3D(device, 32, 32, 32, Format.R11G11B10_Float, "LUT Volume");
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

			Depth?.Dispose();
			Depth = null;
			Depth_Clone?.Dispose();
			Depth_Clone = null;

			SkyGenerateFar?.Dispose();
			SkyGenerateFar = null;
			SkyGenerateNear?.Dispose();
			SkyGenerateNear = null;
			FullHemisphereSkyColor?.Dispose();
			FullHemisphereSkyColor = null;
			DepthAngleDensityLookup?.Dispose();
			DepthAngleDensityLookup = null;
			LUTVolume?.Dispose();
			LUTVolume = null;
		}
	}

	public class RenderTarget2D : IDisposable
	{
		public Texture2D Texture { get; private set; }
		public RenderTargetView RTV { get; private set; }
		public ShaderResourceView SRV { get; private set; }

		public int Width { get; }
		public int Height { get; }

		public RenderTarget2D(
			Device device,
			int width,
			int height,
			Format format = Format.R8G8B8A8_UNorm,
			ResourceOptionFlags resourceOptionFlags = ResourceOptionFlags.None,
			string debugName = "")
		{
			Width = width;
			Height = height;

			// Texture description
			var texDesc = new Texture2DDescription
			{
				Width = width,
				Height = height,
				MipLevels = 1,
				ArraySize = 1,
				Format = format,
				SampleDescription = new SampleDescription(1, 0),
				Usage = ResourceUsage.Default,
				BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
				CpuAccessFlags = CpuAccessFlags.None,
				OptionFlags = resourceOptionFlags
			};

			Texture = new Texture2D(device, texDesc);
			if (debugName != string.Empty)
				Texture.DebugName = debugName;

			RTV = new RenderTargetView(device, Texture);
			SRV = new ShaderResourceView(device, Texture);
			SRV.DebugName = $"{Texture.DebugName} SRV";
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

		public void CopyTo(DeviceContext context, RenderTarget2D target)
		{
			context.CopyResource(this.Texture, target.Texture);
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

			SRV = null;
			RTV = null;
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

		public void Set(DeviceContext context, bool clearDepth = true, bool clearStencil = true)
		{
			context.OutputMerger.SetRenderTargets(DSV);
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

[StructLayout(LayoutKind.Sequential)]
public struct Matrix4x4ButGood
{
	public Vector4 X;
	public Vector4 Y;
	public Vector4 Z;
	public Vector4 W;

	public Matrix4x4ButGood(Vector4 x, Vector4 y, Vector4 z, Vector4 w)
	{
		X = x;
		Y = y;
		Z = z;
		W = w;
	}

	public Matrix4x4ButGood WithW(Vector4 w)
	{
		return new Matrix4x4ButGood(X, Y, Z, w);
	}

	public Matrix4x4ButGood Transpose()
	{
		return Matrix4x4.Transpose(this);
	}

	public static Matrix4x4ButGood Identity => new Matrix4x4ButGood
	{
		X = new Vector4(1, 0, 0, 0),
		Y = new Vector4(0, 1, 0, 0),
		Z = new Vector4(0, 0, 1, 0),
		W = new Vector4(0, 0, 0, 1)
	};

	public static Matrix4x4ButGood Zero => new Matrix4x4ButGood
	{
		X = new Vector4(0, 0, 0, 0),
		Y = new Vector4(0, 0, 0, 0),
		Z = new Vector4(0, 0, 0, 0),
		W = new Vector4(0, 0, 0, 0)
	};

	public static Matrix4x4ButGood LookTo(in Vector3 eye, in Vector3 dir, in Vector3 up)
	{
		var f = Vector3.Normalize(dir);
		var s = Vector3.Normalize(Vector3.Cross(f, up));
		var u = Vector3.Cross(s, f);

		return new Matrix4x4ButGood
		{
			X = new Vector4(s.X, u.X, -f.X, 0.0f),
			Y = new Vector4(s.Y, u.Y, -f.Y, 0.0f),
			Z = new Vector4(s.Z, u.Z, -f.Z, 0.0f),
			W = new Vector4(-Vector3.Dot(eye, s), -Vector3.Dot(eye, u), Vector3.Dot(eye, f), 1.0f)
		};
	}

	public static Matrix4x4ButGood LookAt(in Vector3 eye, in Vector3 center, in Vector3 up)
	{
		return LookTo(eye, center - eye, up);
	}

	public static Matrix4x4ButGood PerspectiveInfiniteReverseRightHanded(float fov, float aspect, float zNear)
	{
		float f = 1.0f / MathF.Tan(fov / 2.0f);
		// Perspective infinite reverse rh projection matrix
		return new Matrix4x4ButGood
		{
			X = new Vector4(f / aspect, 0.0f, 0.0f, 0.0f),
			Y = new Vector4(0.0f, f, 0.0f, 0.0f),
			Z = new Vector4(0.0f, 0.0f, 0.0f, -1.0f),
			W = new Vector4(0.0f, 0.0f, zNear, 0.0f),
		};
	}

	public Matrix4x4ButGood Invert()
	{
		System.Numerics.Matrix4x4.Invert(this, out System.Numerics.Matrix4x4 result);
		return result;
	}

	public static Matrix4x4ButGood operator *(Matrix4x4ButGood left, Matrix4x4ButGood right)
	{
		return new Matrix4x4ButGood
		{
			X = left.X * right.X.X + left.Y * right.X.Y + left.Z * right.X.Z + left.W * right.X.W,
			Y = left.X * right.Y.X + left.Y * right.Y.Y + left.Z * right.Y.Z + left.W * right.Y.W,
			Z = left.X * right.Z.X + left.Y * right.Z.Y + left.Z * right.Z.Z + left.W * right.Z.W,
			W = left.X * right.W.X + left.Y * right.W.Y + left.Z * right.W.Z + left.W * right.W.W
		};
	}

	public static Matrix4x4ButGood operator *(Matrix4x4ButGood left, float right)
	{
		return new Matrix4x4ButGood
		{
			X = left.X * right,
			Y = left.Y * right,
			Z = left.Z * right,
			W = left.W * right
		};
	}

	public static Matrix4x4ButGood operator /(Matrix4x4ButGood left, float right)
	{
		return new Matrix4x4ButGood
		{
			X = left.X / right,
			Y = left.Y / right,
			Z = left.Z / right,
			W = left.W / right
		};
	}

	public static implicit operator Matrix4x4(Matrix4x4ButGood m)
	{
		return Unsafe.As<Matrix4x4ButGood, Matrix4x4>(ref m);
	}

	public static implicit operator Matrix4x4ButGood(Matrix4x4 m)
	{
		return Unsafe.As<Matrix4x4, Matrix4x4ButGood>(ref m);
	}
}

[Flags]
public enum FeatureRendererSubscription : uint
{
	None = 0,

	StaticObjects = 1u << TfxFeatureRenderer.StaticObjects,
	DynamicObjects = 1u << TfxFeatureRenderer.DynamicObjects,
	ExampleEntity = 1u << TfxFeatureRenderer.ExampleEntity,
	SkinnedObject = 1u << TfxFeatureRenderer.SkinnedObject,
	Gear = 1u << TfxFeatureRenderer.Gear,
	RigidObject = 1u << TfxFeatureRenderer.RigidObject,
	Cloth = 1u << TfxFeatureRenderer.Cloth,
	ChunkedInstanceObjects = 1u << TfxFeatureRenderer.ChunkedInstanceObjects,
	SoftDeformable = 1u << TfxFeatureRenderer.SoftDeformable,
	TerrainPatch = 1u << TfxFeatureRenderer.TerrainPatch,
	SpeedtreeTrees = 1u << TfxFeatureRenderer.SpeedtreeTrees,
	EditorTerrainTile = 1u << TfxFeatureRenderer.EditorTerrainTile,
	EditorMesh = 1u << TfxFeatureRenderer.EditorMesh,
	BatchedEditorMesh = 1u << TfxFeatureRenderer.BatchedEditorMesh,
	EditorDecal = 1u << TfxFeatureRenderer.EditorDecal,
	Particles = 1u << TfxFeatureRenderer.Particles,
	ChunkedLights = 1u << TfxFeatureRenderer.ChunkedLights,
	DeferredLights = 1u << TfxFeatureRenderer.DeferredLights,
	SkyTransparent = 1u << TfxFeatureRenderer.SkyTransparent,
	Widget = 1u << TfxFeatureRenderer.Widget,
	Decals = 1u << TfxFeatureRenderer.Decals,
	DynamicDecals = 1u << TfxFeatureRenderer.DynamicDecals,
	RoadDecals = 1u << TfxFeatureRenderer.RoadDecals,
	Water = 1u << TfxFeatureRenderer.Water,
	LensFlares = 1u << TfxFeatureRenderer.LensFlares,
	Volumetrics = 1u << TfxFeatureRenderer.Volumetrics,
	Cubemaps = 1u << TfxFeatureRenderer.Cubemaps,

	All = uint.MaxValue
}

public static class FeatureRendererSubscriptionExtensions
{
	public static FeatureRendererSubscription AllBut(
		TfxFeatureRenderer feature)
	{
		var bit = (FeatureRendererSubscription)(1u << (int)feature);
		return FeatureRendererSubscription.All & ~bit;
	}

	public static bool IsSubscribed(
		this FeatureRendererSubscription subscription,
		TfxFeatureRenderer feature)
	{
		var bit = (FeatureRendererSubscription)(1u << (int)feature);
		return (subscription & bit) != 0;
	}
}

public enum ShaderStage
{
	Pixel = 1,
	Vertex = 2,
	Geometry = 3,
	Hull = 4,
	Compute = 5,
	Domain = 6,
}

public static class ShaderStageExtensions
{
	public static ShaderStage? FromIndex(byte index)
	{
		return index switch
		{
			1 => ShaderStage.Pixel,
			2 => ShaderStage.Vertex,
			3 => ShaderStage.Geometry,
			4 => ShaderStage.Hull,
			5 => ShaderStage.Compute,
			6 => ShaderStage.Domain,
			_ => null
		};
	}
}
