using System.Windows;
using Tiger;
using Tiger.Schema;
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

            GroupVisibility.AddObject(childObj);
        }

        if (entity.Skeleton != null)
            Viewport.OverrideWarning.Visibility = Visibility.Visible;
        else
            Viewport.OverrideWarning.Visibility = Visibility.Collapsed;

        CreateObjectChannels(entity);
        LookAtMesh(obj); // base ent, no children
    }

    public void LoadInvestmentItem(InventoryItem item)
    {
        World?.Dispose();
        EntityObjectChannels = new(item);

        List<Entity> entities = Investment.Get().GetEntitiesFromHash(item);
        Entity? skele = Investment.Get().GetPatternEntityFromHash(item.Parent != null ? item.Parent.TagData.InventoryItemHash : item.TagData.InventoryItemHash);
        if (skele != null && skele.Skeleton != null && entities.Any())
            entities[0].Skeleton = skele.Skeleton;

        foreach (var ent in entities)
        {
            RenderObject obj = new();
            obj.Create(Context, World, ent, item);
        }

        var combinedBB = RenderHelpers.CombineBBs(World.RenderObjects.Select(x => x.BoundingBox).ToList());
        World.LocalOverrideMainBB = combinedBB;
        World.OverrideMainBB = combinedBB;

        bool isWeapon = item.Parent != null ? item.Parent.IsWeapon : item.IsWeapon;
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
        Viewport.OverrideWarning.Visibility = Visibility.Visible;

        if (isWeapon)
        {
            Viewport.AtmosRotation = 0.1f;
        }
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
            if (skele != null && skele.Skeleton != null && entities.Any())
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
            LoadPlayerSkeleton(items.First());

        Viewport.OverrideWarning.Visibility = Visibility.Visible;
        ShouldShowObjectChannels();
    }

    public void LoadPlayerSkeleton(InventoryItem item)
    {
        if (!item.IsGhost && !item.IsArmor && !item.IsArmorOrnament)
            return;

        var skeleHash = item.IsGhost ? "0000603046D31C68" : "0000670F342E9595";
        Entity skele = FileResourcer.Get().GetFile<Entity>(new FileHash(Hash64Map.Get().GetHash32Checked(skeleHash))); // 64 bit more permanent
        EntityObjectChannels.AddObjectChannels(skele);

        RenderObject obj = new();
        obj.Create(Context, World, skele);

        if (item.IsGhost)
        {
            EntityObjectChannels.SetObjectChannel(2309869224, 0f); // hack_int
            EntityObjectChannels.SetObjectChannel(1761166716, 0f); // scan_int
        }
    }


    public void CreateObjectChannels(Entity entity)
    {
        EntityObjectChannels = new(entity);
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
