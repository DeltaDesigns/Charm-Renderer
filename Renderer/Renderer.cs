using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DirectInput;
using Tiger;
using Tiger.Schema;
using Device = SharpDX.Direct3D11.Device;


// Please do not look at this. It is an absolute mess and unoptimized and ugly and im ashamed yet proud at the same time.
// This in its current state *could* handle maps but with very low FPS.
// Massive credits to Cohae cus everything learned making this came from Alkahest.

// Realistically, I shouldn't bother with actual map rendering as thats Alkahest's job and it does it far better than I ever could.
// Charm's renderer should just focus on rendering individual assets, maybe with some more in-depth features for that, idk.

#if DEBUG
using Evergine.Bindings.RenderDoc;
#endif

namespace Charm.Renderer;

public partial class CharmRenderer : IDisposable
{
	public static CharmRenderer Instance { get; set; } // TODO? Stop doing this. Things that use this should live seperately?

	private int _width;
	private int _height;

	public FirstPersonCamera Camera;
	public TempScopes TempScopes;
	public Externs Externs;
	public MatCap MatCapRenderer;

	public RenderWorld World = new();

	public RendererViewport Viewport;
	public GPU _GPU { get; set; }

	public Device Device => _GPU?.Device;
	public DeviceContext Context => _GPU?.Context;

	private volatile bool _isRunning = false;
	private volatile bool _paused = false;
	private Thread _renderThread;
	private ManualResetEvent _mrse = new ManualResetEvent(true);
	private AutoResetEvent _frameCompleteEvent = new AutoResetEvent(true);

	public CharmRenderer()
	{
		Keyboard = new(Input);
		Keyboard.Acquire();

		Mouse = new(Input);
		Mouse.Acquire();

		AppDomain.CurrentDomain.ProcessExit += (s, e) =>
		{
			Stop();
			//Dispose();
		};
	}

	public void Initialize(int width, int height)
	{
		if (_isRunning) return;

		if (_GPU == null)
		{
			if (GPU.Instance is null)
				_GPU = new();
			else
				_GPU = GPU.Instance;

			Load(width, height);
		}
		MatCapRenderer ??= new MatCap(Context);
	}

	private void Load(int width, int height)
	{
		Instance = this;

		_width = width;
		_height = height;

		CreateRenderingResources(width, height);
		TempScopes?.Dispose();
		TempScopes = new();
		Externs = new();

		World.CreateWorld(this, FileResourcer.Get().GetSchemaTag<SBubbleParent>(new(0x81141179)));

		Camera = new(); // Should be last

		Application.Current.Dispatcher.BeginInvoke(() =>
		{
			var width = (int)Viewport.ActualWidth;
			var height = (int)Viewport.ActualHeight;

			Camera.Viewport = new(width, height);
			Camera.ResetCameraTransform();

		}, DispatcherPriority.Send);
	}


	public void Start()
	{
		_isRunning = true;
		_renderThread = new Thread(RenderLoop)
		{
			IsBackground = true
		};
		_renderThread.Start();
	}

	public void Stop()
	{
		if (_isRunning)
		{
			_isRunning = false;
			_renderThread?.Join();
		}
	}

	public void Resume()
	{
		_mrse.Set();
		_paused = false;
		//Log.Debug("Render thread resumed.");
	}

	public void Pause()
	{
		_frameCompleteEvent.WaitOne();
		_mrse.Reset();
		_paused = true;
		//Log.Debug("Render thread paused.");
	}

	public float Time { get; private set; }
	public float DeltaTime { get; private set; }
	public float FPS { get; private set; } = 0;

	private const float MaxDeltaTime = 0.1f;
	private float TargetFPS = 200f;
	private void RenderLoop()
	{
#if DEBUG
		TracyWrapper.Profiler.InitThread("Render Thread");
#endif

		var stopwatch = Stopwatch.StartNew();
		double lastTime = stopwatch.Elapsed.TotalSeconds;

		double fpsTimer = 0.0;
		int fpsFrames = 0;

		while (_isRunning)
		{
			_mrse.WaitOne();
			if (_paused) // this kinda fucking sucks
			{
				Thread.Sleep(100);
				continue;
			}

			TargetFPS = IsAppFocused() ? 200f : 30f;
			double targetFrameTime = Viewport.CapFPS ? (1.0 / TargetFPS) : 0.0;

			double now = stopwatch.Elapsed.TotalSeconds;
			double delta = now - lastTime;
			bool shouldCapFPS = Viewport.CapFPS || !IsAppFocused();

			if (shouldCapFPS && delta < targetFrameTime)
			{
				int sleepMs = (int)((targetFrameTime - delta) * 1000.0);
				if (sleepMs > 0)
					Thread.Sleep(sleepMs);

				continue;
			}

			lastTime = now;

			DeltaTime = (float)Math.Min(delta, MaxDeltaTime);
			Time = (float)now;

			_frameCompleteEvent.Reset();
			Render();
			_frameCompleteEvent.Set();

			fpsFrames++;
			fpsTimer += delta;
			if (fpsTimer >= 0.5)
			{
				FPS = fpsFrames / (float)fpsTimer;
				fpsFrames = 0;
				fpsTimer = 0;
			}

#if DEBUG
			TracyWrapper.Profiler.HeartBeat();
#endif
		}
	}

#if DEBUG
	private RenderDoc _renderDoc = null;
	private bool _captured = false;
#endif
	private void Render()
	{
		if (!_isRunning)
			return;

		KeyboardState = Keyboard.GetCurrentState();
		MouseState = Mouse.GetCurrentState();

#if DEBUG
		if (_renderDoc is null)
			RenderDoc.Load(out _renderDoc);

		if (!_captured & KeyboardState.IsPressed(SharpDX.DirectInput.Key.F12))
		{
			Console.WriteLine("Starting Renderdoc Capture");
			_renderDoc.API.StartFrameCapture(Device.NativePointer, IntPtr.Zero);
		}
#endif

		int newWidth = Math.Max(1, (int)Viewport.ActualWidth);
		int newHeight = Math.Max(1, (int)Viewport.ActualHeight);
		Context.Rasterizer.SetViewport(0, 0, newWidth, newHeight, 0.0f, 1f);

		UpdateCamera(World);

		{
			var near = Camera.Near;
			var far = Camera.Far;
			Externs.Deferred.DepthConstants = new(1.0f / far, (far - near) / (far * near), 0, 0);
			Externs.Decal.DepthConstants = Externs.Deferred.DepthConstants;
			Externs.Frame.ExposureScale = Viewport.Exposure;
		}

		Externs.Update(this);
		World.EvaluateGlobalChannels(Externs.Atmosphere);

		TfxScopes[Tiger.TfxScope.VIEW].Bind(Context);
		TfxScopes[Tiger.TfxScope.FRAME].Bind(Context);
		TempScopes.UpdateFrameScope(this);

		RenderAtmosphere();

		// Gotta set back to main viewport dims since this gets used for other non-atmosphere things for some reason
		Externs.Atmosphere.RTDimensions = new(Camera.Viewport.X, Camera.Viewport.Y, 1f / Camera.Viewport.X, 1f / Camera.Viewport.Y);

		// GBuffer Pass
		RenderGBuffer();
		RenderMatCap();
		RenderShading();
		RenderTransparent();
		RenderPostProcess();

		if (DisplayPass > RenderPass.final_color_grade)
			RenderGlobalPipeline(DisplayPass.ToString());

		var blitRT = DisplayPass == RenderPass.final ? GBuffers.Shading : GBuffers.PostProcessResult;
		if (Viewport.ShowGrid)
		{
			Context.OutputMerger.SetTargets(GBuffers.Depth.DSV, blitRT.RTV);
			RenderGrid();
		}

		if (Viewport.ShowSkele)
		{
			Context.OutputMerger.SetTargets(blitRT.RTV);
			RenderSkeleton();
		}

		CreateStates(new(0, 0, 0, 0));
		// Blits to final RT/Correct format for WPF cus it hates everything
		BlitToWPF(blitRT);
		wpfRT.Present(Context, Viewport.RT0);


#if DEBUG
		if (!_captured & KeyboardState.IsPressed(SharpDX.DirectInput.Key.F12))
		{
			_renderDoc.API.EndFrameCapture(Device.NativePointer, IntPtr.Zero);
			_captured = true;
			Console.WriteLine("Renderdoc Capture Complete");
		}

		if (_captured & !KeyboardState.IsPressed(SharpDX.DirectInput.Key.F12))
			_captured = false;
#endif
	}

	private void UpdateCamera(RenderWorld world)
	{
		if (Camera is null)
			return;

		RenderHelpers.Profile("Update Camera");

		Camera.UpdateProjectionMatrix(Viewport.FOV);
		if (IsAppFocused() && Viewport.ViewportContainer.IsMouseOver)
			Camera.Update(world, KeyboardState, MouseState);

		RenderHelpers.EndProfile();
	}

	public void OnSizeChanged()
	{
		int newWidth = Math.Max(1, (int)Viewport.ActualWidth);
		int newHeight = Math.Max(1, (int)Viewport.ActualHeight);
		if (newWidth != _width || newHeight != _height)
		{
			Stop();

			_width = newWidth;
			_height = newHeight;

			Context?.Flush();
			//Context?.ClearState();
			if (Camera is not null)
				Camera.Viewport = new(_width, _height);

			InitializeRenderTargets(newWidth, newHeight);

			DisposeWPF();
			wpfRT = new WpfRenderTarget(Device, newWidth, newHeight, _rtFinal, Viewport);

			Context.Rasterizer.SetViewport(0, 0, newWidth, newHeight, 0.0f, 1f);

			Start();
		}
	}

	public void DisposeControl()
	{
		DisposeWPF();
		MatCapRenderer?.Dispose();
		MatCapRenderer = null;

#if DEBUG
		_renderDoc = null;
#endif

		DisposeAllMesh();

		AssetManager?.Dispose();
		AssetManager = null;
	}

	public void DisposeAllMesh()
	{
		World?.DisposeAll();
		//AssetManager?.DisposeTextures();
	}

	public void DisposeRenderingResources()
	{
		Utilities.Dispose(ref GBuffers);
		Utilities.Dispose(ref _rtFinal);
		Utilities.Dispose(ref _blitVS);
		Utilities.Dispose(ref _blitPS);
		Utilities.Dispose(ref _gridShaderVS);
		Utilities.Dispose(ref _gridShaderPS);
		Utilities.Dispose(ref _pointSampler);
		Utilities.Dispose(ref _clearAOVS);
		Utilities.Dispose(ref _clearAOPS);
		Utilities.Dispose(ref _fullHemiSkyTempVS);
		Utilities.Dispose(ref _fullHemiSkyTempPS);
		Utilities.Dispose(ref Annotation);
	}


	public void Dispose()
	{
		Stop();

		DisposeControl();
		DisposeRenderingResources();
		DisposeStates();

		foreach (var pipeline in _pipelineCache.Values)
		{
			pipeline?.Dispose();
		}
		_pipelineCache.Clear();

		Externs?.Dispose();
		TempScopes?.Dispose();

		foreach (var scope in TfxScopes.Values)
		{
			scope.Dispose();
		}
		TfxScopes?.Clear();

		Input.Dispose();
		Keyboard.Dispose();
		Mouse.Dispose();

		_GPU?.Dispose();

		Instance = null;
	}

	public void DisposeWPF()
	{
		wpfRT?.Dispose();
		wpfRT = null;
	}
}
