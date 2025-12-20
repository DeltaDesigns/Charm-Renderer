using Arithmic;
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
                                            if (c.Unk10.GetValue(resource.GetReader()) is SD1918080)
                                            {
                                                GlobalChannels = new(resource);
                                                break;
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

    public void EvaluateGlobalChannels(ExternAtmosphere atmosExtern)
    {
        if (GlobalChannels is null)
            return;

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

        GlobalChannels.Evaluate();
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
    private List<GlobalChannel> Channels = new();
    public List<System.Numerics.Vector4> MiscValues = Enumerable.Repeat(System.Numerics.Vector4.Zero, 256).ToList();

    public RendererGlobalChannels(EntityResource sequencer)
    {
        CreateGlobalChannels(sequencer);
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

    public void Evaluate()
    {
        foreach (var channel in Channels)
        {
            channel.Evaulate(this);
        }
    }

    public System.Numerics.Vector4 Get(int index)
    {
        return Channels.First(x => x.Index == index).Value;
    }

    public System.Numerics.Vector4 Get(TigerHash id)
    {
        return Channels.First(x => x.ID == id).Value;
    }

    public System.Numerics.Vector4 Get(string name)
    {
        return Channels.First(x => x.Name == name).Value;
    }

    public void Set(int index, System.Numerics.Vector4 value)
    {
        Channels.First(x => x.Index == index).Value = value;
    }

    public void Set(TigerHash id, System.Numerics.Vector4 value)
    {
        Channels.First(x => x.ID == id).Value = value;
    }

    private class GlobalChannel
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

        public async void Evaulate(RendererGlobalChannels globals)
        {
            if (Bytecode.Length == 0 || BytecodeConstants.Length == 0)
                return;

            var evaluated = await InterpretedBytecode.EvaluateAsync(null, new System.Numerics.Vector4[1], BytecodeConstants, null, null, null, globalChannels: globals);
            Value = evaluated[0];
        }
    }
}
