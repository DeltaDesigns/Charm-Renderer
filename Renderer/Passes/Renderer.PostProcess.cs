using SharpDX;
using SharpDX.Direct3D11;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    // FXAA uses the alpha channel (luminance) from the post process result
    private void RenderPostProcess()
    {
        RenderHelpers.Profile("Render Post Process");
        Annotation.BeginEvent("Post Process");

        RenderBloom();

        // TODO, compute dispatching in MaterialData binding
        {
            CMD.States.SetDefaultState(Context, new(0, 0, 0, 0));
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

            Annotation.BeginEvent($"Global Pipeline: color_grading_convert_to_volume_texture_hdr");
            UnbindAllRTVs();

            Externs.ScreenArea.Unk00 = GBuffers.ColorGradingLUT.SRV;
            Externs.ScreenArea.Unk60 = GBuffers.LUTVolume.UAV;

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

        CMD.States.SetDefaultState(Context, new(0, 0, 0, 0));
        Context.OutputMerger.SetTargets(GBuffers.Depth.DSV, GBuffers.PostProcessResult.RTV);
        Context.Rasterizer.SetViewport(GBuffers.PostProcessResult.GetViewport());

        Externs.ScreenArea.Unk00 = GBuffers.Shading_Clone.SRV;
        Externs.ScreenArea.Unk38 = GBuffers.LUTVolume.SRV;

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
        CMD.States.SetDefaultState(Context, new(0, 0, 0, 0));

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

        CMD.States.SetDefaultState(Context, new(0, 2, 0, 0));
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

    private void RenderHDAO()
    {
        if (!Viewport.HDAO)
        {
            //GBuffers.HDAO.Clear(Context, new(1, 1, 1, 1));
            Externs.ShadowMask.Unk08 = AssetManager.WhiteTexture;
            return;
        }

        RenderHelpers.Profile("Render HDAO");
        Annotation.BeginEvent("HDAO");
        CMD.States.SetDefaultState(Context, new(0, 0, 0, 0));

        GBuffers.HDAO.Bind(Context);
        Externs.HDAO.Update(this);
        RenderGlobalPipeline("hdao");
        Externs.ShadowMask.Unk08 = GBuffers.HDAO.SRV;

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

}
