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
