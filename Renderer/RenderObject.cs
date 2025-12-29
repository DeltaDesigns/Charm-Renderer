using Arithmic;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using System.IO;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;
using static Charm.Renderer.CharmRenderer;
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
	public ShaderResourceView DiffusePlate { get; set; }
	public ShaderResourceView GStackPlate { get; set; }
	public ShaderResourceView NormalPlate { get; set; }
	public ShaderResourceView DyePlate { get; set; }

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
			DiffusePlate ??= AssetManager.GetInstance().CreateFromPlate(context, plates.AlbedoPlate).SRV;
			GStackPlate ??= AssetManager.GetInstance().CreateFromPlate(context, plates.NormalPlate).SRV;
			NormalPlate ??= AssetManager.GetInstance().CreateFromPlate(context, plates.GStackPlate).SRV;
			DyePlate ??= AssetManager.GetInstance().CreateFromPlate(context, plates.DyemapPlate)?.SRV ?? null;

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
				Log.Debug($"Created Dye{index} : {dye.ChannelHash}");
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

	public async void Bind(DeviceContext context)
	{
		if (!_hasData) return;

		context.PixelShader.SetShaderResource(0, DiffusePlate);
		context.PixelShader.SetShaderResource(1, GStackPlate);
		context.PixelShader.SetShaderResource(2, NormalPlate);
		context.PixelShader.SetShaderResource(3, DyePlate);

		if (_isChangingDyes || InvestmentDye0 is null)
			return;

		InvestmentDye0.Bind(context);
		InvestmentDye1.Bind(context);
		InvestmentDye2.Bind(context);

		var eval0 = await InvestmentDye0.Dye.GetEvaluated(context);
		var eval1 = await InvestmentDye1.Dye.GetEvaluated(context);
		var eval2 = await InvestmentDye2.Dye.GetEvaluated(context);

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
			DataBox dataBox = context.MapSubresource(InvestmentBuffer, 0, MapMode.WriteDiscard, MapFlags.None);
			try
			{
				Utilities.Write(dataBox.DataPointer, mergedCB, 0, mergedCB.Length);
			}
			finally
			{
				context.UnmapSubresource(InvestmentBuffer, 0);
			}
		}
		finally
		{
			context.PixelShader.SetConstantBuffer(7, InvestmentBuffer);
		}
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

		DiffusePlate?.Dispose();
		DiffusePlate = null;
		GStackPlate?.Dispose();
		GStackPlate = null;
		NormalPlate?.Dispose();
		NormalPlate = null;
		DyePlate?.Dispose();
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

public class RenderObject : GpuResource
{
	public FileHash Hash;
	public TfxFeatureRenderer MeshType;
	public AABB BoundingBox { get; set; }
	public int InstanceCount = 1;

	private readonly List<MeshPartData> _meshes = new();
	public IReadOnlyList<MeshPartData> Meshes => _meshes;
	public IReadOnlyList<BoneNode> Bones;

	public InvestmentData Investment { get; set; }

	public Transform[] GlobalTransforms = new Transform[]
	{
		new()
		{
			Position = new(0f, 0f, 0f),
			Quaternion = new(0f, 0f, 0f, 1f),
			Scale = Tiger.Schema.Vector3.One
		}
	};

	public void AddMesh(MeshPartData mesh)
	{
		_meshes.Add(mesh);
	}

	public void Create(DeviceContext context, Entity entity)
	{
		Hash = entity.Hash;
		var parts = entity.Load(ExportDetailLevel.MostDetailed);
		parts.AddRange(entity.GetEntityChildren()?.SelectMany(x => x.Load(ExportDetailLevel.MostDetailed)).ToList());
		CreateMesh(context, parts.Cast<MeshPart>().ToList(), TfxFeatureRenderer.DynamicObjects);
		if (entity.Skeleton is not null)
			Bones = entity.Skeleton.GetBoneNodes();

		// This works fine but some entity bounding boxes just dont feel good to orbit around
		if (entity.Model is not null)
		{
			AABB bb = entity.ModelParent.GetBoundingBox();
			//AABB bb = RenderHelpers.ComputeBoundingBox(parts.SelectMany(x => x.VertexPositions).ToList());
			var scale = entity.Model.Scale;
			var trans = entity.Model.Translation;
			BoundingBox = new()
			{
				Min = bb.Min * scale - trans,
				Max = bb.Max * scale + trans
			};
		}
	}

	public void Create(DeviceContext context, EntityModel entModel, TfxFeatureRenderer type)
	{
		Hash = entModel.Hash;
		var parts = entModel.Load(ExportDetailLevel.MostDetailed, null);
		CreateMesh(context, parts.Cast<MeshPart>().ToList(), type);
	}

	public void Create(DeviceContext context, StaticMesh staticMesh)
	{
		Hash = staticMesh.Hash;
		var staticParts = staticMesh.Load(ExportDetailLevel.MostDetailed);
		//var staticDecals = staticMesh.LoadDecals(ExportDetailLevel.MostDetailed);
		BoundingBox = RenderHelpers.ComputeBoundingBox(staticParts.SelectMany(x => x.VertexPositions).ToList());

		CreateMesh(context, staticParts.Cast<MeshPart>().ToList(), TfxFeatureRenderer.StaticObjects);
		//CreateMesh(context, staticDecals.Cast<MeshPart>().ToList(), TfxFeatureRenderer.Decals);
	}

	public void Create(DeviceContext context, Entity entity, InventoryItem inventoryItem)
	{
		Hash = entity.Hash;
		Investment = new(context, entity, inventoryItem);
		var parts = entity.Load(ExportDetailLevel.MostDetailed);
		CreateMesh(context, parts.Cast<MeshPart>().ToList(), TfxFeatureRenderer.Gear);
		if (entity.Skeleton is not null)
			Bones = entity.Skeleton.GetBoneNodes();

		//if (entities[0].Model is not null)
		//{
		//    AABB bb = entities[0].ModelParent.GetBoundingBox();
		//    //AABB bb = RenderHelpers.ComputeBoundingBox(parts.SelectMany(x => x.VertexPositions).ToList());
		//    var scale = entities[0].Model.Scale;
		//    var trans = entities[0].Model.Translation;
		//    BoundingBox = new()
		//    {
		//        Min = bb.Min * scale - trans,
		//        Max = bb.Max * scale + trans
		//    };
		//}
	}

	private void CreateMesh(DeviceContext context, List<MeshPart> parts, TfxFeatureRenderer meshType)
	{
		MeshType = meshType;
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
				Material = AssetManager.GetInstance().GetOrCreateMaterial(context, part.Material),
				GroupIndex = part.GroupIndex
			};

			meshData.Material.UsesVertexColor = part.VertexBuffer2 != null && part.Material.Vertex.Shader.OutputSignatures.Any(x => x.RegisterIndex == 5 && x.SemanticIndex == 8);
			meshData.InputLayout = new InputLayout(context.Device, part.Material.Vertex.Shader.GetBytecode(), RenderHelpers.GetInputLayout(part.VertexLayoutIndex).ToArray());

			AddMesh(meshData);
		}
	}

	public void Bind(CharmRenderer renderer, TfxRenderStage renderStage)
	{
		RenderHelpers.Profile($"{MeshType} {Hash} Bind");

		MeshPartData[] meshes;
		lock (renderer.World.WorldLock)
			meshes = Meshes.ToArray();

		foreach (var mesh in meshes)
		{
			if (mesh.RenderStage != renderStage)
				continue;

			if (MeshType == TfxFeatureRenderer.StaticObjects)
			{
				renderer.TempScopes.UpdateChunkModelScope(renderer.Context, mesh, GlobalTransforms);
				if (InstanceCount > 1)
					mesh.DrawInstanced(renderer.Context, InstanceCount);
				else
					mesh.Draw(renderer.Context);
			}
			else
			{
				renderer.TempScopes.UpdateRigidModelScope(renderer.Context, mesh, GlobalTransforms);
				if (Investment is not null)
					Investment.Bind(renderer.Context);

				mesh.Draw(renderer.Context);
			}

		}
		RenderHelpers.EndProfile();
	}

	Buffer skeletonVB;
	public void RenderSkeleton(CharmRenderer renderer)
	{
		if (Bones is null || Bones.Count == 0)
			return;

		if (skeletonVB is null)
		{
			var vbDesc = new BufferDescription
			{
				Usage = ResourceUsage.Dynamic,
				SizeInBytes = Utilities.SizeOf<Vector4>() * (Bones.Count * 2),
				BindFlags = BindFlags.VertexBuffer,
				CpuAccessFlags = CpuAccessFlags.Write
			};

			skeletonVB = new Buffer(renderer.Device, vbDesc);
		}

		Vector4[] lineVertices = new Vector4[Bones.Count * 2];
		int v = 0;
		for (int i = 0; i < Bones.Count; i++)
		{
			int parent = Bones[i].ParentNodeIndex;
			if (parent < 0)
				continue;

			lineVertices[v++] = new Vector4(Bones[i].DefaultObjectSpaceTransform.Translation, 1);
			lineVertices[v++] = new Vector4(Bones[parent].DefaultObjectSpaceTransform.Translation, 1);
		}

		DataBox dataBox = renderer.Context.MapSubresource(skeletonVB, 0, MapMode.WriteDiscard, MapFlags.None);
		try
		{
			Utilities.Write(dataBox.DataPointer, lineVertices, 0, lineVertices.Length);
		}
		finally
		{
			renderer.Context.UnmapSubresource(skeletonVB, 0);
		}

		renderer.TempScopes.UpdateRigidModelScopeCustom(renderer.Context, new MapTransform
		{
			Translation = Vector4.UnitW,
			Rotation = Vector4.Zero,
		});
		renderer.Context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(skeletonVB, Utilities.SizeOf<Vector4>(), 0));
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

	public void Draw(DeviceContext context)
	{
		Bind(context);
		context.DrawIndexed(IndexCount, IndexOffset, 0);
	}

	public void DrawInstanced(DeviceContext context, int instanceCount)
	{
		Bind(context);
		context.DrawIndexedInstanced(IndexCount, instanceCount, IndexOffset, 0, 0);
	}

	private void Bind(DeviceContext context)
	{
		context.InputAssembler.InputLayout = InputLayout;
		context.InputAssembler.PrimitiveTopology = Topology;

		IndexBuffer.Bind(context);
		VertexBuffer0?.Bind(context, 0);
		VertexBuffer1?.Bind(context, 1);
		VertexBuffer2?.Bind(context, 2);
		if (Material.Skinned)
			VertexBuffer3?.Bind(context, -1, 1);

		Material?.Bind(context);
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

	public void Bind(DeviceContext context)
	{
		Vertex?.Bind(context);
		if (Skinned)
		{
			if (UsesGearDye)
			{
				if (UsesVertexColor)
					context.VertexShader.Set(AssetManager.GetInstance().InvestmentOverrideVS_VC);
				else
					context.VertexShader.Set(AssetManager.GetInstance().InvestmentOverrideVS_NoVC);
			}
			else
			{
				if (UsesVertexColor)
					context.VertexShader.Set(AssetManager.GetInstance().EntityOverrideVS_VC);
				else
					context.VertexShader.Set(AssetManager.GetInstance().EntityOverrideVS_NoVC);
			}
		}

		Pixel?.Bind(context);
		Compute?.Bind(context);

		var states = CharmRenderer.Instance.CurrentState.Select(States);
		CharmRenderer.Instance.CreateStates(states);
	}

	public async Task<Vector4[]> GetEvaluated(DeviceContext context)
	{
		return await Pixel?.GetEvaluated(context);
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

	public void Bind(DeviceContext context)
	{
		Shader?.Bind(context);
		Constants?.Bind(context, Stage);
	}

	public async Task<Vector4[]> GetEvaluated(DeviceContext context)
	{
		return await Constants?.GetEvaluated(context);
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
		Samplers = AssetManager.GetInstance().CreateSamplers(context, shader);
		Textures = AssetManager.GetInstance().CreateTextures(context, shader);

		BytecodeConstants = shader.TFX_Bytecode_Constants.Select(x => new System.Numerics.Vector4(x.Vec.X, x.Vec.Y, x.Vec.Z, x.Vec.W)).ToArray();
		BytecodeInterpreter = new TfxBytecodeInterpreter(TfxBytecodeOp.ParseAll(shader.TFX_Bytecode));
		BytecodeInterpreter.Name = $"Technique {materialHash} : {stage}";
	}

	public async Task Bind(DeviceContext context, ShaderStage stage)
	{
		switch (stage)
		{
			case ShaderStage.Vertex when Slot != -1:
				context.VertexShader.SetConstantBuffer(Slot, Buffer);
				break;

			case ShaderStage.Pixel when Slot != -1:
				context.PixelShader.SetConstantBuffer(Slot, Buffer);
				break;

			case ShaderStage.Compute when Slot != -1:
				context.ComputeShader.SetConstantBuffer(Slot, Buffer);
				break;
		}

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

				case ShaderStage.Compute:
					context.ComputeShader.SetShaderResource((int)tex.Key, tex.Value.SRV);
					break;
			}
		}

		if (BytecodeInterpreter == null)
			return;

		var evaluated = await GetEvaluated(context);

		if (Buffer == null)
			return;

		DataBox dataBox = context.MapSubresource(Buffer, 0, MapMode.WriteDiscard, MapFlags.None);
		try
		{
			Utilities.Write(dataBox.DataPointer, evaluated, 0, evaluated.Length);
		}
		finally
		{
			context.UnmapSubresource(Buffer, 0);
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

	public async Task<Vector4[]> GetEvaluated(DeviceContext context)
	{
		var evaluated = await BytecodeInterpreter.EvaluateAsync(
			context,
			ConstantValues,
			BytecodeConstants,
			Shader,
			Samplers,
			Instance.EntityObjectChannels,
			globalChannels: Instance.World.GlobalChannels);

		return evaluated;
	}

	public override void Dispose()
	{
		Buffer?.Dispose();
		Buffer = null;

		foreach (var tex in Textures) // De-frefs textures, disposing is handled by AssetManager
		{
			AssetManager.GetInstance().ReleaseTexture(tex.Key);
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
