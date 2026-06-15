using System.Collections.Concurrent;
using Arithmic;
using HelixToolkit.Maths;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using static TfxBytecodeOp;

namespace Charm.Renderer;

public class RenderWorld : IDisposable
{
    public SMapAtmosphere? Atmosphere = null;
    public RendererGlobalChannels GlobalChannels;
    public List<Vector4> DayCycleRotations = new();
    public Queue<RenderObject> RenderObjects = new();
    public BoundingBox? LocalOverrideMainBB = null;
    public BoundingBox? OverrideMainBB = null;

    // Temp, this sucks, will fix later
    public Queue<RenderObject> PersistantRenderObjects = new();
    public object WorldLock = new();

    public RenderWorld()
    {

    }

    public void SwitchWorld(CharmRenderer renderer, uint hash)
    {
        DisposePersistant();
        Atmosphere = null;
        GlobalChannels = null;
        DayCycleRotations.Clear();
        CreateWorld(renderer, FileResourcer.Get().GetFile<Tag<SBubbleParent>>(new(hash)));
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
                            foreach (FileHash? resourceHash in entity.Components)
                            {
                                EntityComponent resource = FileResourcer.Get().GetFile<EntityComponent>(resourceHash);
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
                                                Console.WriteLine($"{resource.Hash}");
                                                GlobalChannels = new(resource);
                                            }
                                            else if (c.Unk10.GetValue(resource.GetReader()) is SCF918080 lut && renderer.Externs.ScreenArea.Unk08 is null)
                                            {
                                                if (lut.Unk28 is null || lut.Unk28.TagData.LUT is null)
                                                    continue;

                                                renderer.Externs.ScreenArea.Unk08 = AssetManager.Get().GetOrCreateGlobalTexture(lut.Unk28.TagData.LUT).SRV;
                                            }
                                        }
                                        break;
                                }
                            }
                        }
                    }

                    switch (entry.DataResource.GetValue(dataTable.MapDataTable.GetReader()))
                    {
                        // just a test, Tower Hangar ran like ass (barely 30fps)
                        //case SStaticMapDataResource staticMapResource:
                        //	staticMapResource.StaticMapParent?.Load();
                        //	if (staticMapResource.StaticMapParent is null)
                        //		return;

                        //	CreateStaticMap(renderer, staticMapResource);
                        //	break;

                        case SMapAtmosphere mapAtmosphere:
                            CreateAtmosphere(renderer, mapAtmosphere);
                            break;

                        case S716A8080 dayCycle:
                            CreateDayCycleRotations(dayCycle);
                            break;

                        case SMapSkyObjectsResource skyResource:
                            CreateSkyObjects(renderer, skyResource);
                            break;
                    }
                });
            }
        });

        if (GlobalChannels is null)
            GlobalChannels = RendererGlobalChannels.CreateDefault();
    }

    public void CreateSkyObjects(CharmRenderer renderer, SMapSkyObjectsResource skyResource)
    {
        skyResource.SkyObjects?.Load();
        if (skyResource.SkyObjects is null)
            return;

        if (skyResource.SkyObjects.TagData.Entries is null)
            return;

        int i = 0;
        foreach (SA96A8080 element in skyResource.SkyObjects.TagData.Entries)
        {
            if (element.Model.TagData.Model is null || element.Unk70 == 5 || element.Complex is not null)
                continue;

            var bb = element.Bounds.CreateFrom();
            Tiger.Schema.Matrix4x4 matrix = element.Transform;
            Vector3 scale = new();
            Vector4 trans = new();
            Vector4 quat = new();
            matrix.Decompose(out trans, out quat, out scale);

            RenderObject renderObject = new();
            renderObject.Create(renderer.Context, element.Model.TagData.Model, TfxFeatureRenderer.SkyTransparent);
            renderObject.LocalBoundingBox = bb;
            renderObject.BoundingBox = bb;
            renderObject.GlobalTransforms[0] = new Transform
            {
                Position = trans.ToVec3(),
                Quaternion = quat,
                Scale = scale
            };

            PersistantRenderObjects.Enqueue(renderObject);

            i++;
        }
    }

    public void CreateStaticMap(CharmRenderer renderer, SStaticMapDataResource staticResource)
    {
        var staticMap = staticResource.StaticMapParent.TagData.StaticMap.TagData;
        List<SStaticMeshHash> extractedStatics = staticMap.Statics.DistinctBy(x => x.Static.Hash).ToList();

        foreach (SStaticMeshInstanceMap c in staticMap.InstanceCounts)
        {
            StaticMesh model = staticMap.Statics[c.StaticIndex].Static;

            int remaining = c.InstanceCount;
            int srcOffset = c.InstanceOffset;

            while (remaining > 0)
            {
                int batchCount = Math.Min(remaining, 16);

                RenderObject obj = new();
                obj.Create(renderer.Context, this, model);
                obj.InstanceCount = batchCount;
                obj.GlobalTransforms = new Transform[batchCount];

                for (int i = 0; i < batchCount; i++)
                {
                    var trans = staticMap.Instances[srcOffset + i];

                    obj.GlobalTransforms[i] = new Transform
                    {
                        Position = trans.Position,
                        Quaternion = trans.Rotation,
                        Scale = new(trans.Scale.X)
                    };
                }

                srcOffset += batchCount;
                remaining -= batchCount;
            }
        }
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
            renderer.Externs.Atmosphere.AtmosLookup0 = AssetManager.Get().GetOrCreateGlobalTexture(Atmosphere.Value.Lookup0).SRV;
            renderer.Externs.Atmosphere.AtmosLookup1 = AssetManager.Get().GetOrCreateGlobalTexture(Atmosphere.Value.Lookup1 ?? Atmosphere.Value.Lookup0).SRV;
        }

        if (Atmosphere.Value.Lookup4 != null)
            renderer.Externs.Atmosphere.AtmosLookup2 = AssetManager.Get().GetOrCreateGlobalTexture(Atmosphere.Value.Lookup4).SRV;

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

    private float _dayLength = 3600f;
    public bool UseDayCycle { get; set; } = false;
    public async void EvaluateGlobalChannels(Externs externs)
    {
        if (GlobalChannels is null)
            return;

        RenderHelpers.Profile("Evaluate Global Channels");
        if (DayCycleRotations.Count != 0)
        {
            float tod = (externs.Atmosphere.AtmosTimeOfDay * 3600f);
            float tod_half = Math.Max(0, tod / 2f);

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
        GlobalChannels.Set(7, Vector4.One);
        //GlobalChannels.Set(46, Vector4.One);
        //GlobalChannels.Set(47, Vector4.One);

        RenderHelpers.EndProfile();
    }

    public void Dispose()
    {
        RenderObject[] snapshot;

        lock (WorldLock)
        {
            snapshot = RenderObjects.ToArray();
            RenderObjects.Clear();
        }

        foreach (var renderObject in snapshot)
        {
            renderObject?.Dispose();
        }
    }

    public void DisposePersistant()
    {
        RenderObject[] snapshot;

        lock (WorldLock)
        {
            snapshot = PersistantRenderObjects.ToArray();
            PersistantRenderObjects.Clear();
        }

        foreach (var renderObject in snapshot)
        {
            renderObject?.Dispose();
        }
    }

    // this sucks and is temporary
    public void DisposeAll()
    {
        Dispose();
        DisposePersistant();
    }
}

public class RendererGlobalChannels
{
    public List<GlobalChannel> Channels = new();
    public List<System.Numerics.Vector4> MiscValues = Enumerable.Repeat(System.Numerics.Vector4.Zero, 256).ToList();

    private Dictionary<int, GlobalChannel> channelsByIndex;
    private Dictionary<TigerHash, GlobalChannel> channelsById;
    private Dictionary<string, GlobalChannel> channelsByName;

    public RendererGlobalChannels() { }

    public RendererGlobalChannels(EntityComponent sequencer)
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

    public void CreateGlobalChannels(EntityComponent resource)
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

    public static RendererGlobalChannels CreateDefault()
    {
        RendererGlobalChannels defaultGlobals = new();
        GlobalChannels.RestoreDefaults();
        var defaults = Globals.Get().GlobalChannelDefaults;
        foreach (var defaultChannel in defaults)
        {
            defaultGlobals.Channels.Add(new GlobalChannel
            {
                Name = GlobalChannels.KnownChannelNames.TryGetValue(defaultChannel.Key.Hash32, out string name) ? name : defaultChannel.Key.ToString(),
                ID = defaultChannel.Key,
                Index = defaults.Keys.ToList().IndexOf(defaultChannel.Key),
                Bytecode = Array.Empty<byte>(), // No bytecode for defaults
                BytecodeConstants = Array.Empty<System.Numerics.Vector4>(),
                Value = defaultChannel.Value
            });
        }

        defaultGlobals.InitializeLookups();
        return defaultGlobals;
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
