using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace Charm.Renderer;

public class IndexBuffer : GpuResource
{
	public Buffer Buffer;
	public int Length;
	public Format Format;

	public static IndexBuffer Create(DeviceContext context, Tiger.Schema.IndexBuffer buffer)
	{
		var indexBufferData = buffer.GetReferenceData();
		var indexBuffer = Buffer.Create(
			context.Device,
			indexBufferData,
			new BufferDescription
			{
				SizeInBytes = indexBufferData.Length,
				Usage = ResourceUsage.Immutable,
				BindFlags = BindFlags.IndexBuffer,
				CpuAccessFlags = CpuAccessFlags.None,
				OptionFlags = ResourceOptionFlags.None,
				StructureByteStride = 0
			}
		);
		indexBuffer.DebugName = $"IndexBuffer {buffer.Hash}";

		return new IndexBuffer
		{
			Buffer = indexBuffer,
			Length = buffer.TagData.DataSize / (buffer.TagData.Is32Bit ? 4 : 2),
			Format = buffer.TagData.Is32Bit ? Format.R32_UInt : Format.R16_UInt
		};
	}

	public void Bind(DeviceContext context)
	{
		context.InputAssembler.SetIndexBuffer(Buffer, Format, 0);
	}

	public override void Dispose()
	{
		Buffer?.Dispose();
		base.Dispose();
	}
}

