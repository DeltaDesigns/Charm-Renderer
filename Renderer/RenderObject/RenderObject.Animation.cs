using Arithmic;
using SharpDX;
using SharpDX.Direct3D11;
using Tiger;
using Tiger.Schema.Animation;
using Tiger.Schema.Entity;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace Charm.Renderer;

public partial class RenderObject
{
    // Animation
    public bool IsPlaying = true;
    public bool LoopAnimation = true;
    public float AnimationSpeed = 1f;
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


    public Entity? AnimationEntity;
    public RenderObject? AnimationSource;
    public List<Animation>? OverrideAnimations;
    public TigerHash? WeaponAnimID;

    private Entity? AnimationOwner => AnimationEntity ?? Entity;

    // the animation rig holds more bones than this object, the object's bones are the last ones (first person rig + weapon)
    private bool AnimatesSubset => AnimationEntity?.Skeleton != null && Entity?.Skeleton != null
        && AnimationEntity.Skeleton.GetBoneNodes().Count != Entity.Skeleton.GetBoneNodes().Count;

    public bool CanAnimate
    {
        get
        {
            if (AnimationSource != null)
                return false;

            Entity? owner = AnimationOwner;
            if (owner?.Skeleton == null || owner.RuntimeRig == null || Entity?.Skeleton == null)
                return false;

            return _meshes.Any(x => x.HasSkinWeights || (AnimatesSubset && x.Material?.Skinned == true));
        }
    }

    private float[]? BoneRows => AnimationSource != null ? AnimationSource._boneRows : _boneRows;
    private int PoseVersion => AnimationSource != null ? AnimationSource._poseVersion : _poseVersion;

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
    private readonly object _animationLock = new();
    private bool _hasBaseRequest;
    private bool _hasLayerRequest;
    private SkinningFrames? _baseRequest, _layerRequest;


    private readonly Dictionary<FileHash, string> _animationNames = new();

    public string? GetAnimationName(Tiger.Schema.Animation.Animation animation)
    {
        GetAvailableAnimations();
        return _animationNames.GetValueOrDefault(animation.Hash);
    }

    public IReadOnlyList<Tiger.Schema.Animation.Animation> GetAvailableAnimations()
    {
        if (_animations != null)
            return _animations;

        if (OverrideAnimations != null)
        {
            _animations = OverrideAnimations.Where(x => x != null && x.Hash.IsValid()).DistinctBy(x => x.Hash).ToList();
            return _animations;
        }

        Entity? owner = AnimationOwner;
        Log.Debug($"Entity {owner?.Hash}, Rig {owner?.RuntimeRig?.Hash}, Skele {owner?.Skeleton?.Hash}");

        // animations with no matching control groups cant play
        RigProgram? program = null;
        if (owner?.RuntimeRig != null && owner.Skeleton != null)
            program = new RigProgram(owner.RuntimeRig, owner.Skeleton);


        var animations = new List<Tiger.Schema.Animation.Animation>();
        int unplayable = 0;
        if (owner?.Animations != null)
        {
            HashSet<FileHash> seen = new();
            foreach (EntityAnimations entityAnimations in owner.Animations)
            {
                entityAnimations.GetAllAnimations();

                Dictionary<FileHash, List<string>>? used = null;
                if (AnimatesSubset && WeaponAnimID != null)
                {
                    try
                    {
                        used = entityAnimations.GetWeaponAnimations(WeaponAnimID);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"Couldnt read the weapon animation selector: {e.Message}");
                    }
                }

                foreach (var animation in entityAnimations.AnimationTags)
                {
                    if (used != null && !used.ContainsKey(animation.Hash))
                        continue;

                    if (!seen.Add(animation.Hash))
                        continue;

                    if (program != null && program.CompatibleControlCount(animation.TagData.RigComponents) == 0)
                    {
                        unplayable++;
                        continue;
                    }

                    animations.Add(animation);

                    if (used != null && used[animation.Hash].Count > 0)
                        _animationNames[animation.Hash] = used[animation.Hash][0];
                }

                if (used != null)
                    Log.Debug($"Weapon tag {WeaponAnimID} uses {used.Count} of {entityAnimations.AnimationTags.Count} animations");
            }
        }

        if (unplayable > 0)
            Log.Info($"Skipped {unplayable} animations with no control groups matching rig {owner?.RuntimeRig?.Hash}");

        _animations = animations.OrderBy(x => x.TagData.LayerType).ThenBy(x => x.Hash).ToList();
        return _animations;
    }

    public SkinningFrames? BuildAnimation(
        Tiger.Schema.Animation.Animation animation)
    {
        if (!CanAnimate)
        {
            Log.Info("This object has no rig to animate");
            return null;
        }

        animation.LoadAnimation();
        Entity owner = AnimationOwner!;
        var frames = AnimatesSubset
            ? AnimationSkinning.BuildWeapon(animation, owner.RuntimeRig!, owner.Skeleton!, Entity.Skeleton!)
            : AnimationSkinning.Build(animation, owner.RuntimeRig!, owner.Skeleton!);
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

    private int _poseVersion;
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

            SkinningFrames? frames = BuildAnimation(animation);
            if (frames == null)
            {
                Log.Warning($"Skipping animation {animation.Hash}");
                continue;
            }

            SetAnimation(frames);
            renderer.Viewport.AnimationStatusText.Text = $"{frames.FrameCount} frames, {frames.Duration:0.00} s";
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
        if (AnimationSource != null)
        {
            AnimationSource.UpdateAnimation(renderer);
            foreach (var mesh in _meshes)
                mesh.BoneRowsSRV = AnimationSource._boneSRV;
            return;
        }

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

        _poseVersion++;

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

}
