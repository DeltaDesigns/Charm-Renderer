using System.ComponentModel;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using Tiger;
using Tiger.Schema;
using Buffer = SharpDX.Direct3D11.Buffer;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    private Dictionary<string, MaterialData> _pipelineCache = new();

    private void RenderGBuffer()
    {
        RenderHelpers.Profile("Render GBuffer");

        GBuffers.SetRenderTargets(Context);
        Context.Rasterizer.SetViewport(GBuffers.RT0.GetViewport());

        CMD.States.SetStencilRef(Context, 7);
        CMD.States.CreateStates(Context, new(0, 2, 2, 0));
        RenderMesh(TfxRenderStage.GenerateGbuffer, "GBuffer Pass");

        Externs.Deferred.Update(Context, GBuffers);
        Externs.Decal.Update(Context, GBuffers);

        TfxScopes[Tiger.TfxScope.DECAL].Bind(this);

        // Decal Pass
        CMD.States.CreateStates(Context, new(8, 15, 2, 1));
        RenderMesh(TfxRenderStage.Decals, "Decal Pass");

        // Vertex AO workaround
        CMD.States.CreateStates(Context, new(0, 0, 0, 0));
        GBuffers.RT2.CopyTo(Context, GBuffers.RT2_Clone);

        Context.OutputMerger.SetRenderTargets(null, null, null, GBuffers.RT2.RTV);
        Context.VertexShader.Set(_clearAOVS);
        Context.PixelShader.Set(_clearAOPS);
        Context.PixelShader.SetShaderResources(0, GBuffers.RT2_Clone.SRV);

        DrawScreenQuad();
        RenderHelpers.EndProfile();

        RenderDownsampleDepth();
    }

    private void RenderAtmosphere()
    {
        if (!Viewport.RenderSky)
        {
            CMD.States.CreateStates(Context, new(0, 0, 0, 0));
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

        CMD.States.CreateStates(Context, new(0, 0, 0, 0));
        var far = GBuffers.SkyGenerateFar;
        var near = GBuffers.SkyGenerateNear;
        var hemisphere = GBuffers.FullHemisphereSkyColor;
        var depthangle = GBuffers.DepthAngleDensityLookup;

        Externs.Atmosphere.RTDimensions = far.GetResolutionInverse();
        Externs.Atmosphere.AtmosTimeOfDay = Viewport.TimeOfDay;

        Externs.Atmosphere.Update(this);
        Externs.Atmosphere.AtmosLookup0 = AssetManager.Get().GetOrCreateGlobalTexture(World.Atmosphere?.Lookup0).SRV;
        Externs.Atmosphere.AtmosLookup1 = AssetManager.Get().GetOrCreateGlobalTexture(World.Atmosphere?.Lookup1 ?? World.Atmosphere?.Lookup0).SRV;

        Externs.PostProcess.UpdateStageAtmos(this);

        hemisphere.Bind(Context);
        {
            Annotation.BeginEvent($"Global Pipeline: full_hemisphere_sky_color_generate");
            ExecutePipeline("full_hemisphere_sky_color_generate");
            Externs.Deferred.SkyHemisphereMips = hemisphere.SRV;

            Context.VertexShader.Set(_fullHemiSkyTempVS);
            Context.PixelShader.Set(_fullHemiSkyTempPS);

            DrawScreenQuad();

            // cubemap_apply_sky_copy_ao samples the 8th mipmap as the sky color average
            // the game uses sky_hemisphere_downsample_filter_ggx, but this should be fine (probably)
            Context.GenerateMips(hemisphere.SRV);
            Annotation.EndEvent();
        }

        if (Viewport.GodRays)
        {
            GenerateSkyMask();
        }
        else
        {
            Externs.Atmosphere.SkyMaskBlur = AssetManager.WhiteTexture;
            Externs.Atmosphere.SkyHemisphereBlur = AssetManager.WhiteTexture;
        }

        far.Bind(Context);
        RenderGlobalPipeline("sky_lookup_generate_far");
        Externs.Atmosphere.AtmosFar = far.SRV;
        Externs.Transparent.AtmosFar = far.SRV;

        // I guess this is how it actually works? Far uses first 2 textures, Near uses last 2, even if they are the same
        if (World.Atmosphere?.Lookup2 is not null)
            Externs.Atmosphere.AtmosLookup0 = AssetManager.Get().GetOrCreateGlobalTexture(World.Atmosphere?.Lookup2).SRV;

        if (World.Atmosphere?.Lookup3 is not null)
            Externs.Atmosphere.AtmosLookup1 = AssetManager.Get().GetOrCreateGlobalTexture(World.Atmosphere?.Lookup3).SRV;

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

        (System.Numerics.Vector2 sunScreenUV, float behindFlag) CalculateSunRay()
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

        var buffers = GBuffers;
        var mask = buffers.SkyGenerateMask;

        var sunRay = CalculateSunRay();
        var uv = sunRay.sunScreenUV;
        var behindFlag = sunRay.behindFlag;

        mask.Bind(Context);
        RenderGlobalPipeline("sky_generate_sky_mask");

        BindDownsample(mask, buffers.SkyGenerateMaskHalf);
        RenderGlobalPipeline("downsample_block_2x2");

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

    private void RenderLighting()
    {
        RenderHelpers.Profile("Render Lighting");
        Annotation.BeginEvent("Lighting");

        Context.ClearRenderTargetView(GBuffers.LightDiffuse.RTV, new RawColor4(0, 0, 0, 1));
        Context.ClearRenderTargetView(GBuffers.LightSpecular.RTV, new RawColor4(0, 0, 0, 1));
        Context.ClearRenderTargetView(GBuffers.LightIBL.RTV, new RawColor4(0, 0, 0, 1));
        Context.Rasterizer.SetViewport(GBuffers.LightDiffuse.GetViewport());

        if (Viewport.RenderSky)
        {
            CMD.States.SetStencilRef(Context, 0);
            // Supposed to be Diffuse and IBL but swapping IBL with Specular instead cus it just looks better with this setup
            Context.OutputMerger.SetRenderTargets(null, GBuffers.LightDiffuse.RTV, GBuffers.LightSpecular.RTV);
            RenderGlobalPipeline("cubemap_apply_sky_copy_ao");

            Externs.GlobalLighting.Update(World.GlobalChannels);
            Externs.ShadowMask.Update(GBuffers);

            Context.OutputMerger.SetRenderTargets(GBuffers.Depth.DSV, GBuffers.LightDiffuse.RTV, GBuffers.LightSpecular.RTV);
            CMD.States.CreateStates(Context, new(2, 16, 0, 0));
            RenderGlobalPipeline("global_lighting");
        }
        else
        {
            CMD.States.CreateStates(Context, new(0, 0, 0, 0));
            Context.OutputMerger.SetRenderTargets(null, GBuffers.LightDiffuse.RTV, GBuffers.LightSpecular.RTV);
            MatCapRenderer.Draw(Context, Externs);
        }

        {
            Externs.Deferred.LightDiffuse = GBuffers.LightDiffuse.SRV;
            Externs.Deferred.LightSpecular = GBuffers.LightSpecular.SRV;
            Externs.Deferred.LightIBL = GBuffers.LightSpecular.SRV;
        }
        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }

    private void RenderShading()
    {
        RenderHelpers.Profile("Render Shading");
        Context.OutputMerger.SetRenderTargets(GBuffers.Depth.DSV, GBuffers.Shading.RTV);

        // Sky
        if (Viewport.RenderSky)
        {
            CMD.States.SetStencilRef(Context, 0x10);
            CMD.States.CreateStates(Context, new(0, 77, 0, 0));
            Context.VertexShader.Set(_blitVS);
            Context.PixelShader.Set(null);
            DrawScreenQuad();

            CMD.States.SetStencilRef(Context, 0);
            CMD.States.CreateStates(Context, new(0, 50, 0, 0));
            RenderGlobalPipeline("sky");

            CMD.States.SetStencilRef(Context, 0);
            CMD.States.CreateStates(Context, new(0, 31, 0, 0));
            //RenderGlobalPipeline("deferred_shading");
        }
        else
        {
            CMD.States.SetStencilRef(Context, 0);
            CMD.States.CreateStates(Context, new(0, 0, 0, 0));
        }

        RenderGlobalPipeline("deferred_shading_no_atm");
        RenderHelpers.EndProfile();
    }

    private void RenderTransparent()
    {
        RenderHelpers.Profile("Render Transparent");
        CMD.States.SetStencilRef(Context, 4);
        Context.OutputMerger.SetRenderTargets(GBuffers.Depth.DSV, GBuffers.Shading.RTV);

        TempScopes.UpdateTransparentAdvancedScope(Context);
        TfxScopes[Tiger.TfxScope.TRANSPARENT].Bind(this);

        // Decal Additive Pass
        CMD.States.CreateStates(Context, new(8, 15, 2, 1));
        RenderMesh(TfxRenderStage.DecalsAdditive, "Decal Additive Pass");

        GBuffers.Shading.CopyTo(Context, GBuffers.Shading_Clone);
        Externs.Transparent.ShadingResult = GBuffers.Shading_Clone.SRV;

        // Sky Objects Pass
        if (Viewport.RenderSky && Viewport.RenderSkyObjs)
        {
            CMD.States.CreateStates(Context, new(8, 15, 2, 1));
            RenderMesh(TfxRenderStage.Transparents, FeatureRendererSubscription.SkyTransparent, "Sky Objects Pass");
        }

        // Transparent Pass
        CMD.States.CreateStates(Context, new(8, 15, 2, 1));
        RenderMesh(TfxRenderStage.Transparents,
            FeatureRendererSubscriptionExtensions.AllBut(TfxFeatureRenderer.SkyTransparent),
            "Transparent Pass");

        GBuffers.Shading.CopyTo(Context, GBuffers.Shading_Clone);
        RenderHelpers.EndProfile();
    }

    // FXAA uses the alpha channel (luminance) from the post process result
    private void RenderPostProcess()
    {
        RenderHelpers.Profile("Render Post Process");
        Annotation.BeginEvent("Post Process");

        RenderBloom();

        CMD.States.CreateStates(Context, new(0, 0, 0, 0));
        GBuffers.ColorGradingLUT.Bind(Context);

        if (Externs.ScreenArea.Unk08 is not null)
        {
            TempScopes.UpdateColorGradingScope(Context, true);
            RenderGlobalPipeline("color_grading_fill_using_tint_map_plus_matrix_hdr");
        }
        else
        {
            TempScopes.UpdateColorGradingScope(Context);
            RenderGlobalPipeline("color_grading_fill_using_matrix_hdr");
        }

        { // TODO, compute dispatching in MaterialData binding
            Annotation.BeginEvent($"Global Pipeline: color_grading_convert_to_volume_texture_hdr");
            UnbindAllRTVs();

            {
                Externs.ScreenArea.Unk00 = GBuffers.ColorGradingLUT.SRV;
                Externs.ScreenArea.Unk60 = GBuffers.LUTVolume.UAV;
            }

            ExecutePipeline("color_grading_convert_to_volume_texture_hdr");

            int groupsX = (GBuffers.LUTVolume.Width + 7) / 8;
            int groupsY = (GBuffers.LUTVolume.Height + 7) / 8;
            int groupsZ = GBuffers.LUTVolume.Depth;

            Context.Dispatch(groupsX, groupsY, groupsZ);
            Context.ComputeShader.SetUnorderedAccessView(0, null);
            Context.ComputeShader.SetShaderResource(0, null);
            Context.ComputeShader.Set(null);

            Annotation.EndEvent();
        }

        CMD.States.CreateStates(Context, new(0, 0, 0, 0));
        Context.OutputMerger.SetTargets(GBuffers.Depth.DSV, GBuffers.PostProcessResult.RTV);
        Context.Rasterizer.SetViewport(GBuffers.PostProcessResult.GetViewport());

        {
            Externs.ScreenArea.Unk00 = GBuffers.Shading_Clone.SRV;
            Externs.ScreenArea.Unk38 = GBuffers.LUTVolume.SRV;
        }

        if (Viewport.DisplayPass == RenderPass.final_color_grade)
            RenderGlobalPipeline("screen_area_global_lut3d_hdr");
        else
            RenderGlobalPipeline("screen_area_global_lut3d_no_tonemap");

        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }

    private void RenderFXAA()
    {
        RenderHelpers.Profile("Render FXAA");
        Annotation.BeginEvent("FXAA");
        CMD.States.CreateStates(Context, new(0, 0, 0, 0));

        GBuffers.FXAA.SetRenderTarget(Context, false);
        Externs.FXAA.Update(Context, GBuffers);
        RenderGlobalPipeline("fxaa");

        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }

    private void RenderDownsampleDepth()
    {
        RenderHelpers.Profile("Render Downsample Depth");
        Annotation.BeginEvent("Downsample Depth");

        CMD.States.CreateStates(Context, new(0, 2, 0, 0));
        GBuffers.DepthHalf.Clear(Context, 0, 0);
        GBuffers.DepthHalf.Set(Context);

        Externs.HDAO.Unk60 = GBuffers.Depth_Clone.DepthSRV;
        Externs.HDAO.Unk70 = GBuffers.DepthHalf.GetResolutionInverse();
        Externs.HDAO.Unk80 = GBuffers.Depth.GetResolutionInverse();

        RenderGlobalPipeline("downsample_depth_buffer");

        {
            Annotation.BeginEvent($"Global Pipeline: uber_depth_default");
            UnbindAllRTVs();
            Externs.UberDepth.Update(this);

            ExecutePipeline("uber_depth_default");

            var res = GBuffers.Depth.GetResolution();
            int groupsX = (res.width + 15) / 16;
            int groupsY = (res.height + 15) / 16;

            Context.Dispatch(groupsX, groupsY, 1);
            Context.ComputeShader.SetUnorderedAccessViews(0, [null, null]);

            Annotation.EndEvent();
        }

        // Used for shadow mask, not important rn
        //CMD.States.CreateStates(Context, new(0, 0, 0, 0));
        //GBuffers.UberDepth8th.Bind(Context);
        //{
        //    Externs.DownsampleTextureGeneric.Update(
        //        GBuffers.UberDepthQuarter.SRV,
        //        GBuffers.UberDepth8th.GetResolutionInverse(),
        //        GBuffers.UberDepthQuarter.GetResolutionInverse());
        //}
        //RenderGlobalPipeline("downsample_max_min_avg_no_swizzle");

        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }

    // Old Exposure
    private float _currentExposure = 1.0f;
    private float _targetExposure = 1.0f;
    private void RenderLuminance()
    {
        return;

        if (!Viewport.AutoExposure || !Viewport.RenderSky)
        {
            Externs.Frame.ExposureScale = Viewport.Exposure;
            return;
        }

        RenderHelpers.Profile("Render Luminance");
        Annotation.BeginEvent("Luminance");

        Context.OutputMerger.SetRenderTargets(null, GBuffers.Luminance.RTV);
        Context.VertexShader.Set(_luminanceVS);
        Context.PixelShader.Set(_luminancePS);
        Context.PixelShader.SetShaderResources(0, GBuffers.PostProcessResult.SRV);

        DrawScreenQuad();

        Context.GenerateMips(GBuffers.Luminance.SRV);

        //if (_frameCounter % 2 == 0)
        {
            int lastMip = GBuffers.Luminance.Texture.Description.MipLevels - 1;
            GPU.Instance.ImmediateContext.CopySubresourceRegion(
                GBuffers.Luminance.Texture,
                Resource.CalculateSubResourceIndex(lastMip, 0, GBuffers.Luminance.Texture.Description.MipLevels),
                null,
                GBuffers.LuminanceStaging,
                0
            );

            DataBox box = GPU.Instance.ImmediateContext.MapSubresource(GBuffers.LuminanceStaging, 0, MapMode.Read, MapFlags.None);
            float avgLogLum;
            unsafe
            {
                avgLogLum = *(float*)box.DataPointer;
            }
            GPU.Instance.ImmediateContext.UnmapSubresource(GBuffers.LuminanceStaging, 0);

            float avgLum = MathF.Exp(avgLogLum);
            _targetExposure = ComputeTargetExposure(avgLum);
        }

        _currentExposure = UpdateExposure(_currentExposure, _targetExposure, Externs.Frame.DeltaTime);
        Externs.Frame.ExposureScale = _currentExposure;

        Annotation.EndEvent();
        RenderHelpers.EndProfile();

        float UpdateExposure(float currentExposure, float targetExposure, float deltaTime)
        {
            if (MathF.Abs(targetExposure - currentExposure) < 0.001f)
                return currentExposure;

            const float speedUp = 2.0f;   // dark → bright
            const float speedDown = 1.0f; // bright → dark

            float speed = targetExposure > currentExposure
                ? speedUp
                : speedDown;

            float t = 1.0f - MathF.Exp(-speed * deltaTime);
            return float.Lerp(currentExposure, targetExposure, t);
        }

        float ComputeTargetExposure(float avgLum)
        {
            const float middleGray = 0.18f;
            return middleGray / Math.Max(avgLum, 1e-4f);
        }
    }


    private void RenderSkeleton()
    {
        RenderHelpers.Profile("Render Skeleton");
        Annotation.BeginEvent("Entity Skeleton");
        CMD.States.CreateStates(Context, new(8, 15, 2, 1));

        Context.InputAssembler.InputLayout = _debugLinesLayout;
        Context.VertexShader.Set(_debugLinesVS);
        Context.PixelShader.Set(_debugLinesPS);

        foreach (var renderable in World.RenderObjects)
        {
            renderable?.RenderSkeleton(this);
        }
        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }

    private void RenderBoundingBoxes()
    {
        RenderHelpers.Profile("Render Bounding Boxes");
        Annotation.BeginEvent("Render Bounding Boxes");
        CMD.States.CreateStates(Context, new(8, 15, 2, 1));

        Context.InputAssembler.InputLayout = _debugLinesLayout;
        Context.VertexShader.Set(_debugLinesVS);
        Context.PixelShader.Set(_debugLinesPS);

        foreach (var renderable in World.RenderObjects)
        {
            if ((renderable.IsChild && !Viewport.ShowEntChildren) || !renderable.Visible)
                continue;

            RenderBoundingBox(renderable.BoundingBox, new(1f, 1f, 0f, 1f));
        }

        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }

    private RenderObject[] _renderObjectsSnapshot = Array.Empty<RenderObject>();
    private RenderObject[] _renderPersistentObjectsSnapshot = Array.Empty<RenderObject>();
    private int _renderObjectsCount;
    private int _renderPersistentObjectsCount;

    private void RenderMesh(TfxRenderStage renderStage, string passName)
    {
        Annotation.BeginEvent(passName);
        lock (World.WorldLock)
        {
            _renderObjectsCount = World.RenderObjects.Count;
            if (_renderObjectsSnapshot.Length < _renderObjectsCount)
                _renderObjectsSnapshot = new RenderObject[_renderObjectsCount];
            World.RenderObjects.CopyTo(_renderObjectsSnapshot, 0);

            _renderPersistentObjectsCount = World.PersistantRenderObjects.Count;
            if (_renderPersistentObjectsSnapshot.Length < _renderPersistentObjectsCount)
                _renderPersistentObjectsSnapshot = new RenderObject[_renderPersistentObjectsCount];
            World.PersistantRenderObjects.CopyTo(_renderPersistentObjectsSnapshot, 0);
        }

        foreach (var renderable in _renderObjectsSnapshot.AsSpan(0, _renderObjectsCount))
        {
            renderable?.Bind(this, renderStage);
        }

        foreach (var renderable in _renderPersistentObjectsSnapshot.AsSpan(0, _renderPersistentObjectsCount))
        {
            renderable?.Bind(this, renderStage);
        }

        Annotation.EndEvent();
    }

    private void RenderMesh(TfxRenderStage renderStage, FeatureRendererSubscription features, string passName)
    {
        Annotation.BeginEvent(passName);
        RenderObject[] renderObjects;
        RenderObject[] persistentObjects;

        lock (World.WorldLock)
        {
            renderObjects = World.RenderObjects.ToArray();
            persistentObjects = World.PersistantRenderObjects.ToArray();
        }

        foreach (var renderable in renderObjects)
        {
            if (!features.IsSubscribed(renderable.Feature))
                continue;

            renderable?.Bind(this, renderStage);
        }

        foreach (var renderable in persistentObjects)
        {
            if (!features.IsSubscribed(renderable.Feature))
                continue;

            var bb = renderable.BoundingBox;
            if (!Camera.Frustum.Intersects(ref bb))
                continue;

            renderable?.Bind(this, renderStage);
        }
        Annotation.EndEvent();
    }

    private void RenderGlobalPipeline(string name)
    {
        RenderHelpers.Profile($"Render Global Pipeline {name}");
        Annotation.BeginEvent($"Global Pipeline: {name}");
        ExecutePipeline(name);

        DrawScreenQuad();
        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }

    private void BlitToWPF(RenderTarget2D rt)
    {
        RenderHelpers.Profile("Blit To WPF");
        Annotation.BeginEvent("Blit To WPF");
        CMD.States.CreateStates(Context, new(0, 0, 0, 0));

        _rtFinal.SetRenderTarget(Context, true);
        Context.VertexShader.Set(_blitVS);
        Context.PixelShader.Set(_blitPS);
        //Context.PixelShader.Set(DisplayPass == RenderPass.final_color_grade ? _blitPS_Linear : _blitPS);
        Context.PixelShader.SetSampler(0, _pointSampler);

        rt.SetShaderResource(Context, 0, ShaderStage.Pixel);

        DrawScreenQuad();

        wpfRT.Present(Context);

        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }


    private int _sphereIndexCount;
    public void RenderSphere(
        Transform transform,
        System.Numerics.Vector4 color,
        bool wireframe = false,
        Transform? offset = null)
    {
        if (_debugShapeVB == null || _debugShapeIB == null)
        {
            var meshBuilder = new MeshBuilder();
            meshBuilder.AddSphere(Vector3.Zero, 1, 8, 8);
            var mesh = meshBuilder.ToMesh();

            _sphereIndexCount = mesh.TriangleIndices.Count;

            _debugShapeVB = Buffer.Create(
                Device,
                mesh.Positions.ToArray(),
                new BufferDescription
                {
                    SizeInBytes = Utilities.SizeOf<Vector3>() * mesh.Positions.Count,
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.VertexBuffer,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None,
                    StructureByteStride = 0
                }
            );

            _debugShapeIB = Buffer.Create(
                Device,
                mesh.TriangleIndices.ToArray(),
                new BufferDescription
                {
                    SizeInBytes = Utilities.SizeOf<Vector3>() * mesh.TriangleIndices.Count,
                    Usage = ResourceUsage.Immutable,
                    BindFlags = BindFlags.IndexBuffer,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None,
                    StructureByteStride = 0
                });
        }

        CMD.States.CreateStates(Context, new(8, 15, 2, 1));
        Context.InputAssembler.InputLayout = _debugLinesLayout;
        Context.VertexShader.Set(_debugLinesVS);
        Context.PixelShader.Set(_debugLinesPS);

        var rotated = Vector3.Transform(
            transform.Position,
            transform.Quaternion.ToQuat() * (offset != null ? offset.Value.Quaternion.ToQuat() : System.Numerics.Quaternion.Identity)
        );

        TempScopes.UpdateRigidModelScopeCustom(Context, new Transform
        {
            Position = rotated,
            Scale = transform.Scale,
            Quaternion = transform.Quaternion,
        }, offset != null ? offset.Value : new Transform());

        Context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_debugShapeVB, Utilities.SizeOf<Vector3>(), 0));
        Context.InputAssembler.SetIndexBuffer(_debugShapeIB, SharpDX.DXGI.Format.R32_UInt, 0);
        Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        if (wireframe)
            Context.Rasterizer.State = _wireframeRS;

        Context.UpdateSubresource(ref color, _debugPSCB);
        Context.PixelShader.SetConstantBuffer(0, _debugPSCB);

        Context.DrawIndexed(_sphereIndexCount, 0, 0);
    }
}

public enum RenderPass
{
    [Description("Final")] final,
    [Description("Final (Color Graded)")] final_color_grade,

    // GBuffer
    [Description("Albedo")] debug_source_color,
    [Description("Albedo+AO")] debug_ambient_occlusion_source_color,
    [Description("Normals")] debug_world_normal,
    [Description("Metal")] debug_metalness,
    [Description("Ambient Occlusion")] debug_texture_ao, //debug_ambient_occlusion,
    [Description("Smoothness")] debug_specular_smoothness,
    [Description("Emission")] debug_emissive,
    [Description("Emission Intensity")] debug_emissive_intensity,
    [Description("Transmission")] debug_transmission,
    [Description("Iridescense ID")] debug_colored_overcoat_id,

    // Diffuse
    [Description("Diffuse Color")] debug_diffuse_color,
    [Description("Diffuse Light")] debug_diffuse_light,
    [Description("Diffuse Only")] debug_diffuse_only,
    //[Description("Diffuse IBL")] debug_diffuse_ibl,

    // Specular
    [Description("Specular Color")] debug_specular_color,
    [Description("Specular Light")] debug_specular_light,
    [Description("Specular Only")] debug_specular_only,
    //[Description("Specular IBL")] debug_specular_ibl

    // Depth
    [Description("Depth")] debug_depth,
    [Description("Depth Edges")] debug_depth_edges,

    // Misc
    [Description("Normal Edges")] debug_normal_edges,
    [Description("Grey Diffuse")] debug_grey_diffuse,
    [Description("Luminance")] debug_source_color_luminance,
    [Description("GBuffer Overdraw")] debug_gbuffer_overdraw,
    [Description("Smoothness Heatmap")] debug_valid_smoothness_heatmap,
    [Description("Metalness Heatmap")] debug_valid_layered_metalness,

    //[Description("test")] autoexposure_display,
}
