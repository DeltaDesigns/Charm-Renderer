using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using Tiger;
using Buffer = SharpDX.Direct3D11.Buffer;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Vector4 = System.Numerics.Vector4;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    public static bool IsBasic => !Strategy.IsLatest();

    public static readonly Vector4 BasicMeshColor = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly RawColor4 BasicClearColor = new(0.01f, 0.01f, 0.01f, 1f);

    private VertexShader _basicVS;
    private PixelShader _basicPS;
    private InputLayout _basicLayout;
    private Buffer _basicViewCB;
    private Buffer _basicObjectCB;
    private RasterizerState _basicRS;
    private DepthStencilState _basicDSS;

    private RenderTarget2D FinalColor => IsBasic || Viewport.FXAA ? GBuffers.FXAA : GBuffers.PostProcessResult;

    private void CreateBasicResources()
    {
        if (_basicVS != null)
            return;

        var vs = SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/basic_mesh.hlsl", "VSMain", "vs_5_0");
        _basicVS = new VertexShader(Device, vs)
        {
            DebugName = "Basic Mesh VS"
        };
        _basicPS = new PixelShader(Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/basic_mesh.hlsl", "PSMain", "ps_5_0")) { DebugName = "Basic Mesh PS" };
        _basicLayout = new InputLayout(Device, vs.Bytecode, new[]
        {
            new InputElement("POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
            new InputElement("NORMAL", 0, Format.R32G32B32A32_Float, 16, 0),
        });

        _basicViewCB = new Buffer(Device, Utilities.SizeOf<BasicViewConstants>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0)
        {
            DebugName = "Basic View Buffer"
        };
        _basicObjectCB = new Buffer(Device, Utilities.SizeOf<BasicObjectConstants>(), ResourceUsage.Default, BindFlags.ConstantBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, 0)
        {
            DebugName = "Basic Object Buffer"
        };

        _basicRS = new RasterizerState(Device, new RasterizerStateDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            IsDepthClipEnabled = true,
        });

        _basicDSS = new DepthStencilState(Device, new DepthStencilStateDescription
        {
            IsDepthEnabled = true,
            DepthWriteMask = DepthWriteMask.All,
            DepthComparison = Comparison.GreaterEqual,
            IsStencilEnabled = false,
        });
    }

    private void DisposeBasicResources()
    {
        Utilities.Dispose(ref _basicVS);
        Utilities.Dispose(ref _basicPS);
        Utilities.Dispose(ref _basicLayout);
        Utilities.Dispose(ref _basicViewCB);
        Utilities.Dispose(ref _basicObjectCB);
        Utilities.Dispose(ref _basicRS);
        Utilities.Dispose(ref _basicDSS);
    }

    private void RenderBasicPasses(RenderWorld world)
    {
        RenderHelpers.Profile("Render Basic");
        Annotation.BeginEvent("Basic Pass");

        PrepareRenderObjects(world);

        var target = FinalColor;
        Context.Rasterizer.SetViewport(target.GetViewport());
        Context.ClearRenderTargetView(target.RTV, BasicClearColor);
        Context.ClearDepthStencilView(GBuffers.Depth.DSV, DepthStencilClearFlags.Depth | DepthStencilClearFlags.Stencil, 0f, 0);
        Context.OutputMerger.SetTargets(GBuffers.Depth.DSV, target.RTV);

        UpdateBasicView(target.Width, target.Height);
        Context.VertexShader.SetConstantBuffer(12, _basicViewCB);
        Context.PixelShader.SetConstantBuffer(12, _basicViewCB);

        Context.Rasterizer.State = _basicRS;
        Context.OutputMerger.DepthStencilState = _basicDSS;
        Context.OutputMerger.BlendState = null;
        Context.InputAssembler.InputLayout = _basicLayout;
        Context.VertexShader.Set(_basicVS);
        Context.PixelShader.Set(_basicPS);
        Context.VertexShader.SetConstantBuffer(1, _basicObjectCB);
        Context.PixelShader.SetConstantBuffer(1, _basicObjectCB);

        foreach (var renderable in _renderObjectsSnapshot.AsSpan(0, _renderObjectsCount))
            renderable?.BindBasic(this);

        foreach (var renderable in _renderPersistentObjectsSnapshot.AsSpan(0, _renderPersistentObjectsCount))
            renderable?.BindBasic(this);

        Context.VertexShader.SetShaderResource(MeshPartData.SkinWeightsSlot, null);
        Context.VertexShader.SetShaderResource(MeshPartData.SkinBonesSlot, null);

        CMD.States.CurrentState = StateSelection.FromRaw(0);

        Annotation.EndEvent();

        if (RenderType == RenderType.Full)
        {
            if (Viewport.ShowGrid)
            {
                Context.OutputMerger.SetTargets(GBuffers.Depth.DSV, target.RTV);
                RenderGrid();
            }

            if (Viewport.ShowSkele || Viewport.ShowBB)
            {
                Context.OutputMerger.SetTargets(target.RTV);
                if (Viewport.ShowSkele)
                    RenderSkeleton();

                if (Viewport.ShowBB)
                {
                    RenderBoundingBoxes();
                    if (World.OverrideMainBB is not null)
                        RenderBoundingBox(World.OverrideMainBB.Value, new(1, 0, 0, 1));
                }
            }
        }

        RenderHelpers.EndProfile();
    }

    private void UpdateBasicView(int width, int height)
    {
        var view = Externs.View;
        var constants = new BasicViewConstants
        {
            WorldToProjective = view.WorldToProj,
            CameraToWorld = view.CameraToWorld,
            Target = new Vector4(width, height, 1f / width, 1f / height),
            ViewMisc = view.ViewMisc,
            CameraToProjective = view.CameraToProj,
        };
        Context.UpdateSubresource(ref constants, _basicViewCB);
    }

    public void UpdateBasicObject(Matrix4x4 meshToWorld, Vector4 color, bool skinning, bool hasNormals)
    {
        var constants = new BasicObjectConstants
        {
            MeshToWorld = meshToWorld,
            Color = color,
            Skinning = skinning ? 1u : 0u,
            HasNormals = hasNormals ? 1u : 0u,
        };
        Context.UpdateSubresource(ref constants, _basicObjectCB);
    }
}
