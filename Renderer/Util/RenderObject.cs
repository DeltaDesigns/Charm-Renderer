using System.IO;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;
using Buffer = SharpDX.Direct3D11.Buffer;
using Material = Tiger.Schema.Shaders.Material;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

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

            lock (world.WorldLock)
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

            lock (world.WorldLock)
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

        lock (world.WorldLock)
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

                // eh, dont like this but some physics mesh use vertex color stored in the Old Weights buffer??
                VertexColorBuffer = part.VertexColorBuffer != null
                                ? VertexBuffer.Create(context, part.VertexColorBuffer)
                                : MeshType != MeshType.Normal && part.VertexOldWeightsBuffer != null
                                ? VertexBuffer.Create(context, part.VertexOldWeightsBuffer)
                                : null,

                VertexSkinningBuffer = part.VertexSkinningBuffer != null ? VertexBuffer.Create(context, part.VertexSkinningBuffer, ResourceOptionFlags.BufferAllowRawViews) : null,

                IndexCount = (int)part.IndexCount,
                IndexOffset = (int)part.IndexOffset,
                Topology = part.PrimitiveType == Tiger.PrimitiveType.Triangles
                    ? PrimitiveTopology.TriangleList
                    : PrimitiveTopology.TriangleStrip,

                MeshScale = part.MeshScale,
                MeshTransform = part.MeshTransform,
                MeshUVTransform = part.UVTransform,
                MaxColorIndex = part.MaxVertexColorIndex,
                Material = AssetManager.Get().GetOrCreateMaterial(part.Material),

                GroupIndex = part.GroupIndex,
                VariantMaterialIndex = part.VariantShaderIndex,
            };
            meshData.Material.UsesVertexColor = meshData.VertexColorBuffer != null && part.Material.Vertex.Shader.OutputSignatures.Any(x => x.RegisterIndex == 5 && x.SemanticIndex == 8);
            meshData.InputLayout = new InputLayout(context.Device, part.Material.Vertex.Shader.GetBytecode(), RenderHelpers.GetInputLayout(part.VertexLayoutIndex).ToArray());
            AddMesh(meshData);
        }
    }

    public void AddMesh(MeshPartData mesh)
    {
        _meshes.Add(mesh);
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
                        mesh.Material = AssetManager.Get().GetOrCreateMaterial(mat);

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

    public IndexBuffer IndexBuffer;
    public VertexBuffer VertexBuffer0;
    public VertexBuffer? VertexBuffer1;
    public VertexBuffer? VertexColorBuffer;
    public VertexBuffer? VertexSkinningBuffer;
    public VertexBuffer? VertexOldWeightsBuffer;
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
        VertexColorBuffer?.Bind(renderer.Context, 2);
        if (Material.Skinned)
            VertexSkinningBuffer?.Bind(renderer.Context, -1, 1);

        Material?.Bind(renderer);
    }

    public override void Dispose()
    {
        IndexBuffer?.Dispose();
        VertexBuffer0?.Dispose();
        VertexBuffer1?.Dispose();
        VertexColorBuffer?.Dispose();
        VertexSkinningBuffer?.Dispose();
        VertexOldWeightsBuffer?.Dispose();
        InputLayout?.Dispose();

        AssetManager.Get().ReleaseMaterial(Material.Hash.Hash32);
        Material = null;

        IndexBuffer = null;
        VertexBuffer0 = null;
        VertexBuffer1 = null;
        VertexColorBuffer = null;
        VertexSkinningBuffer = null;
        VertexOldWeightsBuffer = null;
        InputLayout = null;

        base.Dispose();
    }
}

public class MaterialData : GpuResource
{
    public FileHash Hash;
    public ShaderBindMode BindMode { get; set; }
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
        BindMode = material.BindMode;
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
        var states = renderer.CMD.States.DefaultState.Select(States);
        renderer.CMD.States.SetState(renderer.Context, states);

        switch (BindMode)
        {
            case ShaderBindMode.VertexPixel:
                renderer.Context.ComputeShader.Set(null);

                Vertex?.Bind(renderer);
                SetVSOverride(renderer);
                Pixel?.Bind(renderer);
                break;

            case ShaderBindMode.VertexOnly:
                renderer.Context.PixelShader.Set(null);
                renderer.Context.ComputeShader.Set(null);

                Vertex?.Bind(renderer);
                SetVSOverride(renderer);
                break;

            case ShaderBindMode.Compute:
                renderer.Context.VertexShader.Set(null);
                renderer.Context.PixelShader.Set(null);

                Compute?.Bind(renderer);
                break;

            default:
                throw new NotImplementedException($"BindMode {BindMode} not implemented.");
        }
    }

    private void SetVSOverride(CharmRenderer renderer)
    {
        if (Skinned)
        {
            if (UsesGearDye)
            {
                if (UsesVertexColor)
                    renderer.Context.VertexShader.Set(AssetManager.Get().InvestmentOverrideVS_VC);
                else
                    renderer.Context.VertexShader.Set(AssetManager.Get().InvestmentOverrideVS_NoVC);
            }
            else
            {
                if (UsesVertexColor)
                    renderer.Context.VertexShader.Set(AssetManager.Get().EntityOverrideVS_VC);
                else
                    renderer.Context.VertexShader.Set(AssetManager.Get().EntityOverrideVS_NoVC);
            }
        }
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

    public void Unbind(CharmRenderer renderer)
    {
        Shader?.Unbind(renderer.Context);
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
    public List<SamplerAsset> Samplers = new();
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
        Samplers = AssetManager.Get().CreateSamplers(shader);
        Textures = AssetManager.Get().CreateTextures(shader);

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
            AssetManager.Get().ReleaseTexture(tex.Value);
        }
        Textures.Clear();

        foreach (var sampler in Samplers)
        {
            AssetManager.Get().ReleaseSampler(sampler);
        }
        Samplers.Clear();

        Buffer = null;
        BytecodeInterpreter = null;
        ConstantValues = null;
        BytecodeConstants = null;

        base.Dispose();
    }
}
