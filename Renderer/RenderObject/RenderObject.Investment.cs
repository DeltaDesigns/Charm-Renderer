using Arithmic;
using SharpDX;
using SharpDX.Direct3D11;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Tiger.Schema.Investment;
using Buffer = SharpDX.Direct3D11.Buffer;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public class InvestmentData : GpuResource
{
    public InventoryItem BaseItem;
    public Entity OwnerEntity;

    public Buffer InvestmentBuffer;
    public InvestmentDye InvestmentDye0 { get; set; }
    public InvestmentDye InvestmentDye1 { get; set; }
    public InvestmentDye InvestmentDye2 { get; set; }
    private DyeMerger _merger = new();
    private volatile bool _isChangingDyes = false;
    private bool _hasData = false;

    // TODO, tie into AssetManager
    public TextureAsset DiffusePlate { get; set; }
    public TextureAsset GStackPlate { get; set; }
    public TextureAsset NormalPlate { get; set; }
    public TextureAsset DyePlate { get; set; }

    public InvestmentData(DeviceContext context, Entity itemEnt, InventoryItem item)
    {
        CreateInvestmentData(context, itemEnt, item);
    }

    public void CreateInvestmentData(DeviceContext context, Entity itemEnt, InventoryItem item)
    {
        if (itemEnt.ModelParent is null)
            return;

        BaseItem = item;
        OwnerEntity = itemEnt;

        var parentResource = (S80806D8F)itemEnt.ModelParent.TagData.Unk18.GetValue(itemEnt.ModelParent.GetReader());
        if (parentResource.TexturePlates is not null && item.TagData.Unk90.GetValue(item.GetReader()) is S80807377)
        {
            S80806E1C plates = parentResource.TexturePlates.TagData;
            DiffusePlate ??= AssetManager.Get().CreateFromPlate(plates.AlbedoPlate);
            GStackPlate ??= AssetManager.Get().CreateFromPlate(plates.NormalPlate);
            NormalPlate ??= AssetManager.Get().CreateFromPlate(plates.GStackPlate);
            DyePlate ??= AssetManager.Get().CreateFromPlate(plates.DyemapPlate);

            CreateDefaultDyes(context, item);
            InvestmentBuffer = new Buffer(context.Device, new BufferDescription
            {
                SizeInBytes = Utilities.SizeOf<System.Numerics.Vector4>() * 63,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.Write,
                OptionFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });
            _hasData = true;
        }
        else
        {
            _hasData = false;
        }
    }

    public void ResetDyes(DeviceContext context)
    {
        if (!_hasData)
            return;

        CreateDefaultDyes(context, BaseItem);
    }

    public void CreateDefaultDyes(DeviceContext context, InventoryItem item)
    {
        Dictionary<uint, Dye> dyes = new();
        if (item.TagData.Unk90.GetValue(item.GetReader()) is S80807377 translationBlock)
        {
            _isChangingDyes = true;
            foreach (S8080737B dyeEntry in translationBlock.DefaultDyes)
            {
                Dye dye = Investment.Get().GetDyeFromIndex(dyeEntry.GetDyeIndex());
                if (dye is null)
                    continue;

                dyes.Add(Investment.Get().GetChannelHashFromIndex(dyeEntry.GetChannelIndex()), dye);
                //Log.Debug($"DefaultDye {dye.Hash} : {Investment.Get().GetChannelHashFromIndex(dyeEntry.ChannelIndex)}");
            }
            foreach (S8080737B dyeEntry in translationBlock.LockedDyes)
            {
                Dye dye = Investment.Get().GetDyeFromIndex(dyeEntry.GetDyeIndex());
                if (dye is null)
                    continue;

                dyes.Add(Investment.Get().GetChannelHashFromIndex(dyeEntry.GetChannelIndex()), dye);
                //Log.Debug($"LockedDye {dye.Hash} : {Investment.Get().GetChannelHashFromIndex(dyeEntry.ChannelIndex)}");
            }
            if (dyes.Count == 0)
            {
                Log.Debug("Shader has no dyes.");
                _isChangingDyes = false;
                return;
            }

            //Debug.Assert(dyes.Count == 3, $"Only {dyes.Count} dyes : {string.Join(", ", dyes.Values.Select(x => x.Hash))}");
            InvestmentDye0?.Dispose();
            InvestmentDye1?.Dispose();
            InvestmentDye2?.Dispose();

            InvestmentDye0 = CreateDye(0);
            InvestmentDye1 = CreateDye(1);
            InvestmentDye2 = CreateDye(2);

            InvestmentDye CreateDye(int index)
            {
                int safeIndex = Math.Min(index, dyes.Count - 1);
                var entry = dyes.ElementAt(safeIndex);

                var dye = new InvestmentDye(context, entry.Key, entry.Value.TagData);
                //Log.Debug($"Created Dye{index} : {dye.ChannelHash}");
                return dye;
            }

            _isChangingDyes = false;
        }
    }

    public void CreateCustomDyes(DeviceContext context, InventoryItem shader)
    {
        if (!_hasData) return;

        Dictionary<uint, Dye> dyes = new();
        if (shader.TagData.Unk90.GetValue(shader.GetReader()) is S80807377 translationBlock)
        {
            _isChangingDyes = true;
            var dyeEntries = translationBlock.CustomDyes.Any() // Should never happen, only case ive seen is the Shared Experience shader (which isnt even an actual shader)
                ? translationBlock.CustomDyes
                : translationBlock.DefaultDyes;

            foreach (S8080737B dyeEntry in dyeEntries)
            {
                Dye dye = Investment.Get().GetDyeFromIndex(dyeEntry.GetDyeIndex());
                if (dye is null)
                    continue;

                dyes.Add(Investment.Get().GetChannelHashFromIndex(dyeEntry.GetChannelIndex()), dye);
            }
            if (dyes.Count == 0)
            {
                Log.Debug("Shader contains no dyes");
                return;
            }

            InvestmentDye0?.Dispose();
            InvestmentDye1?.Dispose();
            InvestmentDye2?.Dispose();

            if (!translationBlock.CustomDyes.Any() && dyes.Count == 3) // again, should never happen
            {
                if (InvestmentDye0 is not null)
                    InvestmentDye0.Dye = new(dyes.ElementAt(0).Value.TagData, context);
                if (InvestmentDye1 is not null)
                    InvestmentDye1.Dye = new(dyes.ElementAt(1).Value.TagData, context);
                if (InvestmentDye2 is not null)
                    InvestmentDye2.Dye = new(dyes.ElementAt(2).Value.TagData, context);
            }
            else
            {
                if (InvestmentDye0 is not null)
                    InvestmentDye0.Dye = new(dyes[InvestmentDye0.ChannelHash].TagData, context);
                if (InvestmentDye1 is not null)
                    InvestmentDye1.Dye = new(dyes[InvestmentDye1.ChannelHash].TagData, context);
                if (InvestmentDye2 is not null)
                    InvestmentDye2.Dye = new(dyes[InvestmentDye2.ChannelHash].TagData, context);
            }

            _isChangingDyes = false;
        }
    }

    public async void Bind(CharmRenderer renderer)
    {
        if (!_hasData) return;
        RenderHelpers.Profile("Investment Dye Bind");

        renderer.Context.PixelShader.SetShaderResource(0, DiffusePlate?.SRV);
        renderer.Context.PixelShader.SetShaderResource(1, GStackPlate?.SRV);
        renderer.Context.PixelShader.SetShaderResource(2, NormalPlate?.SRV);
        renderer.Context.PixelShader.SetShaderResource(3, DyePlate?.SRV);

        if (_isChangingDyes || InvestmentDye0 is null)
            return;

        InvestmentDye0.Bind(renderer.Context);
        InvestmentDye1.Bind(renderer.Context);
        InvestmentDye2.Bind(renderer.Context);

        var eval0 = await InvestmentDye0.Dye.GetEvaluated(renderer);
        var eval1 = await InvestmentDye1.Dye.GetEvaluated(renderer);
        var eval2 = await InvestmentDye2.Dye.GetEvaluated(renderer);

        try
        {
            _merger.Merge(eval0, eval1, eval2);
            _merger.Move(21, 3);
            _merger.Move(22, 4);
            _merger.Move(23, 5);

            _merger.Move(42, 6);
            _merger.Move(43, 7);
            _merger.Move(44, 8);

            Vector4[] mergedCB = _merger.ToArray();
            DataBox dataBox = renderer.Context.MapSubresource(InvestmentBuffer, 0, MapMode.WriteDiscard, MapFlags.None);
            try
            {
                Utilities.Write(dataBox.DataPointer, mergedCB, 0, mergedCB.Length);
            }
            finally
            {
                renderer.Context.UnmapSubresource(InvestmentBuffer, 0);
            }
        }
        finally
        {
            renderer.Context.PixelShader.SetConstantBuffer(7, InvestmentBuffer);
        }
        RenderHelpers.EndProfile();
    }

    public override void Dispose()
    {
        Utilities.Dispose(ref InvestmentBuffer);
        InvestmentDye0?.Dispose();
        InvestmentDye0 = null;
        InvestmentDye1?.Dispose();
        InvestmentDye1 = null;
        InvestmentDye2?.Dispose();
        InvestmentDye2 = null;

        AssetManager.Get().ReleaseTexture(DiffusePlate);
        DiffusePlate = null;
        AssetManager.Get().ReleaseTexture(GStackPlate);
        GStackPlate = null;
        AssetManager.Get().ReleaseTexture(NormalPlate);
        NormalPlate = null;
        AssetManager.Get().ReleaseTexture(DyePlate);
        DyePlate = null;

        _merger = null;

        base.Dispose();
    }
}

public class InvestmentDye : GpuResource
{
    public uint ChannelHash { get; set; }
    public TfxScope Dye { get; set; }

    public InvestmentDye(DeviceContext context, uint channelHash, SScope dye)
    {
        ChannelHash = channelHash;
        Dye = new(dye, context);
    }

    public void Bind(DeviceContext context)
    {
        Dye?.BindTextures(context);
    }

    public override void Dispose()
    {
        Dye?.Dispose();
        Dye = null;

        base.Dispose();
    }
}
