using System.Diagnostics;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Vector3 = System.Numerics.Vector3;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    public AssetManager AssetManager { get; private set; }

    public GBuffer GBuffers;
    private RenderTarget2D _rtFinal;
    private RenderTarget2D _rtFinal_Clone;

    private WpfRenderTarget wpfRT;

    private Stopwatch _clock;
    public float Time;
    public float DeltaTime;

    private VertexShader _blitVS;
    private PixelShader _blitPS;

    private VertexShader _clearAOVS;
    private PixelShader _clearAOPS;

    private VertexShader _fullHemiSkyTempVS;
    private PixelShader _fullHemiSkyTempPS;

    private SamplerState _pointSampler;

    private DateTime _lastRender = DateTime.MinValue;
    private readonly double _fps = 1000.0 / 240f; // 90 FPS
    private DateTime _lastFrameTime = DateTime.Now;

    public enum MeshType
    {
        Static,
        Entity,
        Investment
    }

    public ObjectChannels EntityObjectChannels { get; set; }

    private void LookAtMeshInitial()
    {
        var bbox = World.RenderObjects.First().BoundingBox; // TODO
        var center = (bbox.Min + bbox.Max) / 2f;
        var size = bbox.Max - bbox.Min;
        var radius = size.Length() / 2f;
        Camera.Position = new Vector3(center.X, center.Y - radius * 1.75f, center.Z + radius * 0.75f);
        Camera.LookAt(new Vector3(center.X, center.Y, center.Z));
        Camera.RotateAround(new Vector3(center.X, center.Y, center.Z), 90f, 0f);

        Camera.Yaw -= 30f;
        Camera.Position = new Vector3(Camera.Position.X, Camera.Position.Y - radius, Camera.Position.Z);
        Camera.UpdateVectors();
    }

    private void CreateRenderingResources(int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            throw new Exception($"Width or Height can not be zero! ({imageWidth}x{imageHeight})");

        DisposeRenderingResources();

        CreateDefaults();
        CreateScopes();

        InitializeRenderTargets(imageWidth, imageHeight);

        DisposeWPF();
        wpfRT = new WpfRenderTarget(Device, imageWidth, imageHeight, _rtFinal_Clone, Viewport);

        Context.Rasterizer.SetViewport(0, 0, imageWidth, imageHeight, 0.0f, 1f);
    }

    private void InitializeRenderTargets(int width, int height)
    {
        GBuffers?.Dispose();
        GBuffers = new GBuffer(Device, width, height);

        _rtFinal?.Dispose();
        _rtFinal = new RenderTarget2D(Device, width, height, Format.B8G8R8A8_UNorm_SRgb, resourceOptionFlags: ResourceOptionFlags.Shared, debugName: "RT Final");

        _rtFinal_Clone?.Dispose();
        _rtFinal_Clone = new RenderTarget2D(Device, width, height, Format.B8G8R8A8_UNorm_SRgb, resourceOptionFlags: ResourceOptionFlags.Shared, debugName: "RT Final Clone");
    }

    private SharpDX.Direct3D11.Texture2D CreateRT(int width, int height, SharpDX.DXGI.Format format, string debugName = "")
    {
        var rtTexture = new SharpDX.Direct3D11.Texture2D(Device, new SharpDX.Direct3D11.Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
            Usage = SharpDX.Direct3D11.ResourceUsage.Default,
            BindFlags = SharpDX.Direct3D11.BindFlags.RenderTarget | SharpDX.Direct3D11.BindFlags.ShaderResource,
            CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.None,
            OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None
        });
        if (debugName != string.Empty)
            rtTexture.DebugName = debugName;

        return rtTexture;
    }


    private int gridSize = 10;
    private int gridSpacing = 2;
    public void RenderGrid()
    {
        CreateStates(new(8, 15, 2, 1));

        int numLines = (int)(gridSize * 2 / gridSpacing) + 1;
        int vertexCount = numLines * 2 * 2; // horizontal + vertical, 2 vertices per line

        Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;

        Context.VertexShader.Set(_gridShaderVS);
        Context.PixelShader.Set(_gridShaderPS);

        Context.Draw(vertexCount, 0);
    }

    private VertexShader _gridShaderVS;
    private PixelShader _gridShaderPS;
    public void CreateGrid()
    {
        _gridShaderVS ??= new VertexShader(Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders/grid.hlsl", "VSMain", "vs_5_0"));
        _gridShaderPS ??= new PixelShader(Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("shaders/grid.hlsl", "PSMain", "ps_5_0"));
    }
}

public enum RendererState
{
    None,               // Constructed, nothing allocated
    DeviceReady,        // GPU device exists
    ResourcesCreated,   // Meshes, textures, buffers created
    AttachedToWpf,      // D3DImage / Image.Source set
    Running,            // Render loop active
    Stopping,           // Render loop stopping
    DetachedFromWpf,    // WPF no longer references backbuffer
    Disposed            // All GPU resources released
}
