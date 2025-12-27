using System.Windows;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;

namespace Charm.Renderer;

public partial class CharmRenderer
{
	// TODO? Move these to RenderWorld
	public void LoadStatic(FileHash hash, MapTransform transform)
	{
		World?.Dispose();

		var staticMesh = FileResourcer.Get().GetFile<StaticMesh>(hash, shouldCache: false);
		RenderObject obj = new();
		obj.Create(Context, staticMesh);
		World.RenderObjects.Enqueue(obj);

		LookAtMeshInitial();
	}

	public void LoadEntity(Entity entity, MapTransform transform, bool lookAt = true)
	{
		World?.Dispose();

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
		World?.Dispose();

		List<Entity> entities = Investment.Get().GetEntitiesFromHash(item);
		Entity? skele = Investment.Get().GetPatternEntityFromHash(item.Parent != null ? item.Parent.TagData.InventoryItemHash : item.TagData.InventoryItemHash);
		if (skele != null && skele.Skeleton != null && entities.Any())
			entities[0].Skeleton = skele.Skeleton;

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
