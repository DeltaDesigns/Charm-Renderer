using System.Windows;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    public void LoadStatic(FileHash hash, MapTransform transform)
    {
        DisposeMesh();

        var staticMesh = FileResourcer.Get().GetFile<StaticMesh>(hash, shouldCache: false);
        RenderObject obj = new();
        obj.Create(Context, staticMesh);
        World.RenderObjects.Enqueue(obj);

        LookAtMeshInitial();
    }

    public void LoadEntity(Entity entity, MapTransform transform, bool lookAt = true)
    {
        DisposeMesh();

        RenderObject obj = new();
        obj.Create(Context, entity);
        World.RenderObjects.Enqueue(obj);

        if (entity.Skeleton != null)
            Viewport.OverrideWarning.Visibility = Visibility.Visible;
        else
            Viewport.OverrideWarning.Visibility = Visibility.Collapsed;

        EntityObjectChannels = new(entity);
        Viewport.ObjectChannelsExpander.Visibility = EntityObjectChannels.Channels.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Viewport.ObjectChannelsEditor.ItemsSource = EntityObjectChannels.Channels;

        if (lookAt)
            LookAtMeshInitial();
    }

    public void LoadInvestmentItem(InventoryItem item)
    {
        DisposeMesh();

        List<Entity> entities = Investment.Get().GetEntitiesFromHash(item);
        foreach (var ent in entities)
        {
            RenderObject obj = new();
            obj.Create(Context, ent, item);
            World.RenderObjects.Enqueue(obj);
        }

        Viewport.OverrideWarning.Visibility = Visibility.Visible;

        EntityObjectChannels = new(item);
        Viewport.ObjectChannelsExpander.Visibility = EntityObjectChannels.Channels.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Viewport.ObjectChannelsEditor.ItemsSource = EntityObjectChannels.Channels;

        LookAtMeshInitial();
    }
}
