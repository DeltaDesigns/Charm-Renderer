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
		obj.Create(Context, staticMesh);
		World.RenderObjects.Enqueue(obj);

		LookAtMeshInitial();
	}

	public void LoadEntity(Entity entity, bool lookAt = true)
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
			obj.Create(Context, ent, World, item);
		}
		LoadPlayerSkeleton(item);

		Viewport.OverrideWarning.Visibility = Visibility.Visible;
		ShouldShowObjectChannels();

		LookAtMeshInitial();
	}

	public void LoadInvestmentItems(IEnumerable<InventoryItem> items)
	{
		World?.Dispose();
		EntityObjectChannels = new();

		foreach (var item in items.DistinctBy(x => x.ApiHash))
		{
			if (item.GetArtArrangementIndex() == -1)
				continue;

			List<Entity> entities = Investment.Get().GetEntitiesFromHash(item);
			Entity? skele = Investment.Get().GetPatternEntityFromHash(item.Parent != null ? item.Parent.TagData.InventoryItemHash : item.TagData.InventoryItemHash);
			if (skele != null && skele.Skeleton != null && entities.Any())
				entities[0].Skeleton = skele.Skeleton;

			foreach (var ent in entities)
			{
				RenderObject obj = new();
				obj.Create(Context, ent, World, item);
			}

			EntityObjectChannels.AddObjectChannels(item);
		}

		if (items.Where(x => x.IsArmor || x.IsArmorOrnament).Any()) // meh
			LoadPlayerSkeleton(items.First());

		Viewport.OverrideWarning.Visibility = Visibility.Visible;
		ShouldShowObjectChannels();

		var combinedBB = RenderHelpers.CombineBBs(World.RenderObjects.Select(x => x.BoundingBox).ToList());
		// so the view is centered better
		combinedBB = new HelixToolkit.Maths.BoundingBox()
		{
			Minimum = combinedBB.Minimum * new System.Numerics.Vector3(1, 0, 1),
			Maximum = combinedBB.Maximum * new System.Numerics.Vector3(1, 0, 1)
		};
		World.OverrideMainBB = combinedBB;
		LookAtBoundingBox(combinedBB);
	}

	public void LoadPlayerSkeleton(InventoryItem item)
	{
		if (!item.IsGhost && !item.IsArmor && !item.IsArmorOrnament)
			return;

		var skeleHash = item.IsGhost ? "0000603046D31C68" : "0000670F342E9595";
		Entity skele = FileResourcer.Get().GetFile<Entity>(new FileHash(Hash64Map.Get().GetHash32Checked(skeleHash))); // 64 bit more permanent
		EntityObjectChannels.AddObjectChannels(skele);

		RenderObject obj = new();
		obj.Create(Context, skele, World);
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
