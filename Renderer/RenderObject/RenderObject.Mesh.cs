using System.Runtime.InteropServices;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Buffer = SharpDX.Direct3D11.Buffer;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

// One vertex of the animation skinning buffer, matches SkinVertex in the entity/investment override shaders
[StructLayout(LayoutKind.Sequential)]
public struct SkinVertex
{
    public uint Index0, Index1, Index2, Index3;
    public float Weight0, Weight1, Weight2, Weight3;
}

public class MeshPartData : GpuResource
{
    // Vertex shader SRV slots the override shaders read the skinning data from
    public const int SkinWeightsSlot = 20;
    public const int SkinBonesSlot = 21;

    public MeshPartData()
    {
    }

    public IndexBuffer IndexBuffer;
    public VertexBuffer VertexBuffer0;
    public VertexBuffer? VertexBuffer1;
    public VertexBuffer? VertexColorBuffer;
    public VertexBuffer? VertexSkinningBuffer;
    public VertexBuffer? VertexOldWeightsBuffer;

    // Per-vertex bone indices and weights for animation, indexed by the raw vertex index. Only present for entity
    // parts whose weights could be decoded.
    private Buffer? _skinWeightsBuffer;
    private ShaderResourceView? _skinWeightsSRV;

    // The owning RenderObject's bone rows. Null while it is not animating, which makes the shader skip skinning.
    public ShaderResourceView? BoneRowsSRV;

    public bool HasSkinWeights => _skinWeightsSRV != null;
    public int IndexCount;
    public int IndexOffset;

    public TfxRenderStage RenderStage;
    public PrimitiveTopology Topology;
    public MaterialData Material;
    public InputLayout InputLayout;

    public Vector4 MeshScale;
    public Vector4 MeshTransform;
    public Vector4 MeshUVTransform;
    public int MaxColorIndex;
    public int GroupIndex;
    public int VariantMaterialIndex;
    public int PermutationMaterialIndex = -1;

    public void Draw(CharmRenderer renderer)
    {
        Bind(renderer);
        renderer.Context.DrawIndexed(IndexCount, IndexOffset, 0);
    }

    public void DrawInstanced(CharmRenderer renderer, int instanceCount)
    {
        Bind(renderer);
        renderer.Context.DrawIndexedInstanced(IndexCount, instanceCount, IndexOffset, 0, 0);
    }

    private void Bind(CharmRenderer renderer)
    {
        renderer.Context.InputAssembler.InputLayout = InputLayout;
        renderer.Context.InputAssembler.PrimitiveTopology = Topology;

        IndexBuffer.Bind(renderer.Context);
        VertexBuffer0?.Bind(renderer.Context, 0);
        VertexBuffer1?.Bind(renderer.Context, 1);
        VertexColorBuffer?.Bind(renderer.Context, 2);
        if (Material.Skinned)
            VertexSkinningBuffer?.Bind(renderer.Context, -1, 1);

        Material?.Bind(renderer);

        // after the material, as its own bind can overwrite vertex shader resources
        if (Material.Skinned)
        {
            bool animating = BoneRowsSRV != null && _skinWeightsSRV != null;
            renderer.Context.VertexShader.SetShaderResource(SkinWeightsSlot, animating ? _skinWeightsSRV : null);
            renderer.Context.VertexShader.SetShaderResource(SkinBonesSlot, animating ? BoneRowsSRV : null);
        }
    }

    /// <summary>
    /// Builds the animation weights buffer from the CPU decoded weights. The weights are stored per unique vertex,
    /// while the GPU draws with raw vertex indices, so they are expanded through the part's index list.
    /// </summary>
    public void CreateSkinWeights(Device device, DynamicMeshPart part, int vertexCount)
    {
        if (part.VertexWeights.Count == 0 || part.VertexWeights.Count != part.VertexIndices.Count || vertexCount <= 0)
            return;

        var vertices = new SkinVertex[vertexCount];
        for (int i = 0; i < part.VertexIndices.Count; i++)
        {
            uint raw = part.VertexIndices[i];
            if (raw >= vertexCount)
                continue;

            VertexWeight vw = part.VertexWeights[i];
            float sum = vw.WeightValues.X + vw.WeightValues.Y + vw.WeightValues.Z + vw.WeightValues.W;
            if (sum <= 0)
                continue;

            vertices[raw] = new SkinVertex
            {
                Index0 = (uint)vw.WeightIndices.X,
                Index1 = (uint)vw.WeightIndices.Y,
                Index2 = (uint)vw.WeightIndices.Z,
                Index3 = (uint)vw.WeightIndices.W,
                Weight0 = vw.WeightValues.X / sum,
                Weight1 = vw.WeightValues.Y / sum,
                Weight2 = vw.WeightValues.Z / sum,
                Weight3 = vw.WeightValues.W / sum,
            };
        }

        int stride = Marshal.SizeOf<SkinVertex>();
        _skinWeightsBuffer = Buffer.Create(device, BindFlags.ShaderResource, vertices,
            stride * vertices.Length, ResourceUsage.Immutable, CpuAccessFlags.None,
            ResourceOptionFlags.BufferStructured, stride);
        _skinWeightsSRV = new ShaderResourceView(device, _skinWeightsBuffer);
    }

    public override void Dispose()
    {
        IndexBuffer?.Dispose();
        VertexBuffer0?.Dispose();
        VertexBuffer1?.Dispose();
        VertexColorBuffer?.Dispose();
        VertexSkinningBuffer?.Dispose();
        VertexOldWeightsBuffer?.Dispose();
        InputLayout?.Dispose();
        _skinWeightsSRV?.Dispose();
        _skinWeightsBuffer?.Dispose();
        _skinWeightsSRV = null;
        _skinWeightsBuffer = null;
        BoneRowsSRV = null;

        AssetManager.Get().ReleaseMaterial(Material.Hash.Hash32);
        Material = null;

        IndexBuffer = null;
        VertexBuffer0 = null;
        VertexBuffer1 = null;
        VertexColorBuffer = null;
        VertexSkinningBuffer = null;
        VertexOldWeightsBuffer = null;
        InputLayout = null;

        base.Dispose();
    }
}

// eh, dont like this
public enum MeshType
{
    Normal,
    Physics
}
