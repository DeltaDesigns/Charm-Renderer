using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using Tiger.Schema;
using Buffer = SharpDX.Direct3D11.Buffer;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public class TempScopes : GpuResource
{
    public Buffer ChunkModelScopeBuffer;  // Static
    public Buffer RigidModelScopeBuffer; // Entity, non skinned
    public Buffer FrameScopeBuffer;
    public Buffer TransAdvScopeBuffer;
    public Buffer ColorGradingScopeBuffer;
    public Buffer PostProcessScopeBuffer;

    public TempScopes()
    {
    }

    public void UpdateChunkModelScope(DeviceContext context, MeshPartData mesh, Transform[] transforms)
    {
        if (_disposed)
            return;

        RenderHelpers.Profile("UpdateChunkModelScope");
        if (ChunkModelScopeBuffer is null)
        {
            ChunkModelScopeBuffer = new Buffer(context.Device, new BufferDescription
            {
                // cb1[80]
                SizeInBytes = Utilities.SizeOf<ScopeChunkModelTemp>(), // Utilities.SizeOf<Vector4>() * 80
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.Write,
                OptionFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });
            ChunkModelScopeBuffer.DebugName = $"ChunkModelScopeBuffer Buffer";
        }

        var box = context.MapSubresource(ChunkModelScopeBuffer, 0, MapMode.WriteDiscard, MapFlags.None);
        try
        {
            unsafe
            {
                ScopeChunkModelTemp* dst = (ScopeChunkModelTemp*)box.DataPointer;
                dst->MeshTransform = mesh.MeshTransform;
                dst->UVTransform = new Vector4(mesh.MeshUVTransform.X, mesh.MeshUVTransform.Z, mesh.MeshUVTransform.W, mesh.MaxColorIndex);

                for (int i = 0; i < transforms.Length; i++)
                {
                    var t = transforms[i];
                    Matrix4x4ButGood transform =
                        Matrix4x4.CreateScale(t.Scale)
                        * Matrix4x4.CreateFromQuaternion(t.Quaternion.ToQuat())
                        * Matrix4x4.CreateTranslation(t.Position);
                    transform = transform.Transpose().WithW(new(1f, 1f, 1f, 9.4039E-38f));

                    Unsafe.Write(&dst->Transforms[i * 16], transform);
                }
            }
        }
        finally
        {
            context.UnmapSubresource(ChunkModelScopeBuffer, 0);
        }

        context.VertexShader.SetConstantBuffer(1, ChunkModelScopeBuffer);
        RenderHelpers.EndProfile();
    }

    private ScopeRigidModelTemp _cachedRigidModel = new ScopeRigidModelTemp();
    public void UpdateRigidModelScope(DeviceContext context, MeshPartData mesh, Transform[] transforms, Transform offset)
    {
        if (_disposed)
            return;

        RenderHelpers.Profile("UpdateRigidModelScope");
        if (RigidModelScopeBuffer is null)
        {
            RigidModelScopeBuffer = new Buffer(context.Device, new BufferDescription
            {
                SizeInBytes = Utilities.SizeOf<ScopeRigidModelTemp>(),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });
            RigidModelScopeBuffer.DebugName = $"RigidModelScopeBuffer Buffer";
        }

        Debug.Assert(transforms.Length == 1, "Rigid models should only have one global transform.");

        var t = transforms[0];
        Matrix4x4ButGood transform =
            Matrix4x4.CreateScale(!mesh.Material.Skinned ? (t.Scale * offset.Scale) : t.Scale) *
            Matrix4x4.CreateFromQuaternion(t.Quaternion.ToQuat() * offset.Quaternion.ToQuat()) *
            Matrix4x4.CreateTranslation(t.Position + offset.Position);

        ref var cb1_data = ref _cachedRigidModel;

        cb1_data.LocalToWorld = transform;
        cb1_data.MeshScale = mesh.MeshScale;
        cb1_data.MeshOffset = mesh.MeshTransform;
        cb1_data.UVTransform = mesh.MeshUVTransform;
        cb1_data.DynamicAOValues = Vector4.UnitW;

        context.UpdateSubresource(ref cb1_data, RigidModelScopeBuffer);
        context.VertexShader.SetConstantBuffer(1, RigidModelScopeBuffer);
        context.PixelShader.SetConstantBuffer(1, RigidModelScopeBuffer);
        RenderHelpers.EndProfile();
    }

    private ScopeRigidModelTemp _cachedRigidModelCustom = new ScopeRigidModelTemp();
    public void UpdateRigidModelScopeCustom(DeviceContext context, Transform mapTrans)
    {
        UpdateRigidModelScopeCustom(context, mapTrans, new Transform());
    }
    public void UpdateRigidModelScopeCustom(DeviceContext context, Transform mapTrans, Transform offset)
    {
        if (_disposed)
            return;

        RenderHelpers.Profile("UpdateRigidModelScopeCustom");
        if (RigidModelScopeBuffer is null)
        {
            RigidModelScopeBuffer = new Buffer(context.Device, new BufferDescription
            {
                SizeInBytes = Utilities.SizeOf<ScopeRigidModelTemp>(),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });
            RigidModelScopeBuffer.DebugName = $"RigidModelScopeBuffer Buffer";
        }


        Matrix4x4ButGood transform =
            Matrix4x4.CreateScale(mapTrans.Scale * offset.Scale) *
            Matrix4x4.CreateFromQuaternion(mapTrans.Quaternion.ToQuat() * offset.Quaternion.ToQuat()) *
            Matrix4x4.CreateTranslation(mapTrans.Position + offset.Position);

        ref var cb1_data = ref _cachedRigidModelCustom;

        cb1_data.LocalToWorld = transform;
        cb1_data.MeshScale = Vector4.One;
        cb1_data.MeshOffset = Vector4.Zero;
        //cb1_data.UVTransform = mesh.MeshUVTransform;
        //cb1_data.DynamicAOValues = Vector4.UnitW;

        context.UpdateSubresource(ref cb1_data, RigidModelScopeBuffer);
        context.VertexShader.SetConstantBuffer(1, RigidModelScopeBuffer);
        context.PixelShader.SetConstantBuffer(1, RigidModelScopeBuffer);
        RenderHelpers.EndProfile();
    }

    private ScopeFrameTemp _cachedFrame = new ScopeFrameTemp();
    public void UpdateFrameScope(CharmRenderer renderer)
    {
        if (_disposed)
            return;

        RenderHelpers.Profile("UpdateFrameScope");
        if (FrameScopeBuffer is null)
        {
            FrameScopeBuffer = new Buffer(renderer.Device, new BufferDescription
            {
                SizeInBytes = Utilities.SizeOf<ScopeFrameTemp>(),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });
            FrameScopeBuffer.DebugName = $"FrameScopeBuffer Buffer";
        }

        var frame = renderer.Externs.Frame;
        ref var cb13_data = ref _cachedFrame;

        cb13_data.Row0 = new Vector4(frame.GameTime, frame.RenderTime, frame.DeltaTime, frame.ExposureTime);
        cb13_data.Row1 = new Vector4(frame.ExposureScale, frame.ExposureIllumRelative * 16f, frame.ExposureScale, frame.ExposureIllumRelative);
        cb13_data.Row2 = new Vector4(
            (frame.RenderTime + 33.75f) * 1.258699f,
            (frame.RenderTime + 60.0f) * 0.9583125f,
            (frame.RenderTime + 60.0f) * 8.789123f,
            (frame.RenderTime + 33.75f) * 2.311535f);

        cb13_data.Row4 = frame.Unk1C0;
        cb13_data.Row6 = new Vector4(0f, 1f, MathF.Sin(frame.GameTime * 6.0f) * 0.5f + 0.5f, 0f);


        renderer.Context.UpdateSubresource(ref cb13_data, FrameScopeBuffer);
        renderer.Context.VertexShader.SetConstantBuffer(13, FrameScopeBuffer);
        renderer.Context.PixelShader.SetConstantBuffer(13, FrameScopeBuffer);
        RenderHelpers.EndProfile();
    }

    private ScopeTransparentAdvanced _cachedTransAdv = new ScopeTransparentAdvanced();
    public void UpdateTransparentAdvancedScope(DeviceContext context)
    {
        if (_disposed)
            return;

        RenderHelpers.Profile("UpdateTransparentAdvancedScope");
        if (TransAdvScopeBuffer is null)
        {
            TransAdvScopeBuffer = new Buffer(context.Device, new BufferDescription
            {
                SizeInBytes = Utilities.SizeOf<ScopeTransparentAdvanced>(),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });
            TransAdvScopeBuffer.DebugName = $"TransAdvScopeBuffer Buffer";
        }

        ref var cb8_data = ref _cachedTransAdv;
        context.UpdateSubresource(ref cb8_data, TransAdvScopeBuffer);
        context.VertexShader.SetConstantBuffer(8, TransAdvScopeBuffer);
        context.PixelShader.SetConstantBuffer(8, TransAdvScopeBuffer);
        RenderHelpers.EndProfile();
    }

    private ScopeColorGrading _cachedColorGrading = new ScopeColorGrading();
    private static readonly Vector4[] zeroColorGrade = new Vector4[Utilities.SizeOf<ScopeColorGrading>()];
    public void UpdateColorGradingScope(DeviceContext context, bool fillZero = false)
    {
        if (_disposed)
            return;

        RenderHelpers.Profile("UpdateColorGradingScope");
        if (ColorGradingScopeBuffer is null)
        {
            ColorGradingScopeBuffer = new Buffer(context.Device, new BufferDescription
            {
                SizeInBytes = Utilities.SizeOf<ScopeColorGrading>(),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });
            ColorGradingScopeBuffer.DebugName = $"ColorGradingScope Buffer";
        }

        ref var cb7_data = ref _cachedColorGrading;
        if (fillZero)
            context.UpdateSubresource(zeroColorGrade, ColorGradingScopeBuffer);
        else
            context.UpdateSubresource(ref cb7_data, ColorGradingScopeBuffer);

        context.VertexShader.SetConstantBuffer(7, ColorGradingScopeBuffer);
        context.PixelShader.SetConstantBuffer(7, ColorGradingScopeBuffer);
        RenderHelpers.EndProfile();
    }

    private ScopePostProcess _cachedPostProcess = new ScopePostProcess();
    public void UpdatePostProcessScope(DeviceContext context, ScopePostProcess scope)
    {
        if (_disposed)
            return;

        RenderHelpers.Profile("UpdatePostProcessScope");
        if (PostProcessScopeBuffer is null)
        {
            PostProcessScopeBuffer = new Buffer(context.Device, new BufferDescription
            {
                SizeInBytes = Utilities.SizeOf<ScopePostProcess>(),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });
            PostProcessScopeBuffer.DebugName = $"PostProcessScope Buffer";
        }

        //ref var cb11_data = ref _cachedPostProcess;
        context.UpdateSubresource(ref scope, PostProcessScopeBuffer);

        //context.VertexShader.SetConstantBuffer(11, PostProcessScopeBuffer);
        context.PixelShader.SetConstantBuffer(11, PostProcessScopeBuffer);
        RenderHelpers.EndProfile();
    }

    private bool _disposed;
    public override void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        ChunkModelScopeBuffer?.Dispose();
        RigidModelScopeBuffer?.Dispose();
        FrameScopeBuffer?.Dispose();
        TransAdvScopeBuffer?.Dispose();
        ColorGradingScopeBuffer?.Dispose();
        PostProcessScopeBuffer?.Dispose();

        base.Dispose();
    }
}

public class TfxScope : GpuResource
{
    public TfxScope(SScope scope, DeviceContext context)
    {
        Scope = scope;
        Load(context);
    }

    public string Name { get; set; }
    public SScope Scope { get; set; }
    public TfxScopeStage Pixel { get; set; }
    public TfxScopeStage Vertex { get; set; }

    public void Load(DeviceContext context)
    {
        Name = Scope.DevName.Value;
        Pixel = Scope.Pixel.Value.CBufferSlot != -1 ? new TfxScopeStage(Scope.Pixel.Value, ShaderStage.Pixel, Name, context) : null;
        Vertex = Scope.Vertex.Value.CBufferSlot != -1 ? new TfxScopeStage(Scope.Vertex.Value, ShaderStage.Vertex, Name, context) : null;
    }

    public async Task<Vector4[]> GetEvaluated(CharmRenderer renderer)
    {
        return await Pixel?.GetEvaluated(renderer);
    }

    public void BindTextures(DeviceContext context)
    {
        Pixel?.BindTextures(context);
        Vertex?.BindTextures(context);
    }

    public void Bind(CharmRenderer renderer)
    {
        Pixel?.Bind(renderer);
        Vertex?.Bind(renderer);
    }

    public override void Dispose()
    {
        Vertex?.Dispose();
        Pixel?.Dispose();
        base.Dispose();
    }
}

public class TfxScopeStage : GpuResource
{
    public TfxScopeStage(SScopeStage stage, ShaderStage shaderStage, string name, DeviceContext context)
    {
        ScopeStage = stage;
        ShaderStage = shaderStage;
        Load(name, context);
    }

    public SScopeStage ScopeStage { get; set; }
    public ShaderStage ShaderStage { get; set; }
    public Constants ScopeConstants { get; set; }

    public void Load(string name, DeviceContext context)
    {
        ScopeConstants = new($"Constants Scope {name}");
        var cbuffer = ScopeStage.GetCBuffer();
        if (cbuffer.Count != 0)
        {
            var casted = cbuffer.Select(x => new System.Numerics.Vector4(x.X, x.Y, x.Z, x.W)).ToArray();
            ScopeConstants.Buffer = new Buffer(context.Device, new BufferDescription
            {
                SizeInBytes = Utilities.SizeOf<System.Numerics.Vector4>() * casted.Length,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.Write,
                OptionFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });
            ScopeConstants.Buffer.DebugName = "ScopeConstants Buffer";
            ScopeConstants.ConstantValues = casted;
            context.UpdateSubresource(casted, ScopeConstants.Buffer);
        }

        ScopeConstants.Slot = ScopeStage.CBufferSlot;
        ScopeConstants.Textures = AssetManager.Get().CreateTextures(ScopeStage.Textures);
        ScopeConstants.Samplers = AssetManager.Get().CreateSamplers(ScopeStage.EnumerateSamplers().ToList());
        ScopeConstants.BytecodeConstants = ScopeStage.TFX_Bytecode_Constants.Select(x => new System.Numerics.Vector4(x.Vec.X, x.Vec.Y, x.Vec.Z, x.Vec.W)).ToArray();
        ScopeConstants.BytecodeInterpreter = new TfxBytecodeInterpreter(TfxBytecodeOp.ParseAll(ScopeStage.TFX_Bytecode));
        ScopeConstants.BytecodeInterpreter.Name = $"Scope {name} : {ShaderStage}";
    }

    public async Task<Vector4[]> GetEvaluated(CharmRenderer renderer)
    {
        return await ScopeConstants?.GetEvaluated(renderer);
    }

    public void BindTextures(DeviceContext context)
    {
        ScopeConstants.BindTextures(context, ShaderStage);
    }

    public void Bind(CharmRenderer renderer)
    {
        ScopeConstants.Bind(renderer, ShaderStage);
    }

    public override void Dispose()
    {
        ScopeConstants?.Dispose();
        ScopeConstants = null;

        base.Dispose();
    }
}

// Statics
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ScopeChunkModelTemp
{
    public Vector4 MeshTransform;
    public Vector4 UVTransform;
    public fixed float Transforms[16 * 16]; // 16 Matrix4x4s inline
}

// Entities, non skinned
[StructLayout(LayoutKind.Sequential)]
public struct ScopeRigidModelTemp
{
    public Matrix4x4 LocalToWorld;
    public Vector4 MeshScale;
    public Vector4 MeshOffset;
    public Vector4 UVTransform;
    public Vector4 DynamicAOValues;
}

[StructLayout(LayoutKind.Sequential)]
public struct ScopeFrameTemp
{
    public ScopeFrameTemp()
    {

    }

    public Vector4 Row0 = Vector4.Zero;
    public Vector4 Row1 = Vector4.Zero;
    public Vector4 Row2 = Vector4.Zero;
    public Vector4 Row3 = new(0.5f, 0.5f, 0f, 0f);
    public Vector4 Row4 = new(1f, 1f, 0f, 1f);
    public Vector4 Row5 = new(0f, 0f, 512f, 0f);
    public Vector4 Row6 = Vector4.Zero;
    public Vector4 Row7 = new(0f, 0.5f, 180f, 0f);
}

[StructLayout(LayoutKind.Sequential)]
public struct ScopeTransparentAdvanced
{
    public ScopeTransparentAdvanced()
    {

    }

    // GROSS
    public Vector4 Row0 = new(0.0009849314f, 0.0019836868f, 0.0007783567f, 0.0015586712f);
    public Vector4 Row1 = new(0.00098604f, 0.002085914f, 0.0009838239f, 0.0018864698f);
    public Vector4 Row2 = new(0.0011860824f, 0.0024346288f, 0.0009468408f, 0.001850187f);
    public Vector4 Row3 = new(0.7903466f, 0.7319064f, 0.56213695f, 0.0f);
    public Vector4 Row4 = new(0.0f, 1.0f, 0.109375f, 0.046875f);
    public Vector4 Row5 = new(0.0f, 0.0f, 0.0f, 0.00086945295f);
    public Vector4 Row6 = new(0.55f, 0.41091052f, 0.22670946f, 0.50381273f);
    public Vector4 Row7 = new(1.0f, 1.0f, 1.0f, 0.9997778f);
    public Vector4 Row8 = new(132.92885f, 66.40444f, 56.853416f, 0.0f);
    public Vector4 Row9 = new(132.92885f, 66.40444f, 1000.0f, 0.0001f);
    public Vector4 Row10 = new(131.92885f, 65.40444f, 55.853416f, 0.6784314f);
    public Vector4 Row11 = new(131.92885f, 65.40444f, 999.0f, 5.5f);
    public Vector4 Row12 = new(0.0f, 0.5f, 25.575994f, 0.0f);
    public Vector4 Row13 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row14 = new(0.025f, 10000.0f, -9999.0f, 1.0f);
    public Vector4 Row15 = new(1.0f, 1.0f, 1.0f, 0.0f);
    public Vector4 Row16 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row17 = new(10.979255f, 7.1482353f, 6.3034935f, 0.0f);
    public Vector4 Row18 = new(0.0037614072f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row19 = new(0.0f, 0.0075296126f, 0.0f, 0.0f);
    public Vector4 Row20 = new(0.0f, 0.0f, 0.017589089f, 0.0f);
    public Vector4 Row21 = new(0.27266484f, -0.31473818f, -0.15603681f, 1.0f);
    public Vector4 Row22 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row23 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row24 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row25 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row26 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row27 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row28 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row29 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row30 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row31 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row32 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row33 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row34 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row35 = new(0.0f, 0.0f, 0.0f, 0.0f);
    public Vector4 Row36 = new(1.0f, 0.0f, 0.0f, 0.0f);
}

[StructLayout(LayoutKind.Sequential)]
public struct ScopeColorGrading
{
    public ScopeColorGrading()
    {

    }

    public Vector4 Row0 = new(1f, 0, 0, 0);
    public Vector4 Row1 = new(2f, 1f, 0.065f, 0.1f);
    public Vector4 Row2 = new(0.69138f, 0.75f, 0.65642f, 0.00f);
    public Vector4 Row3 = new(0.46957f, 0.02f, 0.08f, 0.90f);
    public Vector4 Row4 = new(0.80f, 1.00f, 1.00f, 0.00f);
}

[StructLayout(LayoutKind.Sequential)]
public struct ScopePostProcess
{
    public ScopePostProcess()
    {

    }

    public Vector4 OutRes;
    public Vector4 InRes;
    public Vector4 Unk02;
    public Vector4 Unk03;
    public Vector4 Unk04;
    public Vector4 Unk05;
    public Vector4 Unk06;
    public Vector4 Unk07;
}
