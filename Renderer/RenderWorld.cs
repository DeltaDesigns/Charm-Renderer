using Arithmic;
using System.Collections.Concurrent;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using static Charm.Renderer.Externs;
using static TfxBytecodeOp;

namespace Charm.Renderer;

public class RenderWorld : IDisposable
{
	public SMapAtmosphere? Atmosphere = null;
	public RendererGlobalChannels GlobalChannels;
	public List<Vector4> DayCycleRotations;
	public Queue<RenderObject> RenderObjects = new();

	public RenderWorld()
	{

	}

	public void CreateWorld(CharmRenderer renderer, Tag<SBubbleParent> bubble)
	{
		bubble.TagData.ChildMapReference.TagData.MapResources.ForEach(m =>
		{
			foreach (SMapDataTableEntry dataTable in m.MapContainer.TagData.MapDataTables)
			{
				if (dataTable.MapDataTable is null)
					continue;

				dataTable.MapDataTable.TagData.DataEntries.ForEach(entry =>
				{
					if (GlobalChannels is null)
					{
						Entity entity = FileResourcer.Get().GetFile<Entity>(entry.Entity.Hash, shouldCache: false);
						if (entity != null && !entity.HasGeometry())
						{
							foreach (FileHash? resourceHash in entity.TagData.EntityResources.Select(entity.GetReader(), r => r.Resource))
							{
								EntityResource resource = FileResourcer.Get().GetFile<EntityResource>(resourceHash);
								switch (resource.TagData.Unk10.GetValue(resource.GetReader()))
								{
									case S79948080:
										var a = ((S79818080)resource.TagData.Unk18.GetValue(resource.GetReader()));
										DynamicArray<SF1918080> b = a.Array1;
										b.AddRange(a.Array2);

										foreach (SF1918080 c in b)
										{
											if (c.Unk10.GetValue(resource.GetReader()) is SD1918080 && GlobalChannels is null)
											{
												GlobalChannels = new(resource);
											}
											else if (c.Unk10.GetValue(resource.GetReader()) is SCF918080 lut && renderer.Externs.ScreenArea.Unk08 is null)
											{
												if (lut.Unk28 is null || lut.Unk28.TagData.LUT is null)
													continue;

												renderer.Externs.ScreenArea.Unk08 = AssetManager.GetInstance().GetOrCreateGlobalTexture(renderer.Context, lut.Unk28.TagData.LUT);
											}
										}
										break;
								}
							}
						}
					}

					switch (entry.DataResource.GetValue(dataTable.MapDataTable.GetReader()))
					{
						//case SMapDataResource staticMapResource:
						//    staticMapResource.StaticMapParent?.Load();
						//    if (staticMapResource.StaticMapParent is null)
						//        return;

						//    CreateStaticMap(renderer, staticMapResource);
						//    break;

						case SMapAtmosphere mapAtmosphere:
							CreateAtmosphere(renderer, mapAtmosphere);
							break;

						case S716A8080 dayCycle:
							CreateDayCycleRotations(dayCycle);
							break;
					}
				});
			}
		});
	}

	public void CreateStaticMap(CharmRenderer renderer, SMapDataResource staticResource)
	{

	}

	public void CreateAtmosphere(CharmRenderer renderer, SMapAtmosphere mapAtmosphere)
	{
		if (Atmosphere.HasValue)
		{
			Log.Debug("Atmosphere already created!");
			return;
		}

		Atmosphere = mapAtmosphere;
		if (Atmosphere.Value.Lookup0 == null)
		{
			Log.Debug("Atmosphere has no Lookup texture to use");
		}
		else
		{
			renderer.Externs.Atmosphere.AtmosLookup0 = AssetManager.GetInstance().GetOrCreateGlobalTexture(GPU.Instance.Context, Atmosphere.Value.Lookup0);
			renderer.Externs.Atmosphere.AtmosLookup1 = AssetManager.GetInstance().GetOrCreateGlobalTexture(GPU.Instance.Context, Atmosphere.Value.Lookup1 ?? Atmosphere.Value.Lookup0);
		}

		if (Atmosphere.Value.Lookup4 != null)
			renderer.Externs.Atmosphere.AtmosLookup2 = AssetManager.GetInstance().GetOrCreateGlobalTexture(GPU.Instance.Context, Atmosphere.Value.Lookup4);

		Log.Debug("Assigned Atmopshere Extern Textures.");
	}

	public void CreateDayCycleRotations(S716A8080 dayCycle)
	{
		if (dayCycle.Unk10 is null || dayCycle.Unk10.TagData.Unk18 is null)
			return;

		DayCycleRotations = new();
		var entry = dayCycle.Unk10.TagData.Unk18;
		foreach (var rot in entry.TagData.Unk30.Enumerate(entry.GetReader()))
		{
			DayCycleRotations.Add(rot.Vec);
		}
	}

	public async void EvaluateGlobalChannels(ExternAtmosphere atmosExtern)
	{
		if (GlobalChannels is null)
			return;

		RenderHelpers.Profile("Evaluate Global Channels");
		if (DayCycleRotations.Count != 0)
		{
			float tod_half = Math.Max(0, (atmosExtern.AtmosTimeOfDay * 3600f) / 2f);

			int fromIndex = Math.Clamp((int)MathF.Floor(tod_half), 0, DayCycleRotations.Count - 1);
			int toIndex = Math.Clamp((int)MathF.Ceiling(tod_half), 0, DayCycleRotations.Count - 1);

			Vector4 from = DayCycleRotations[fromIndex];
			Vector4 to = DayCycleRotations[toIndex];
			float t = tod_half - MathF.Floor(tod_half);
			Vector4 lerpedRotation = System.Numerics.Vector4.Lerp(from, to, t);

			// sun_track_direction
			GlobalChannels.Set(102, lerpedRotation);
			GlobalChannels.Set(100, lerpedRotation); // unsure
		}

		await GlobalChannels.Evaluate();
		RenderHelpers.EndProfile();
	}

	public void Dispose()
	{
		foreach (var renderObject in RenderObjects)
		{
			renderObject?.Dispose();
		}
		RenderObjects?.Clear();
	}
}

public class RendererGlobalChannels
{
	public List<GlobalChannel> Channels = new();
	public List<System.Numerics.Vector4> MiscValues = Enumerable.Repeat(System.Numerics.Vector4.Zero, 256).ToList();

	private Dictionary<int, GlobalChannel> channelsByIndex;
	private Dictionary<TigerHash, GlobalChannel> channelsById;
	private Dictionary<string, GlobalChannel> channelsByName;

	public RendererGlobalChannels(EntityResource sequencer)
	{
		CreateGlobalChannels(sequencer);
		InitializeLookups();
	}

	public void InitializeLookups()
	{
		channelsByIndex = Channels.ToDictionary(c => c.Index);
		channelsById = Channels.ToDictionary(c => c.ID);
		channelsByName = Channels.ToDictionary(c => c.Name);
	}

	public void CreateGlobalChannels(EntityResource resource)
	{
		var globals = ((S79818080)resource.TagData.Unk18.GetValue(resource.GetReader()));
		DynamicArray<SF1918080> map = globals.Array1;
		map.AddRange(globals.Array2);

		var defaults = Globals.Get().GlobalChannelDefaults;
		foreach (var defaultChannel in defaults)
		{
			Channels.Add(new GlobalChannel
			{
				Name = GlobalChannels.KnownChannelNames.TryGetValue(defaultChannel.Key.Hash32, out string name) ? name : defaultChannel.Key.ToString(),
				ID = defaultChannel.Key,
				Index = defaults.Keys.ToList().IndexOf(defaultChannel.Key),
				Bytecode = Array.Empty<byte>(), // No bytecode for defaults
				BytecodeConstants = Array.Empty<System.Numerics.Vector4>(),
				Value = defaultChannel.Value
			});
		}

		foreach (SF1918080 entry in map)
		{
			if (entry.Unk10.GetValue(resource.GetReader()) is SD1918080 global)
			{
				var id = globals.Array3[global.ChannelIndex].ID;
				var index = Globals.Get().GlobalChannelDefaults.Keys.ToList().IndexOf(id);

				if (Channels.Any(x => x.ID == id))
				{
					var channel = new GlobalChannel
					{
						Name = GlobalChannels.KnownChannelNames.TryGetValue(id.Hash32, out string name) ? name : id.ToString(),
						ID = id,
						Index = index,
						Bytecode = global.UnkBytecode.Select(x => x.Value).ToArray(),
						BytecodeConstants = global.Values.Select(x => x.Vec.ToSys()).ToArray(),
						InterpretedBytecode = new(TfxBytecodeOp.ParseAll(global.UnkBytecode, BytecodeType.Sequencer), BytecodeType.Sequencer),
						Value = global.Values.FirstOrDefault().Vec
					};
					channel.InterpretedBytecode.Name = $"Global Channel {name} ({index})";
					Channels[index] = channel;
				}
			}
		}
	}

	//public void Evaluate()
	//{
	//	foreach (var channel in Channels)
	//	{
	//		channel.Evaulate(this);
	//	}
	//}

	public async Task Evaluate()
	{
		var partitioner = Partitioner.Create(Channels, true);
		var tasks = partitioner.GetPartitions(Environment.ProcessorCount).Select(async partition =>
		{
			using (partition)
			{
				while (partition.MoveNext())
				{
					await partition.Current.Evaulate(this);
				}
			}
		});

		await Task.WhenAll(tasks);
	}

	public System.Numerics.Vector4 Get(int index) => channelsByIndex[index].Value;
	public System.Numerics.Vector4 Get(TigerHash id) => channelsById[id].Value;
	public System.Numerics.Vector4 Get(string name) => channelsByName[name].Value;

	public void Set(int index, System.Numerics.Vector4 value) => channelsByIndex[index].Value = value;
	public void Set(TigerHash id, System.Numerics.Vector4 value) => channelsById[id].Value = value;
	public void Set(string name, System.Numerics.Vector4 value) => channelsByName[name].Value = value;

	public class GlobalChannel
	{
		public string Name;
		public TigerHash ID;
		public int Index;
		public byte[] Bytecode;
		public System.Numerics.Vector4[] BytecodeConstants;
		public TfxBytecodeInterpreter InterpretedBytecode;
		public System.Numerics.Vector4 Value;

		public GlobalChannel()
		{

		}

		public async Task Evaulate(RendererGlobalChannels globals)
		{
			if (Bytecode.Length == 0 || BytecodeConstants.Length == 0)
				return;

			var evaluated = await InterpretedBytecode.EvaluateAsync(null, new System.Numerics.Vector4[1], BytecodeConstants, null, null, null, globalChannels: globals);
			Value = evaluated[0];
		}
	}
}
