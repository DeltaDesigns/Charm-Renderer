using Arithmic;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Animation;
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
    public MaterialPermutation Permutations;
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
            // This works fine but some entity bounding boxes just dont feel good to orbit around
            LocalBoundingBox = entity.ModelParent.GetBoundingBox().CreateFrom(); // Is entity scale actually not used for bb calc?

            var parts = entity.LoadModel(ExportDetailLevel.MostDetailed, LoadLevel.Full);
            CreateMesh(context, parts.Cast<MeshPart>().ToList(), TfxFeatureRenderer.DynamicObjects);

            if (renderType == RenderType.Full)
            {
                using TigerReader reader = entity.ModelParent.GetReader();
                Permutations = entity.ModelParent.MaterialPermutations;
                MaterialMap = entity.ModelParent.Reader.ExternalMaterials.Enumerate(reader).Select(x => x.Material).ToList();
                MaterialRangeMap = entity.ModelParent.Reader.ExternalMaterialsMap.Enumerate(reader).ToList();
            }

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
            {
                using TigerReader reader = entity.PhysicsModelParent.GetReader();
                obj.Permutations = entity.PhysicsModelParent.MaterialPermutations;
                obj.MaterialMap = entity.PhysicsModelParent.Reader.ExternalMaterials.Enumerate(reader).Select(x => x.Material).ToList();
                obj.MaterialRangeMap = entity.PhysicsModelParent.Reader.ExternalMaterialsMap.Enumerate(reader).ToList();
            }

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

                renderer.TempScopes.UpdateRigidModelScope(renderer.Context, mesh, GlobalTransforms, TransformOffset, attachment);
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

    // ---- animation playback ---------------------------------------------------------------------------------

    private SkinningFrames? _skinning;   // the clip that is playing
    private SkinningFrames? _layer;      // optional clip blended over it
    private Buffer? _boneBuffer;
    private ShaderResourceView? _boneSRV;
    private float[]? _boneRows;
    private double _time;                // seconds
    private int _lastTimeFrame = -1;
    private int _lastUploadFrame = -1;
    private volatile bool _poseDirty;
    private List<Tiger.Schema.Animation.Animation>? _animations;
    private int _animationIndex = -1;

    // Requests made from other threads, applied on the render thread where the GPU resources live
    private readonly object _animationLock = new();
    private bool _hasBaseRequest, _hasLayerRequest;
    private SkinningFrames? _baseRequest, _layerRequest;

    public bool IsPlaying = true;
    public bool LoopAnimation = true;
    public float AnimationSpeed = 1f;
    /// <summary>How much of the layer clip is blended over the playing clip, from 0 to 1.</summary>
    public float LayerWeight = 0.5f;

    public bool IsAnimating => _skinning != null;
    public bool HasLayer => _layer != null;
    public double AnimationDuration => _skinning?.Duration ?? 0;

    public double AnimationTime
    {
        get => _time;
        set
        {
            _time = Math.Max(0, value);
            _poseDirty = true;
        }
    }

    /// <summary>Whether this object has skinned parts with weights and an entity that can be animated.</summary>
    public bool CanAnimate => Entity?.Skeleton != null && Entity.RuntimeRig != null && _meshes.Any(x => x.HasSkinWeights);

    /// <summary>Every animation the entity has, without duplicates.</summary>
    public IReadOnlyList<Tiger.Schema.Animation.Animation> GetAvailableAnimations()
    {
        if (_animations != null)
            return _animations;

        Log.Debug($"Entity {Entity.Hash}, Rig {Entity.RuntimeRig?.Hash}, Skele {Entity.Skeleton?.Hash}");

        var animations = new List<Tiger.Schema.Animation.Animation>();
        if (Entity?.Animations != null)
        {
            HashSet<FileHash> seen = new();
            foreach (EntityAnimations entityAnimations in Entity.Animations)
            {
                entityAnimations.GetAllAnimations();
                foreach (var animation in entityAnimations.AnimationTags)
                {
                    if (seen.Add(animation.Hash))
                        animations.Add(animation);
                }
            }
        }

        // one fixed order, shared by the dropdown and by stepping with next / previous, so an index means the same clip
        _animations = animations.OrderBy(x => x.Hash).ToList();
        return _animations;
    }

    /// <summary>
    /// Loads and evaluates an animation for this object's rig and skeleton. This can take a while, so it is safe to run
    /// off the render thread. Returns null with the reason when the animation cannot be used.
    /// </summary>
    public SkinningFrames? BuildAnimation(
        Tiger.Schema.Animation.Animation animation, out string? error, RetargetMode mode = RetargetMode.Auto)
    {
        error = null;
        if (!CanAnimate)
        {
            error = "This object has no rig to animate";
            return null;
        }
        animation.LoadAnimation();
        var frames = AnimationSkinning.Build(animation, Entity.RuntimeRig!, Entity.Skeleton!, mode: mode);
        Log.Info($"Playing animation {animation.Hash}, {frames.FrameCount} frames");
        return frames;
    }

    public void SetAnimation(SkinningFrames? frames)
    {
        lock (_animationLock)
        {
            _hasBaseRequest = true;
            _baseRequest = frames;
        }
    }

    public void SetLayer(SkinningFrames? frames)
    {
        lock (_animationLock)
        {
            _hasLayerRequest = true;
            _layerRequest = frames;
        }
    }

    public void MarkPoseDirty() => _poseDirty = true;

    public int PlayNextAnimation(CharmRenderer renderer, bool previous = false, int? currentIndex = null)
    {
        var animations = GetAvailableAnimations();
        int count = animations.Count;
        if (count == 0)
            return -1;

        if (currentIndex.HasValue)
            _animationIndex = currentIndex.Value;

        int step = previous ? -1 : 1;
        if (_animationIndex < 0 || _animationIndex >= count)
            _animationIndex = previous ? 0 : -1;

        for (int attempt = 0; attempt < count; attempt++)
        {
            _animationIndex = ((_animationIndex + step) % count + count) % count;
            var animation = animations[_animationIndex];

            SkinningFrames? frames = BuildAnimation(animation, out string? error);
            if (frames == null)
            {
                Log.Warning($"Skipping animation {animation.Hash}: {error}");
                continue;
            }

            SetAnimation(frames);
            return _animationIndex;
        }

        return -1;
    }

    private void StartAnimation(CharmRenderer renderer, SkinningFrames frames)
    {
        // a different skeleton size needs new buffers, and a layer made for another size no longer fits
        if (_boneBuffer == null || _boneRows == null || _boneRows.Length != frames.BoneCount * SkinningFrames.FloatsPerBone)
        {
            ReleaseBoneBuffer();

            int rows = frames.BoneCount * 3;
            _boneBuffer = new Buffer(renderer.Device, new BufferDescription
            {
                SizeInBytes = rows * 16,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.Write,
                OptionFlags = ResourceOptionFlags.BufferStructured,
                StructureByteStride = 16,
            });
            _boneSRV = new ShaderResourceView(renderer.Device, _boneBuffer);
            _boneRows = new float[frames.BoneCount * SkinningFrames.FloatsPerBone];
            _layer = null;
        }

        _time = 0;
        _lastTimeFrame = -1;
        _lastUploadFrame = -1;
        _poseDirty = true;
        _skinning = frames;

        foreach (var mesh in _meshes)
            mesh.BoneRowsSRV = _boneSRV;
    }

    public void StopAnimation()
    {
        _skinning = null;
        _layer = null;
        foreach (var mesh in _meshes)
            mesh.BoneRowsSRV = null;

        ReleaseBoneBuffer();
    }

    private void ReleaseBoneBuffer()
    {
        _boneSRV?.Dispose();
        _boneBuffer?.Dispose();
        _boneSRV = null;
        _boneBuffer = null;
        _boneRows = null;
    }

    private void ApplyAnimationRequests(CharmRenderer renderer)
    {
        bool hasBase, hasLayer;
        SkinningFrames? newBase, newLayer;
        lock (_animationLock)
        {
            hasBase = _hasBaseRequest;
            hasLayer = _hasLayerRequest;
            newBase = _baseRequest;
            newLayer = _layerRequest;
            _hasBaseRequest = _hasLayerRequest = false;
            _baseRequest = _layerRequest = null;
        }

        if (hasBase)
        {
            if (newBase == null)
                StopAnimation();
            else
                StartAnimation(renderer, newBase);
        }

        if (hasLayer)
        {
            if (newLayer == null || _skinning == null)
            {
                _layer = null;
            }
            else if (newLayer.BoneCount == _skinning.BoneCount)
            {
                _layer = newLayer;
            }
            else
            {
                Log.Warning("The layer animation was made for a different skeleton and was ignored");
                _layer = null;
            }
            _poseDirty = true;
        }
    }

    private void UpdateAnimation(CharmRenderer renderer)
    {
        ApplyAnimationRequests(renderer);

        if (_skinning == null || _boneBuffer == null || _boneRows == null)
            return;

        int frameIndex = renderer.FrameIndex;
        if (frameIndex != _lastTimeFrame)
        {
            _lastTimeFrame = frameIndex;
            if (IsPlaying)
                AdvanceTime(renderer.DeltaTime);
        }

        if (frameIndex == _lastUploadFrame && !_poseDirty)
            return;
        _lastUploadFrame = frameIndex;
        _poseDirty = false;

        double frame = _time * _skinning.FrameRate;
        if (_layer != null && LayerWeight > 0f)
            SkinningFrames.Blend(_skinning, frame, _layer, _time * _layer.FrameRate, LayerWeight, LoopAnimation, _boneRows);
        else
            _skinning.Sample(frame, LoopAnimation, _boneRows);

        DataBox dataBox = renderer.Context.MapSubresource(_boneBuffer, 0, MapMode.WriteDiscard, MapFlags.None);
        try
        {
            Utilities.Write(dataBox.DataPointer, _boneRows, 0, _boneRows.Length);
        }
        finally
        {
            renderer.Context.UnmapSubresource(_boneBuffer, 0);
        }
    }

    private void AdvanceTime(double deltaSeconds)
    {
        _time += deltaSeconds * AnimationSpeed;

        double duration = AnimationDuration;
        if (duration <= 0)
            return;

        if (LoopAnimation)
        {
            _time %= duration;
            if (_time < 0)
                _time += duration;
        }
        else if (_time >= duration)
        {
            _time = duration;
            IsPlaying = false;
        }
    }

    public bool TryGetBoneMotion(CharmRenderer renderer, int bone, out System.Numerics.Matrix4x4 motion)
    {
        motion = System.Numerics.Matrix4x4.Identity;

        UpdateAnimation(renderer);

        if (_skinning == null || _boneRows == null || bone < 0 || (bone + 1) * SkinningFrames.FloatsPerBone > _boneRows.Length)
            return false;

        int o = bone * SkinningFrames.FloatsPerBone;
        float[] r = _boneRows;
        motion = new System.Numerics.Matrix4x4(
            r[o + 0], r[o + 4], r[o + 8], 0,
            r[o + 1], r[o + 5], r[o + 9], 0,
            r[o + 2], r[o + 6], r[o + 10], 0,
            r[o + 3], r[o + 7], r[o + 11], 1);
        return true;
    }

    private (float X, float Y, float Z) GetBonePosition(int index)
    {
        var t = Bones[index].DefaultObjectSpaceTransform.Translation;
        if (_skinning == null || _boneRows == null || (index + 1) * SkinningFrames.FloatsPerBone > _boneRows.Length)
            return (t.X, t.Y, t.Z);

        int o = index * SkinningFrames.FloatsPerBone;
        float[] r = _boneRows;
        return (
            r[o + 0] * t.X + r[o + 1] * t.Y + r[o + 2] * t.Z + r[o + 3],
            r[o + 4] * t.X + r[o + 5] * t.Y + r[o + 6] * t.Z + r[o + 7],
            r[o + 8] * t.X + r[o + 9] * t.Y + r[o + 10] * t.Z + r[o + 11]);
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
