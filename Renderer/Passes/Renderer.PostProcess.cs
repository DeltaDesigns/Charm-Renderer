namespace Charm.Renderer;

public partial class CharmRenderer
{
    // FXAA uses the alpha channel (luminance) from the post process result
    private void RenderPostProcess()
    {
        RenderHelpers.Profile("Render Post Process");
        Annotation.BeginEvent("Post Process");

        RenderBloom();
        bool colorGrade = Viewport.DisplayPass == RenderPass.final_color_grade;

        // TODO, compute dispatching in MaterialData binding
        {
            CMD.States.SetDefaultState(Context, new(0, 0, 0, 0));
            GBuffers.ColorGradingLUT.Bind(Context);

            if (colorGrade && Externs.ScreenArea.Unk08 is not null)
            {
                TempScopes.UpdateColorGradingScope(Context, true);
                RenderGlobalPipeline("color_grading_fill_using_tint_map_plus_matrix_hdr");
            }
            else
            {
                //TempScopes.UpdateColorGradingScope(Context);
                //RenderGlobalPipeline("color_grading_fill_using_matrix_hdr");
                RenderGlobalPipeline("color_grading_utility_hdr");
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

        if (colorGrade)
        {
            RenderGlobalPipeline("screen_area_global_lut3d_distort_hdr");
        }
        else
        {
            // Hate that I have to do this but screen_area_global_lut3d_no_tonemap doesnt support distortion.
            // I personally like the look of no_tonemap as the other kinda washes things out a little.
            // So screw it, I made a version of no_tonemap that adds distortion support.

            RenderHelpers.Profile($"Render Global Pipeline screen_area_global_lut3d_no_tonemap");
            Annotation.BeginEvent($"Global Pipeline: screen_area_global_lut3d_no_tonemap");

            // executing this one since it sets the distortion buffer on t6, then the ps gets overriden
            ExecutePipeline("screen_area_global_lut3d_distort_hdr");
            Context.PixelShader.Set(AssetManager.GlobalLUT3D_No_Tonemap_Distort);
            DrawScreenQuad();

            Annotation.EndEvent();
            RenderHelpers.EndProfile();
        }

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
}
