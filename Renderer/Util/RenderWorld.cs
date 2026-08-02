using Arithmic;
using HelixToolkit.Maths;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;

namespace Charm.Renderer;

public class RenderWorld : IDisposable
{
    public SMapAtmosphere? Atmosphere = null;
    public RendererGlobalChannels GlobalChannels;
    public List<Vector4> DayCycleRotations = new();
    public List<Vector4> AtmosphereDirections = new();
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
        renderer.Viewport.AtmosIntensity = GlobalChannels?.Get("sky_snapshot_intensity").X ?? 1f;
        renderer.Viewport.AtmosRotation = GlobalChannels?.Get("sky_snapshot_rotation").X / 360f ?? 1f;
    }

    public void CreateWorld(CharmRenderer renderer, Tag<SBubbleParent> bubble)
    {
        renderer.Externs.Atmosphere.SkySnapshot1 = null;
        renderer.Externs.Atmosphere.SkySnapshot2 = null;
        renderer.Externs.Atmosphere.SkyDensityLookup = null;
        renderer.Externs.ScreenArea.Unk08 = null;

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
                                    case S80809479:
                                        var a = ((S80808179)resource.TagData.Unk18.GetValue(resource.GetReader()));
                                        DynamicArray<S808091F1> b = a.Array1;
                                        b.AddRange(a.Array2);

                                        foreach (S808091F1 c in b)
                                        {
                                            // 3600 check at unk14 just cus S808091D1 isnt *just* for global channels, idk a better way to determine atm
                                            if (c.Unk10.GetValue(resource.GetReader()) is S808091D1 p && p.Unk14 == 3600f && GlobalChannels is null)
                                            {
                                                GlobalChannels = new(resource);
                                                Log.Debug($"Global Channels {resource.Hash}");
                                            }
                                            else if (c.Unk10.GetValue(resource.GetReader()) is S808091CF lut && renderer.Externs.ScreenArea.Unk08 is null)
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

                        case S80806A71 dayCycle:
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
        foreach (S80806AA9 element in skyResource.SkyObjects.TagData.Entries)
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

            lock (WorldLock)
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
            renderer.Externs.Atmosphere.SkySnapshot1 = AssetManager.Get().GetOrCreateGlobalTexture(Atmosphere.Value.Lookup0).SRV;
            renderer.Externs.Atmosphere.SkySnapshot2 = AssetManager.Get().GetOrCreateGlobalTexture(Atmosphere.Value.Lookup1 ?? Atmosphere.Value.Lookup0).SRV;
        }

        if (Atmosphere.Value.Lookup4 != null)
            renderer.Externs.Atmosphere.SkyDensityLookup = AssetManager.Get().GetOrCreateGlobalTexture(Atmosphere.Value.Lookup4).SRV;

        Log.Debug("Assigned Atmopshere Extern Textures.");
    }

    public void CreateDayCycleRotations(S80806A71 dayCycle)
    {
        if (dayCycle.Unk10 is null)
            return;

        if (dayCycle.Unk10.TagData.Unk18 != null)
        {
            DayCycleRotations = new();
            var entry = dayCycle.Unk10.TagData.Unk18;
            foreach (var rot in entry.TagData.Unk30.Enumerate(entry.GetReader()))
            {
                DayCycleRotations.Add(rot.Vec);
            }
        }

        if (dayCycle.Unk10.TagData.Unk10 != null)
        {
            AtmosphereDirections = new();
            var entry = dayCycle.Unk10.TagData.Unk10;
            foreach (var rot in entry.TagData.Unk30.Enumerate(entry.GetReader()))
            {
                AtmosphereDirections.Add(rot.Vec);
            }
        }
        else
        {
            AtmosphereDirections = Enumerable.Repeat(new Vector4(-0.577f, -0.577f, -0.577f, 0f), 3600).ToList();
        }
    }

    private float _dayLength = 3600f;
    public bool UseDayCycle { get; set; } = false;
    public async void EvaluateGlobalChannels(Externs externs)
    {
        if (GlobalChannels is null)
            return;

        RenderHelpers.Profile("Evaluate Global Channels");

        float tod = (externs.Atmosphere.AtmosTimeOfDay * 3600f);
        if (DayCycleRotations.Count != 0)
        {
            float tod_half = Math.Max(0, tod / 2f);
            float t = tod_half - MathF.Floor(tod_half);

            int fromIndex = Math.Clamp((int)MathF.Floor(tod_half), 0, DayCycleRotations.Count - 1);
            int toIndex = Math.Clamp((int)MathF.Ceiling(tod_half), 0, DayCycleRotations.Count - 1);

            Vector4 from = DayCycleRotations[fromIndex];
            Vector4 to = DayCycleRotations[toIndex];
            Vector4 lerpedTrack = System.Numerics.Vector4.Lerp(from, to, t);
            GlobalChannels.Set("sun_track_direction", lerpedTrack);
            //GlobalChannels.Set("sun_light_direction", lerpedTrack);

            from = AtmosphereDirections[fromIndex];
            to = AtmosphereDirections[toIndex];
            Vector4 lerpedAtmosDir = System.Numerics.Vector4.Lerp(from, to, t);
            GlobalChannels.Set("sun_atmosphere_direction", lerpedAtmosDir); // unsure
        }

        float DistanceToNight = MathF.Abs(tod / 1800f - 1f);
        GlobalChannels.Set("cubemap_relighting_sky_intensity", new(1f - DistanceToNight));
        GlobalChannels.MiscValues[0] = new((1f - DistanceToNight) * 0.725f);

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
