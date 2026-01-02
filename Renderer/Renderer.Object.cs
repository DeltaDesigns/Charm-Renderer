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
		GroupVisibility.Clear();

		// todo? move away
		Viewport.CreateMeshGroups(entity);

		RenderObject obj = new();
		obj.Create(Context, entity, World);
		GroupVisibility.AddObject(obj);

		var children = entity.GetEntityChildren();
		foreach (var child in children)
		{
			if (child.Model is null)
				continue;

			obj = new();
			obj.Create(Context, child, World);
			obj.TransformOffset = new Transform
			{
				Quaternion = child.Model.RotationOffset,
				Position = child.Model.TranslationOffset.ToVec3()
			};

			GroupVisibility.AddObject(obj);
		}

		if (entity.Skeleton != null)
			Viewport.OverrideWarning.Visibility = Visibility.Visible;
		else
			Viewport.OverrideWarning.Visibility = Visibility.Collapsed;

		CreateObjectChannels(entity);
		if (lookAt)
			LookAtMeshInitial();
	}

	public void CreateObjectChannels(Entity entity)
	{
		EntityObjectChannels = new(entity);
		Viewport.ObjectChannelsExpander.Visibility = EntityObjectChannels.Channels.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
		Viewport.ObjectChannelsEditor.ItemsSource = EntityObjectChannels.Channels;
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
			obj.Create(Context, ent, World, item);
		}

		Viewport.OverrideWarning.Visibility = Visibility.Visible;

		EntityObjectChannels = new(item);
		Viewport.ObjectChannelsExpander.Visibility = EntityObjectChannels.Channels.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
		Viewport.ObjectChannelsEditor.ItemsSource = EntityObjectChannels.Channels;

		LookAtMeshInitial();
	}
}
