using HelixToolkit.SharpDX.Utilities;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using System.Numerics;
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
	public SamplerState LinearSampler { get; set; }

	public MatCap(DeviceContext context)
	{
		if (VertexShader is null)
			VertexShader = new VertexShader(context.Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders/matcap.hlsl", "VSMain", "vs_5_0"));

		if (PixelShader is null)
			PixelShader = new PixelShader(context.Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders/matcap.hlsl", "PSMain", "ps_5_0"));

		if (LinearSampler is null)
			LinearSampler = new SamplerState(context.Device, new SamplerStateDescription
			{
				Filter = Filter.MinMagMipLinear,
				AddressU = TextureAddressMode.Clamp,
				AddressV = TextureAddressMode.Clamp,
				AddressW = TextureAddressMode.Clamp,
			});

		MatCapDiffuse = TextureLoader.FromFileAsShaderResourceView(context.Device, "textures/matcap_new.png", true);
		MatCapDiffuse.DebugName = "MatCap Diffuse";

		MatCapSpecular = TextureLoader.FromFileAsShaderResourceView(context.Device, "textures/matcap_specular_new.png", true);
		MatCapSpecular.DebugName = "MatCap Specular";

		Constants = new Constants("Constants MatCap")
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
		context.PixelShader.SetSampler(0, LinearSampler);

		context.PixelShader.SetShaderResource(0, externs.Deferred.DeferredRT1);
		context.PixelShader.SetShaderResource(1, MatCapDiffuse);
		context.PixelShader.SetShaderResource(2, MatCapSpecular);

		context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
		context.OutputMerger.SetDepthStencilState(null);

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
		LinearSampler?.Dispose();
		LinearSampler = null;

		base.Dispose();
	}
}
