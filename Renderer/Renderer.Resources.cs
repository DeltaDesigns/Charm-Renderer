using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using Vector3 = System.Numerics.Vector3;

namespace Charm.Renderer;

public partial class CharmRenderer
{
	public AssetManager AssetManager { get; private set; }

	public GBuffer GBuffers;
	private RenderTarget2D _rtFinal;

	private WpfRenderTarget wpfRT;

	private VertexShader _blitVS;
	private PixelShader _blitPS;
	private PixelShader _blitPS_Linear;

	private VertexShader _clearAOVS;
	private PixelShader _clearAOPS;

	private VertexShader _luminanceVS;
	private PixelShader _luminancePS;

	private VertexShader _fullHemiSkyTempVS;
	private PixelShader _fullHemiSkyTempPS;

	private VertexShader _debugLinesVS;
	private PixelShader _debugLinesPS;
	private InputLayout _debugLinesLayout;
	public Buffer _debugPSCB;
	private RasterizerState _wireframeRS;

	private Buffer _debugShapeVB;
	private Buffer _debugShapeIB;

	public Buffer _bboxVB;

	private SamplerState _pointSampler;

	public ObjectChannels EntityObjectChannels { get; set; }

	private void LookAtMeshInitial()
	{
		var bbox = World.RenderObjects.FirstOrDefault()?.BoundingBox ?? new HelixToolkit.Maths.BoundingBox();
		var center = (bbox.Minimum + bbox.Maximum) / 2f;
		var size = bbox.Maximum - bbox.Minimum;
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
		wpfRT = new WpfRenderTarget(Device, imageWidth, imageHeight, _rtFinal, Viewport);

		Context.Rasterizer.SetViewport(0, 0, imageWidth, imageHeight, 0.0f, 1f);
	}

	private void InitializeRenderTargets(int width, int height)
	{
		GBuffers?.Dispose();
		GBuffers = new GBuffer(Device, width, height);

		_rtFinal?.Dispose();
		_rtFinal = new RenderTarget2D(Device, width, height, Format.B8G8R8A8_UNorm_SRgb, resourceOptionFlags: ResourceOptionFlags.Shared, debugName: "RT Final");
	}

	private int gridSize = 10;
	private int gridSpacing = 2;
	public void RenderGrid()
	{
		RenderHelpers.Profile("Render Grid");
		Annotation.BeginEvent("Draw Grid");
		CMD.States.CreateStates(Context, new(8, 15, 2, 1));

		int numLines = (int)(gridSize * 2 / gridSpacing) + 1;
		int vertexCount = numLines * 2 * 2; // horizontal + vertical, 2 vertices per line

		Context.InputAssembler.PrimitiveTopology = PrimitiveTopology.LineList;

		Context.VertexShader.Set(_gridShaderVS);
		Context.PixelShader.Set(_gridShaderPS);

		Context.Draw(vertexCount, 0);
		Annotation.EndEvent();
		RenderHelpers.EndProfile();
	}

	private VertexShader _gridShaderVS;
	private PixelShader _gridShaderPS;
	public void CreateGrid()
	{
		_gridShaderVS ??= new VertexShader(Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/grid.hlsl", "VSMain", "vs_5_0"));
		_gridShaderPS ??= new PixelShader(Device, SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile("renderer assets/shaders/grid.hlsl", "PSMain", "ps_5_0"));
	}
}