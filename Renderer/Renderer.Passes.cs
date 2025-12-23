using SharpDX.Direct3D;
using SharpDX.Mathematics.Interop;
using System.ComponentModel;
using Tiger;

namespace Charm.Renderer;

public partial class CharmRenderer
{
	public RenderPass DisplayPass = RenderPass.final_combine_no_pp;

	private Dictionary<string, MaterialData> _pipelineCache = new();

	private void RenderGBuffer()
	{
		GBuffers.SetRenderTargets(Context);
		Context.Rasterizer.SetViewport(GBuffers.RT0.GetViewport());

		SetStencilRef(7);
		CreateStates(new(0, 2, 2, 0));
		RenderMesh(TfxRenderStage.GenerateGbuffer, "GBuffer Pass");

		Externs.Deferred.Update(Context, GBuffers);
		Externs.Decal.Update(Context, GBuffers);

		TfxScopes[Tiger.TfxScope.DECAL].Bind(Context);

		// Decal Pass
		CreateStates(new(8, 15, 2, 1));
		RenderMesh(TfxRenderStage.Decals, "Decal Pass");

		// Vertex AO workaround
		CreateStates(new(0, 0, 0, 0));
		GBuffers.RT2.CopyTo(Context, GBuffers.RT2_Clone);

		Context.OutputMerger.SetRenderTargets(null, null, null, GBuffers.RT2.RTV);
		Context.VertexShader.Set(_clearAOVS);
		Context.PixelShader.Set(_clearAOPS);
		Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
		Context.PixelShader.SetShaderResources(0, GBuffers.RT2_Clone.SRV);

		Context.Draw(4, 0);
	}

	private void RenderAtmosphere()
	{
		//UnbindAllRTVs();
		if (!Viewport.RenderSky)
		{
			//Externs.Atmosphere.AtmosFar = AssetManager.BlackTexture;
			//Externs.Transparent.AtmosFarLookup = AssetManager.BlackTexture;
			//Externs.Atmosphere.AtmosNear = AssetManager.BlackTexture;
			//Externs.Transparent.AtmosNearLookup = AssetManager.BlackTexture;
			//Externs.Deferred.SkyHemisphereMips = AssetManager.BlackTexture;
			return;
		}

		var far = GBuffers.SkyGenerateFar;
		var near = GBuffers.SkyGenerateNear;
		var hemisphere = GBuffers.FullHemisphereSkyColor;
		var depthangle = GBuffers.DepthAngleDensityLookup;

		Externs.Frame.Unk10 = Viewport.TimeOfDay;
		Externs.Atmosphere.RTDimensions = new(far.Width, far.Height, 1f / far.Width, 1f / far.Height);
		Externs.Atmosphere.AtmosTimeOfDay = Viewport.TimeOfDay;
		//Externs.Atmosphere.AtmosTimeOfDay = 0.42879f;

		//Externs.Atmosphere.AtmosRotation = Viewport.AtmosRotation;
		//Externs.Atmosphere.AtmosIntensity = Viewport.AtmosIntensity;
		//Externs.Atmosphere.AtmosSunColor = new System.Numerics.Vector4(1.0f, 0.95f, 0.85f, 1.0f) * MathF.Sin(MathF.PI * Math.Clamp(Viewport.TimeOfDay, 0.1f, 0.9f));
		//Externs.Atmosphere.AtmosTimeOfDay = 0.75f;

		far.Bind(Context);
		RenderGlobalPipeline("sky_lookup_generate_far");
		Externs.Atmosphere.AtmosFar = far.SRV;
		Externs.Transparent.AtmosFarLookup = far.SRV;

		near.Bind(Context);
		RenderGlobalPipeline("sky_lookup_generate_near");
		Externs.Atmosphere.AtmosNear = near.SRV;
		Externs.Transparent.AtmosNearLookup = far.SRV;

		depthangle.Bind(Context);
		RenderGlobalPipeline("atmo_depth_angle_density_lookup_generate");
		Externs.Transparent.AtmosDepthAngleDensity = depthangle.SRV;

		hemisphere.Bind(Context);
		//RenderGlobalPipeline("full_hemisphere_sky_color_generate");
		{
			Annotation.BeginEvent($"Global Pipeline: full_hemisphere_sky_color_generate");
			ExecutePipeline("full_hemisphere_sky_color_generate");
			Externs.Deferred.SkyHemisphereMips = hemisphere.SRV;

			Context.VertexShader.Set(_fullHemiSkyTempVS);
			Context.PixelShader.Set(_fullHemiSkyTempPS);

			Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
			Context.Draw(4, 0);
			Annotation.EndEvent();
		}
		//MatCapRenderer.MatCapSpecular = hemisphere.SRV;
	}

	private void RenderMatCap()
	{
		Annotation.BeginEvent("Matcap");

		Context.ClearRenderTargetView(GBuffers.LightDiffuse.RTV, new RawColor4(0, 0, 0, 1));
		Context.ClearRenderTargetView(GBuffers.LightSpecular.RTV, new RawColor4(0, 0, 0, 1));
		Context.ClearRenderTargetView(GBuffers.LightIBL.RTV, new RawColor4(0, 0, 0, 1));

		if (Viewport.RenderSky)
		{
			SetStencilRef(0);
			// Supposed to be Diffuse and IBL but swapping IBL with Specular instead cus it just looks better with this setup
			Context.OutputMerger.SetRenderTargets(null, GBuffers.LightDiffuse.RTV, GBuffers.LightSpecular.RTV);
			RenderGlobalPipeline("cubemap_apply_sky_copy_ao");

			Externs.GlobalLighting.Update(World.GlobalChannels);
			Context.OutputMerger.SetRenderTargets(GBuffers.Depth.DSV, GBuffers.LightDiffuse.RTV, GBuffers.LightSpecular.RTV);
			CreateStates(new(2, 16, 0, 0));
			RenderGlobalPipeline("global_lighting_gel");
		}
		else
		{
			CreateStates(new(0, 0, 0, 0));
			Context.OutputMerger.SetRenderTargets(null, GBuffers.LightDiffuse.RTV, GBuffers.LightSpecular.RTV);
			MatCapRenderer.Draw(Context, Externs);
		}

		{
			Externs.Deferred.LightDiffuse = GBuffers.LightDiffuse.SRV;
			Externs.Deferred.LightSpecular = GBuffers.LightSpecular.SRV;
			Externs.Deferred.LightIBL = GBuffers.LightSpecular.SRV;
		}
		Annotation.EndEvent();
	}

	private void RenderShading()
	{
		Context.OutputMerger.SetRenderTargets(GBuffers.Depth.DSV, GBuffers.Shading.RTV);

		// Sky
		if (Viewport.RenderSky)
		{
			SetStencilRef(0x10);
			CreateStates(new(0, 77, 0, 0));
			Context.VertexShader.Set(_blitVS);
			Context.PixelShader.Set(null);
			Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
			Context.Draw(4, 0);

			SetStencilRef(0);
			CreateStates(new(0, 50, 0, 0));
			RenderGlobalPipeline("sky");

			SetStencilRef(0);
			CreateStates(new(0, 31, 0, 0));
			//RenderGlobalPipeline("deferred_shading");
		}
		else
		{
			SetStencilRef(0);
			CreateStates(new(0, 0, 0, 0));
		}

		RenderGlobalPipeline("deferred_shading_no_atm");
	}

	private void RenderTransparent()
	{
		SetStencilRef(4);
		Context.OutputMerger.SetRenderTargets(GBuffers.Depth.DSV, GBuffers.Shading.RTV);

		TempScopes.UpdateTransparentAdvancedScope(Context);
		TfxScopes[Tiger.TfxScope.TRANSPARENT].Bind(Context);

		// Decal Additive Pass
		CreateStates(new(8, 15, 2, 1));
		RenderMesh(TfxRenderStage.DecalsAdditive, "Decal Additive Pass");

		GBuffers.Shading.CopyTo(Context, GBuffers.Shading_Clone);
		Externs.Transparent.ShadingResult = GBuffers.Shading_Clone.SRV;

		// Transparent Pass
		CreateStates(new(8, 15, 2, 1));
		RenderMesh(TfxRenderStage.Transparents, "Transparent Pass");

		GBuffers.Shading.CopyTo(Context, GBuffers.Shading_Clone);
	}

	private void RenderPostProcess()
	{
		Annotation.BeginEvent("Post Process");
		CreateStates(new(0, 0, 0, 0));

		//Externs.PostProcess.Update(Context, GBuffers);
		Externs.ScreenArea.Update(Context, GBuffers);
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

		{ // TODO, let interpreter handle resource binding
			Annotation.BeginEvent($"Global Pipeline: color_grading_convert_to_volume_texture_hdr");
			UnbindAllRTVs();

			ExecutePipeline("color_grading_convert_to_volume_texture_hdr");
			Context.VertexShader.Set(null);
			Context.PixelShader.Set(null);

			Context.ComputeShader.SetShaderResource(0, GBuffers.ColorGradingLUT.SRV);
			Context.ComputeShader.SetUnorderedAccessView(0, GBuffers.LUTVolume.UAV);

			int groupsX = (GBuffers.LUTVolume.Width + 7) / 8;
			int groupsY = (GBuffers.LUTVolume.Height + 7) / 8;
			int groupsZ = GBuffers.LUTVolume.Depth;

			Context.Dispatch(groupsX, groupsY, groupsZ);
			Context.ComputeShader.SetUnorderedAccessView(0, null);
			Context.ComputeShader.SetShaderResource(0, null);
			Context.ComputeShader.Set(null);

			Annotation.EndEvent();
		}

		CreateStates(new(0, 0, 0, 0));
		Externs.ScreenArea.Unk38 = GBuffers.LUTVolume.SRV;

		Context.OutputMerger.SetTargets(GBuffers.Depth.DSV, GBuffers.PostProcessResult.RTV);
		Context.Rasterizer.SetViewport(GBuffers.PostProcessResult.GetViewport());

		RenderGlobalPipeline("screen_area_global_lut3d");

		// I cant notice a difference here, not sure whats going on
		//GBuffers.Shading_Clone.SetRenderTarget(Context, false);
		//Externs.Fxaa.Update(Context, GBuffers);
		//RenderGlobalPipeline("fxaa");
		//Context.OutputMerger.SetTargets(GBuffers.Depth.DSV, GBuffers.Shading.RTV);

		Annotation.EndEvent();
	}

	private void RenderMesh(TfxRenderStage renderStage, string passName)
	{
		Annotation.BeginEvent(passName);
		foreach (var renderable in World.RenderObjects)
		{
			renderable?.Bind(this, renderStage);
		}
		Annotation.EndEvent();
	}

	private void RenderGlobalPipeline(string name)
	{
		Annotation.BeginEvent($"Global Pipeline: {name}");
		ExecutePipeline(name);

		Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
		Context.Draw(4, 0);
		Annotation.EndEvent();
	}

	private void BlitToWPF(RenderTarget2D rt)
	{
		Annotation.BeginEvent("Blit To WPF");

		_rtFinal.SetRenderTarget(Context, true);
		Context.VertexShader.Set(_blitVS);
		Context.PixelShader.Set(_blitPS);
		Context.PixelShader.SetSampler(0, _pointSampler);

		rt.SetShaderResource(Context, 0, ShaderStage.Pixel);

		Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
		Context.Draw(4, 0);

		Annotation.EndEvent();
	}

	public enum RenderPass
	{
		[Description("Final")] final,
		[Description("Final (No Color Grading)")] final_combine_no_pp,

		// Actual pipelines
		[Description("Albedo")] debug_source_color,
		[Description("Normals")] debug_world_normal,
		[Description("Metal")] debug_metalness,
		[Description("AO")] debug_ambient_occlusion,
		//[Description("Texture AO")] debug_texture_ao,
		[Description("Smoothness")] debug_specular_smoothness,
		[Description("Emissive")] debug_emissive,
		[Description("Emissive Intensity")] debug_emissive_intensity,
		[Description("Transmission")] debug_transmission,
		[Description("Diffuse Color")] debug_diffuse_color,
		[Description("Diffuse Light")] debug_diffuse_light,
		[Description("Diffuse Only")] debug_diffuse_only,
		//[Description("Diffuse IBL")] debug_diffuse_ibl,
		[Description("Specular Color")] debug_specular_color,
		//[Description("Specular Tint")] debug_specular_tint,
		[Description("Specular Light")] debug_specular_light,
		[Description("Specular Only")] debug_specular_only,
		[Description("Depth")] debug_depth,
		[Description("Depth Edges")] debug_depth_edges,
		//[Description("Specular IBL")] debug_specular_ibl
	}
}
