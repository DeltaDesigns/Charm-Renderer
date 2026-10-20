using System.Runtime.InteropServices;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using Buffer = SharpDX.Direct3D11.Buffer;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public class MeshPartData : GpuResource
{
    // for override vs
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


    private Buffer? _skinWeightsBuffer;
    private ShaderResourceView? _skinWeightsSRV;
    public ShaderResourceView? BoneRowsSRV;
    public bool HasSkinWeights => _skinWeightsSRV != null;
    public bool UsesGameSkinning = false;

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
    public Tiger.Schema.Shaders.Material? VariantMaterial;

    private Buffer? _basicVertexBuffer;
    private Buffer? _basicIndexBuffer;
    public bool BasicHasNormals;

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

        UpdateGameSkinning(renderer);

        Material?.Bind(renderer);

        if (Material.Skinned)
        {
            bool animating = BoneRowsSRV != null && _skinWeightsSRV != null;
            renderer.Context.VertexShader.SetShaderResource(SkinWeightsSlot, animating ? _skinWeightsSRV : null);
            renderer.Context.VertexShader.SetShaderResource(SkinBonesSlot, animating ? BoneRowsSRV : null);
        }
    }

    public static MeshPartData? CreateBasic(Device device, MeshPart part)
    {
        int vertexCount = part.VertexIndices.Count;
        if (vertexCount == 0 || part.Indices.Count == 0 || part.VertexPositions.Count != vertexCount)
            return null;

        bool hasNormals = part.VertexNormals.Count == vertexCount;
        var vertices = new BasicVertex[vertexCount];
        var remap = new Dictionary<uint, uint>(vertexCount);
        for (int i = 0; i < vertexCount; i++)
        {
            remap[part.VertexIndices[i]] = (uint)i;

            var p = part.VertexPositions[i];
            vertices[i].Position = new Vector4(p.X, p.Y, p.Z, 1f);
            if (hasNormals)
            {
                var n = part.VertexNormals[i];
                vertices[i].Normal = new Vector4(n.X, n.Y, n.Z, 0f);
            }
        }

        var indices = new uint[part.Indices.Count * 3];
        for (int i = 0; i < part.Indices.Count; i++)
        {
            var tri = part.Indices[i];
            indices[i * 3 + 0] = remap[tri.X];
            indices[i * 3 + 1] = remap[tri.Y];
            indices[i * 3 + 2] = remap[tri.Z];
        }

        var mesh = new MeshPartData
        {
            RenderStage = part.RenderStage,
            IndexCount = indices.Length,
            IndexOffset = 0,
            Topology = PrimitiveTopology.TriangleList,
            GroupIndex = part.GroupIndex,
            VariantMaterialIndex = -1,
            BasicHasNormals = hasNormals,
            _basicVertexBuffer = Buffer.Create(device, BindFlags.VertexBuffer, vertices),
            _basicIndexBuffer = Buffer.Create(device, BindFlags.IndexBuffer, indices),
        };

        if (part is DynamicMeshPart dynamicPart && dynamicPart.VertexWeights.Count == vertexCount)
            mesh.CreateSkinWeights(device, dynamicPart.VertexWeights.Select(ToSkinVertex).ToArray());

        return mesh;
    }

    public void DrawBasic(CharmRenderer renderer, bool skinning)
    {
        var context = renderer.Context;
        context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_basicVertexBuffer, Marshal.SizeOf<BasicVertex>(), 0));
        context.InputAssembler.SetIndexBuffer(_basicIndexBuffer, SharpDX.DXGI.Format.R32_UInt, 0);

        context.VertexShader.SetShaderResource(SkinWeightsSlot, skinning ? _skinWeightsSRV : null);
        context.VertexShader.SetShaderResource(SkinBonesSlot, skinning ? BoneRowsSRV : null);

        context.DrawIndexed(IndexCount, 0, 0);
    }

    public void UpdateGameSkinning(CharmRenderer renderer)
    {
        if (Material == null)
            return;

        UsesGameSkinning = renderer.Viewport.UseGameSkinning && Material.Skinned && VertexSkinningBuffer != null;
        Material.GameSkinningForPart = UsesGameSkinning;
    }

    public static bool FillSkinWeights(SkinVertex[] vertices, DynamicMeshPart part)
    {
        if (part.VertexWeights.Count == 0 || part.VertexWeights.Count != part.VertexIndices.Count || vertices.Length == 0)
            return false;

        for (int i = 0; i < part.VertexIndices.Count; i++)
        {
            uint raw = part.VertexIndices[i];
            if (raw >= vertices.Length)
                continue;

            vertices[raw] = ToSkinVertex(part.VertexWeights[i]);
        }

        return true;
    }

    public static bool FillRigidSkinWeights(SkinVertex[] vertices, DynamicMeshPart part, int boneCount)
    {
        if (part.VertexPositions.Count != part.VertexIndices.Count || vertices.Length == 0)
            return false;

        for (int i = 0; i < part.VertexIndices.Count; i++)
        {
            int bone = (int)(part.VertexPositions[i].W * 32767f + 0.1f);
            if (part.VertexIndices[i] < vertices.Length && (bone < 0 || bone >= boneCount))
                return false;
        }

        for (int i = 0; i < part.VertexIndices.Count; i++)
        {
            uint raw = part.VertexIndices[i];
            if (raw >= vertices.Length)
                continue;

            vertices[raw] = new SkinVertex
            {
                Index0 = (uint)(part.VertexPositions[i].W * 32767f + 0.1f),
                Weight0 = 1
            };
        }

        return true;
    }

    public static (Buffer Buffer, ShaderResourceView SRV) CreateSkinWeightsBuffer(Device device, SkinVertex[] vertices)
    {
        int stride = Marshal.SizeOf<SkinVertex>();
        var buffer = Buffer.Create(device, BindFlags.ShaderResource, vertices,
            stride * vertices.Length, ResourceUsage.Immutable, CpuAccessFlags.None,
            ResourceOptionFlags.BufferStructured, stride);
        return (buffer, new ShaderResourceView(device, buffer));
    }

    public void SetSkinWeights(Buffer buffer, ShaderResourceView srv)
    {
        _skinWeightsBuffer = buffer;
        _skinWeightsSRV = srv;
    }

    private static SkinVertex ToSkinVertex(VertexWeight vw)
    {
        float sum = vw.WeightValues.X + vw.WeightValues.Y + vw.WeightValues.Z + vw.WeightValues.W;
        if (sum <= 0)
            return default;

        return new SkinVertex
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

    private void CreateSkinWeights(Device device, SkinVertex[] vertices)
    {
        (_skinWeightsBuffer, _skinWeightsSRV) = CreateSkinWeightsBuffer(device, vertices);
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
        _basicVertexBuffer?.Dispose();
        _basicIndexBuffer?.Dispose();

        if (Material != null)
            AssetManager.Get().ReleaseMaterial(Material.Hash.Hash32);
        Material = null;

        IndexBuffer = null;
        VertexBuffer0 = null;
        VertexBuffer1 = null;
        VertexColorBuffer = null;
        VertexSkinningBuffer = null;
        VertexOldWeightsBuffer = null;
        InputLayout = null;
        BoneRowsSRV = null;
        _skinWeightsSRV = null;
        _skinWeightsBuffer = null;
        _basicVertexBuffer = null;
        _basicIndexBuffer = null;


        base.Dispose();
    }
}

// eh, dont like this
public enum MeshType
{
    Normal,
    Physics
}

public enum SkinningType
{
    None,
    DualQuaternion,   // 2 float4 per bone
    Matrix3x4,        // 3 float4 per bone
    Matrix3x4Padded,  // 4 float4 per bone, the last one unused
}
