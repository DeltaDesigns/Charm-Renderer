using System.Numerics;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace Charm.Renderer;

public class VertexBuffer : GpuResource
{
    public Buffer Buffer;
    public VertexBufferBinding Binding;
    public int Size;
    public int Length;
    public int Stride;
    public ShaderResourceView SRV;

    public static VertexBuffer Create(DeviceContext context, Tiger.Schema.VertexBuffer buffer, ResourceOptionFlags optionFlags = ResourceOptionFlags.None)
    {
        int stride = buffer.TagData.Stride;
        int type = buffer.TagData.Type;
        byte[] vertexBufferData = buffer.GetReferenceData();

        BindFlags bindFlags = BindFlags.VertexBuffer;
        // TODO, only use as SRV for actual srv buffers (buffers with texcoords and such usually have a stride of 4)
        if (stride == 1 || stride == 4 || stride == 8)
            bindFlags |= BindFlags.ShaderResource;

        var vertexBuffer = Buffer.Create(
            context.Device,
            vertexBufferData,
            new BufferDescription
            {
                SizeInBytes = vertexBufferData.Length,
                Usage = ResourceUsage.Default,
                BindFlags = bindFlags,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = optionFlags,
                StructureByteStride = 0
            }
        );
        vertexBuffer.DebugName = $"VertexBuffer {buffer.Hash}";

        ShaderResourceView srv = null;
        if (stride == 1 || stride == 4 || stride == 8)
        {
            Format format = Format.R8G8B8A8_UNorm;
            if (stride == 1)
                format = type == 6 ? Format.R8_UInt : Format.R8_UNorm;
            else if (stride == 4)
                format = type == 6 ? Format.R8G8B8A8_UInt : Format.R8G8B8A8_UNorm;
            else if (stride == 8)
                format = type == 1 ? Format.R16G16B16A16_UInt : Format.R16G16B16A16_UNorm;

            srv = new ShaderResourceView(context.Device, vertexBuffer,
            new ShaderResourceViewDescription
            {
                Format = format,

                Dimension = ShaderResourceViewDimension.Buffer,

                Buffer = new ShaderResourceViewDescription.BufferResource
                {
                    ElementOffset = 0,
                    ElementWidth = vertexBufferData.Length / stride
                }
            });
            srv.DebugName = $"VertexBuffer {buffer.Hash} SRV";
        }

        return new VertexBuffer
        {
            Buffer = vertexBuffer,
            Length = vertexBufferData.Length / stride,
            Stride = stride,
            Size = vertexBufferData.Length,
            SRV = srv,
            Binding = new VertexBufferBinding(vertexBuffer, stride, 0)
        };
    }

    public void Bind(DeviceContext context, int slot, int srvSlot = 0)
    {
        if (slot != -1)
            context.InputAssembler.SetVertexBuffers(slot, Binding);

        if (SRV is not null)
            context.VertexShader.SetShaderResource(srvSlot, SRV);
    }

    public override void Dispose()
    {
        Buffer?.Dispose();
        Buffer = null;
        SRV?.Dispose();
        SRV = null;
        base.Dispose();
    }
}

public struct Vertex
{
    public Vector3 Position;
    public Vector3 Color;
}

