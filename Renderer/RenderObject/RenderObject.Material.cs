using System.IO;
using SharpDX;
using SharpDX.Direct3D11;
using Tiger;
using Tiger.Schema;
using Buffer = SharpDX.Direct3D11.Buffer;
using Material = Tiger.Schema.Shaders.Material;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public class MaterialData : GpuResource
{
    public FileHash Hash;
    public ShaderBindMode BindMode { get; set; }
    public StateSelection States;
    public List<Tiger.TfxScope> UsedScopes;

    public TechniqueStage Vertex;
    public TechniqueStage Pixel;
    public TechniqueStage Compute;

    // temp, for vs override
    public bool Skinned = false;
    public bool UsesVertexColor = false;
    public bool UsesGearDye = false;

    public int RefCount;

    public MaterialData(DeviceContext context, Material material)
    {
        Hash = material.Hash;
        BindMode = material.BindMode;
        States = material.RenderStates;
        UsedScopes = material.EnumerateScopes().ToList();
        Skinned = UsedScopes.Contains(Tiger.TfxScope.SKINNING);
        UsesGearDye = UsedScopes.Contains(Tiger.TfxScope.GEAR_DYE_012);

        if (material.Vertex.Shader != null)
            Vertex = new TechniqueStage(context, material.Vertex, ShaderStage.Vertex, material.Hash);

        if (material.Pixel.Shader != null)
            Pixel = new TechniqueStage(context, material.Pixel, ShaderStage.Pixel, material.Hash);

        if (material.Compute.Shader != null)
            Compute = new TechniqueStage(context, material.Compute, ShaderStage.Compute, material.Hash);
    }

    public void Bind(CharmRenderer renderer)
    {
        var states = renderer.CMD.States.DefaultState.Select(States);
        renderer.CMD.States.SetState(renderer.Context, states);

        switch (BindMode)
        {
            case ShaderBindMode.VertexPixel:
                renderer.Context.ComputeShader.Set(null);

                Vertex?.Bind(renderer);
                SetVSOverride(renderer);
                Pixel?.Bind(renderer);
                break;

            case ShaderBindMode.VertexOnly:
                renderer.Context.PixelShader.Set(null);
                renderer.Context.ComputeShader.Set(null);

                Vertex?.Bind(renderer);
                SetVSOverride(renderer);
                break;

            case ShaderBindMode.Compute:
                renderer.Context.VertexShader.Set(null);
                renderer.Context.PixelShader.Set(null);

                Compute?.Bind(renderer);
                break;

            default:
                throw new NotImplementedException($"BindMode {BindMode} not implemented.");
        }
    }

    private void SetVSOverride(CharmRenderer renderer)
    {
        if (Skinned)
        {
            if (UsesGearDye)
            {
                if (UsesVertexColor)
                    renderer.Context.VertexShader.Set(AssetManager.Get().InvestmentOverrideVS_VC);
                else
                    renderer.Context.VertexShader.Set(AssetManager.Get().InvestmentOverrideVS_NoVC);
            }
            else
            {
                if (UsesVertexColor)
                    renderer.Context.VertexShader.Set(AssetManager.Get().EntityOverrideVS_VC);
                else
                    renderer.Context.VertexShader.Set(AssetManager.Get().EntityOverrideVS_NoVC);
            }
        }
    }

    public async Task<Vector4[]> GetEvaluated(CharmRenderer renderer)
    {
        return await Pixel?.GetEvaluated(renderer);
    }

    public void AddRef()
    {
        RefCount++;
    }

    public bool Release()
    {
        RefCount--;
        return RefCount <= 0;
    }

    public override void Dispose()
    {
        RefCount = 0;
        Vertex?.Dispose();
        Pixel?.Dispose();

        base.Dispose();
    }
}

public class TechniqueStage : GpuResource
{
    public IShader Shader { get; set; }
    public ShaderStage Stage { get; set; }
    public Constants Constants { get; set; }
    public string DebugName { get; set; }

    public TechniqueStage(DeviceContext context, SMaterialShader shader, ShaderStage stage, FileHash materialHash)
    {
        Stage = stage;
        Constants = new(context, shader, stage, materialHash);
        DebugName = $"TechniqueStage {materialHash}";

        using (var stream = new MemoryStream(shader.Shader.GetBytecode()))
        {
            var shaderByteCode = SharpDX.D3DCompiler.ShaderBytecode.Load(stream);
            Shader = ShaderFactory.CreateShader(context, stage, shaderByteCode, materialHash, shader.Shader.Hash);
        }
    }

    public void Bind(CharmRenderer renderer)
    {
        Shader?.Bind(renderer.Context);
        Constants?.Bind(renderer, Stage);
    }

    public void Unbind(CharmRenderer renderer)
    {
        Shader?.Unbind(renderer.Context);
    }

    public async Task<Vector4[]> GetEvaluated(CharmRenderer renderer)
    {
        return await Constants?.GetEvaluated(renderer);
    }

    public override void Dispose()
    {
        Shader?.Dispose();
        Shader = null;
        Constants?.Dispose();
        Constants = null;

        base.Dispose();
    }
}

public class Constants : GpuResource
{
    public Buffer Buffer;
    public int Slot;

    public SMaterialShader Shader;
    public TfxBytecodeInterpreter? BytecodeInterpreter;

    public Vector4[] ConstantValues;
    public Vector4[] BytecodeConstants;
    public List<SamplerAsset> Samplers = new();
    public Dictionary<uint, TextureAsset> Textures = new();
    public string DebugName { get; set; }

    public Constants(string debugName)
    {
        DebugName = debugName;
    }

    public Constants(DeviceContext context, SMaterialShader shader, ShaderStage stage, FileHash materialHash)
    {
        DebugName = $"Constants {materialHash}";

        if (shader.GetCBuffer0().Count != 0)
        {
            var cbuffer = shader.GetCBuffer0().Select(x => new System.Numerics.Vector4(x.X, x.Y, x.Z, x.W)).ToArray();
            Buffer = new Buffer(context.Device, new BufferDescription
            {
                SizeInBytes = Utilities.SizeOf<System.Numerics.Vector4>() * cbuffer.Length,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.Write,
                OptionFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });
            Buffer.DebugName = $"{materialHash} Buffer";
            ConstantValues = cbuffer;
            context.UpdateSubresource(cbuffer, Buffer);
        }
        Shader = shader;
        Slot = shader.BufferSlot;
        Samplers = AssetManager.Get().CreateSamplers(shader);
        Textures = AssetManager.Get().CreateTextures(shader);

        BytecodeConstants = shader.TFX_Bytecode_Constants.Select(x => new System.Numerics.Vector4(x.Vec.X, x.Vec.Y, x.Vec.Z, x.Vec.W)).ToArray();
        BytecodeInterpreter = new TfxBytecodeInterpreter(TfxBytecodeOp.ParseAll(shader.TFX_Bytecode));
        BytecodeInterpreter.Name = $"Technique {materialHash} : {stage}";
    }

    public async Task Bind(CharmRenderer renderer, ShaderStage stage)
    {
        switch (stage)
        {
            case ShaderStage.Vertex when Slot != -1:
                renderer.Context.VertexShader.SetConstantBuffer(Slot, Buffer);
                break;

            case ShaderStage.Pixel when Slot != -1:
                renderer.Context.PixelShader.SetConstantBuffer(Slot, Buffer);
                break;

            case ShaderStage.Compute when Slot != -1:
                renderer.Context.ComputeShader.SetConstantBuffer(Slot, Buffer);
                break;
        }

        foreach (var tex in Textures)
        {
            switch (stage)
            {
                case ShaderStage.Vertex:
                    renderer.Context.VertexShader.SetShaderResource((int)tex.Key, tex.Value.SRV);
                    break;

                case ShaderStage.Pixel:
                    renderer.Context.PixelShader.SetShaderResource((int)tex.Key, tex.Value.SRV);
                    break;

                case ShaderStage.Compute:
                    renderer.Context.ComputeShader.SetShaderResource((int)tex.Key, tex.Value.SRV);
                    break;
            }
        }

        if (BytecodeInterpreter == null)
            return;

        var evaluated = await GetEvaluated(renderer);

        if (Buffer == null)
            return;

        DataBox dataBox = renderer.Context.MapSubresource(Buffer, 0, MapMode.WriteDiscard, MapFlags.None);
        try
        {
            Utilities.Write(dataBox.DataPointer, evaluated, 0, evaluated.Length);
        }
        finally
        {
            renderer.Context.UnmapSubresource(Buffer, 0);
        }
    }

    public void BindTextures(DeviceContext context, ShaderStage stage)
    {
        foreach (var tex in Textures)
        {
            switch (stage)
            {
                case ShaderStage.Vertex:
                    context.VertexShader.SetShaderResource((int)tex.Key, tex.Value.SRV);
                    break;

                case ShaderStage.Pixel:
                    context.PixelShader.SetShaderResource((int)tex.Key, tex.Value.SRV);
                    break;
            }
        }
    }

    public async Task<Vector4[]> GetEvaluated(CharmRenderer renderer)
    {
        var evaluated = await BytecodeInterpreter.EvaluateAsync(
            renderer,
            ConstantValues,
            BytecodeConstants,
            Shader,
            Samplers,
            renderer.EntityObjectChannels,
            globalChannels: renderer.World.GlobalChannels);

        return evaluated;
    }

    public override void Dispose()
    {
        Buffer?.Dispose();
        Buffer = null;

        foreach (var tex in Textures) // De-frefs textures, disposing is handled by AssetManager
        {
            AssetManager.Get().ReleaseTexture(tex.Value);
        }
        Textures.Clear();

        foreach (var sampler in Samplers)
        {
            AssetManager.Get().ReleaseSampler(sampler);
        }
        Samplers.Clear();

        Buffer = null;
        BytecodeInterpreter = null;
        ConstantValues = null;
        BytecodeConstants = null;

        base.Dispose();
    }
}
