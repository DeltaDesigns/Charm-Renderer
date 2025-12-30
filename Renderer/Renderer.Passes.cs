using HelixToolkit.Geometry;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using System.ComponentModel;
using Tiger;
using Tiger.Schema;
using Buffer = SharpDX.Direct3D11.Buffer;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public partial class CharmRenderer
{
	public RenderPass DisplayPass = RenderPass.final;

	private Dictionary<string, MaterialData> _pipelineCache = new();

	private void RenderGBuffer()
	{
		RenderHelpers.Profile("Render GBuffer");

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

		RenderHelpers.EndProfile();
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

		RenderHelpers.Profile("Render Atmosphere");

		var far = GBuffers.SkyGenerateFar;
		var near = GBuffers.SkyGenerateNear;
		var hemisphere = GBuffers.FullHemisphereSkyColor;
		var depthangle = GBuffers.DepthAngleDensityLookup;

		Externs.Frame.Unk10 = Viewport.TimeOfDay;
		Externs.Atmosphere.RTDimensions = new(far.Width, far.Height, 1f / far.Width, 1f / far.Height);
		Externs.Atmosphere.AtmosTimeOfDay = Viewport.TimeOfDay;
		//Externs.Atmosphere.AtmosRotation = Viewport.AtmosRotation;
		//Externs.Atmosphere.AtmosIntensity = Viewport.AtmosIntensity;

		Externs.Atmosphere.Update(this);

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

		RenderHelpers.EndProfile();
	}

	private void RenderMatCap()
	{
		RenderHelpers.Profile("Render Matcap");
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
		RenderHelpers.EndProfile();
	}

	private void RenderShading()
	{
		RenderHelpers.Profile("Render Shading");
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
		RenderHelpers.EndProfile();
	}

	private void RenderTransparent()
	{
		RenderHelpers.Profile("Render Transparent");
		SetStencilRef(4);
		Context.OutputMerger.SetRenderTargets(GBuffers.Depth.DSV, GBuffers.Shading.RTV);

		TempScopes.UpdateTransparentAdvancedScope(Context);
		TfxScopes[Tiger.TfxScope.TRANSPARENT].Bind(Context);

		// Decal Additive Pass
		CreateStates(new(8, 15, 2, 1));
		RenderMesh(TfxRenderStage.DecalsAdditive, "Decal Additive Pass");

		GBuffers.Shading.CopyTo(Context, GBuffers.Shading_Clone);
		Externs.Transparent.ShadingResult = GBuffers.Shading_Clone.SRV;

		// Sky Objects Pass
		if (Viewport.RenderSky && Viewport.RenderSkyObjs)
		{
			CreateStates(new(8, 15, 2, 1));
			RenderMesh(TfxRenderStage.Transparents, FeatureRendererSubscription.SkyTransparent, "Sky Objects Pass");
		}

		// Transparent Pass
		CreateStates(new(8, 15, 2, 1));
		RenderMesh(TfxRenderStage.Transparents,
			FeatureRendererSubscriptionExtensions.AllBut(TfxFeatureRenderer.SkyTransparent),
			"Transparent Pass");

		GBuffers.Shading.CopyTo(Context, GBuffers.Shading_Clone);
		RenderHelpers.EndProfile();
	}

	private void RenderPostProcess()
	{
		RenderHelpers.Profile("Render Post Process");
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
		RenderHelpers.EndProfile();
	}

	private void RenderSkeleton()
	{
		RenderHelpers.Profile("Render Skeleton");
		Annotation.BeginEvent("Entity Skeleton");
		CreateStates(new(8, 15, 2, 1));

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
		CreateStates(new(8, 15, 2, 1));

		Context.InputAssembler.InputLayout = _debugLinesLayout;
		Context.VertexShader.Set(_debugLinesVS);
		Context.PixelShader.Set(_debugLinesPS);

		foreach (var renderable in World.RenderObjects)
		{
			renderable?.RenderBoundingBox(this);
		}
		Annotation.EndEvent();
		RenderHelpers.EndProfile();
	}

	private void RenderMesh(TfxRenderStage renderStage, string passName)
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
			renderable?.Bind(this, renderStage);
		}

		foreach (var renderable in persistentObjects)
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
			if (!features.IsSubscribed(renderable.MeshType))
				continue;

			renderable?.Bind(this, renderStage);
		}

		foreach (var renderable in persistentObjects)
		{
			if (!features.IsSubscribed(renderable.MeshType))
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

		Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
		Context.Draw(4, 0);
		Annotation.EndEvent();
		RenderHelpers.EndProfile();
	}

	private void BlitToWPF(RenderTarget2D rt)
	{
		RenderHelpers.Profile("Blit To WPF");
		Annotation.BeginEvent("Blit To WPF");

		_rtFinal.SetRenderTarget(Context, true);
		Context.VertexShader.Set(_blitVS);
		Context.PixelShader.Set(_blitPS);
		Context.PixelShader.SetSampler(0, _pointSampler);

		rt.SetShaderResource(Context, 0, ShaderStage.Pixel);

		Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
		Context.Draw(4, 0);

		Annotation.EndEvent();
		RenderHelpers.EndProfile();
	}


	private int _sphereIndexCount;
	public void RenderSphere(
		System.Numerics.Vector3 pos,
		float radius,
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

		CreateStates(new(8, 15, 2, 1));
		Context.InputAssembler.InputLayout = _debugLinesLayout;
		Context.VertexShader.Set(_debugLinesVS);
		Context.PixelShader.Set(_debugLinesPS);

		var rotated = Vector3.Transform(
			pos,
			offset != null ? offset.Value.Quaternion.ToQuat() : System.Numerics.Quaternion.Identity
		);

		TempScopes.UpdateRigidModelScopeCustom(Context, new MapTransform
		{
			Translation = new Tiger.Schema.Vector4(rotated, radius),
			Rotation = Vector4.UnitW,
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

	public enum RenderPass
	{
		[Description("Final")] final,
		[Description("Final (Color Graded)")] final_color_grade,

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
