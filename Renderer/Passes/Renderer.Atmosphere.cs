using System.Numerics;
using HelixToolkit.Maths;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    private void RenderAtmosphere()
    {
        if (!Viewport.RenderSky)
        {
            CMD.States.SetDefaultState(Context, new(0, 0, 0, 0));
            Externs.Atmosphere.RTDimensions = Camera.GetResolutionInverse();

            Externs.Atmosphere.AtmosNear = AssetManager.BlackTextureWAlpha;
            Externs.Atmosphere.AtmosFar = AssetManager.BlackTextureWAlpha;
            Externs.Atmosphere.SkyMaskBlur = AssetManager.WhiteTexture;
            Externs.Atmosphere.SkyHemisphereBlur = AssetManager.WhiteTexture;

            Externs.Transparent.AtmosNear = AssetManager.BlackTextureWAlpha;
            Externs.Transparent.AtmosFar = AssetManager.BlackTextureWAlpha;
            Externs.Transparent.AtmosDepthAngleDensity = AssetManager.WhiteTexture;
            return;
        }

        RenderHelpers.Profile("Render Atmosphere");
        Annotation.BeginEvent("Atmosphere");

        CMD.States.SetDefaultState(Context, new(0, 0, 0, 0));
        var far = GBuffers.SkyGenerateFar;
        var near = GBuffers.SkyGenerateNear;
        var hemisphere = GBuffers.FullHemisphereSkyColor;
        var hemisphereHalf = GBuffers.HalfHemisphereSkyColor;
        var depthangle = GBuffers.DepthAngleDensityLookup;

        Externs.Atmosphere.RTDimensions = far.GetResolutionInverse();
        Externs.Atmosphere.AtmosTimeOfDay = Viewport.TimeOfDay;

        Externs.Atmosphere.Update(this);
        Externs.Atmosphere.SkySnapshot1 = AssetManager.Get().GetOrCreateGlobalTexture(World.Atmosphere?.Lookup0).SRV;
        Externs.Atmosphere.SkySnapshot2 = AssetManager.Get().GetOrCreateGlobalTexture(World.Atmosphere?.Lookup1 ?? World.Atmosphere?.Lookup0).SRV;

        {
            Annotation.BeginEvent($"Global Pipeline: full_hemisphere_sky_color_generate");

            // Not entirely game accurate as the hemisphere is drawn with sky objects. Frustum stuff or something.
            // full_hemisphere_sky_color_generate is supposed to be 64x64 but idk how to do the above, so its drawn at 512x512
            hemisphere.Bind(Context);
            ExecutePipeline("full_hemisphere_sky_color_generate");
            Context.VertexShader.Set(_fullHemiSkyTempVS);
            Context.PixelShader.Set(_fullHemiSkyTempPS);
            DrawScreenQuad();

            // While this does provide some under shading, its kinda a bit too dark and makes metals look not that great
            if (Viewport.UseSkyCopyTint_Debug)
            {
                hemisphereHalf.Bind(Context);
                Externs.PostProcess.Unk00 = hemisphere.SRV;
                RenderGlobalPipeline("sky_hemisphere_copy_and_tint");

                Externs.Deferred.SkyHemisphereMips = hemisphereHalf.SRV;
                Context.GenerateMips(hemisphereHalf.SRV);
            }
            else
            {
                Externs.Deferred.SkyHemisphereMips = hemisphere.SRV;
                // cubemap_apply_sky_copy_ao samples the 8th mipmap as the sky color average.
                // the game uses sky_hemisphere_downsample_filter_ggx, but this should be fine (probably)
                Context.GenerateMips(hemisphere.SRV);
            }

            Annotation.EndEvent();
        }

        GenerateSkyMask();

        Externs.PostProcess.UpdateAtmos(this);

        far.Bind(Context);
        RenderGlobalPipeline("sky_lookup_generate_far");
        Externs.Atmosphere.AtmosFar = far.SRV;
        Externs.Transparent.AtmosFar = far.SRV;

        // I guess this is how it actually works? Far uses first 2 textures, Near uses last 2, even if they are the same
        if (World.Atmosphere?.Lookup2 is not null)
            Externs.Atmosphere.SkySnapshot1 = AssetManager.Get().GetOrCreateGlobalTexture(World.Atmosphere?.Lookup2).SRV;

        if (World.Atmosphere?.Lookup3 is not null)
            Externs.Atmosphere.SkySnapshot2 = AssetManager.Get().GetOrCreateGlobalTexture(World.Atmosphere?.Lookup3).SRV;

        near.Bind(Context);
        RenderGlobalPipeline("sky_lookup_generate_near");
        Externs.Atmosphere.AtmosNear = near.SRV;
        Externs.Transparent.AtmosNear = near.SRV;

        depthangle.Bind(Context);
        RenderGlobalPipeline("atmo_depth_angle_density_lookup_generate");
        Externs.Transparent.AtmosDepthAngleDensity = depthangle.SRV;

        // Gotta set back to main viewport dims since this gets used for other non-atmosphere things for some reason
        Externs.Atmosphere.RTDimensions = Camera.GetResolutionInverse();

        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }

    private void GenerateSkyMask()
    {
        var pp = Externs.PostProcess;
        void BindDownsample(RenderTarget2D rtIn, RenderTarget2D rtOut)
        {
            rtOut.Bind(Context);
            pp.Unk00 = rtIn.SRV;
            pp.Unk60 = rtIn.GetResolutionInverse();
            pp.Unk50 = rtOut.GetResolutionInverse();
        }

        void BindRadialBlur(RenderTarget2D rtIn, RenderTarget2D rtOut, Vector4 unkC0, Vector4 unkD0)
        {
            rtOut.Bind(Context);
            pp.Unk00 = rtIn.SRV;
            pp.Unk50 = rtOut.GetResolutionInverse();
            pp.UnkC0 = unkC0;
            pp.UnkD0 = unkD0;

            //RenderGlobalPipeline("radial_blur_8"); // used on low settings
            RenderGlobalPipeline("radial_blur_16");
        }

        (Vector2 sunScreenUV, float behindFlag) CalculateSunRay()
        {
            Vector4 clipPos = Vector4.Transform(
                Externs.Atmosphere.AtmosSunDir,
                Externs.View.WorldToProj);

            float behindFlag = clipPos.W >= 0f ? 1f : -1f;

            const float epsilon = 1e-5f;
            float w = MathF.Abs(clipPos.W) < epsilon ? epsilon * behindFlag : clipPos.W;

            Vector3 ndc = new Vector3(clipPos.X, clipPos.Y, clipPos.Z) / w;
            Vector2 sunScreenUV = new(
                ndc.X * 0.5f + 0.5f,
                1f - (ndc.Y * 0.5f + 0.5f));

            return (sunScreenUV, behindFlag);
        }

        Externs.PostProcess.Unk00 = GBuffers.UberDepthHalf.SRV;
        Externs.PostProcess.Unk50 = GBuffers.SkyGenerateMask.GetResolutionInverse();
        Externs.PostProcess.Unk60 = GBuffers.UberDepthHalf.GetResolutionInverse();

        var buffers = GBuffers;
        var mask = buffers.SkyGenerateMask;
        mask.Bind(Context);
        RenderGlobalPipeline("sky_generate_sky_mask");

        BindDownsample(mask, buffers.SkyGenerateMaskHalf);
        RenderGlobalPipeline("downsample_block_2x2");

        if (Viewport.GodRays)
        {
            var sunRay = CalculateSunRay();
            var uv = sunRay.sunScreenUV;
            var behindFlag = sunRay.behindFlag;

            BindRadialBlur(buffers.SkyGenerateMaskHalf, buffers.SkyBlur1,
                new Vector4(uv.X, uv.Y, 0.03f, 0.022f),
                new Vector4(behindFlag, 1f, 1f, 1f)
            );

            BindRadialBlur(buffers.SkyBlur1, buffers.SkyBlur2,
                new Vector4(uv.X, uv.Y, 0.08f, 0.05867f),
                new Vector4(behindFlag, 0.273f, 1f, 1f)
            );
            Externs.Atmosphere.SkyMaskBlur = buffers.SkyBlur2.SRV;

            var up = Externs.Atmosphere.AtmosSunDir.ToVector3().GetUp();
            var right = Externs.Atmosphere.AtmosSunDir.ToVector3().GetRight(up);
            // seed_inscattering
            {
                buffers.SkyHemiSeedInscatter.Bind(Context);
                pp.Unk00 = buffers.FullHemisphereSkyColor.SRV;
                pp.UnkC0 = new Vector4(0.175f);
                pp.UnkD0 = right.ToVector4(right.Z);
                pp.UnkE0 = up.ToVector4(up.Z);
                pp.UnkF0 = Externs.Atmosphere.AtmosSunDir;

                RenderGlobalPipeline("sky_hemisphere_seed_inscattering");
            }

            // spherical_blur
            {
                buffers.SkyHemiBlur.Bind(Context);
                pp.Unk00 = buffers.SkyHemiSeedInscatter.SRV;
                pp.UnkC0 = new Vector4(0.70f);

                RenderGlobalPipeline("sky_hemisphere_spherical_blur");
            }
            Externs.Atmosphere.SkyHemisphereBlur = buffers.SkyHemiBlur.SRV;
        }
        else
        {
            Externs.Atmosphere.SkyMaskBlur = buffers.SkyGenerateMask.SRV;
            Externs.Atmosphere.SkyHemisphereBlur = AssetManager.WhiteTexture;
        }
    }
}
