using System.Windows;
using Arithmic;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Animation;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    // TODO? Move these to RenderWorld
    public void LoadStatic(FileHash hash)
    {
        World?.Dispose();

        var staticMesh = FileResourcer.Get().GetFile<StaticMesh>(hash, shouldCache: false);
        RenderObject obj = new();
        obj.Create(Context, World, staticMesh);

        LookAtMesh(obj);
    }

    public void LoadEntity(Entity entity)
    {
        World?.Dispose();
        GroupVisibility.Clear();

        // todo? move away
        Viewport.CreateMeshGroups(entity);

        RenderObject obj = new();
        obj.Create(Context, World, entity);
        GroupVisibility.AddObject(obj);

        var children = entity.GetEntityChildren();
        foreach (var child in children)
        {
            if (child.Model is null)
                continue;

            RenderObject childObj = new();
            childObj.TransformOffset = new Transform
            {
                Quaternion = child.Model.RotationOffset,
                Position = child.Model.TranslationOffset.ToVec3()
            };

            childObj.Create(Context, World, child);
            childObj.IsChild = true;
            childObj.AttachParent = obj;
            childObj.AttachBone = child.Model.BoneAttachIndex;

            GroupVisibility.AddObject(childObj);
        }

        CreateObjectChannels(entity);
        LookAtMesh(obj); // base ent, no children
    }

    public void LoadInvestmentItem(InventoryItem item)
    {
        World?.Dispose();
        EntityObjectChannels = new(item);

        if (item.IsEmote || item.IsFinisher)
        {
            LoadEmote(item);
            return;
        }

        List<Entity> entities = Investment.Get().GetEntitiesFromHash(item);
        Entity? skele = Investment.Get().GetPatternEntityFromHash(item.Parent != null ? item.Parent.TagData.InventoryItemHash : item.TagData.InventoryItemHash);
        if (item.IsGhost)
        {
            var skeleHash = 0xDF3F359A2E9E0000;
            skele = new Entity(new FileHash(Hash64Map.Get().GetHash32Checked(skeleHash))); // 64 bit more permanent
        }

        if (skele != null && skele.Skeleton != null && entities.Any())
            entities[0].Skeleton = skele.Skeleton;

        bool isArmor = item.Parent != null ? item.Parent.IsArmor : item.IsArmor;
        bool isWeapon = item.Parent != null ? item.Parent.IsWeapon : item.IsWeapon;

        // weapons are animated by their first person rig, other items like sparrows by their pattern entity
        Entity? weaponAnimations = null;
        TigerHash? weaponAnimationTag = null;
        if (isWeapon && entities.Any() && entities[0].Skeleton != null)
        {
            try
            {
                weaponAnimations = Investment.Get().GetWeaponAnimationEntity(item, out weaponAnimationTag);
            }
            catch (Exception e)
            {
                Log.Error($"Couldnt get weapon animation entity: {e.Message}");
            }
        }

        Entity? animationEntity = weaponAnimations;
        if (animationEntity == null && !isWeapon && skele?.Skeleton != null && skele.RuntimeRig != null && skele.Animations?.Count > 0)
            animationEntity = skele;

        int weaponBones = weaponAnimations != null ? entities[0].Skeleton!.GetBoneNodes().Count : 0;
        RenderObject? animationSource = null;
        foreach (var ent in entities)
        {
            RenderObject obj = new();
            obj.RigidBoneCount = weaponBones;
            obj.Create(Context, World, ent, item);

            if (animationEntity == null)
                continue;

            if (animationSource == null)
            {
                obj.AnimationEntity = animationEntity;
                obj.WeaponAnimID = weaponAnimationTag;
                animationSource = obj;
            }
            else
            {
                obj.AnimationSource = animationSource;
            }
        }

        var combinedBB = RenderHelpers.CombineBBs(World.RenderObjects.Select(x => x.BoundingBox).ToList());
        World.LocalOverrideMainBB = combinedBB;
        World.OverrideMainBB = combinedBB;

        bool isSword = (item.Parent != null ? item.Parent.ItemTraits : item.ItemTraits).Any(x => x == DestinyTraitID.item_weapon_sword);
        bool isShip = item.IsShip || item.IsSparrow;
        LookAtBoundingBox(
            combinedBB,
            isWeapon ? -125f : (isShip ? 130f : 160f),
            isWeapon ? 13f : (isShip ? 20f : 10f),
            isWeapon ? (isSword ? 1f : 0.7f) : (isShip ? 0.7f : 1f));

        // load player/ghost skele after so its additional meshes dont get included in the main bounding box
        LoadPlayerSkeleton(item);

        ShouldShowObjectChannels();

        if (isWeapon)
        {
            Viewport.AtmosRotation = 0f;
            Viewport.TimeOfDay = 0.635f;
        }
        else if (isArmor)
        {
            Viewport.AtmosRotation = 0.17f;
            Viewport.TimeOfDay = 0.635f;
        }
    }

    private void LoadEmote(InventoryItem item)
    {
        List<Animation>? emote = null;
        emote = Investment.Get().GetEmote(item, item.IsFinisher);

        if (emote == null || emote.Count == 0)
        {
            Log.Error($"{item.Name} has no animations.");
            return;
        }

        Entity player = FileResourcer.Get().GetFile<Entity>(new FileHash(Hash64Map.Get().GetHash32Checked(SkeletonHash)));

        RenderObject obj = new();
        obj.OverrideAnimations = emote;
        obj.Create(Context, World, player);

        LookAtMesh(obj);
        ShouldShowObjectChannels();

        Viewport.AtmosRotation = 0.17f;
        Viewport.TimeOfDay = 0.635f;
    }

    public void LoadInvestmentItems(IEnumerable<InventoryItem> items)
    {
        World?.Dispose();
        EntityObjectChannels = new();

        foreach (var item in items.DistinctBy(x => x.ApiHash))
        {
            if (item.ArtArrangementIndex == -1)
                continue;

            List<Entity> entities = Investment.Get().GetEntitiesFromHash(item);
            Entity? skele = Investment.Get().GetPatternEntityFromHash(item.Parent != null ? item.Parent.TagData.InventoryItemHash : item.TagData.InventoryItemHash);
            if (skele != null && skele.Skeleton != null && entities.Count > 0)
                entities[0].Skeleton = skele.Skeleton;

            foreach (var ent in entities)
            {
                RenderObject obj = new();
                obj.Create(Context, World, ent, item);
            }

            EntityObjectChannels.AddObjectChannels(item);
        }

        var combinedBB = RenderHelpers.CombineBBs(World.RenderObjects.Select(x => x.BoundingBox).ToList());
        World.LocalOverrideMainBB = combinedBB;
        World.OverrideMainBB = combinedBB;
        LookAtBoundingBox(combinedBB, 160f, 15f, 0.85f);

        if (items.Where(x => x.IsArmor || x.IsArmorOrnament).Any()) // meh
            LoadPlayerSkeleton(items.Where(x => x.IsArmor || x.IsArmorOrnament).First());

        ShouldShowObjectChannels();
    }

    public void LoadPlayerSkeleton(InventoryItem item)
    {
        if (!item.IsGhost && !item.IsArmor && !item.IsArmorOrnament)
            return;

        var skeleHash = item.IsGhost ? 0xDF3F359A2E9E0000 : SkeletonHash;
        Entity skele = new Entity(new FileHash(Hash64Map.Get().GetHash32Checked(skeleHash))); // 64 bit more permanent
        EntityObjectChannels.AddObjectChannels(skele);

        RenderObject obj = new();
        obj.Create(Context, World, skele);

        if (!item.IsGhost)
        {
            FollowPlayerSkeleton(obj);
            AttachWeaponsToGrip(obj);
        }

        if (item.IsGhost)
        {
            EntityObjectChannels.SetObjectChannel(2309869224, 0f); // hack_int
            EntityObjectChannels.SetObjectChannel(1761166716, 0f); // scan_int
        }
    }

    private static bool IsWeapon(InventoryItem? item) =>
        item != null && (item.IsWeapon || item.IsWeaponOrnament || item.Parent?.IsWeapon == true);

    private void AttachWeaponsToGrip(RenderObject skeleton)
    {
        var bones = skeleton.Entity?.Skeleton?.GetBoneNodes();
        var grip = Helpers.Fnv1a32("b_r_grip");
        var gripIndex = bones?.FindIndex(x => x.Hash.Hash32 == grip) ?? -1;

        if (gripIndex < 0)
            return;

        var bind = bones![gripIndex].DefaultObjectSpaceTransform;
        lock (World.WorldLock)
        {
            foreach (var obj in World.RenderObjects)
            {
                if (obj == skeleton || obj.IsChild || !IsWeapon(obj.Investment?.BaseItem))
                    continue;

                obj.TransformOffset = obj.TransformOffset with
                {
                    Position = bind.Translation,
                    Quaternion = bind.QuaternionRotation
                };
                obj.AttachParent = skeleton;
                obj.AttachBone = gripIndex;
                obj.UpdateBoundingBox();
            }
        }
    }

    public void CreateObjectChannels(Entity entity)
    {
        EntityObjectChannels = IsBasic ? new() : new(entity);
        ShouldShowObjectChannels();
    }

    public void CreateObjectChannels(InventoryItem item)
    {
        EntityObjectChannels = new(item);
        ShouldShowObjectChannels();
    }

    public void ShouldShowObjectChannels()
    {
        Viewport.ObjectChannelsExpander.Visibility = EntityObjectChannels.Channels.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Viewport.ObjectChannelsEditor.ItemsSource = EntityObjectChannels.Channels;
    }
}
