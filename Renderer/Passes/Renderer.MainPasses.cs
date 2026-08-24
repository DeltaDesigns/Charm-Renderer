using SharpDX.Mathematics.Interop;
using Tiger;

namespace Charm.Renderer;

// Main Deferred Passes
public partial class CharmRenderer
{
    private void RenderPasses()
    {
        PrepareRenderObjects();
        PrepareSunShadows();
        RenderGBuffer();
        RenderAtmosphere();
        RenderHDAO();
        RenderLighting();
        RenderShading();
        RenderTransparent();
        RenderPostProcess();

        if (Viewport.DisplayPass > RenderPass.final_color_grade)
        {
            CMD.States.SetDefaultState(Context, new(0, 0, 0, 0));
            RenderGlobalPipeline(Viewport.DisplayPass.ToString());
        }

        var blitRT = Viewport.FXAA ? GBuffers.FXAA : GBuffers.PostProcessResult;
        if (Viewport.ShowGrid)
        {
            Context.OutputMerger.SetTargets(GBuffers.Depth.DSV, blitRT.RTV);
            RenderGrid();
        }

        if (Viewport.ShowSkele || Viewport.ShowBB)
        {
            Context.OutputMerger.SetTargets(blitRT.RTV);
            if (Viewport.ShowSkele)
                RenderSkeleton();

            if (Viewport.ShowBB)
            {
                RenderBoundingBoxes();
                if (World.OverrideMainBB is not null)
                    RenderBoundingBox(World.OverrideMainBB.Value, new(1, 0, 0, 1));
            }
        }

        //{
        //    var overlay = GBuffers.ShadowMask;
        //    BlitOverlayTexture(blitRT, overlay.Width, overlay.Height, overlay.SRV, 2, 1, 0);

        //    int i = 0;
        //    foreach (var cascade in GBuffers.SunShadowCascades)
        //    {
        //        BlitOverlayTexture(blitRT, cascade.Width, cascade.Height, cascade.DepthSRV, 8, i / 8f, 0);
        //        i++;
        //    }
        //}
    }

    private void RenderGBuffer()
    {
        RenderHelpers.Profile("Render GBuffer");

        GBuffers.SetRenderTargets(Context);
        Context.Rasterizer.SetViewport(GBuffers.RT0.GetViewport());

        CMD.States.SetStencilRef(Context, 7);
        CMD.States.SetDefaultState(Context, new(0, 2, 2, 0));
        RenderMesh(TfxRenderStage.GenerateGbuffer, "GBuffer Pass");

        Externs.Deferred.Update(Context, GBuffers);
        Externs.Decal.Update(Context, GBuffers);

        TfxScopes[Tiger.TfxScope.DECAL].Bind(this);

        // Decal Pass
        CMD.States.SetDefaultState(Context, new(8, 15, 2, 1));
        RenderMesh(TfxRenderStage.Decals, "Decal Pass");

        // Vertex AO workaround
        CMD.States.SetState(Context, new(0, 0, 0, 0));
        GBuffers.RT2.CopyTo(Context, GBuffers.RT2_Clone);

        Context.OutputMerger.SetRenderTargets(null, null, null, GBuffers.RT2.RTV);
        Context.VertexShader.Set(_clearAOVS);
        Context.PixelShader.Set(_clearAOPS);
        Context.PixelShader.SetShaderResources(0, GBuffers.RT2_Clone.SRV);

        DrawScreenQuad();
        RenderHelpers.EndProfile();

        RenderDownsampleDepth();
    }

    private void RenderTransparent()
    {
        RenderHelpers.Profile("Render Transparent");
        CMD.States.SetStencilRef(Context, 4);
        Context.OutputMerger.SetRenderTargets(GBuffers.Depth.DSV, GBuffers.Shading.RTV);

        TempScopes.UpdateTransparentAdvancedScope(Context);
        TfxScopes[Tiger.TfxScope.TRANSPARENT].Bind(this);

        // Decal Additive Pass
        CMD.States.SetDefaultState(Context, new(8, 15, 2, 1));
        RenderMesh(TfxRenderStage.DecalsAdditive, "Decal Additive Pass");

        GBuffers.Shading.CopyTo(Context, GBuffers.Shading_Clone);
        Externs.Transparent.ShadingResult = GBuffers.Shading_Clone.SRV;

        // Sky Objects Pass
        if (Viewport.RenderSky && Viewport.RenderSkyObjs)
        {
            CMD.States.SetDefaultState(Context, new(8, 15, 2, 1));
            RenderMesh(TfxRenderStage.Transparents, FeatureRendererSubscription.SkyTransparent, "Sky Objects Pass");
        }

        // Transparent Pass
        CMD.States.SetDefaultState(Context, new(8, 15, 2, 1));
        RenderMesh(TfxRenderStage.Transparents,
            FeatureRendererSubscriptionExtensions.AllBut(TfxFeatureRenderer.SkyTransparent),
            "Transparent Pass");

        // Distortion Pass
        {
            GBuffers.Distortion.Clear(Context);
            GBuffers.Distortion.Bind(Context, GBuffers.DepthHalf.DSV);

            Externs.View.UpdateMatrices(GBuffers.Distortion.Width, GBuffers.Distortion.Height);
            Externs.Deferred.DeferredDepth = GBuffers.UberDepthHalf.SRV;
            TfxScopes[Tiger.TfxScope.VIEW].Bind(this);

            CMD.States.SetDefaultState(Context, new(8, 15, 2, 1));
            RenderMesh(TfxRenderStage.Distortion, FeatureRendererSubscription.All, "Distortion Pass");
            Externs.ScreenArea.Unk48 = GBuffers.Distortion.SRV;

            // reset
            Externs.View.UpdateMatrices(Camera.Viewport.Width, Camera.Viewport.Height);
            Externs.Deferred.DeferredDepth = GBuffers.Depth.DepthSRV;
            TfxScopes[Tiger.TfxScope.VIEW].Bind(this);
        }

        GBuffers.Shading.CopyTo(Context, GBuffers.Shading_Clone);
        RenderHelpers.EndProfile();
    }

    private void RenderLighting()
    {
        RenderHelpers.Profile("Render Lighting");
        Annotation.BeginEvent("Lighting");

        RenderShadowMask();

        Context.ClearRenderTargetView(GBuffers.LightDiffuse.RTV, new RawColor4(0, 0, 0, 1));
        Context.ClearRenderTargetView(GBuffers.LightSpecular.RTV, new RawColor4(0, 0, 0, 1));
        Context.ClearRenderTargetView(GBuffers.LightIBL.RTV, new RawColor4(0, 0, 0, 1));
        Context.Rasterizer.SetViewport(GBuffers.LightDiffuse.GetViewport());

        if (Viewport.RenderSky)
        {
            CMD.States.SetStencilRef(Context, 0);
            Context.OutputMerger.SetRenderTargets(null, GBuffers.LightDiffuse.RTV, GBuffers.LightIBL.RTV);
            RenderGlobalPipeline("cubemap_apply_sky_copy_ao");

            Externs.GlobalLighting.Update(World.GlobalChannels);
            Externs.ShadowMask.Update(GBuffers);

            Context.OutputMerger.SetRenderTargets(GBuffers.Depth.DSV, GBuffers.LightDiffuse.RTV, GBuffers.LightSpecular.RTV);
            CMD.States.SetDefaultState(Context, new(2, 16, 0, 0));
            RenderGlobalPipeline("global_lighting");

            if (Viewport.HDAO)
            {
                CMD.States.SetDefaultState(Context, new(3, 0, 0, 0));
                Externs.PostProcess.Unk00 = GBuffers.HDAO.SRV;
                Externs.PostProcess.UnkC0 = new(0.6f, 0.6f, 1, 1);
                Externs.PostProcess.Unk50 = GBuffers.HDAO.GetResolutionInverse();
                RenderGlobalPipeline("apply_ssao_to_light_buffers");
            }
        }
        else
        {
            CMD.States.SetState(Context, new(0, 0, 0, 0));
            Context.OutputMerger.SetRenderTargets(null, GBuffers.LightDiffuse.RTV, GBuffers.LightSpecular.RTV);
            MatCapRenderer.Draw(Context, Externs);
        }

        {
            Externs.Deferred.LightDiffuse = GBuffers.LightDiffuse.SRV;
            Externs.Deferred.LightSpecular = GBuffers.LightSpecular.SRV;
            Externs.Deferred.LightIBL = GBuffers.LightIBL.SRV;
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
            CMD.States.SetState(Context, new(0, 77, 0, 0));
            Context.VertexShader.Set(_blitVS);
            Context.PixelShader.Set(null);
            DrawScreenQuad();

            CMD.States.SetStencilRef(Context, 0);
            CMD.States.SetDefaultState(Context, new(0, 50, 0, 0));
            RenderGlobalPipeline("sky");

            CMD.States.SetStencilRef(Context, 0);
            CMD.States.SetDefaultState(Context, new(0, 31, 0, 0));
            RenderGlobalPipeline("deferred_shading");
        }
        else
        {
            CMD.States.SetStencilRef(Context, 0);
            CMD.States.SetDefaultState(Context, new(0, 0, 0, 0));
            RenderGlobalPipeline("deferred_shading_no_atm");
        }

        RenderHelpers.EndProfile();
    }

    private void BlitToWPF(RenderTarget2D rt)
    {
        BlitTo(rt, _rtFinal);
        wpfRT.Present(Context);
    }

    private void BlitTo(RenderTarget2D source, RenderTarget2D destination)
    {
        RenderHelpers.Profile("Blit To Target");
        Annotation.BeginEvent("Blit To Target");
        CMD.States.SetState(Context, new(0, 0, 0, 0));

        destination.SetRenderTarget(Context, true);
        Context.VertexShader.Set(_blitVS);
        Context.PixelShader.Set(_blitPS);
        Context.PixelShader.SetSampler(0, _pointSampler);

        source.SetShaderResource(Context, 0, ShaderStage.Pixel);
        DrawScreenQuad();

        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }
}


