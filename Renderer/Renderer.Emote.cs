using Arithmic;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;
using Animation = Tiger.Schema.Animation.Animation;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    private const ulong SkeletonHash = 0xD9378260016E0000;

    private RenderObject? _emoteSkeleton;
    private readonly List<RenderObject> _emoteDuplicates = new();
    private readonly List<RenderObject> _emoteDrivers = new();
    private EmoteAnimations? _emote;
    private int _emoteRequest;

    public EmoteAnimations? CurrentEmote => _emote;

    public static bool IsArmor(InventoryItem? item) =>
        item != null && (item.IsArmor || item.IsArmorOrnament || item.Parent?.IsArmor == true);

    public async Task PlayEmote(EmoteAnimations emote)
    {
        int request = ++_emoteRequest;
        _emote = null;
        _emoteDrivers.Clear();

        List<RenderObject> drivers;
        try
        {
            drivers = await RunOnRenderThread(() => SetupEmote(emote));
        }
        catch (Exception e)
        {
            Log.Error($"Couldnt set up the emote: {e.Message}");
            return;
        }

        if (request != _emoteRequest || drivers.Count == 0)
            return;

        _emote = emote;
        _emoteDrivers.AddRange(drivers);

        // initiator first, then each participant in order
        List<List<(Animation Animation, bool Loop)>> clips = new();
        clips.Add(emote.Kind switch
        {
            EmoteKind.Looping when EmoteAnimations.IsValid(emote.Intro) => [(emote.Intro!, false), (emote.Loop!, true)],
            _ => [(emote.Loop!, true)],
        });

        foreach (var participant in emote.Participants.Take(drivers.Count - 1))
            clips.Add([(participant.Loop!, true)]);

        var sequences = await Task.Run(() => drivers.Select((driver, i) => BuildEmoteSteps(driver, clips[i])).ToList());
        if (request != _emoteRequest)
            return;

        for (int i = 0; i < drivers.Count; i++)
            drivers[i].PlaySequence(sequences[i]);
    }

    public async Task StopEmote()
    {
        if (_emote is null || _emoteDrivers.Count == 0)
            return;

        int request = ++_emoteRequest;
        var driver = _emoteDrivers[0];
        if (_emote.Kind != EmoteKind.Looping || !EmoteAnimations.IsValid(_emote.Outro))
        {
            foreach (var d in _emoteDrivers)
                d.SetAnimation(null);
            return;
        }

        var outro = _emote.Outro!;
        var steps = await Task.Run(() => BuildEmoteSteps(driver, [(outro, false)]));
        if (request != _emoteRequest)
            return;

        driver.PlaySequence(steps);
    }

    public void ClearEmote()
    {
        _emoteRequest++;
        _emoteDrivers.Clear();
        _emote = null;

        RunOnRenderThread(() =>
        {
            RestoreEmoteObjects();
            return true;
        }).ContinueWith(t => Log.Error($"Couldnt clear the emote: {t.Exception?.InnerException?.Message}"), TaskContinuationOptions.OnlyOnFaulted);
    }

    private List<RenderObject> SetupEmote(EmoteAnimations emote)
    {
        RestoreEmoteObjects();

        List<RenderObject> drivers = new();
        if (FindPlayerSkeleton() is not { CanAnimate: true } skeleton)
        {
            Log.Warning("No player skeleton to play the emote on.");
            return drivers;
        }

        _emoteSkeleton = skeleton;
        drivers.Add(skeleton);

        if (emote.Kind == EmoteKind.Multiplayer)
        {
            List<RenderObject> followers;
            lock (World.WorldLock)
                followers = World.RenderObjects.Where(x => x.AnimationSource == skeleton).ToList();

            foreach (var participant in emote.Participants)
            {
                var copy = DuplicateEmoteObject(skeleton, participant.Transform);
                foreach (var follower in followers)
                    DuplicateEmoteObject(follower, participant.Transform).AnimationSource = copy;

                drivers.Add(copy);
            }
        }

        return drivers;
    }

    private RenderObject? FindPlayerSkeleton()
    {
        FileHash playerSkeleton = new(Hash64Map.Get().GetHash32Checked(SkeletonHash));
        lock (World.WorldLock)
            return World.RenderObjects.FirstOrDefault(x => x.Hash.Equals(playerSkeleton) && x.MeshType == MeshType.Normal && !_emoteDuplicates.Contains(x));
    }

    private void FollowPlayerSkeleton(RenderObject skeleton)
    {
        if (skeleton.Entity?.Skeleton is not { } playerSkeleton)
            return;

        lock (World.WorldLock)
        {
            foreach (var obj in World.RenderObjects)
            {
                if (obj == skeleton || obj.IsChild || obj.MeshType != MeshType.Normal || !IsArmor(obj.Investment?.BaseItem))
                    continue;

                if (obj.AnimationEntity != null || obj.AnimationSource != null)
                    continue;

                if (obj.Entity?.Skeleton != null && !HasSameBones(obj.Entity.Skeleton, playerSkeleton))
                {
                    Log.Warning($"{obj.Hash}: skeleton {obj.Entity.Skeleton.Hash} doesnt match the player skeleton {playerSkeleton.Hash}, it wont follow its animations");
                    continue;
                }

                obj.AnimationSource = skeleton;
            }
        }
    }

    private static List<AnimationStep> BuildEmoteSteps(RenderObject driver, List<(Animation Animation, bool Loop)> clips)
    {
        List<AnimationStep> steps = new();
        foreach (var (animation, loop) in clips)
        {
            if (!EmoteAnimations.IsValid(animation))
                continue;

            try
            {
                if (driver.BuildAnimation(animation) is { } frames)
                    steps.Add(new AnimationStep(frames, loop));
            }
            catch (Exception e)
            {
                Log.Error($"Couldnt build emote animation {animation.Hash}: {e.Message}");
            }
        }

        return steps;
    }

    private static bool HasSameBones(EntitySkeleton a, EntitySkeleton b)
    {
        var x = a.GetBoneNodes();
        var y = b.GetBoneNodes();
        if (x.Count != y.Count)
            return false;

        for (int i = 0; i < x.Count; i++)
        {
            if (x[i].Hash.Hash32 != y[i].Hash.Hash32)
                return false;
        }

        return true;
    }

    private RenderObject DuplicateEmoteObject(RenderObject source, MapTransform placement)
    {
        RenderObject[] before;
        lock (World.WorldLock)
            before = World.RenderObjects.ToArray();

        Tiger.Schema.Vector4 rotation = source.TransformOffset.Quaternion;
        if (placement.Rotation.Magnitude >= 0.5f)
        {
            var q = placement.Rotation.ToQuat() * System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, MathF.PI);
            rotation = new Tiger.Schema.Vector4(q.X, q.Y, q.Z, q.W);
        }

        RenderObject copy = new();
        copy.TransformOffset = source.TransformOffset with
        {
            Position = source.TransformOffset.Position + placement.Translation.ToVec3(),
            Quaternion = rotation
        };
        copy.RigidBoneCount = source.RigidBoneCount;
        copy.Create(Context, World, source.Entity, source.Investment?.BaseItem);

        lock (World.WorldLock)
        {
            foreach (var added in World.RenderObjects.Except(before))
            {
                added.Visible = source.Visible;
                _emoteDuplicates.Add(added);
            }
        }

        return copy;
    }

    private void RestoreEmoteObjects()
    {
        HashSet<RenderObject> alive;
        lock (World.WorldLock)
            alive = World.RenderObjects.ToHashSet();

        if (_emoteSkeleton != null && alive.Contains(_emoteSkeleton))
            _emoteSkeleton.SetAnimation(null);

        var duplicates = _emoteDuplicates.Where(alive.Contains).ToHashSet();
        if (duplicates.Count > 0)
        {
            lock (World.WorldLock)
                World.RenderObjects = new Queue<RenderObject>(World.RenderObjects.Where(x => !duplicates.Contains(x)));

            foreach (var duplicate in duplicates)
                duplicate.Dispose();
        }

        _emoteSkeleton = null;
        _emoteDuplicates.Clear();
    }
}
