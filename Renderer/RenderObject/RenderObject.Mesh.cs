using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Tiger;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public class MeshPartData : GpuResource
{
    public MeshPartData()
    {
    }

    public IndexBuffer IndexBuffer;
    public VertexBuffer VertexBuffer0;
    public VertexBuffer? VertexBuffer1;
    public VertexBuffer? VertexColorBuffer;
    public VertexBuffer? VertexSkinningBuffer;
    public VertexBuffer? VertexOldWeightsBuffer;
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
