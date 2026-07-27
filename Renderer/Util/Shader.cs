using System.Diagnostics;
using SharpDX.Direct3D11;
using Tiger;

namespace Charm.Renderer;

public interface IShader : IDisposable
{
    void Bind(DeviceContext context);
    void Unbind(DeviceContext context);
}

public struct VertexShaderWrapper : IShader
{
    public VertexShader Shader;

    public VertexShaderWrapper(DeviceContext context, byte[] bytecode,
        FileHash materialHash,
        FileHash shaderHash)
    {
        Debug.Assert(bytecode.Length != 0);
        Shader = new VertexShader(context.Device, bytecode);
        Shader.DebugName = $"Technique {materialHash} : Vertex {shaderHash}";
    }

    public void Bind(DeviceContext context)
    {
        context.VertexShader.Set(Shader);
    }

    public void Unbind(DeviceContext context)
    {
        context.VertexShader.Set(null);
    }

    public void Dispose()
    {
        Shader?.Dispose();
        Shader = null;
    }
}

public struct PixelShaderWrapper : IShader
{
    public PixelShader Shader;

    public PixelShaderWrapper(DeviceContext context,
        byte[] bytecode,
        FileHash materialHash,
        FileHash shaderHash)
    {
        Debug.Assert(bytecode.Length != 0);
        Shader = new PixelShader(context.Device, bytecode);
        Shader.DebugName = $"Technique {materialHash} : Pixel {shaderHash}";
    }

    public void Bind(DeviceContext context)
    {
        context.PixelShader.Set(Shader);
    }

    public void Unbind(DeviceContext context)
    {
        context.PixelShader.Set(null);
    }

    public void Dispose()
    {
        Shader?.Dispose();
        Shader = null;
    }
}

public struct ComputeShaderWrapper : IShader
{
    public ComputeShader Shader;

    public ComputeShaderWrapper(DeviceContext context,
        byte[] bytecode,
        FileHash materialHash,
        FileHash shaderHash)
    {
        Debug.Assert(bytecode.Length != 0);
        Shader = new ComputeShader(context.Device, bytecode);
        Shader.DebugName = $"Technique {materialHash} : Compute {shaderHash}";
    }

    public void Bind(DeviceContext context)
    {
        context.ComputeShader.Set(Shader);
    }

    public void Unbind(DeviceContext context)
    {
        context.ComputeShader.Set(null);
    }

    public void Dispose()
    {
        Shader?.Dispose();
        Shader = null;
    }
}

public static class ShaderFactory
{
    public static IShader CreateShader(DeviceContext context,
        ShaderStage stage,
        byte[] bytecode,
        FileHash materialHash,
        FileHash shaderHash)
    {
        return stage switch
        {
            ShaderStage.Vertex => new VertexShaderWrapper(context, bytecode, materialHash, shaderHash),
            ShaderStage.Pixel => new PixelShaderWrapper(context, bytecode, materialHash, shaderHash),
            ShaderStage.Compute => new ComputeShaderWrapper(context, bytecode, materialHash, shaderHash),
            _ => throw new NotImplementedException()
        };
    }
}

public enum ShaderStage
{
    Pixel = 1,
    Vertex = 2,
    Geometry = 3,
    Hull = 4,
    Compute = 5,
    Domain = 6,
}

public static class ShaderStageExtensions
{
    public static ShaderStage? FromIndex(byte index)
    {
        return index switch
        {
            1 => ShaderStage.Pixel,
            2 => ShaderStage.Vertex,
            3 => ShaderStage.Geometry,
            4 => ShaderStage.Hull,
            5 => ShaderStage.Compute,
            6 => ShaderStage.Domain,
            _ => null
        };
    }
}
