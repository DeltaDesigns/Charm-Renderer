using SharpDX.Direct3D11;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using Tiger;
using Tiger.Schema;
using static Charm.Renderer.CharmRenderer;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public class Externs : IDisposable
{
	private static readonly ConcurrentDictionary<Type, Dictionary<int, Func<object, object>>> _fieldMaps =
		   new ConcurrentDictionary<Type, Dictionary<int, Func<object, object>>>();

	public Externs()
	{
		Frame = new();
		View = new();
		Transparent = new();
		Deferred = new();
		Atmosphere = new();
		Decal = new();
		ShadowMask = new();
		PostProcess = new();
		ScreenArea = new();
		Fxaa = new();
		GlobalLighting = new();
	}

	public ExternFrame Frame;
	public ExternView View;
	public ExternTransparent Transparent;
	public ExternDeferred Deferred;
	public ExternAtmosphere Atmosphere;
	public ExternDecal Decal;
	public ExternShadowMask ShadowMask;
	public ExternPostProcess PostProcess;
	public ExternScreenArea ScreenArea;
	public ExternFxaa Fxaa;
	public ExternGlobalLighting GlobalLighting;

	public class ExternFrame : IDisposable
	{
		[ExternField(0x00)] public float GameTime { get; set; }
		[ExternField(0x04)] public float RenderTime { get; set; }
		[ExternField(0x0C)] public float Unk0C { get; set; }
		[ExternField(0x10)] public float Unk10 { get; set; } = 0.5f;
		[ExternField(0x14)] public float DeltaTime { get; set; }
		[ExternField(0x18)] public float ExposureTime { get; set; } = 0.016666668f;
		[ExternField(0x1C)] public float ExposureScale { get; set; } = 0.8f;
		[ExternField(0x20)] public float Unk20 { get; set; } = 1f;
		[ExternField(0x28)] public float ExposureIllumRelative { get; set; } = 1f;
		[ExternField(0xC0)] public ShaderResourceView IridesenceLookup { get; set; }

		[ExternField(0x1B0)] public Vector4 Unk1B0 { get; set; } = new(0f, 0f, 0f, 1f);
		[ExternField(0x1C0)] public Vector4 Unk1C0 { get; set; } = new(1f, 1f, 0f, 1f);

		public ExternFrame()
		{
			var tex = Globals.Get().RenderGlobals.TagData.Textures.TagData.IridescenceLookup;
			IridesenceLookup = AssetManager.GetInstance().GetOrCreateGlobalTexture(GPU.Instance.Context, tex);
		}

		public void Update(CharmRenderer renderer)
		{
			RenderHelpers.Profile("Extern Frame Update");
			GameTime = renderer.Time;
			RenderTime = renderer.Time;
			DeltaTime = renderer.DeltaTime;
			RenderHelpers.EndProfile();
		}

		public void Dispose()
		{
			IridesenceLookup?.Dispose();
			IridesenceLookup = null;
		}
	}

	public class ExternView : IDisposable
	{
		private Matrix4x4ButGood UNormToSNorm = new()
		{
			X = new(2.0f, 0.0f, 0.0f, 0.0f),
			Y = new(0.0f, -2.0f, 0.0f, 0.0f),
			Z = new(0.0f, 0.0f, 1.0f, 0.0f),
			W = new(-1.0f, 1.0f, 0.0f, 1.0f)
		};

		[ExternField(0x0)] public float ResolutionX { get; set; }
		[ExternField(0x4)] public float ResolutionY { get; set; }
		[ExternField(0x10)] public Vector4 ViewMisc { get; set; } = Vector4.UnitY;
		[ExternField(0x20)] public Vector4 Position { get; set; }
		[ExternField(0x30)] public Vector4 Unk30 { get; set; }
		[ExternField(0x40)] public Matrix4x4ButGood WorldToCamera { get; set; }
		[ExternField(0x80)] public Matrix4x4ButGood CameraToProj { get; set; }
		[ExternField(0xC0)] public Matrix4x4ButGood CameraToWorld { get; set; }
		[ExternField(0x100)] public Matrix4x4ButGood ProjToCamera { get; set; }
		[ExternField(0x140)] public Matrix4x4ButGood WorldToProj { get; set; }
		[ExternField(0x180)] public Matrix4x4ButGood ProjToWorld { get; set; }
		[ExternField(0x1C0)] public Matrix4x4ButGood TargetPixelToWorld { get; set; }
		[ExternField(0x200)] public Matrix4x4ButGood TargetPixelToCamera { get; set; }
		[ExternField(0x240)] public Matrix4x4ButGood Unk240 { get; set; }
		[ExternField(0x280)] public Matrix4x4ButGood TpToWw_No_Proj_W { get; set; }
		[ExternField(0x2C0)] public Matrix4x4ButGood Unk2C0 { get; set; }

		public void Update(CharmRenderer renderer)
		{
			RenderHelpers.Profile("Extern View Update");
			var cam = renderer.Camera;
			ResolutionX = cam.Viewport.X;
			ResolutionY = cam.Viewport.Y;
			Position = new Vector4(cam.Position, 1f);

			WorldToCamera = cam.WorldToCamera;
			CameraToWorld = WorldToCamera.Invert();

			CameraToProj = cam.CameraToProjective;
			ProjToCamera = CameraToProj.Invert();

			WorldToProj = CameraToProj * WorldToCamera;
			ProjToWorld = WorldToProj.Invert();

			TargetPixelToCamera = ProjToCamera * cam.TargetPixelToProjective();
			TargetPixelToWorld = CameraToWorld * TargetPixelToCamera;

			Matrix4x4ButGood ptow_no_proj_w = CameraToWorld.WithW(Vector4.UnitW) * ProjToCamera;
			TpToWw_No_Proj_W = ptow_no_proj_w * cam.TargetPixelToProjective();

			Unk240 = ProjToWorld * UNormToSNorm;
			Unk2C0 = ptow_no_proj_w * UNormToSNorm;
			Unk30 = Vector4.UnitZ * WorldToProj.W;
			RenderHelpers.EndProfile();
		}

		public void Dispose()
		{

		}
	}

	public class ExternTransparent : IDisposable
	{
		[ExternField(0x00)] public ShaderResourceView AtmosFarLookup { get; set; }
		[ExternField(0x08)] public ShaderResourceView AtmosFarLookupDS { get; set; }
		[ExternField(0x10)] public ShaderResourceView AtmosNearLookup { get; set; }
		[ExternField(0x18)] public ShaderResourceView AtmosNearLookupDS { get; set; }
		[ExternField(0x20)] public ShaderResourceView AtmosDepthAngleDensity { get; set; }

		[ExternField(0x48)] public ShaderResourceView Unk48 { get; set; }
		[ExternField(0x50)] public ShaderResourceView Unk50 { get; set; }
		[ExternField(0x60)] public ShaderResourceView ShadingResult { get; set; }
		[ExternField(0x70)] public Vector4 Unk70 { get; set; } = new(1.15643f, 0.00f, 0.70f, 44.00f);
		[ExternField(0x80)] public Vector4 Unk80 { get; set; } = new(0.00f, 0.00f, -0.00938f, 0.05583f);
		[ExternField(0x90)] public Vector4 Unk90 { get; set; } = new(0.00f, 0.00f, -0.01315f, 0.10422f);
		[ExternField(0xA0)] public Vector4 UnkA0 { get; set; } = new(0.00f, 0.00f, -0.00815f, 0.16667f);
		[ExternField(0xB0)] public Vector4 UnkB0 { get; set; } = Vector4.Zero;

		public ExternTransparent()
		{
			Unk48 = AssetManager.GetInstance().BlackTexture;
			Unk50 = AssetManager.GetInstance().BlackTexture;
		}

		public void Dispose()
		{
			AtmosFarLookup?.Dispose();
			AtmosFarLookup = null;
			AtmosNearLookup?.Dispose();
			AtmosNearLookup = null;
			AtmosFarLookupDS?.Dispose();
			AtmosFarLookupDS = null;
			AtmosNearLookupDS?.Dispose();
			AtmosNearLookupDS = null;
			AtmosDepthAngleDensity?.Dispose();
			AtmosDepthAngleDensity = null;
			ShadingResult?.Dispose();
			ShadingResult = null;
		}
	}

	public class ExternDeferred : IDisposable
	{
		[ExternField(0x00)] public Vector4 DepthConstants { get; set; } = new(0.0f, 1f / 0.01f, 0.0f, 0.0f);
		[ExternField(0x78)] public ShaderResourceView DeferredDepth { get; set; }
		[ExternField(0x88)] public ShaderResourceView DeferredRT0 { get; set; }
		[ExternField(0x90)] public ShaderResourceView DeferredRT1 { get; set; }
		[ExternField(0x98)] public ShaderResourceView DeferredRT2 { get; set; }
		[ExternField(0xA0)] public ShaderResourceView LightDiffuse { get; set; }
		[ExternField(0xA8)] public ShaderResourceView LightSpecular { get; set; }
		[ExternField(0xB0)] public ShaderResourceView LightIBL { get; set; }

		[ExternField(0xD8)] public ShaderResourceView SkyHemisphereMips { get; set; }

		public void Update(DeviceContext context, GBuffer gbuffer)
		{
			RenderHelpers.Profile("Extern Deferred Update");
			gbuffer.Depth.CopyTo(context, gbuffer.Depth_Clone);
			DeferredDepth = gbuffer.Depth_Clone.DepthSRV;

			DeferredRT0 = gbuffer.RT0.SRV;

			gbuffer.RT1.CopyTo(context, gbuffer.RT1_Clone);
			DeferredRT1 = gbuffer.RT1_Clone.SRV;

			DeferredRT2 = gbuffer.RT2.SRV;
			RenderHelpers.EndProfile();
		}

		public void Dispose()
		{
			DeferredDepth?.Dispose();
			DeferredDepth = null;
			DeferredRT0?.Dispose();
			DeferredRT0 = null;
			DeferredRT1?.Dispose();
			DeferredRT1 = null;
			DeferredRT2?.Dispose();
			DeferredRT2 = null;
			LightDiffuse?.Dispose();
			LightDiffuse = null;
			LightSpecular?.Dispose();
			LightSpecular = null;
			LightIBL?.Dispose();
			LightIBL = null;
			SkyHemisphereMips?.Dispose();
			SkyHemisphereMips = null;
		}
	}

	public class ExternAtmosphere : IDisposable
	{
		[ExternField(0x40)] public ShaderResourceView AtmosLookup0 { get; set; }
		[ExternField(0x58)] public ShaderResourceView AtmosLookup1 { get; set; }
		[ExternField(0x70)] public float AtmosTimeOfDay { get; set; } = 0.5f;
		[ExternField(0x74)] public float AtmosUnk74 { get; set; } = 0f;
		[ExternField(0x78)] public float AtmosUnk78 { get; set; } = 0f;
		[ExternField(0x80)] public ShaderResourceView AtmosLookup2 { get; set; }
		[ExternField(0x90)] public Vector4 RTDimensions { get; set; } = new(0);
		[ExternField(0xA0)] public ShaderResourceView UnkA0 { get; set; }
		[ExternField(0xC0)] public ShaderResourceView UnkC0 { get; set; }
		[ExternField(0xD0)] public Vector4 DepthAngleRTDimensions { get; set; } = new(512f, 512f, 1f / 512f, 1f / 512f);
		[ExternField(0xE0)] public ShaderResourceView AtmosFar { get; set; }
		[ExternField(0xF0)] public ShaderResourceView AtmosNear { get; set; }
		[ExternField(0x110)] public Vector4 AtmosSunDir { get; set; } = new(-0.30372f, -0.59835f, 0.74144f, 0.0f);
		[ExternField(0x140)] public Vector4 AtmosSunColor { get; set; } = new(1.0f, 0.95f, 0.85f, 1.0f);
		[ExternField(0x150)] public float AtmosUnk150 { get; set; } = -0.85f;
		[ExternField(0x154)] public float AtmosUnk154 { get; set; } = 1.329f;
		[ExternField(0x160)] public float AtmosFogIntensity { get; set; } = 0.9f;
		[ExternField(0x164)] public float AtmosUnk164 { get; set; } = 0.1f;
		[ExternField(0x168)] public float AtmosUnk168 { get; set; } = 12f;
		[ExternField(0x16C)] public float AtmosUnk16C { get; set; } = 20000f;
		[ExternField(0x170)] public float AtmosUnk170 { get; set; } = 0.03f;
		[ExternField(0x180)] public Vector4 AtmosUnk180 { get; set; } = Vector4.Zero;
		[ExternField(0x190)] public float AtmosUnk190 { get; set; } = 1f;
		[ExternField(0x194)] public float AtmosUnk194 { get; set; } = 0.109f;
		[ExternField(0x198)] public float AtmosUnk198 { get; set; } = 5.939f;
		[ExternField(0x1B4)] public float AtmosRotation { get; set; } = 0f;
		[ExternField(0x1B8)] public float AtmosIntensity { get; set; } = 1f;
		[ExternField(0x1BC)] public float AtmosUnk1BC { get; set; } = 0.33713f;
		[ExternField(0x1C0)] public float AtmosUnk1C0 { get; set; } = 0f;
		[ExternField(0x1C4)] public float AtmosUnk1C4 { get; set; } = 0f;
		[ExternField(0x1D0)] public Vector4 AtmosUnk1D0 { get; set; } = Vector4.Zero;
		[ExternField(0x1E0)] public float AtmosUnk1E0 { get; set; } = -0.8365f;
		[ExternField(0x1E4)] public float AtmosSunIntensity { get; set; } = 0.05923f;
		[ExternField(0x1E8)] public float AtmosUnk1E8 { get; set; } = 0f;
		[ExternField(0x1EC)] public float AtmosUnk1EC { get; set; } = 0f;
		[ExternField(0x1F8)] public float AtmosUnk1F8 { get; set; } = 0f;
		[ExternField(0x1FC)] public float AtmosUnk1FC { get; set; } = 0f;
		[ExternField(0x208)] public float AtmosUnk208 { get; set; } = 0f;
		[ExternField(0x210)] public Vector4 AtmosUnk210 { get; set; } = Vector4.Zero;

		public ExternAtmosphere()
		{
			UnkA0 = AssetManager.GetInstance().WhiteTexture;
			UnkC0 = AssetManager.GetInstance().WhiteTexture;
		}

		public void Update(CharmRenderer renderer)
		{
			RenderHelpers.Profile("Extern Atmosphere Update");

			var cam = renderer.Camera;
			RTDimensions = new Vector4(cam.Viewport.X, cam.Viewport.Y, 1f / cam.Viewport.X, 1f / cam.Viewport.Y);
			AtmosSunDir = renderer.World.GlobalChannels.Get("sun_track_direction");
			AtmosSunColor = renderer.World.GlobalChannels.Get("sun_glow_color");
			AtmosUnk150 = renderer.World.GlobalChannels.Get(new TigerHash(0x4aa1bef5)).X;
			AtmosUnk154 = renderer.World.GlobalChannels.Get(new TigerHash(0x9859daf1)).X;
			AtmosFogIntensity = renderer.World.GlobalChannels.Get(26).X;
			AtmosUnk164 = renderer.World.GlobalChannels.Get(15).X; // Fog density? Unsure
			AtmosUnk168 = renderer.World.GlobalChannels.Get(16).X;
			AtmosUnk16C = renderer.World.GlobalChannels.Get(17).X;
			AtmosUnk170 = renderer.World.GlobalChannels.Get(19).X; // fog_height_falloff
			AtmosUnk180 = renderer.World.GlobalChannels.Get(20); // fog_decay_color
			AtmosUnk190 = renderer.World.GlobalChannels.Get(21).X; // fog_decay_scale
			AtmosRotation = renderer.World.GlobalChannels.Get("sky_snapshot_rotation").X / 360f;
			AtmosIntensity = renderer.World.GlobalChannels.Get("sky_snapshot_intensity").X;
			AtmosUnk1BC = renderer.World.GlobalChannels.Get(36).X;
			AtmosUnk1C0 = renderer.World.GlobalChannels.Get(35).X;
			AtmosUnk1D0 = renderer.World.GlobalChannels.Get("sky_color_override"); // sky_color_override?
			AtmosUnk1E8 = renderer.World.GlobalChannels.Get(38).X;
			AtmosSunIntensity = renderer.World.GlobalChannels.Get("sun_glow_intensity").X;
			//SunDirTemp();
			RenderHelpers.EndProfile();
		}

		// Temp
		public void SunDirTemp()
		{
			float dayAngle = AtmosTimeOfDay * MathF.Tau;

			System.Numerics.Vector3 dir = new(
				0f,
				MathF.Sin(dayAngle),
				-MathF.Cos(dayAngle)
			);

			float rotX = AtmosRotation * MathF.Tau + 90;
			var tilt = Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, -rotX);

			dir = System.Numerics.Vector3.Transform(dir, tilt);

			AtmosSunDir = new(dir, 1f);
		}

		public void Dispose()
		{
			AtmosLookup0?.Dispose();
			AtmosLookup0 = null;
			AtmosLookup1?.Dispose();
			AtmosLookup1 = null;
			AtmosLookup2?.Dispose();
			AtmosLookup2 = null;
			UnkA0?.Dispose();
			UnkA0 = null;
			UnkC0?.Dispose();
			UnkC0 = null;
			AtmosFar?.Dispose();
			AtmosFar = null;
			AtmosNear?.Dispose();
			AtmosNear = null;
		}
	}

	public class ExternDecal : IDisposable
	{
		[ExternField(0x8)] public ShaderResourceView DeferredRT1 { get; set; }

		public void Update(DeviceContext context, GBuffer gbuffer)
		{
			RenderHelpers.Profile("Extern Decal Update");
			DeferredRT1 = gbuffer.RT1_Clone.SRV;
			RenderHelpers.EndProfile();
		}

		public void Dispose()
		{
			DeferredRT1?.Dispose();
			DeferredRT1 = null;
		}
	}

	public class ExternShadowMask : IDisposable
	{
		[ExternField(0x0)] public ShaderResourceView Unk00 { get; set; }
		[ExternField(0x8)] public ShaderResourceView Unk08 { get; set; }
		[ExternField(0x10)] public ShaderResourceView Unk10 { get; set; }
		[ExternField(0x20)] public Vector4 Unk20 { get; set; }
		[ExternField(0x30)] public float Unk30 { get; set; }
		[ExternField(0x34)] public float Unk34 { get; set; }

		public ExternShadowMask()
		{
			Unk00 = AssetManager.GetInstance().WhiteTexture;
			Unk08 = AssetManager.GetInstance().WhiteTexture;
			Unk10 = AssetManager.GetInstance().WhiteTexture;
		}

		public void Dispose()
		{
			Unk00?.Dispose();
			Unk00 = null;
			Unk08?.Dispose();
			Unk08 = null;
			Unk10?.Dispose();
			Unk10 = null;
		}
	}

	public class ExternPostProcess : IDisposable
	{
		[ExternField(0x0)] public ShaderResourceView Unk00 { get; set; }
		[ExternField(0xC0)] public Vector4 UnkC0 { get; set; } = new(0.92537f, 0.0f, 0.37906f, 0.37906f);
		[ExternField(0xD0)] public Vector4 UnkD0 { get; set; } = new(-0.22681f, 0.80123f, 0.5537f, 0.5537f);
		[ExternField(0xE0)] public Vector4 UnkE0 { get; set; } = new(-0.30372f, -0.59835f, 0.74144f, 0.74144f);

		public ExternPostProcess()
		{
		}

		public void Update(DeviceContext context, GBuffer gbuffer)
		{
			RenderHelpers.Profile("Extern PostProcess Update");
			gbuffer.Shading.CopyTo(context, gbuffer.Shading_Clone);
			Unk00 = gbuffer.Shading_Clone.SRV;
			RenderHelpers.EndProfile();
		}

		public void Dispose()
		{
			Unk00?.Dispose();
			Unk00 = null;
		}
	}

	public class ExternScreenArea : IDisposable
	{
		[ExternField(0x0)] public ShaderResourceView Unk00 { get; set; }
		[ExternField(0x08)] public ShaderResourceView Unk08 { get; set; }
		[ExternField(0x30)] public ShaderResourceView Unk30 { get; set; }
		[ExternField(0x38)] public ShaderResourceView Unk38 { get; set; }
		[ExternField(0x40)] public ShaderResourceView Unk40 { get; set; }
		[ExternField(0x50)] public ShaderResourceView Unk50 { get; set; }
		[ExternField(0x58)] public ShaderResourceView Unk58 { get; set; }
		[ExternField(0x6C)] public float Unk6C { get; set; } = 0.5f;
		[ExternField(0x7C)] public float Unk7C { get; set; } = 0.9968f;
		[ExternField(0x90)] public Vector4 LUTDimensions { get; set; } = new(32f, 1024f, 0, 0); // height x width
		[ExternField(0xA0)] public Vector4 UnkA0 { get; set; } = new(0.03125f, -5.00f, 14.00f, 2.50f);
		[ExternField(0xB0)] public float UnkB0 { get; set; } = 0.5f;
		[ExternField(0xB4)] public float UnkB4 { get; set; } = 2f;
		[ExternField(0xB8)] public float UnkB8 { get; set; } = 0f;
		[ExternField(0xC0)] public Vector4 UnkC0 { get; set; } = new(0f, 0.4f, -1f, -1f);
		[ExternField(0xD0)] public Vector4 UnkD0 { get; set; } = new(0.5f, 0f, 0f, 0f);
		[ExternField(0x100)] public Matrix4x4ButGood Unk100 { get; set; } = Matrix4x4ButGood.Identity / 2f;
		[ExternField(0x140)] public float Unk140 { get; set; } = 0.05f;
		[ExternField(0x150)] public Vector4 Unk150 { get; set; } = new(0.3f, 0.5f, 0f, 0.02f);
		[ExternField(0x160)] public Vector4 Unk160 { get; set; } = new(0.3f, 0.5f, 0f, 0.5f);
		[ExternField(0x170)] public Vector4 Unk170 { get; set; } = Vector4.One;

		public ExternScreenArea()
		{
			Unk40 = AssetManager.GetInstance().BlackTextureWAlpha;
			Unk50 = AssetManager.GetInstance().BlackTextureWAlpha;
			Unk58 = AssetManager.GetInstance().WhiteTexture;
		}

		public void Update(DeviceContext context, GBuffer gbuffer)
		{
			RenderHelpers.Profile("Extern ScreenArea Update");
			Unk00 = gbuffer.Shading_Clone.SRV;
			//UnkD0 = new(0.5f, 0f, 0f, 0f);
			//Unk150 = new(0.3f, 0.5f, 0f, 0.02f);
			//Unk160 = new(0.3f, 0.5f, 0f, 0.5f);
			RenderHelpers.EndProfile();
		}

		public void Dispose()
		{
			Unk00?.Dispose();
			Unk00 = null;
			Unk08?.Dispose();
			Unk08 = null;
			Unk30?.Dispose();
			Unk30 = null;
			Unk38?.Dispose();
			Unk38 = null;
			Unk40?.Dispose();
			Unk40 = null;
			Unk50?.Dispose();
			Unk50 = null;
			Unk58?.Dispose();
			Unk58 = null;
		}
	}

	public class ExternFxaa : IDisposable
	{
		[ExternField(0x0)] public ShaderResourceView Unk00 { get; set; }
		[ExternField(0x50)] public float Unk50 { get; set; } = 0.75f;
		[ExternField(0x54)] public float Unk54 { get; set; } = 0.166f;
		[ExternField(0x58)] public float Unk58 { get; set; } = 0.0833f;

		public ExternFxaa()
		{
		}

		public void Update(DeviceContext context, GBuffer gbuffer)
		{
			RenderHelpers.Profile("Extern Fxaa Update");
			Unk00 = gbuffer.Shading.SRV;
			Unk50 = 0.75f;
			Unk54 = 0.166f;
			Unk58 = 0.0833f;
			RenderHelpers.EndProfile();
		}

		public void Dispose()
		{
			Unk00?.Dispose();
			Unk00 = null;
		}
	}

	public class ExternGlobalLighting : IDisposable
	{
		[ExternField(0x08)] public ShaderResourceView Unk08 { get; set; }
		[ExternField(0x10)] public Vector4 Unk10 { get; set; } = Vector4.Zero;
		[ExternField(0x30)] public Vector4 Unk30 { get; set; } = Vector4.UnitZ * -1f;
		[ExternField(0x50)] public Vector4 Unk50 { get; set; } = Vector4.Zero;
		[ExternField(0x70)] public Vector4 Unk70 { get; set; } = Vector4.Zero;
		[ExternField(0x80)] public Vector4 Unk80 { get; set; } = Vector4.Zero;
		[ExternField(0x90)] public float Unk90 { get; set; }
		[ExternField(0x94)] public float Unk94 { get; set; }
		[ExternField(0x98)] public float Unk98 { get; set; }
		[ExternField(0x9C)] public float Unk9C { get; set; }
		[ExternField(0xA0)] public float UnkA0 { get; set; }
		[ExternField(0xB0)] public Vector4 UnkB0 { get; set; } = Vector4.Zero;
		[ExternField(0xC0)] public Vector4 UnkC0 { get; set; } = Vector4.Zero;
		[ExternField(0xD0)] public Vector4 UnkD0 { get; set; } = Vector4.Zero;

		public ExternGlobalLighting()
		{
			Unk08 = AssetManager.GetInstance().WhiteTexture;
		}

		public void Update(RendererGlobalChannels globals)
		{
			RenderHelpers.Profile("Extern GlobalLighting Update");
			Unk10 = globals.Get("sun_color") * globals.Get("sun_intensity").X * 5;
			Unk30 = globals.Get("sun_track_direction");
			Unk50 = globals.Get("sun_ambient_direction");
			Unk70 = globals.Get("up_ambient_color") * globals.Get("up_ambient_intensity").X;
			Unk80 = globals.Get("down_ambient_color") * globals.Get("down_ambient_intensity").X;
			Unk90 = globals.Get("up_ambient_sharpness").X;
			Unk94 = globals.Get("down_ambient_sharpness").X;
			UnkA0 = 0.2f;
			UnkB0 = new(0.00067f, 0.00067f, -0.3481f, -0.40235f);
			UnkC0 = new(1, 0, 1, 0);
			UnkD0 = new(0.00056f, -0.38889f, 0.00f, 0.00f);
			RenderHelpers.EndProfile();
		}

		public void Dispose()
		{
			Unk08?.Dispose();
			Unk08 = null;
		}
	}

	public void Update(CharmRenderer renderer)
	{
		if (renderer is null)
			return;

		RenderHelpers.Profile("Update Externs");
		Frame.Update(renderer);
		View.Update(renderer);
		Atmosphere.Update(renderer);
		//Deferred.Update();
		//Decal.Update();
		RenderHelpers.EndProfile();
	}

	public void Dispose()
	{
		Frame.Dispose();
		View.Dispose();
		Transparent.Dispose();
		Deferred.Dispose();
		Atmosphere.Dispose();
		Decal.Dispose();
		ShadowMask.Dispose();
		PostProcess.Dispose();
		ScreenArea.Dispose();
	}

	private static Dictionary<int, Func<object, object>> GetFieldMap(Type type)
	{
		return _fieldMaps.GetOrAdd(type, t =>
		{
			var map = new Dictionary<int, Func<object, object>>();
			foreach (var prop in t.GetProperties())
			{
				var attr = prop.GetCustomAttribute<ExternFieldAttribute>();
				if (attr == null)
					continue;

				var getter = CreateGetterDelegate(prop, t);
				map[attr.Element] = getter;
			}
			return map;
		});
	}

	private static Func<object, object> CreateGetterDelegate(PropertyInfo prop, Type declaringType)
	{
		var typedDelegateType = typeof(Func<,>).MakeGenericType(declaringType, prop.PropertyType);
		var typedGetter = (Delegate)prop.GetMethod.CreateDelegate(typedDelegateType);

		return instance =>
		{
			try
			{
				return typedGetter.DynamicInvoke(Convert.ChangeType(instance, declaringType));
			}
			catch
			{
				return prop.PropertyType.IsValueType ? Activator.CreateInstance(prop.PropertyType) : null;
			}
		};
	}

	public T Get<T>(TfxExtern tfxExtern, int element)
	{
		object target = tfxExtern switch
		{
			TfxExtern.Frame => Frame,
			TfxExtern.Transparent => Transparent,
			TfxExtern.View => View,
			TfxExtern.Deferred => Deferred,
			TfxExtern.Atmosphere => Atmosphere,
			TfxExtern.Decal => Decal,
			TfxExtern.ShadowMask => ShadowMask,
			TfxExtern.Postprocess => PostProcess,
			TfxExtern.ScreenArea => ScreenArea,
			TfxExtern.Fxaa => Fxaa,
			TfxExtern.GlobalLighting => GlobalLighting,
			_ => null
		};

		if (target == null)
			//throw new NotImplementedException($"Extern field not implemented: {tfxExtern} , element 0x{element:X}");
			return default;


		var map = GetFieldMap(target.GetType());
		if (map.TryGetValue(element, out var getter))
		{
			var value = getter(target);
			if (value is T typedValue)
				return typedValue;

			if (typeof(T).IsNumericType() && value?.GetType().IsNumericType() == true)
				return (T)Convert.ChangeType(value, typeof(T));
		}

		//throw new NotImplementedException($"Extern value not found: {tfxExtern}, element 0x{element:X}");
		return default;
	}
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ExternFieldAttribute : Attribute
{
	public int Element { get; }
	public ExternFieldAttribute(int element) => Element = element;
}

public static class TypeExtensions
{
	public static bool IsNumericType(this Type type)
	{
		if (type == null) return false;
		TypeCode typeCode = Type.GetTypeCode(type);
		return typeCode switch
		{
			TypeCode.Byte or TypeCode.SByte or TypeCode.UInt16 or TypeCode.UInt32 or
			TypeCode.UInt64 or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 or
			TypeCode.Decimal or TypeCode.Double or TypeCode.Single => true,
			_ => false,
		};
	}
}
