using SharpDX.Mathematics.Interop;
using Tiger;

namespace Charm.Renderer;

// Main Deferred Passes
public partial class CharmRenderer
{
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
            Context.OutputMerger.SetRenderTargets(null, GBuffers.LightDiffuse.RTV, GBuffers.LightIBL.RTV);
            RenderGlobalPipeline("cubemap_apply_sky_copy_ao");

            Externs.GlobalLighting.Update(World.GlobalChannels);
            Externs.ShadowMask.Update(GBuffers);

            Context.OutputMerger.SetRenderTargets(GBuffers.Depth.DSV, GBuffers.LightDiffuse.RTV, GBuffers.LightSpecular.RTV);
            CMD.States.CreateStates(Context, new(2, 16, 0, 0));
            RenderGlobalPipeline("global_lighting");

            if (Viewport.HDAO)
            {
                CMD.States.CreateStates(Context, new(3, 0, 0, 0));
                Externs.PostProcess.Unk00 = GBuffers.HDAO.SRV;
                Externs.PostProcess.UnkC0 = new(0.6f, 0.6f, 1, 1);
                Externs.PostProcess.Unk50 = GBuffers.HDAO.GetResolutionInverse();
                RenderGlobalPipeline("apply_ssao_to_light_buffers");
            }
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
            CMD.States.CreateStates(Context, new(0, 77, 0, 0));
            Context.VertexShader.Set(_blitVS);
            Context.PixelShader.Set(null);
            DrawScreenQuad();

            CMD.States.SetStencilRef(Context, 0);
            CMD.States.CreateStates(Context, new(0, 50, 0, 0));
            RenderGlobalPipeline("sky");

            CMD.States.SetStencilRef(Context, 0);
            CMD.States.CreateStates(Context, new(0, 31, 0, 0));
            RenderGlobalPipeline("deferred_shading");
        }
        else
        {
            CMD.States.SetStencilRef(Context, 0);
            CMD.States.CreateStates(Context, new(0, 0, 0, 0));
            RenderGlobalPipeline("deferred_shading_no_atm");
        }

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
}


