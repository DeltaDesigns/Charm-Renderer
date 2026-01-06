using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;
using Tiger.Schema.Shaders;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public class ObjectChannels
{
	public ObservableDictionary<uint, EditableVector4> Channels = new();

	public ObjectChannels(Entity entity)
	{
		var parts = entity.Load(ExportDetailLevel.MostDetailed, LoadLevel.Minimal);
		parts.AddRange(entity.GetEntityChildren()?.SelectMany(x => x.Load(ExportDetailLevel.MostDetailed, LoadLevel.Minimal)).ToList());

		GetObjectChannels(parts);
	}

	public ObjectChannels(InventoryItem item)
	{
		List<Entity> entities = Investment.Get().GetEntitiesFromHash(item);
		List<DynamicMeshPart> parts = new List<DynamicMeshPart>();
		foreach (var entity in entities)
		{
			parts.AddRange(entity.Load(Tiger.Schema.ExportDetailLevel.MostDetailed));
		}

		GetObjectChannels(parts);

		if (item.IsGhost)
		{
			ResetAllChannels(Vector4.Zero);
			SetObjectChannel(0x14BDBC8F, new(5f));
		}
	}

	private void SetObjectChannel(uint hash, Vector4 value)
	{
		if (!Channels.TryGetValue(hash, out var temp))
			return;

		Channels[hash] = new EditableVector4(value, temp.VectorType);
	}

	private void GetObjectChannels(List<DynamicMeshPart> parts)
	{
		foreach (var part in parts)
		{
			var opcodes = TfxBytecodeOp.ParseAll(part.Material.Pixel.TFX_Bytecode);
			opcodes.AddRange(TfxBytecodeOp.ParseAll(part.Material.Vertex.TFX_Bytecode));

			for (int i = 0; i < opcodes.Count; i++)
			{
				var op = opcodes[i];
				if (op.op == TfxBytecode.PopOutput || i + 1 >= opcodes.Count)
					continue;

				var nextOp = opcodes[i + 1];

				if (op.op == TfxBytecode.PushObjectChannelVector)
				{
					var hash = ((PushObjectChannelVectorData)op.data).hash;
					bool isFloat = (nextOp.op == TfxBytecode.PermuteAllX)
						|| (nextOp.op == TfxBytecode.Permute && ((PermuteData)(nextOp.data)).fields == 0b00_00_00_00);

					Vector4 val = Vector4.One;
					switch (hash)
					{
						case 2812804675: // interpolated_world_position
						case 2046642570: // parent.fp_iron_sight
							val = Vector4.Zero;
							break;
					}
					Channels.TryAdd(hash, new(val, isFloat ? EditableVector4.VectorInputType.Float : EditableVector4.VectorInputType.Vec4));
				}
			}
		}
	}

	// todo, this only adds from the given material, it doesnt remove if the given replacement material has different hashes
	public void UpdateChannels(Material material)
	{
		var opcodes = TfxBytecodeOp.ParseAll(material.Pixel.TFX_Bytecode);
		opcodes.AddRange(TfxBytecodeOp.ParseAll(material.Vertex.TFX_Bytecode));

		for (int i = 0; i < opcodes.Count; i++)
		{
			var op = opcodes[i];
			if (op.op == TfxBytecode.PopOutput || i + 1 >= opcodes.Count)
				continue;

			var nextOp = opcodes[i + 1];

			if (op.op == TfxBytecode.PushObjectChannelVector)
			{
				var hash = ((PushObjectChannelVectorData)op.data).hash;
				bool isFloat = (nextOp.op == TfxBytecode.PermuteAllX)
					|| (nextOp.op == TfxBytecode.Permute && ((PermuteData)(nextOp.data)).fields == 0b00_00_00_00);

				Vector4 val = Vector4.One;
				switch (hash)
				{
					case 2812804675: // interpolated_world_position
					case 2046642570: // parent.fp_iron_sight
						val = Vector4.Zero;
						break;
				}
				try
				{
					Channels.TryAdd(hash, new(val, isFloat ? EditableVector4.VectorInputType.Float : EditableVector4.VectorInputType.Vec4));
				}
				catch { }
			}
		}
	}

	public void ResetAllChannels()
	{
		foreach (var channel in Channels.Values)
		{
			channel.Reset(Vector4.One);
		}
	}

	public void ResetAllChannels(Vector4 vec)
	{
		foreach (var channel in Channels.Values)
		{
			channel.Reset(vec);
		}
	}
}
