using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;
using Buffer = SharpDX.Direct3D11.Buffer;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public partial class RenderObject : GpuResource
{
    public FileHash Hash;
    public TfxFeatureRenderer Feature;
    public MeshType MeshType = MeshType.Normal;
    public SkinningType SkinningType = SkinningType.None;
    public RenderStageSubscription Stages;
    public bool Visible = true;
    public bool IsChild = false; // For entity children/attachments

    public RenderObject? AttachParent;
    public int AttachBone = -1;

    public HelixToolkit.Maths.BoundingBox LocalBoundingBox { get; set; }
    public HelixToolkit.Maths.BoundingBox BoundingBox { get; set; }
    public int InstanceCount = 1;

    private readonly List<MeshPartData> _meshes = new();
    public IReadOnlyList<MeshPartData> Meshes => _meshes;
    public IReadOnlyList<BoneNode> Bones;

    public InvestmentData Investment { get; set; }

    // todo, seperate different types (static, entity, etc) into own RenderObject type class
    public Entity Entity;
    public EntityMaterialVariants MaterialVariants;
    private int _materialVersion = -1;
    private int _materialStateVersion = -1;
    private int _materialOverride = -1;

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

    public void Create(DeviceContext context, RenderWorld world, Entity entity, InventoryItem inventoryItem = null, RenderType renderType = RenderType.Full)
    {
        Hash = entity.Hash;
        Entity = entity;
        if (inventoryItem is not null)
            Investment = new(context, entity, inventoryItem);

        if (entity.Skeleton is not null)
            Bones = entity.Skeleton.GetBoneNodes();

        if (entity.Model is not null)
        {
            SkinningType = (SkinningType)entity.ModelParent.Reader.UnkFlag;

            // This works fine but some entity bounding boxes just dont feel good to orbit around
            LocalBoundingBox = entity.ModelParent.GetBoundingBox().CreateFrom(); // Is entity scale actually not used for bb calc?

            var parts = entity.LoadModel(ExportDetailLevel.MostDetailed, LoadLevel.Full);
            CreateMesh(context, parts.Cast<MeshPart>().ToList(), TfxFeatureRenderer.DynamicObjects);

            if (renderType == RenderType.Full)
                MaterialVariants = entity.ModelParent.MaterialVariants;

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

            var parts = entity.LoadPhysicsModel(ExportDetailLevel.MostDetailed, LoadLevel.Full);
            obj.CreateMesh(context, parts.Cast<MeshPart>().ToList(), TfxFeatureRenderer.DynamicObjects);

            if (renderType == RenderType.Full)
                obj.MaterialVariants = entity.PhysicsModelParent.MaterialVariants;

            lock (world.WorldLock)
                world.RenderObjects.Enqueue(obj);
        }
    }

    public void Create(DeviceContext context, RenderWorld world, StaticMesh staticMesh)
    {
        Hash = staticMesh.Hash;
        var staticParts = staticMesh.LoadAllParts(ExportDetailLevel.MostDetailed);
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
            if (part.Material == null || part.Material.Vertex.Shader == null)
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
            if (part is DynamicMeshPart dynamicPart && meshData.VertexBuffer0 != null)
                meshData.CreateSkinWeights(context.Device, dynamicPart, meshData.VertexBuffer0.Length);

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

        UpdateAnimation(renderer);

        System.Numerics.Matrix4x4? attachment = null;
        if (AttachParent != null && AttachParent.TryGetBoneMotion(renderer, AttachBone, out System.Numerics.Matrix4x4 boneMotion))
            attachment = boneMotion;

        RenderHelpers.Profile($"{Feature} {Hash} Bind");

        MeshPartData[] meshes;
        lock (renderer.World.WorldLock)
            meshes = Meshes.ToArray();

        UpdateMaterialVariants(renderer, meshes);

        foreach (var mesh in meshes)
        {
            if (mesh.RenderStage != renderStage || (!renderer.GroupVisibility.IsVisible(this, mesh.GroupIndex) && !IsChild))
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

                mesh.UpdateGameSkinning(renderer);
                if (mesh.UsesGameSkinning)
                {
                    var cam = renderer.Externs.View.Position;
                    renderer.TempScopes.UpdateSkinnedModelScope(renderer.Context, mesh, GlobalTransforms, TransformOffset, attachment, _boneRows, _boneRows?.Length ?? 0, cam, SkinningType, _poseVersion);
                }

                else
                    renderer.TempScopes.UpdateRigidModelScope(renderer.Context, mesh, GlobalTransforms, TransformOffset, attachment);

                if (Investment is not null)
                    Investment.Bind(renderer);

                mesh.Draw(renderer);
            }

        }
        RenderHelpers.EndProfile();
    }

    // only redo the materials when the visible parts or the override change
    private void UpdateMaterialVariants(CharmRenderer renderer, MeshPartData[] meshes)
    {
        if (MaterialVariants is null)
            return;

        // physics models have their own parts, the applied state only covers the normal model
        var modelParent = MeshType == MeshType.Normal ? Entity?.ModelParent : null;

        int version = renderer.GroupVisibility.Version;
        int stateVersion = modelParent?.UpdateMeshGroups ?? 0;
        if (_materialVersion == version && _materialStateVersion == stateVersion && _materialOverride == MaterialVariants.OverrideIndex)
            return;

        _materialVersion = version;
        _materialStateVersion = stateVersion;
        _materialOverride = MaterialVariants.OverrideIndex;

        // most material conditions point at parts with no mesh (and no toggle), so those come from the applied state.
        // children dont use the group toggles so only the applied state counts for them
        Func<int, bool>? isEnabled;
        if (IsChild)
            isEnabled = modelParent is null ? null : modelParent.IsPartEnabled;
        else if (modelParent is null)
            isEnabled = x => renderer.GroupVisibility.IsVisible(this, x);
        else
            isEnabled = x => modelParent.IsPartEnabled(x) && renderer.GroupVisibility.IsVisible(this, x);

        RenderHelpers.Profile($"{Feature} {Hash} Material Variants");
        foreach (var mesh in meshes)
        {
            if (mesh.VariantMaterialIndex == -1)
                continue;

            var material = MaterialVariants.GetMaterial(mesh.VariantMaterialIndex, isEnabled);
            if (material is null || material.Hash == mesh.VariantMaterial?.Hash)
                continue;

            mesh.VariantMaterial = material;
            mesh.Material = AssetManager.Get().GetOrCreateMaterial(material);
            renderer.EntityObjectChannels?.UpdateChannels(material);
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

        UpdateAnimation(renderer);

        System.Numerics.Matrix4x4? attachment = null;
        if (AttachParent != null && AttachParent.TryGetBoneMotion(renderer, AttachBone, out System.Numerics.Matrix4x4 boneMotion))
            attachment = boneMotion;

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

            var (bx, by, bz) = GetBonePosition(i);

            if (parentIndex > 0)
            {
                var (px, py, pz) = GetBonePosition(parentIndex);
                lineVertices[v++] = new Vector4(bx, by, bz, 1);
                lineVertices[v++] = new Vector4(px, py, pz, 1);
            }

            // not the best option but it works, would be better to use MeshBuilder to make them all one mesh
            if (attachment is System.Numerics.Matrix4x4 motion)
            {
                var point = new System.Numerics.Vector3(bx, by, bz);
                var offsetPosition = TransformOffset.Position;

                point = System.Numerics.Vector3.Transform(point, TransformOffset.Quaternion.ToQuat())
                        + new System.Numerics.Vector3(offsetPosition.X, offsetPosition.Y, offsetPosition.Z);
                point = System.Numerics.Vector3.Transform(point, motion);

                var globalPosition = GlobalTransforms[0].Position;
                point = System.Numerics.Vector3.Transform(point, GlobalTransforms[0].Quaternion.ToQuat())
                        + new System.Numerics.Vector3(globalPosition.X, globalPosition.Y, globalPosition.Z);

                renderer.RenderSphere(new Transform
                {
                    Position = point,
                    Scale = new(0.01f),
                }, new(0.5f, 0, 0, 1));
            }
            else
            {
                renderer.RenderSphere(new Transform
                {
                    Position = new Tiger.Schema.Vector3(bx, by, bz),
                    Scale = new(0.01f),
                    Quaternion = GlobalTransforms[0].Quaternion
                }, new(0.5f, 0, 0, 1), offset: new Transform
                {
                    Position = GlobalTransforms[0].Position + TransformOffset.Position,
                    Quaternion = TransformOffset.Quaternion
                });
            }
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

        renderer.TempScopes.UpdateRigidModelScopeCustom(renderer.Context, GlobalTransforms[0], TransformOffset, attachment);

        Vector4 col = new(1f, 0f, 0f, 1f);
        renderer.Context.UpdateSubresource(ref col, renderer._debugPSCB);
        renderer.Context.PixelShader.SetConstantBuffer(0, renderer._debugPSCB);

        renderer.Context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_skeletonVB, Utilities.SizeOf<Vector4>(), 0));
        renderer.Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
        renderer.Context.Draw(lineVertices.Length, 0);
    }

    public override void Dispose()
    {
        StopAnimation();
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
