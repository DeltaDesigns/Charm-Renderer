using System.Diagnostics;
using SharpDX.Direct3D11;
using Tiger;

namespace Charm.Renderer;

public interface IShader : IDisposable
{
    void Bind(DeviceContext context);
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
            _ => throw new NotImplementedException()
        };
    }
}
