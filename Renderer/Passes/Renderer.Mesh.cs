using HelixToolkit.Geometry;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Tiger;
using Tiger.Schema;
using BoundingBox = HelixToolkit.Maths.BoundingBox;
using Buffer = SharpDX.Direct3D11.Buffer;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    private RenderObject[] _renderObjectsSnapshot = Array.Empty<RenderObject>();
    private RenderObject[] _renderPersistentObjectsSnapshot = Array.Empty<RenderObject>();
    private int _renderObjectsCount;
    private int _renderPersistentObjectsCount;

    private void PrepareRenderObjects()
    {
        lock (World.WorldLock)
        {
            _renderObjectsCount = World.RenderObjects.Count;
            if (_renderObjectsSnapshot.Length < _renderObjectsCount)
                _renderObjectsSnapshot = new RenderObject[_renderObjectsCount];
            World.RenderObjects.CopyTo(_renderObjectsSnapshot, 0);

            _renderPersistentObjectsCount = World.PersistantRenderObjects.Count;
            if (_renderPersistentObjectsSnapshot.Length < _renderPersistentObjectsCount)
                _renderPersistentObjectsSnapshot = new RenderObject[_renderPersistentObjectsCount];
            World.PersistantRenderObjects.CopyTo(_renderPersistentObjectsSnapshot, 0);
        }
    }

    private void RenderMesh(TfxRenderStage renderStage, string passName)
    {
        Annotation.BeginEvent(passName);
        foreach (var renderable in _renderObjectsSnapshot.AsSpan(0, _renderObjectsCount))
        {
            renderable?.Bind(this, renderStage);
        }

        foreach (var renderable in _renderPersistentObjectsSnapshot.AsSpan(0, _renderPersistentObjectsCount))
        {
            renderable?.Bind(this, renderStage);
        }
        Annotation.EndEvent();
    }

    private void RenderMesh(TfxRenderStage renderStage, FeatureRendererSubscription features, string passName)
    {
        Annotation.BeginEvent(passName);
        RenderObject[] renderObjects;
        RenderObject[] persistentObjects;

        lock (World.WorldLock)
        {
            renderObjects = World.RenderObjects.ToArray();
            persistentObjects = World.PersistantRenderObjects.ToArray();
        }

        foreach (var renderable in renderObjects)
        {
            if (!features.IsSubscribed(renderable.Feature))
                continue;

            renderable?.Bind(this, renderStage);
        }

        foreach (var renderable in persistentObjects)
        {
            if (!features.IsSubscribed(renderable.Feature))
                continue;

            var bb = renderable.BoundingBox;
            if (!Camera.Frustum.Intersects(ref bb))
                continue;

            renderable?.Bind(this, renderStage);
        }
        Annotation.EndEvent();
    }

    private void RenderSkeleton()
    {
        RenderHelpers.Profile("Render Skeleton");
        Annotation.BeginEvent("Entity Skeleton");
        CMD.States.SetState(Context, new(8, 15, 2, 1));

        Context.InputAssembler.InputLayout = _debugLinesLayout;
        Context.VertexShader.Set(_debugLinesVS);
        Context.PixelShader.Set(_debugLinesPS);

        foreach (var renderable in World.RenderObjects)
        {
            renderable?.RenderSkeleton(this);
        }
        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }

    public void RenderBoundingBox(BoundingBox bbox, Vector4 color)
    {
        Vector3[] lines = RenderHelpers.GetBoundingBoxLines(bbox);
        if (lines.Length == 0)
            return;

        if (_bboxVB is null)
        {
            _bboxVB = Buffer.Create(
                Device,
                BindFlags.VertexBuffer,
                lines,
                Utilities.SizeOf<Vector3>() * lines.Length,
                ResourceUsage.Dynamic,
                CpuAccessFlags.Write
            );
        }

        DataBox dataBox = Context.MapSubresource(_bboxVB, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None);
        try
        {
            Utilities.Write(dataBox.DataPointer, lines, 0, lines.Length);
        }
        finally
        {
            Context.UnmapSubresource(_bboxVB, 0);
        }

        TempScopes.UpdateRigidModelScopeCustom(Context, new(), new());

        Context.UpdateSubresource(ref color, _debugPSCB);
        Context.PixelShader.SetConstantBuffer(0, _debugPSCB);

        Context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_bboxVB, Utilities.SizeOf<Vector3>(), 0));
        Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;
        Context.Draw(lines.Length, 0);
    }

    private void RenderBoundingBoxes()
    {
        RenderHelpers.Profile("Render Bounding Boxes");
        Annotation.BeginEvent("Render Bounding Boxes");
        CMD.States.SetState(Context, new(8, 15, 2, 1));

        Context.InputAssembler.InputLayout = _debugLinesLayout;
        Context.VertexShader.Set(_debugLinesVS);
        Context.PixelShader.Set(_debugLinesPS);

        foreach (var renderable in World.RenderObjects)
        {
            if ((renderable.IsChild && !Viewport.ShowEntChildren) || !renderable.Visible)
                continue;

            RenderBoundingBox(renderable.BoundingBox, new(1f, 1f, 0f, 1f));
        }

        Annotation.EndEvent();
        RenderHelpers.EndProfile();
    }

    private int _sphereIndexCount;
    public void RenderSphere(
        Transform transform,
        System.Numerics.Vector4 color,
        bool wireframe = false,
        Transform? offset = null)
    {
        if (_debugShapeVB == null || _debugShapeIB == null)
        {
            var meshBuilder = new MeshBuilder();
            meshBuilder.AddSphere(Vector3.Zero, 1, 8, 8);
            var mesh = meshBuilder.ToMesh();

            _sphereIndexCount = mesh.TriangleIndices.Count;

            _debugShapeVB = Buffer.Create(
                Device,
                mesh.Positions.ToArray(),
                new BufferDescription
                {
                    SizeInBytes = Utilities.SizeOf<Vector3>() * mesh.Positions.Count,
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.VertexBuffer,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None,
                    StructureByteStride = 0
                }
            );

            _debugShapeIB = Buffer.Create(
                Device,
                mesh.TriangleIndices.ToArray(),
                new BufferDescription
                {
                    SizeInBytes = Utilities.SizeOf<Vector3>() * mesh.TriangleIndices.Count,
                    Usage = ResourceUsage.Immutable,
                    BindFlags = BindFlags.IndexBuffer,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None,
                    StructureByteStride = 0
                });
        }

        CMD.States.SetState(Context, new(8, 15, 2, 1));
        Context.InputAssembler.InputLayout = _debugLinesLayout;
        Context.VertexShader.Set(_debugLinesVS);
        Context.PixelShader.Set(_debugLinesPS);

        var rotated = Vector3.Transform(
            transform.Position,
            transform.Quaternion.ToQuat() * (offset != null ? offset.Value.Quaternion.ToQuat() : System.Numerics.Quaternion.Identity)
        );

        TempScopes.UpdateRigidModelScopeCustom(Context, new Transform
        {
            Position = rotated,
            Scale = transform.Scale,
            Quaternion = transform.Quaternion,
        }, offset != null ? offset.Value : new Transform());

        Context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_debugShapeVB, Utilities.SizeOf<Vector3>(), 0));
        Context.InputAssembler.SetIndexBuffer(_debugShapeIB, SharpDX.DXGI.Format.R32_UInt, 0);
        Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        if (wireframe)
            Context.Rasterizer.State = _wireframeRS;

        Context.UpdateSubresource(ref color, _debugPSCB);
        Context.PixelShader.SetConstantBuffer(0, _debugPSCB);

        Context.DrawIndexed(_sphereIndexCount, 0, 0);
    }

    public void DrawScreenQuad()
    {
        Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
        Context.Draw(4, 0);
    }
}
