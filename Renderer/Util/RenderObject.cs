using Arithmic;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using System.IO;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;
using Buffer = SharpDX.Direct3D11.Buffer;
using Material = Tiger.Schema.Shaders.Material;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public class InvestmentData : GpuResource
{
	public InventoryItem BaseItem;
	public Entity OwnerEntity;

	public Buffer InvestmentBuffer;
	public InvestmentDye InvestmentDye0 { get; set; }
	public InvestmentDye InvestmentDye1 { get; set; }
	public InvestmentDye InvestmentDye2 { get; set; }
	private DyeMerger _merger = new();
	private bool _isChangingDyes = false;
	private bool _hasData = false;

	// TODO, tie into AssetManager
	public TextureAsset DiffusePlate { get; set; }
	public TextureAsset GStackPlate { get; set; }
	public TextureAsset NormalPlate { get; set; }
	public TextureAsset DyePlate { get; set; }

	public InvestmentData(DeviceContext context, Entity itemEnt, InventoryItem item)
	{
		CreateInvestmentData(context, itemEnt, item);
	}

	public void CreateInvestmentData(DeviceContext context, Entity itemEnt, InventoryItem item)
	{
		if (itemEnt.ModelParent is null)
			return;

		BaseItem = item;
		OwnerEntity = itemEnt;

		var parentResource = (S8F6D8080)itemEnt.ModelParent.TagData.Unk18.GetValue(itemEnt.ModelParent.GetReader());
		if (parentResource.TexturePlates is not null && item.TagData.Unk90.GetValue(item.GetReader()) is S77738080)
		{
			S1C6E8080 plates = parentResource.TexturePlates.TagData;
			DiffusePlate ??= AssetManager.GetInstance().CreateFromPlate(plates.AlbedoPlate);
			GStackPlate ??= AssetManager.GetInstance().CreateFromPlate(plates.NormalPlate);
			NormalPlate ??= AssetManager.GetInstance().CreateFromPlate(plates.GStackPlate);
			DyePlate ??= AssetManager.GetInstance().CreateFromPlate(plates.DyemapPlate);

			CreateDefaultDyes(context, item);
			InvestmentBuffer = new Buffer(context.Device, new BufferDescription
			{
				SizeInBytes = Utilities.SizeOf<System.Numerics.Vector4>() * 63,
				Usage = ResourceUsage.Dynamic,
				BindFlags = BindFlags.ConstantBuffer,
				CpuAccessFlags = CpuAccessFlags.Write,
				OptionFlags = ResourceOptionFlags.None,
				StructureByteStride = 0
			});
			_hasData = true;
		}
		else
		{
			_hasData = false;
		}
	}

	public void ResetDyes(DeviceContext context)
	{
		if (!_hasData)
			return;

		CreateDefaultDyes(context, BaseItem);
	}

	public void CreateDefaultDyes(DeviceContext context, InventoryItem item)
	{
		Dictionary<uint, Dye> dyes = new();
		if (item.TagData.Unk90.GetValue(item.GetReader()) is S77738080 translationBlock)
		{
			_isChangingDyes = true;
			foreach (S7B738080 dyeEntry in translationBlock.DefaultDyes)
			{
				Dye dye = Investment.Get().GetDyeFromIndex(dyeEntry.DyeIndex);
				if (dye is null)
					continue;

				dyes.Add(Investment.Get().GetChannelHashFromIndex(dyeEntry.ChannelIndex), dye);
				//Log.Debug($"DefaultDye {dye.Hash} : {Investment.Get().GetChannelHashFromIndex(dyeEntry.ChannelIndex)}");
			}
			foreach (S7B738080 dyeEntry in translationBlock.LockedDyes)
			{
				Dye dye = Investment.Get().GetDyeFromIndex(dyeEntry.DyeIndex);
				if (dye is null)
					continue;

				dyes.Add(Investment.Get().GetChannelHashFromIndex(dyeEntry.ChannelIndex), dye);
				//Log.Debug($"LockedDye {dye.Hash} : {Investment.Get().GetChannelHashFromIndex(dyeEntry.ChannelIndex)}");
			}
			if (dyes.Count == 0)
			{
				Log.Debug("Shader has no dyes.");
				_isChangingDyes = false;
				return;
			}

			//Debug.Assert(dyes.Count == 3, $"Only {dyes.Count} dyes : {string.Join(", ", dyes.Values.Select(x => x.Hash))}");
			InvestmentDye0?.Dispose();
			InvestmentDye1?.Dispose();
			InvestmentDye2?.Dispose();

			InvestmentDye0 = CreateDye(0);
			InvestmentDye1 = CreateDye(1);
			InvestmentDye2 = CreateDye(2);

			InvestmentDye CreateDye(int index)
			{
				int safeIndex = Math.Min(index, dyes.Count - 1);
				var entry = dyes.ElementAt(safeIndex);

				var dye = new InvestmentDye(context, entry.Key, entry.Value.TagData);
				//Log.Debug($"Created Dye{index} : {dye.ChannelHash}");
				return dye;
			}

			_isChangingDyes = false;
		}
	}

	public void CreateCustomDyes(DeviceContext context, InventoryItem shader)
	{
		if (!_hasData) return;

		Dictionary<uint, Dye> dyes = new();
		if (shader.TagData.Unk90.GetValue(shader.GetReader()) is S77738080 translationBlock)
		{
			_isChangingDyes = true;
			var dyeEntries = translationBlock.CustomDyes.Any() // Should never happen, only case ive seen is the Shared Experience shader (which isnt even an actual shader)
				? translationBlock.CustomDyes
				: translationBlock.DefaultDyes;

			foreach (S7B738080 dyeEntry in dyeEntries)
			{
				Dye dye = Investment.Get().GetDyeFromIndex(dyeEntry.DyeIndex);
				if (dye is null)
					continue;

				dyes.Add(Investment.Get().GetChannelHashFromIndex(dyeEntry.ChannelIndex), dye);
			}
			if (dyes.Count == 0)
			{
				Log.Debug("Shader contains no dyes");
				return;
			}

			InvestmentDye0?.Dispose();
			InvestmentDye1?.Dispose();
			InvestmentDye2?.Dispose();

			if (!translationBlock.CustomDyes.Any() && dyes.Count == 3) // again, should never happen
			{
				InvestmentDye0.Dye = new(dyes.ElementAt(0).Value.TagData, context);
				InvestmentDye1.Dye = new(dyes.ElementAt(1).Value.TagData, context);
				InvestmentDye2.Dye = new(dyes.ElementAt(2).Value.TagData, context);
			}
			else
			{
				InvestmentDye0.Dye = new(dyes[InvestmentDye0.ChannelHash].TagData, context);
				InvestmentDye1.Dye = new(dyes[InvestmentDye1.ChannelHash].TagData, context);
				InvestmentDye2.Dye = new(dyes[InvestmentDye2.ChannelHash].TagData, context);
			}

			_isChangingDyes = false;
		}
	}

	public async void Bind(CharmRenderer renderer)
	{
		if (!_hasData) return;
		RenderHelpers.Profile("Investment Dye Bind");

		renderer.Context.PixelShader.SetShaderResource(0, DiffusePlate?.SRV);
		renderer.Context.PixelShader.SetShaderResource(1, GStackPlate?.SRV);
		renderer.Context.PixelShader.SetShaderResource(2, NormalPlate?.SRV);
		renderer.Context.PixelShader.SetShaderResource(3, DyePlate?.SRV);

		if (_isChangingDyes || InvestmentDye0 is null)
			return;

		InvestmentDye0.Bind(renderer.Context);
		InvestmentDye1.Bind(renderer.Context);
		InvestmentDye2.Bind(renderer.Context);

		var eval0 = await InvestmentDye0.Dye.GetEvaluated(renderer);
		var eval1 = await InvestmentDye1.Dye.GetEvaluated(renderer);
		var eval2 = await InvestmentDye2.Dye.GetEvaluated(renderer);

		try
		{
			_merger.Merge(eval0, eval1, eval2);
			_merger.Move(21, 3);
			_merger.Move(22, 4);
			_merger.Move(23, 5);

			_merger.Move(42, 6);
			_merger.Move(43, 7);
			_merger.Move(44, 8);

			Vector4[] mergedCB = _merger.ToArray();
			DataBox dataBox = renderer.Context.MapSubresource(InvestmentBuffer, 0, MapMode.WriteDiscard, MapFlags.None);
			try
			{
				Utilities.Write(dataBox.DataPointer, mergedCB, 0, mergedCB.Length);
			}
			finally
			{
				renderer.Context.UnmapSubresource(InvestmentBuffer, 0);
			}
		}
		finally
		{
			renderer.Context.PixelShader.SetConstantBuffer(7, InvestmentBuffer);
		}
		RenderHelpers.EndProfile();
	}

	public override void Dispose()
	{
		Utilities.Dispose(ref InvestmentBuffer);
		InvestmentDye0?.Dispose();
		InvestmentDye0 = null;
		InvestmentDye1?.Dispose();
		InvestmentDye1 = null;
		InvestmentDye2?.Dispose();
		InvestmentDye2 = null;

		AssetManager.GetInstance().ReleaseTexture(DiffusePlate);
		DiffusePlate = null;
		AssetManager.GetInstance().ReleaseTexture(GStackPlate);
		GStackPlate = null;
		AssetManager.GetInstance().ReleaseTexture(NormalPlate);
		NormalPlate = null;
		AssetManager.GetInstance().ReleaseTexture(DyePlate);
		DyePlate = null;

		_merger = null;

		base.Dispose();
	}
}

public class InvestmentDye : GpuResource
{
	public uint ChannelHash { get; set; }
	public TfxScope Dye { get; set; }

	public InvestmentDye(DeviceContext context, uint channelHash, SScope dye)
	{
		ChannelHash = channelHash;
		Dye = new(dye, context);
	}

	public void Bind(DeviceContext context)
	{
		Dye?.BindTextures(context);
	}

	public override void Dispose()
	{
		Dye?.Dispose();
		Dye = null;

		base.Dispose();
	}
}

// eh, dont like this
public enum MeshType
{
	Normal,
	Physics
}

public class RenderObject : GpuResource
{
	public FileHash Hash;
	public TfxFeatureRenderer Feature;
	public MeshType MeshType = MeshType.Normal;
	public RenderStageSubscription Stages;
	public bool Visible = true;
	public bool IsChild = false; // For entity children/attachments

	public HelixToolkit.Maths.BoundingBox LocalBoundingBox { get; set; }
	public HelixToolkit.Maths.BoundingBox BoundingBox { get; set; }
	public int InstanceCount = 1;

	private readonly List<MeshPartData> _meshes = new();
	public IReadOnlyList<MeshPartData> Meshes => _meshes;
	public IReadOnlyList<BoneNode> Bones;

	public InvestmentData Investment { get; set; }

	// todo, seperate different types (static, entity, etc) into own RenderObject type class
	public Entity Entity;
	public ModelPermutation Permutations;
	public List<Material> MaterialMap;
	public List<SExternalMaterialMapEntry> MaterialRangeMap;

	public Transform[] GlobalTransforms = new Transform[]
	{
		new()
		{
			Position = new(0f, 0f, 0f),
			Quaternion = new(0f, 0f, 0f, 1f),
			Scale = Tiger.Schema.Vector3.One
		}
	};

	public Transform TransformOffset = new Transform
	{
		Position = new(0f, 0f, 0f),
		Quaternion = new(0f, 0f, 0f, 1f),
		Scale = Tiger.Schema.Vector3.One
	};

	public void Create(DeviceContext context, EntityModel entModel, TfxFeatureRenderer type)
	{
		Hash = entModel.Hash;
		var parts = entModel.Load(ExportDetailLevel.MostDetailed, null);
		CreateMesh(context, parts.Cast<MeshPart>().ToList(), type);
	}

	public void Create(DeviceContext context, RenderWorld world, Entity entity, InventoryItem inventoryItem = null)
	{
		Hash = entity.Hash;
		Entity = entity;
		if (inventoryItem is not null)
			Investment = new(context, entity, inventoryItem);

		if (entity.Skeleton is not null)
			Bones = entity.Skeleton.GetBoneNodes();

		if (entity.Model is not null)
		{
			// This works fine but some entity bounding boxes just dont feel good to orbit around
			LocalBoundingBox = entity.ModelParent.GetBoundingBox().CreateFrom(); // Is entity scale actually not used for bb calc?

			var parts = entity.LoadModel(ExportDetailLevel.MostDetailed);
			CreateMesh(context, parts.Cast<MeshPart>().ToList(), TfxFeatureRenderer.DynamicObjects);

			using TigerReader reader = entity.ModelParent.GetReader();
			Permutations = entity.ModelParent.MaterialPermutations;
			MaterialMap = entity.ModelParent.Reader.ExternalMaterials.Enumerate(reader).Select(x => x.Material).ToList();
			MaterialRangeMap = entity.ModelParent.Reader.ExternalMaterialsMap.Enumerate(reader).ToList();

			world.RenderObjects.Enqueue(this);
		}

		// this kinda sucks but i want physics models to be separate render objects
		if (entity.PhysicsModel is not null)
		{
			RenderObject obj = new();
			obj.LocalBoundingBox = entity.PhysicsModelParent.GetBoundingBox().CreateFrom();

			obj.Hash = entity.Hash;
			obj.Entity = entity;
			obj.MeshType = MeshType.Physics;
			if (inventoryItem is not null)
				obj.Investment = new(context, entity, inventoryItem);

			var parts = entity.LoadPhysicsModel(ExportDetailLevel.MostDetailed);
			obj.CreateMesh(context, parts.Cast<MeshPart>().ToList(), TfxFeatureRenderer.DynamicObjects);

			using TigerReader reader = entity.PhysicsModelParent.GetReader();
			obj.Permutations = entity.PhysicsModelParent.MaterialPermutations;
			obj.MaterialMap = entity.PhysicsModelParent.Reader.ExternalMaterials.Enumerate(reader).Select(x => x.Material).ToList();
			obj.MaterialRangeMap = entity.PhysicsModelParent.Reader.ExternalMaterialsMap.Enumerate(reader).ToList();

			world.RenderObjects.Enqueue(obj);
		}
	}

	public void Create(DeviceContext context, RenderWorld world, StaticMesh staticMesh)
	{
		Hash = staticMesh.Hash;
		var staticParts = staticMesh.Load(ExportDetailLevel.MostDetailed);
		//var staticDecals = staticMesh.LoadDecals(ExportDetailLevel.MostDetailed);
		LocalBoundingBox = RenderHelpers.ComputeBoundingBox(staticParts.SelectMany(x => x.VertexPositions).ToList());

		CreateMesh(context, staticParts.Cast<MeshPart>().ToList(), TfxFeatureRenderer.StaticObjects);
		world.RenderObjects.Enqueue(this);
		//CreateMesh(context, staticDecals.Cast<MeshPart>().ToList(), TfxFeatureRenderer.Decals);
	}

	private void CreateMesh(DeviceContext context, List<MeshPart> parts, TfxFeatureRenderer meshType)
	{
		Feature = meshType;
		Stages = RenderStageSubscriptionExtensions.FromStages(parts.Select(x => x.RenderStage).Distinct());

		BoundingBox = RenderHelpers.TransformBoundingBox(
				LocalBoundingBox,
				GlobalTransforms[0].Position + TransformOffset.Position,
				GlobalTransforms[0].Quaternion.ToQuat() * TransformOffset.Quaternion.ToQuat(),
				GlobalTransforms[0].Scale);

		foreach (var part in parts)
		{
			if (part.Material is null)
				continue;

			var meshData = new MeshPartData
			{
				RenderStage = part.RenderStage,
				IndexBuffer = IndexBuffer.Create(context, part.IndexBuffer),
				VertexBuffer0 = VertexBuffer.Create(context, part.VertexBuffer0),
				VertexBuffer1 = part.VertexBuffer1 != null ? VertexBuffer.Create(context, part.VertexBuffer1) : null,
				VertexBuffer2 = part.VertexBuffer2 != null ? VertexBuffer.Create(context, part.VertexBuffer2) : null,
				VertexBuffer3 = part.VertexBuffer3 != null ? VertexBuffer.Create(context, part.VertexBuffer3, ResourceOptionFlags.BufferAllowRawViews) : null,
				IndexCount = (int)part.IndexCount,
				IndexOffset = (int)part.IndexOffset,
				Topology = part.PrimitiveType == Tiger.PrimitiveType.Triangles
					? PrimitiveTopology.TriangleList
					: PrimitiveTopology.TriangleStrip,

				MeshScale = part.MeshScale,
				MeshTransform = part.MeshTransform,
				MeshUVTransform = part.UVTransform,
				MaxColorIndex = part.MaxVertexColorIndex,
				Material = AssetManager.GetInstance().GetOrCreateMaterial(part.Material),

				GroupIndex = part.GroupIndex,
				VariantMaterialIndex = part.VariantShaderIndex,
			};
			meshData.Material.UsesVertexColor = part.VertexBuffer2 != null && part.Material.Vertex.Shader.OutputSignatures.Any(x => x.RegisterIndex == 5 && x.SemanticIndex == 8);
			meshData.InputLayout = new InputLayout(context.Device, part.Material.Vertex.Shader.GetBytecode(), RenderHelpers.GetInputLayout(part.VertexLayoutIndex).ToArray());

			AddMesh(meshData);
		}
	}

	public void AddMesh(MeshPartData mesh)
	{
		_meshes.Add(mesh);
	}

	// TODO, WIP
	public void BindParallel(CharmRenderer renderer, TfxRenderStage renderStage, int jobCount)
	{
		if (!Stages.IsSubscribed(renderStage))
			return;

		RenderHelpers.Profile($"{Feature} {Hash} Bind (Parallel)");

		MeshPartData[] meshes;
		lock (renderer.World.WorldLock)
			meshes = Meshes.ToArray();

		// Filter once on main thread
		var stageMeshes = meshes
			.Where(m => m.RenderStage == renderStage)
			.ToArray();

		if (stageMeshes.Length == 0)
			return;

		jobCount = Math.Min(jobCount, stageMeshes.Length);

		var initialState = new GPUState().Backup(renderer.CMD);

		var deferredContexts = new DeviceContext[jobCount];
		var commandLists = new SharpDX.Direct3D11.CommandList[jobCount];

		for (int i = 0; i < jobCount; i++)
			deferredContexts[i] = new DeviceContext(renderer.Device);

		Parallel.For(0, jobCount, jobIndex =>
		{
			int start = (jobIndex * stageMeshes.Length) / jobCount;
			int end = ((jobIndex + 1) * stageMeshes.Length) / jobCount;

			var ctx = deferredContexts[jobIndex];

			var cmdCopy = new CommandList
			{
				Parent = renderer.CMD.Parent,
				GpuState = renderer.CMD.GpuState,
				DeferredContext = ctx,
				States = renderer.CMD.States
			};
			initialState.Restore(cmdCopy);

			for (int i = start; i < end; i++)
			{
				var mesh = stageMeshes[i];

				if (Feature == TfxFeatureRenderer.StaticObjects)
				{
					renderer.TempScopes.UpdateChunkModelScope(ctx, mesh, GlobalTransforms);
					if (InstanceCount > 1)
						mesh.DrawInstanced(renderer, InstanceCount);
					else
						mesh.Draw(renderer);
				}
				else
				{
					renderer.TempScopes.UpdateRigidModelScope(ctx, mesh, GlobalTransforms, TransformOffset);
					if (Investment is not null)
						Investment.Bind(renderer);

					mesh.Draw(renderer);
				}
			}

			commandLists[jobIndex] = ctx.FinishCommandList(false);
		});

		for (int i = 0; i < jobCount; i++)
		{
			renderer.Context.ExecuteCommandList(commandLists[i], false);
			commandLists[i].Dispose();
			deferredContexts[i].Dispose();
		}

		initialState.Restore(renderer.CMD);

		RenderHelpers.EndProfile();
	}

	public void Bind(CharmRenderer renderer, TfxRenderStage renderStage)
	{
		if ((IsChild && !renderer.Viewport.ShowEntChildren) || !Visible || !Stages.IsSubscribed(renderStage))
			return;

		RenderHelpers.Profile($"{Feature} {Hash} Bind");

		MeshPartData[] meshes;
		lock (renderer.World.WorldLock)
			meshes = Meshes.ToArray();

		foreach (var mesh in meshes)
		{
			if (mesh.RenderStage != renderStage || !renderer.GroupVisibility.IsVisible(this, mesh.GroupIndex))
				continue;

			if (Feature == TfxFeatureRenderer.StaticObjects)
			{
				renderer.TempScopes.UpdateChunkModelScope(renderer.Context, mesh, GlobalTransforms);
				if (InstanceCount > 1)
					mesh.DrawInstanced(renderer, InstanceCount);
				else
					mesh.Draw(renderer);
			}
			else
			{
				// meh, hopefully 'fixes' the random physics mesh with incorrect transforms
				if (MeshType == MeshType.Physics && !mesh.Material.Skinned)
					continue;

				renderer.TempScopes.UpdateRigidModelScope(renderer.Context, mesh, GlobalTransforms, TransformOffset);
				if (Investment is not null)
					Investment.Bind(renderer);

				// This is bad and stupid and sucks
				if (Permutations is not null && mesh.VariantMaterialIndex != -1)
				{
					RenderHelpers.Profile($"{Feature} {Hash} Permutations");

					var index = Permutations.CalculatePermutationIndexFast();
					var overrideIndex = Permutations.OverrideIndex;

					var newIndex = overrideIndex != -1 ? overrideIndex : (index != null ? index.Value : 0);
					if (mesh.PermutationMaterialIndex != newIndex)
					{
						mesh.PermutationMaterialIndex = newIndex;
						var mapEntry = MaterialRangeMap[mesh.VariantMaterialIndex];
						var mat = MaterialMap[mapEntry.MaterialStartIndex + (mesh.PermutationMaterialIndex % mapEntry.MaterialCount)];
						mesh.Material = AssetManager.GetInstance().GetOrCreateMaterial(mat);

						renderer.EntityObjectChannels?.UpdateChannels(mat);
					}
					RenderHelpers.EndProfile();
				}


				mesh.Draw(renderer);
			}

		}
		RenderHelpers.EndProfile();
	}

	public Buffer _skeletonVB;
	public void RenderSkeleton(CharmRenderer renderer)
	{
		if ((IsChild && !renderer.Viewport.ShowEntChildren) || !Visible)
			return;

		if (Bones is null || Bones.Count == 0)
			return;

		if (_skeletonVB is null)
		{
			var vbDesc = new BufferDescription
			{
				Usage = ResourceUsage.Dynamic,
				SizeInBytes = Utilities.SizeOf<Vector4>() * (Bones.Count * 2),
				BindFlags = BindFlags.VertexBuffer,
				CpuAccessFlags = CpuAccessFlags.Write
			};

			_skeletonVB = new Buffer(renderer.Device, vbDesc);
		}

		Vector4[] lineVertices = new Vector4[Bones.Count * 2];
		int v = 0;
		for (int i = 0; i < Bones.Count; i++)
		{
			int parentIndex = Bones[i].ParentNodeIndex;
			if (parentIndex < 0)
				continue;

			var bone = Bones[i];
			var parent = Bones[parentIndex];

			// stops lines coming from the root/pedestal bone
			if (parentIndex > 0)
			{
				lineVertices[v++] = new Vector4(bone.DefaultObjectSpaceTransform.Translation, 1);
				lineVertices[v++] = new Vector4(parent.DefaultObjectSpaceTransform.Translation, 1);
			}

			// not the best option but it works, would be better to use MeshBuilder to make them all one mesh
			renderer.RenderSphere(new Transform
			{
				Position = bone.DefaultObjectSpaceTransform.Translation,
				Scale = new(0.01f),
				Quaternion = GlobalTransforms[0].Quaternion
			}, new(0.5f, 0, 0, 1), offset: new Transform
			{
				Position = GlobalTransforms[0].Position + TransformOffset.Position,
				Quaternion = TransformOffset.Quaternion
			});
		}

		DataBox dataBox = renderer.Context.MapSubresource(_skeletonVB, 0, MapMode.WriteDiscard, MapFlags.None);
		try
		{
			Utilities.Write(dataBox.DataPointer, lineVertices, 0, lineVertices.Length);
		}
		finally
		{
			renderer.Context.UnmapSubresource(_skeletonVB, 0);
		}

		renderer.TempScopes.UpdateRigidModelScopeCustom(renderer.Context, GlobalTransforms[0], TransformOffset);

		Vector4 col = new(1f, 0f, 0f, 1f);
		renderer.Context.UpdateSubresource(ref col, renderer._debugPSCB);
		renderer.Context.PixelShader.SetConstantBuffer(0, renderer._debugPSCB);

		renderer.Context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_skeletonVB, Utilities.SizeOf<Vector4>(), 0));
		renderer.Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
		renderer.Context.Draw(lineVertices.Length, 0);
	}

	public override void Dispose()
	{
		Investment?.Dispose();
		Investment = null;

		foreach (var mesh in _meshes)
		{
			mesh.Dispose();
		}
		_meshes?.Clear();
		_skeletonVB?.Dispose();
		_skeletonVB = null;

		base.Dispose();
	}
}

public class MeshPartData : GpuResource
{
	public MeshPartData()
	{
	}

	public IndexBuffer? IndexBuffer;
	public VertexBuffer? VertexBuffer0;
	public VertexBuffer? VertexBuffer1;
	public VertexBuffer? VertexBuffer2;
	public VertexBuffer? VertexBuffer3;
	public int IndexCount;
	public int IndexOffset;

	public TfxRenderStage RenderStage;
	public PrimitiveTopology Topology;
	public MaterialData Material;
	public InputLayout InputLayout;

	public Vector4 MeshScale;
	public Vector4 MeshTransform;
	public Vector4 MeshUVTransform;
	public int MaxColorIndex;
	public int GroupIndex;
	public int VariantMaterialIndex;
	public int PermutationMaterialIndex = -1;

	public void Draw(CharmRenderer renderer)
	{
		Bind(renderer);
		renderer.Context.DrawIndexed(IndexCount, IndexOffset, 0);
	}

	public void DrawInstanced(CharmRenderer renderer, int instanceCount)
	{
		Bind(renderer);
		renderer.Context.DrawIndexedInstanced(IndexCount, instanceCount, IndexOffset, 0, 0);
	}

	private void Bind(CharmRenderer renderer)
	{
		renderer.Context.InputAssembler.InputLayout = InputLayout;
		renderer.Context.InputAssembler.PrimitiveTopology = Topology;

		IndexBuffer.Bind(renderer.Context);
		VertexBuffer0?.Bind(renderer.Context, 0);
		VertexBuffer1?.Bind(renderer.Context, 1);
		VertexBuffer2?.Bind(renderer.Context, 2);
		if (Material.Skinned)
			VertexBuffer3?.Bind(renderer.Context, -1, 1);

		Material?.Bind(renderer);
	}

	public override void Dispose()
	{
		VertexBuffer0?.Dispose();
		VertexBuffer1?.Dispose();
		VertexBuffer2?.Dispose();
		VertexBuffer3?.Dispose();
		IndexBuffer?.Dispose();
		InputLayout?.Dispose();

		AssetManager.GetInstance().ReleaseMaterial(Material.Hash);
		Material = null;

		VertexBuffer0 = null;
		VertexBuffer1 = null;
		VertexBuffer2 = null;
		VertexBuffer3 = null;
		IndexBuffer = null;
		InputLayout = null;

		base.Dispose();
	}
}

public class MaterialData : GpuResource
{
	public FileHash Hash;
	public StateSelection States;
	public List<Tiger.TfxScope> UsedScopes;

	public TechniqueStage Vertex;
	public TechniqueStage Pixel;
	public TechniqueStage Compute;

	// temp, for vs override
	public bool Skinned = false;
	public bool UsesVertexColor = false;
	public bool UsesGearDye = false;

	public int RefCount;

	public MaterialData(DeviceContext context, Material material)
	{
		Hash = material.Hash;
		States = material.RenderStates;
		UsedScopes = material.EnumerateScopes().ToList();
		Skinned = UsedScopes.Contains(Tiger.TfxScope.SKINNING);
		UsesGearDye = UsedScopes.Contains(Tiger.TfxScope.GEAR_DYE_012);

		if (material.Vertex.Shader != null)
			Vertex = new TechniqueStage(context, material.Vertex, ShaderStage.Vertex, material.Hash);

		if (material.Pixel.Shader != null)
			Pixel = new TechniqueStage(context, material.Pixel, ShaderStage.Pixel, material.Hash);

		if (material.Compute.Shader != null)
			Compute = new TechniqueStage(context, material.Compute, ShaderStage.Compute, material.Hash);
	}

	public void Bind(CharmRenderer renderer)
	{
		Vertex?.Bind(renderer);
		if (Skinned)
		{
			if (UsesGearDye)
			{
				if (UsesVertexColor)
					renderer.Context.VertexShader.Set(AssetManager.GetInstance().InvestmentOverrideVS_VC);
				else
					renderer.Context.VertexShader.Set(AssetManager.GetInstance().InvestmentOverrideVS_NoVC);
			}
			else
			{
				if (UsesVertexColor)
					renderer.Context.VertexShader.Set(AssetManager.GetInstance().EntityOverrideVS_VC);
				else
					renderer.Context.VertexShader.Set(AssetManager.GetInstance().EntityOverrideVS_NoVC);
			}
		}

		Pixel?.Bind(renderer);
		Compute?.Bind(renderer);

		var states = renderer.CMD.CurrentState.Select(States);
		renderer.CMD.States.CreateStates(renderer.Context, states);
	}

	public async Task<Vector4[]> GetEvaluated(CharmRenderer renderer)
	{
		return await Pixel?.GetEvaluated(renderer);
	}

	public void AddRef()
	{
		RefCount++;
	}

	public bool Release()
	{
		RefCount--;
		return RefCount <= 0;
	}

	public override void Dispose()
	{
		RefCount = 0;
		Vertex?.Dispose();
		Pixel?.Dispose();

		base.Dispose();
	}
}

public class TechniqueStage : GpuResource
{
	public IShader Shader { get; set; }
	public ShaderStage Stage { get; set; }
	public Constants Constants { get; set; }
	public string DebugName { get; set; }

	public TechniqueStage(DeviceContext context, SMaterialShader shader, ShaderStage stage, FileHash materialHash)
	{
		Stage = stage;
		Constants = new(context, shader, stage, materialHash);
		DebugName = $"TechniqueStage {materialHash}";

		using (var stream = new MemoryStream(shader.Shader.GetBytecode()))
		{
			var shaderByteCode = SharpDX.D3DCompiler.ShaderBytecode.Load(stream);
			Shader = ShaderFactory.CreateShader(context, stage, shaderByteCode, materialHash, shader.Shader.Hash);
		}
	}

	public void Bind(CharmRenderer renderer)
	{
		Shader?.Bind(renderer.Context);
		Constants?.Bind(renderer, Stage);
	}

	public async Task<Vector4[]> GetEvaluated(CharmRenderer renderer)
	{
		return await Constants?.GetEvaluated(renderer);
	}

	public override void Dispose()
	{
		Shader?.Dispose();
		Shader = null;
		Constants?.Dispose();
		Constants = null;

		base.Dispose();
	}
}

public class Constants : GpuResource
{
	public Buffer Buffer;
	public int Slot;

	public SMaterialShader Shader;
	public TfxBytecodeInterpreter? BytecodeInterpreter;

	public Vector4[] ConstantValues;
	public Vector4[] BytecodeConstants;
	public List<SamplerState> Samplers = new();
	public Dictionary<uint, TextureAsset> Textures = new();
	public string DebugName { get; set; }

	public Constants(string debugName)
	{
		DebugName = debugName;
	}

	public Constants(DeviceContext context, SMaterialShader shader, ShaderStage stage, FileHash materialHash)
	{
		DebugName = $"Constants {materialHash}";

		if (shader.GetCBuffer0().Count != 0)
		{
			var cbuffer = shader.GetCBuffer0().Select(x => new System.Numerics.Vector4(x.X, x.Y, x.Z, x.W)).ToArray();
			Buffer = new Buffer(context.Device, new BufferDescription
			{
				SizeInBytes = Utilities.SizeOf<System.Numerics.Vector4>() * cbuffer.Length,
				Usage = ResourceUsage.Dynamic,
				BindFlags = BindFlags.ConstantBuffer,
				CpuAccessFlags = CpuAccessFlags.Write,
				OptionFlags = ResourceOptionFlags.None,
				StructureByteStride = 0
			});
			Buffer.DebugName = $"{materialHash} Buffer";
			ConstantValues = cbuffer;
			context.UpdateSubresource(cbuffer, Buffer);
		}
		Shader = shader;
		Slot = shader.BufferSlot;
		Samplers = AssetManager.GetInstance().CreateSamplers(shader);
		Textures = AssetManager.GetInstance().CreateTextures(shader);

		BytecodeConstants = shader.TFX_Bytecode_Constants.Select(x => new System.Numerics.Vector4(x.Vec.X, x.Vec.Y, x.Vec.Z, x.Vec.W)).ToArray();
		BytecodeInterpreter = new TfxBytecodeInterpreter(TfxBytecodeOp.ParseAll(shader.TFX_Bytecode));
		BytecodeInterpreter.Name = $"Technique {materialHash} : {stage}";
	}

	public async Task Bind(CharmRenderer renderer, ShaderStage stage)
	{
		switch (stage)
		{
			case ShaderStage.Vertex when Slot != -1:
				renderer.Context.VertexShader.SetConstantBuffer(Slot, Buffer);
				break;

			case ShaderStage.Pixel when Slot != -1:
				renderer.Context.PixelShader.SetConstantBuffer(Slot, Buffer);
				break;

			case ShaderStage.Compute when Slot != -1:
				renderer.Context.ComputeShader.SetConstantBuffer(Slot, Buffer);
				break;
		}

		foreach (var tex in Textures)
		{
			switch (stage)
			{
				case ShaderStage.Vertex:
					renderer.Context.VertexShader.SetShaderResource((int)tex.Key, tex.Value.SRV);
					break;

				case ShaderStage.Pixel:
					renderer.Context.PixelShader.SetShaderResource((int)tex.Key, tex.Value.SRV);
					break;

				case ShaderStage.Compute:
					renderer.Context.ComputeShader.SetShaderResource((int)tex.Key, tex.Value.SRV);
					break;
			}
		}

		if (BytecodeInterpreter == null)
			return;

		var evaluated = await GetEvaluated(renderer);

		if (Buffer == null)
			return;

		DataBox dataBox = renderer.Context.MapSubresource(Buffer, 0, MapMode.WriteDiscard, MapFlags.None);
		try
		{
			Utilities.Write(dataBox.DataPointer, evaluated, 0, evaluated.Length);
		}
		finally
		{
			renderer.Context.UnmapSubresource(Buffer, 0);
		}
	}

	public void BindTextures(DeviceContext context, ShaderStage stage)
	{
		foreach (var tex in Textures)
		{
			switch (stage)
			{
				case ShaderStage.Vertex:
					context.VertexShader.SetShaderResource((int)tex.Key, tex.Value.SRV);
					break;

				case ShaderStage.Pixel:
					context.PixelShader.SetShaderResource((int)tex.Key, tex.Value.SRV);
					break;
			}
		}
	}

	public async Task<Vector4[]> GetEvaluated(CharmRenderer renderer)
	{
		var evaluated = await BytecodeInterpreter.EvaluateAsync(
			renderer,
			ConstantValues,
			BytecodeConstants,
			Shader,
			Samplers,
			renderer.EntityObjectChannels,
			globalChannels: renderer.World.GlobalChannels);

		return evaluated;
	}

	public override void Dispose()
	{
		Buffer?.Dispose();
		Buffer = null;

		foreach (var tex in Textures) // De-frefs textures, disposing is handled by AssetManager
		{
			AssetManager.GetInstance().ReleaseTexture(tex.Value);
		}
		Textures.Clear();

		foreach (var samp in Samplers)
		{
			samp?.Dispose();
		}
		Samplers.Clear();

		Buffer = null;
		BytecodeInterpreter = null;
		ConstantValues = null;
		BytecodeConstants = null;

		base.Dispose();
	}
}
