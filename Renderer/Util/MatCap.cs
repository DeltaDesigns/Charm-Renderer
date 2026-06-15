using System.Numerics;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using static Charm.Renderer.Externs;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace Charm.Renderer;

public class MatCap : GpuResource
{
    public VertexShader VertexShader { get; set; }
    public PixelShader PixelShader { get; set; }
    public Constants Constants { get; set; }
    public ShaderResourceView MatCapDiffuse { get; set; }
    public ShaderResourceView MatCapSpecular { get; set; }

    public MatCap(DeviceContext context)
    {
        VertexShader ??= new VertexShader(context.Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/matcap.hlsl", "VSMain", "vs_5_0"));
        PixelShader ??= new PixelShader(context.Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/matcap.hlsl", "PSMain", "ps_5_0"));

        MatCapDiffuse ??= HelixToolkit.SharpDX.Utilities.TextureLoader.FromFileAsShaderResourceView(context.Device, "renderer assets/textures/matcap_new.png", true);
        MatCapDiffuse.DebugName = "MatCap Diffuse";

        MatCapSpecular ??= HelixToolkit.SharpDX.Utilities.TextureLoader.FromFileAsShaderResourceView(context.Device, "renderer assets/textures/matcap_specular_new.png", true);
        MatCapSpecular.DebugName = "MatCap Specular";

        Constants ??= new Constants("Constants MatCap")
        {
            Buffer = new Buffer(context.Device, new BufferDescription
            {
                SizeInBytes = Utilities.SizeOf<System.Numerics.Matrix4x4>(),
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                CpuAccessFlags = CpuAccessFlags.Write,
                OptionFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            }),
        };
        Constants.Buffer.DebugName = "Matcap Constants Buffer";

        var def = Matrix4x4.Identity;
        context.UpdateSubresource(ref def, Constants.Buffer);
    }

    public void Draw(DeviceContext context, Externs externs)
    {
        UpdateCB(context, externs.View);
        context.VertexShader.Set(VertexShader);

        context.PixelShader.Set(PixelShader);
        context.PixelShader.SetConstantBuffer(0, Constants.Buffer);

        context.PixelShader.SetShaderResources(0, new ShaderResourceView[]
        {
            externs.Deferred.DeferredRT1,
            MatCapDiffuse,
            MatCapSpecular
        });

        context.OutputMerger.SetDepthStencilState(null);
        context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

        context.Draw(3, 0);
    }

    private void UpdateCB(DeviceContext context, ExternView view)
    {
        DataStream stream;
        context.MapSubresource(
            Constants.Buffer,
            0,
            MapMode.WriteDiscard,
            MapFlags.None,
            out stream
        );

        stream.Write(view.WorldToCamera);

        context.UnmapSubresource(Constants.Buffer, 0);
    }

    public override void Dispose()
    {
        VertexShader?.Dispose();
        VertexShader = null;
        PixelShader?.Dispose();
        PixelShader = null;
        Constants?.Dispose();
        Constants = null;
        MatCapDiffuse?.Dispose();
        MatCapDiffuse = null;
        MatCapSpecular?.Dispose();
        MatCapSpecular = null;

        base.Dispose();
    }
}
