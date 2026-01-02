using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
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
					Channels.TryAdd(hash, new(val, isFloat));
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
					Channels.TryAdd(hash, new(val, isFloat));
				}
				catch { }
			}
		}
	}

	public void ResetAllChannels()
	{
		foreach (var channel in Channels.Values)
		{
			channel.Reset();
		}
	}
}


public class EditableVector4 : INotifyPropertyChanged
{
	private float x, y, z, w;

	public EditableVector4(Vector4 vec, bool isVector = true)
	{
		X = vec.X;
		Y = vec.Y;
		Z = vec.Z;
		W = vec.W;
		IsFloat = !isVector;
	}

	public Vector4 Vec4 => new Vector4(X, Y, Z, W);

	public bool IsFloat { get; set; }
	public float X { get => x; set { x = value; OnPropertyChanged(nameof(X)); } }
	public float Y { get => y; set { y = value; OnPropertyChanged(nameof(Y)); } }
	public float Z { get => z; set { z = value; OnPropertyChanged(nameof(Z)); } }
	public float W { get => w; set { w = value; OnPropertyChanged(nameof(W)); } }

	public event PropertyChangedEventHandler PropertyChanged;
	private void OnPropertyChanged(string propertyName)
	{
		//Console.WriteLine($"EditableVector4 changed: {propertyName} = {GetValue(propertyName)}");
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	private float GetValue(string name) => name switch
	{
		nameof(X) => X,
		nameof(Y) => Y,
		nameof(Z) => Z,
		nameof(W) => W,
		_ => 0
	};

	public void Reset()
	{
		X = 1f;
		Y = 1f;
		Z = 1f;
		W = 1f;
	}
}

public class FloatConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return value;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		// return an invalid value in case of the value ends with a point
		//if (value.ToString() == string.Empty)
		//    return 0.0f;

		return value.ToString().EndsWith(".") ? "." : value;
	}
}
