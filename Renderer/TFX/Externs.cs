using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using HelixToolkit.Maths;
using SharpDX.Direct3D11;
using Tiger;
using Tiger.Schema;
using static Charm.Renderer.CharmRenderer;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public interface IExtern : IDisposable
{
}

public class Externs : IDisposable
{
    private readonly List<IExtern> _externs = new();
    private T Track<T>(T _extern) where T : IExtern
    {
        _externs.Add(_extern);
        return _extern;
    }

    public ExternFrame Frame;
    public ExternView View;
    public ExternTransparent Transparent;
    public ExternDeferred Deferred;
    public ExternAtmosphere Atmosphere;
    public ExternDecal Decal;
    public ExternShadowMask ShadowMask;
    public ExternPostProcess PostProcess;
    public ExternPostprocessInitialDownsample PostprocessInitialDownsample;
    public ExternScreenArea ScreenArea;
    public ExternFxaa FXAA;
    public ExternGlobalLighting GlobalLighting;
    public ExternHDAO HDAO;
    public ExternUberDepth UberDepth;
    public ExternDownsampleTextureGeneric DownsampleTextureGeneric;
    public ExternDecalSetTransform DecalSetTransform;
    public ExternDebugShadingOutput DebugShadingOutput;

    public Externs(CharmRenderer renderer)
    {
        Frame = Track(new ExternFrame());
        View = Track(new ExternView());
        Transparent = Track(new ExternTransparent());
        Deferred = Track(new ExternDeferred());
        Atmosphere = Track(new ExternAtmosphere());
        Decal = Track(new ExternDecal());
        ShadowMask = Track(new ExternShadowMask());
        PostProcess = Track(new ExternPostProcess());
        PostprocessInitialDownsample = Track(new ExternPostprocessInitialDownsample());
        ScreenArea = Track(new ExternScreenArea());
        FXAA = Track(new ExternFxaa());
        GlobalLighting = Track(new ExternGlobalLighting());
        HDAO = Track(new ExternHDAO());
        UberDepth = Track(new ExternUberDepth());
        DownsampleTextureGeneric = Track(new ExternDownsampleTextureGeneric());
        DecalSetTransform = Track(new ExternDecalSetTransform());
        DebugShadingOutput = Track(new ExternDebugShadingOutput());
    }

    public class ExternFrame : IExtern
    {
        [ExternField(0x00)] public float GameTime { get; set; }
        [ExternField(0x04)] public float RenderTime { get; set; }
        [ExternField(0x0C)] public float Unk0C { get; set; }
        [ExternField(0x10)] public float Unk10 { get; set; } = 0.5f;
        [ExternField(0x14)] public float DeltaTime { get; set; }
        [ExternField(0x18)] public float ExposureTime { get; set; } = 0.016666668f;
        [ExternField(0x1C)] public float ExposureScale { get; set; } = 0.25f;
        [ExternField(0x20)] public float Unk20 { get; set; } = 1f;
        [ExternField(0x28)] public float ExposureIllumRelative { get; set; } = 1f;
        [ExternField(0xA8)] public ShaderResourceView SpecularLobeLookup { get; set; }
        [ExternField(0xB0)] public ShaderResourceView SpecularLobe3DLookup { get; set; }
        [ExternField(0xB8)] public ShaderResourceView SpecularTintLookup { get; set; }
        [ExternField(0xC0)] public ShaderResourceView IridesenceLookup { get; set; }

        [ExternField(0x1B0)] public Vector4 Unk1B0 { get; set; } = new(0f, 0f, 1f, 1f);
        [ExternField(0x1C0)] public Vector4 Unk1C0 { get; set; } = new(1f, 1f, 0f, 1f);

        public ExternFrame()
        {
            var textures = Globals.Get().RenderGlobals.TagData.Textures.TagData;
            var speclobe = textures.SpecularLobeLookup;
            var speclobe3d = textures.SpecularLobeLookup3D;
            var spectint = textures.SpecularTintLookup;
            var iri = textures.IridescenceLookup;

            SpecularLobeLookup = AssetManager.Get().GetOrCreateGlobalTexture(speclobe).SRV;
            SpecularLobe3DLookup = AssetManager.Get().GetOrCreateGlobalTexture(speclobe3d).SRV;
            SpecularTintLookup = AssetManager.Get().GetOrCreateGlobalTexture(spectint).SRV;
            IridesenceLookup = AssetManager.Get().GetOrCreateGlobalTexture(iri).SRV;
        }

        public void Update(CharmRenderer renderer)
        {
            RenderHelpers.Profile("Extern Frame Update");
            ExposureIllumRelative = renderer.Viewport.ExposureIllum;
            Unk10 = renderer.Viewport.TimeOfDay;
            GameTime = renderer.Time * renderer.Viewport.TimeScale;
            RenderTime = renderer.Time * renderer.Viewport.TimeScale;
            DeltaTime = renderer.DeltaTime;
            RenderHelpers.EndProfile();
        }

        public void Dispose()
        {
        }
    }

    public class ExternView : IExtern
    {
        private static readonly Matrix4x4ButGood UNormToSNorm = new()
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
            WorldToCamera = cam.WorldToCamera;
            CameraToProj = cam.CameraToProjective;
            UpdateMatrices(cam.Viewport.Width, cam.Viewport.Height);

            RenderHelpers.EndProfile();
        }

        public void UpdateMatrices(int width, int height)
        {
            var targetPixelToProj = UNormToSNorm * Matrix4x4ButGood.FromScale(new(1f / width, 1f / height, 1f));

            ResolutionX = width;
            ResolutionY = height;

            CameraToWorld = WorldToCamera.Invert();
            ProjToCamera = CameraToProj.Invert();
            Position = CameraToWorld.W;

            WorldToProj = CameraToProj * WorldToCamera;
            ProjToWorld = WorldToProj.Invert();

            TargetPixelToCamera = ProjToCamera * targetPixelToProj;
            TargetPixelToWorld = CameraToWorld * TargetPixelToCamera;

            Matrix4x4ButGood ptow_no_proj_w = CameraToWorld.WithW(Vector4.UnitW) * ProjToCamera;
            TpToWw_No_Proj_W = ptow_no_proj_w * targetPixelToProj;

            Unk240 = ProjToWorld * UNormToSNorm;
            Unk2C0 = ptow_no_proj_w * UNormToSNorm;
            Unk30 = Vector4.UnitZ * WorldToProj.W;
        }

        public void Dispose()
        {
        }
    }

    public class ExternTransparent : IExtern
    {
        [ExternField(0x00)] public ShaderResourceView AtmosFar { get; set; }
        [ExternField(0x08)] public ShaderResourceView AtmosFarDS { get; set; }
        [ExternField(0x10)] public ShaderResourceView AtmosNear { get; set; }
        [ExternField(0x18)] public ShaderResourceView AtmosNearDS { get; set; }
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
            Unk48 = AssetManager.Get().BlackTextureWAlpha;
            Unk50 = AssetManager.Get().BlackTexture;
        }

        public void Dispose()
        {
        }
    }

    public class ExternDeferred : IExtern
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
        }
    }

    public class ExternAtmosphere : IExtern
    {
        [ExternField(0x40)] public ShaderResourceView SkySnapshot1 { get; set; }
        [ExternField(0x58)] public ShaderResourceView SkySnapshot2 { get; set; }
        [ExternField(0x70)] public float AtmosTimeOfDay { get; set; } = 0.5f;
        [ExternField(0x74)] public float AtmosUnk74 { get; set; } = 0f;
        [ExternField(0x78)] public float AtmosUnk78 { get; set; } = 0f;
        [ExternField(0x80)] public ShaderResourceView SkyDensityLookup { get; set; }
        [ExternField(0x90)] public Vector4 RTDimensions { get; set; } = new(0);
        [ExternField(0xA0)] public ShaderResourceView SkyMaskBlur { get; set; }
        [ExternField(0xC0)] public ShaderResourceView SkyHemisphereBlur { get; set; }
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
        [ExternField(0x1C4)] public float AtmosUnk1C4 { get; set; } = 1f;
        [ExternField(0x1D0)] public Vector4 AtmosUnk1D0 { get; set; } = Vector4.Zero;
        [ExternField(0x1E0)] public float AtmosUnk1E0 { get; set; } = -0.85f;
        [ExternField(0x1E4)] public float AtmosUnk1E4 { get; set; } = 0.05923f;
        [ExternField(0x1E8)] public float AtmosUnk1E8 { get; set; } = 0f;
        [ExternField(0x1EC)] public float AtmosUnk1EC { get; set; } = 0f;
        [ExternField(0x1F8)] public float AtmosUnk1F8 { get; set; } = 0f;
        [ExternField(0x1FC)] public float AtmosUnk1FC { get; set; } = 0f;
        [ExternField(0x208)] public float AtmosUnk208 { get; set; } = 0f;
        [ExternField(0x210)] public Vector4 AtmosUnk210 { get; set; } = Vector4.Zero;

        public ExternAtmosphere()
        {
            SkyMaskBlur = AssetManager.Get().WhiteTexture;
            SkyHemisphereBlur = AssetManager.Get().WhiteTexture;
        }

        public void Update(CharmRenderer renderer)
        {
            RenderHelpers.Profile("Extern Atmosphere Update");
            var channels = renderer.World.GlobalChannels;

            float distanceToNight = 1f - MathF.Abs((AtmosTimeOfDay * 3600f) / 1800f - 1f) * 0.725f;
            var sunDiskSize = -0.8f;
            var sunDiskIntensity = distanceToNight * 1.25f;

            AtmosSunColor = channels.Get("skybox_sun_color");

            //AtmosUnk150 = channels.Get("sun_glow_shape").X;
            AtmosUnk150 = sunDiskSize;
            AtmosUnk154 = sunDiskIntensity; //channels.Get("sun_glow_intensity").X;

            AtmosFogIntensity = channels.Get(26).X;
            AtmosUnk164 = channels.Get(15).X; // Fog density? Unsure
            AtmosUnk168 = channels.Get(16).X;
            AtmosUnk16C = channels.Get(17).X;
            AtmosUnk170 = channels.Get(19).X; // fog_height_falloff
            AtmosUnk180 = channels.Get(20); // fog_decay_color
            AtmosUnk190 = channels.Get(21).X; // fog_decay_scale
            AtmosUnk194 = channels.Get(new TigerHash(0x3eeacb23)).X;
            AtmosUnk198 = channels.Get(new TigerHash(0x7e92eb31)).X;
            AtmosRotation = channels.Get("sky_snapshot_rotation").X / 360f;
            AtmosIntensity = channels.Get("sky_snapshot_intensity").X;
            AtmosUnk1BC = channels.Get(new TigerHash(0x79f2e305)).X; // god ray intensity
            AtmosUnk1C0 = channels.Get(new TigerHash(0x62e4542e)).X;
            AtmosUnk1C4 = channels.Get(new TigerHash(0x949768cf)).X;
            AtmosUnk1D0 = channels.Get("sky_color_override");

            //AtmosUnk1E0 = channels.Get(new TigerHash(0x4aa1bef5)).X;
            AtmosUnk1E0 = sunDiskSize;
            AtmosUnk1E4 = sunDiskIntensity; //channels.Get("sun_glow_intensity").X;

            AtmosUnk1E8 = channels.Get(new TigerHash(0xe685c537)).X;
            AtmosUnk1EC = channels.Get(new TigerHash(0xe4a1bf60)).X;

            SunDirRotate(channels.Get("sun_track_direction"));
            AtmosSunDir = channels.Get("sun_track_direction");

            // No use in locking sky rotation to global channels, but also want the sun to rotate with it
            void SunDirRotate(Vector4 sundir)
            {
                float rotX = AtmosRotation * MathF.Tau + 45;
                var tilt = Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, -rotX);

                var dir = System.Numerics.Vector4.Transform(sundir, tilt);
                channels.Set("sun_track_direction", dir);

                dir = System.Numerics.Vector4.Transform(channels.Get("sun_atmosphere_direction"), tilt);
                channels.Set("sun_atmosphere_direction", dir);

            }

            RenderHelpers.EndProfile();
        }

        public void Dispose()
        {
        }
    }

    public class ExternDecal : IExtern
    {
        [ExternField(0x8)] public ShaderResourceView DeferredDepth { get; set; }
        [ExternField(0x8)] public ShaderResourceView DeferredRT1 { get; set; }
        [ExternField(0x10)] public Vector4 DepthConstants { get; set; } = new(0.0f, 1f / 0.01f, 0.0f, 0.0f);

        public void Update(DeviceContext context, GBuffer gbuffer)
        {
            DeferredDepth = gbuffer.Depth_Clone.DepthSRV;
            DeferredRT1 = gbuffer.RT1_Clone.SRV;
        }

        public void Dispose()
        {
        }
    }

    public class ExternShadowMask : IExtern
    {
        [ExternField(0x0)] public ShaderResourceView Unk00 { get; set; }
        [ExternField(0x8)] public ShaderResourceView Unk08 { get; set; }
        [ExternField(0x10)] public ShaderResourceView Unk10 { get; set; }
        [ExternField(0x20)] public Vector4 Unk20 { get; set; }
        [ExternField(0x30)] public float Unk30 { get; set; }
        [ExternField(0x34)] public float Unk34 { get; set; }

        public ExternShadowMask()
        {
            Unk00 = AssetManager.Get().WhiteTexture;
            Unk08 = AssetManager.Get().WhiteTexture;
            Unk10 = AssetManager.Get().WhiteTexture;
        }

        public void Update(GBuffer buffers)
        {
            Unk00 = buffers.ShadowMask.SRV;
            Unk20 = buffers.ShadowMask.GetResolutionInverse();
        }

        public void Dispose()
        {
        }
    }

    public class ExternPostProcess : IExtern
    {
        [ExternField(0x0)] public ShaderResourceView Unk00 { get; set; }
        [ExternField(0x08)] public ShaderResourceView Unk08 { get; set; }
        [ExternField(0x10)] public ShaderResourceView Unk10 { get; set; }
        [ExternField(0x50)] public Vector4 Unk50 { get; set; }
        [ExternField(0x60)] public Vector4 Unk60 { get; set; }
        [ExternField(0xC0)] public Vector4 UnkC0 { get; set; } = new(0.92537f, 0.0f, 0.37906f, 0.37906f);
        [ExternField(0xD0)] public Vector4 UnkD0 { get; set; } = new(-0.22681f, 0.80123f, 0.5537f, 0.5537f);
        [ExternField(0xE0)] public Vector4 UnkE0 { get; set; } = new(-0.30372f, -0.59835f, 0.74144f, 0.74144f);
        [ExternField(0xF0)] public Vector4 UnkF0 { get; set; }

        public ExternPostProcess()
        {
        }

        public void Update()
        {
        }

        public void UpdateAtmos(CharmRenderer renderer)
        {
            var atmos = renderer.Externs.Atmosphere;
            var up = atmos.AtmosSunDir.ToVector3().GetUp();
            var right = atmos.AtmosSunDir.ToVector3().GetRight(up);
            UnkC0 = right.ToVector4(right.Z);
            UnkD0 = up.ToVector4(up.Z);
            UnkE0 = atmos.AtmosSunDir;
        }

        public void UpdateAutoExposure(CharmRenderer renderer)
        {
            float val = 0.3f;
            if (renderer.Viewport.DisplayPass == RenderPass.final_color_grade)
                val = 0.6f;

            Unk00 = renderer.GBuffers.Bloom.Bloom24th.SRV;
            Unk50 = renderer.GBuffers.Bloom.AutoExposureColumns.GetResolutionInverse();
            UnkC0 = new(0.01f, 0.9f, val, 1f); //new(0.01f, 0.9f, 1f, 1f);
        }

        public void Dispose()
        {
        }
    }

    public class ExternPostprocessInitialDownsample : IExtern
    {
        [ExternField(0x0)] public ShaderResourceView Distorion { get; set; }
        [ExternField(0x8)] public ShaderResourceView Unk08 { get; set; }
        [ExternField(0x10)] public Vector4 Unk10 { get; set; }
        [ExternField(0x20)] public Vector4 UnkC0 { get; set; } = new(0.13281f, 0.23611f, 0.0f, 0.0f);
        [ExternField(0x30)] public Vector4 UnkD0 { get; set; } = Vector4.UnitW;
        [ExternField(0x40)] public float Unk40 { get; set; }

        public ExternPostprocessInitialDownsample()
        {
            Distorion = AssetManager.Get().BlackTextureWAlpha;
        }

        public void Update()
        {
        }

        public void Dispose()
        {
        }
    }

    public class ExternScreenArea : IExtern
    {
        [ExternField(0x0)] public ShaderResourceView Unk00 { get; set; }
        [ExternField(0x08)] public ShaderResourceView Unk08 { get; set; }
        [ExternField(0x30)] public ShaderResourceView Unk30 { get; set; } // HP Overlay
        [ExternField(0x38)] public ShaderResourceView Unk38 { get; set; } // LUT
        [ExternField(0x40)] public ShaderResourceView Unk40 { get; set; } // Bloom result
        [ExternField(0x48)] public ShaderResourceView Unk48 { get; set; } // Distortion
        [ExternField(0x50)] public ShaderResourceView Unk50 { get; set; } // Unk
        [ExternField(0x58)] public ShaderResourceView Unk58 { get; set; } // Vignette
        [ExternField(0x60)] public UnorderedAccessView Unk60 { get; set; }
        [ExternField(0x6C)] public float Unk6C { get; set; } = 0.5f;
        [ExternField(0x7C)] public float Unk7C { get; set; } = 0.9968f;
        [ExternField(0x90)] public Vector4 LUTDimensions { get; set; } = new(32f, 1024f, 0, 0); // height x width
        [ExternField(0xA0)] public Vector4 UnkA0 { get; set; } = new(0.03125f, -5.00f, 14.00f, 2.50f);
        [ExternField(0xB0)] public float UnkB0 { get; set; } = 0.5f;
        [ExternField(0xB4)] public float UnkB4 { get; set; } = 2f;
        [ExternField(0xB8)] public float UnkB8 { get; set; } = 0f;
        [ExternField(0xC0)] public Vector4 UnkC0 { get; set; } = new(0f, 0.4f, -1f, -1f);
        [ExternField(0xD0)] public Vector4 UnkD0 { get; set; } = new(0.5f, 0f, 0f, 0f);
        [ExternField(0xE0)] public Vector4 UnkE0 { get; set; } = new(0.25f, -0.225f, 0.40f, 0.96f);
        [ExternField(0xF0)] public Vector4 UnkF0 { get; set; } = new(0.13281f, 0.23611f, 0f, 0f);
        [ExternField(0x100)] public Matrix4x4ButGood Unk100 { get; set; } = Matrix4x4ButGood.Identity / 2f;
        [ExternField(0x140)] public float Unk140 { get; set; } = 0.05f;
        [ExternField(0x150)] public Vector4 Unk150 { get; set; } = new(0.3f, 0.5f, 0f, 0.02f);
        [ExternField(0x160)] public Vector4 Unk160 { get; set; } = new(0.3f, 0.5f, 0f, 0.5f);
        [ExternField(0x170)] public Vector4 Unk170 { get; set; } = Vector4.One;
        [ExternField(0x18C)] public float Unk18C { get; set; } = 0.5f;

        public ExternScreenArea()
        {
            Unk40 = AssetManager.Get().BlackTextureWAlpha;
            Unk50 = AssetManager.Get().BlackTextureWAlpha;
            Unk58 = AssetManager.Get().Vignette;
        }

        public void Update()
        {
        }

        public void Dispose()
        {
        }
    }

    public class ExternFxaa : IExtern
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
            Unk00 = gbuffer.PostProcessResult.SRV;
            Unk50 = 0.75f;
            Unk54 = 0.166f;
            Unk58 = 0.0833f;
        }

        public void Dispose()
        {
        }
    }

    public class ExternGlobalLighting : IExtern
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
            Unk08 = AssetManager.Get().WhiteTexture;
        }

        public void Update(RendererGlobalChannels globals)
        {
            RenderHelpers.Profile("Extern GlobalLighting Update");
            Unk10 = globals.Get("sun_color") * globals.Get("sun_intensity").X;
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
        }
    }

    public class ExternHDAO : IExtern
    {
        [ExternField(0x0)] public Vector4 Unk00 { get; set; } = new(8f, -6.6f, 0, 0);
        [ExternField(0x10)] public Vector4 Unk10 { get; set; } = new(-140f, 279.29999f, 0, 0);
        [ExternField(0x20)] public Vector4 Unk20 { get; set; } = new(0.00104f, 0.00185f, 9.00f, 9.00f);
        [ExternField(0x30)] public Vector4 Unk30 { get; set; } = new(0.4f, 0.4f, 0.6f, 0.6f);
        [ExternField(0x40)] public Vector4 Unk40 { get; set; } = new(-0.0015f); // new(-0.00098f);
        [ExternField(0x50)] public Vector4 Unk50 { get; set; } = new(10000, 50000, -0.02f, 20);
        [ExternField(0x60)] public ShaderResourceView Unk60 { get; set; }
        [ExternField(0x68)] public ShaderResourceView Unk68 { get; set; }
        [ExternField(0x70)] public Vector4 Unk70 { get; set; }
        [ExternField(0x80)] public Vector4 Unk80 { get; set; }
        [ExternField(0x90)] public Vector4 Unk90 { get; set; }
        [ExternField(0xA0)] public UnorderedAccessView UnkA0 { get; set; }

        public ExternHDAO()
        {
        }

        public void Update(CharmRenderer renderer)
        {
            var buffers = renderer.GBuffers;
            var depthConstants = renderer.Externs.Deferred.DepthConstants;
            Unk60 = buffers.Depth_Clone.DepthSRV;
            Unk68 = buffers.UberDepthHalf.SRV;
            Unk70 = buffers.UberDepthHalf.GetResolutionInverse();
            Unk80 = buffers.Depth.GetResolutionInverse();
            Unk90 = new(depthConstants.X, depthConstants.Y, 114.58865f, 1f);
        }

        public void Dispose()
        {
        }
    }

    public class ExternUberDepth : IExtern
    {
        [ExternField(0x0)] public ShaderResourceView Depth { get; set; }
        [ExternField(0x18)] public UnorderedAccessView Unk18 { get; set; }
        [ExternField(0x28)] public UnorderedAccessView Unk28 { get; set; }
        [ExternField(0x30)] public UnorderedAccessView Unk30 { get; set; }
        [ExternField(0x40)] public UnorderedAccessView Unk40 { get; set; }
        [ExternField(0x48)] public UnorderedAccessView Unk48 { get; set; }
        [ExternField(0x50)] public Vector4 Unk50 { get; set; }
        [ExternField(0x70)] public Vector4 Unk70 { get; set; }
        [ExternField(0x80)] public Vector4 Unk80 { get; set; }
        [ExternField(0x90)] public Vector4 Unk90 { get; set; }
        [ExternField(0xA0)] public Vector4 UnkA0 { get; set; }
        [ExternField(0xB0)] public Vector4 UnkB0 { get; set; }
        [ExternField(0xC0)] public UnorderedAccessView UnkC0 { get; set; }
        [ExternField(0xC8)] public UnorderedAccessView UnkC8 { get; set; }
        [ExternField(0xD0)] public UnorderedAccessView UnkD0 { get; set; }
        [ExternField(0xD8)] public UnorderedAccessView UnkD8 { get; set; }

        public ExternUberDepth()
        {
        }

        public void Update(CharmRenderer renderer)
        {
            var buffers = renderer.GBuffers;
            Depth = buffers.Depth_Clone.DepthSRV;
            Unk30 = buffers.UberDepthHalf.UAV;
            Unk40 = buffers.UberDepthQuarter.UAV;
            Unk50 = renderer.Externs.Deferred.DepthConstants;
            Unk70 = new Vector4(0, 0, buffers.Depth.Width, buffers.Depth.Height);
        }

        public void Dispose()
        {
        }
    }

    public class ExternDownsampleTextureGeneric : IExtern
    {
        [ExternField(0x38)] public ShaderResourceView Source { get; set; }
        [ExternField(0x40)] public Vector4 ResDest { get; set; }
        [ExternField(0x50)] public Vector4 ResSource { get; set; }

        public ExternDownsampleTextureGeneric()
        {
        }

        public void Update(ShaderResourceView source, Vector4 resDest, Vector4 resSource)
        {
            Source = source;
            ResDest = resDest;
            ResSource = resSource;
        }

        public void Dispose()
        {
        }
    }

    public class ExternDecalSetTransform : IExtern
    {
        [ExternField(0x0)] public Vector4 Unk00 { get; set; } = Vector4.UnitW;
        [ExternField(0x10)] public Vector4 Unk10 { get; set; } = Vector4.UnitW;

        public ExternDecalSetTransform()
        {
        }

        public void Update()
        {
        }

        public void Dispose()
        {
        }
    }

    public class ExternDebugShadingOutput : IExtern
    {
        [ExternField(0x0)] public float Unk00 { get; set; }
        [ExternField(0x20)] public Vector4 Unk20 { get; set; }
        [ExternField(0x30)] public Vector4 Unk30 { get; set; }
        [ExternField(0x80)] public Vector4 Unk80 { get; set; }
        [ExternField(0x90)] public Vector4 Unk90 { get; set; }

        public ExternDebugShadingOutput()
        {
        }

        public void Update()
        {
        }

        public void Dispose()
        {
        }
    }

    public void Update(CharmRenderer renderer)
    {
        if (renderer is null)
            return;

        RenderHelpers.Profile("Update Externs");
        var near = renderer.Camera.Near;
        var far = renderer.Camera.Far;
        Deferred.DepthConstants = new(1.0f / far, (far - near) / (far * near), 0, 0);
        Decal.DepthConstants = Deferred.DepthConstants;

        Frame.Update(renderer);
        View.Update(renderer);
        UberDepth.Update(renderer);

        RenderHelpers.EndProfile();
    }

    public void Dispose()
    {
        foreach (var _extern in _externs)
        {
            _extern.Dispose();
        }
    }

    private static readonly ConcurrentDictionary<(Type, int, Type), Delegate> _typedGetters = new();
    private static Func<object, T> BuildTypedGetter<T>(Type declaringType, int element)
    {
        var prop = declaringType.GetProperties()
            .FirstOrDefault(p => p.GetCustomAttribute<ExternFieldAttribute>()?.Element == element);

        if (prop is null)
            return _ => default;

        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var typedInstance = Expression.Convert(instanceParam, declaringType);
        var propertyAccess = Expression.Property(typedInstance, prop);
        var typedResult = Expression.Convert(propertyAccess, typeof(T));

        return Expression.Lambda<Func<object, T>>(typedResult, instanceParam).Compile();
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
            TfxExtern.Fxaa => FXAA,
            TfxExtern.GlobalLighting => GlobalLighting,
            TfxExtern.Hdao => HDAO,
            TfxExtern.UberDepth => UberDepth,
            TfxExtern.DownsampleTextureGeneric => DownsampleTextureGeneric,
            TfxExtern.PostprocessInitialDownsample => PostprocessInitialDownsample,
            TfxExtern.DecalSetTransform => DecalSetTransform,
            TfxExtern.DebugShadingOutput => DebugShadingOutput,
            _ => null
        };

#if DEBUG
        if (target == null)
        {
            Debug.Assert(false, $"Unimplemented Extern: {tfxExtern}");
            return default;
        }
#else
        if (target == null) return default;
#endif

        var key = (target.GetType(), element, typeof(T));
        var getter = (Func<object, T>)_typedGetters.GetOrAdd(key, _ => BuildTypedGetter<T>(target.GetType(), element));
        return getter(target);
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ExternFieldAttribute : Attribute
{
    public TigerStrategy Strategy { get; set; } = Tiger.Strategy.CurrentStrategy;
    public int Element { get; }
    public ExternFieldAttribute(int element) => Element = element;
    public ExternFieldAttribute(TigerStrategy strategy, int element)
    {
        Element = element;
        Strategy = strategy;
    }
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
