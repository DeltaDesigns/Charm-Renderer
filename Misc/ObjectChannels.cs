using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;
using Tiger.Schema.Shaders;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public class ObjectChannels
{
    public ObservableDictionary<uint, EditableVector4> Channels = new();

    public ObjectChannels()
    {
    }

    public ObjectChannels(Entity entity)
    {
        AddObjectChannels(entity);
    }

    public ObjectChannels(InventoryItem item)
    {
        AddObjectChannels(item);
    }

    public void AddObjectChannels(Entity entity)
    {
        var parts = entity.Load(ExportDetailLevel.MostDetailed, LoadLevel.Minimal);
        parts.AddRange(entity.GetEntityChildren()?.SelectMany(x => x.Load(ExportDetailLevel.MostDetailed, LoadLevel.Minimal)).ToList());
        GetObjectChannels(parts);
    }

    public void AddObjectChannels(InventoryItem item)
    {
        List<Entity> entities = Investment.Get().GetEntitiesFromHash(item);
        List<DynamicMeshPart> parts = new List<DynamicMeshPart>();
        foreach (var entity in entities)
        {
            parts.AddRange(entity.Load(Tiger.Schema.ExportDetailLevel.MostDetailed, LoadLevel.Minimal));
        }

        GetObjectChannels(parts);

        if (item.IsGhost)
        {
            ResetAllChannels(Vector4.Zero);
            SetObjectChannel(0x14BDBC8F, 5f);
        }

        SetObjectChannel(0x8B16FB15, 0f);
        SetObjectChannel(0x3A16369C, 0f);
        SetObjectChannel(0x64C24EBB, 0f);
        SetObjectChannel(0x44859892, 0f);
        SetObjectChannel(0xADA8EE58, 0f);
        SetObjectChannel(0x4BD9F2B8, 0f);
        SetObjectChannel(0x50BC8D0A, 0f);
        SetObjectChannel(Helpers.Fnv1a32("firing_ramp"), 0f);
        SetObjectChannel(Helpers.Fnv1a32("weapon_firing"), 0f);
        SetObjectChannel(Helpers.Fnv1a32("perk_fire"), 0f);
        SetObjectChannel(Helpers.Fnv1a32("damage_type"),
            item.GetDamageType() switch
            {
                DestinyDamageTypeEnum.Kinetic => 0.0f,
                DestinyDamageTypeEnum.Solar => 1.0f,
                DestinyDamageTypeEnum.Arc => 2.0f,
                DestinyDamageTypeEnum.Void => 3.0f,
                DestinyDamageTypeEnum.Stasis => 5.0f,
                DestinyDamageTypeEnum.Strand => 6.0f,
                _ => 0.0f
            });
    }

    public void SetObjectChannel(uint hash, float value)
    {
        SetObjectChannel(hash, new Vector4(value));
    }

    public void SetObjectChannel(uint hash, Vector4 value)
    {
        if (!Channels.TryGetValue(hash, out var temp))
            return;

        Channels[hash] = new EditableVector4(value, temp.VectorType);
    }

    private void GetObjectChannels(List<DynamicMeshPart> parts)
    {
        foreach (var part in parts)
        {
            UpdateChannels(part.Material);
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
                var hash = ((TfxDataUint)op.data).value;
                bool isFloat = (nextOp.op == TfxBytecode.PermuteAllX)
                    || (nextOp.op == TfxBytecode.Permute && ((TfxData1Byte)(nextOp.data)).value == 0b00_00_00_00);

                Vector4 val = Vector4.One;
                switch (hash)
                {
                    case 2812804675: // interpolated_world_position
                    case 2046642570: // parent.fp_iron_sight
                    case 286711233: // hydra shield
                    case 2786922960: // belmon shield
                    case 0xFB9CD72C: // trials metal color
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
