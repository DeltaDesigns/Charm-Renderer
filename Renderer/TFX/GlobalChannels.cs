using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using static TfxBytecodeOp;

namespace Charm.Renderer;

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
        var globals = ((S80808179)resource.TagData.Unk18.GetValue(resource.GetReader()));
        DynamicArray<S808091F1> map = globals.Array1;
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

        foreach (S808091F1 entry in map)
        {
            if (entry.Unk10.GetValue(resource.GetReader()) is S808091D1 global)
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
                        Value = global.Values.FirstOrDefault().Vec,
                    };
                    channel.IsDynamic = channel.Bytecode.Length > 4;
                    channel.InterpretedBytecode.Name = $"Global Channel {name} ({index})";
                    Channels[index] = channel;

                    //if (channel.Name == "sun_glow_intensity" || channel.ID.Hash32 == 0x56007c7)
                    //{
                    //    Console.WriteLine($"--- {channel.Name} ({channel.ID.Hash32:X2}) ---");
                    //    foreach (var op in channel.InterpretedBytecode.Opcodes)
                    //    {
                    //        var opString = $"0x{op.rawOp:X2} {op.op} : {TfxBytecodeOp.TfxToString(op, global.Values, null)}";
                    //        Console.WriteLine(opString);
                    //    }
                    //    Console.WriteLine("\n");
                    //}
                }
            }
        }

        Evaluate();
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

    public void Evaluate()
    {
        foreach (var channel in Channels)
        {
            channel.Evaluate(this);
        }
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
        public bool IsDynamic = false;

        public GlobalChannel()
        {

        }

        public void Evaluate(RendererGlobalChannels globals)
        {
            if (Bytecode.Length == 0 || BytecodeConstants.Length == 0)
                return;

            InterpretedBytecode.Evaluate(
                null,
                new System.Numerics.Vector4[1],
                BytecodeConstants,
                null,
                null,
                null,
                out System.Numerics.Vector4[] evaluated,
                globalChannels: globals);

            Value = evaluated[0];
        }
    }
}
