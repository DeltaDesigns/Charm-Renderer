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
// This in its current state CAN NOT handle maps. This is just a simple singular asset viewer.
// Massive credits to Cohae cus everything learned making this came from Alkahest.

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

	private bool _isRunning = false;
	private readonly AutoResetEvent _renderSignal = new(false);
	private Thread _renderThread;

	public CharmRenderer()
	{
		Keyboard = new(Input);
		Keyboard.Acquire();

		Mouse = new(Input);
		Mouse.Acquire();

		AppDomain.CurrentDomain.ProcessExit += (s, e) =>
		{
			StopRenderLoop();
			//Dispose();
		};
	}

	public void Initialize(int width, int height)
	{
		if (_isRunning) return;

		if (_clock is null)
			_clock = new();

		_clock.Start();

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

	public void Start()
	{
		_isRunning = true;
		_renderThread = new Thread(RenderLoop)
		{
			IsBackground = true
		};
		_renderThread.Start();
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

		// 81141179 Tower
		// 80BB30E1 EDZ
		// 80C8DACB Cosmo
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



	public float FPS { get; private set; } = 0;
	private const float targetFPS = 200f;
	private void RenderLoop()
	{
#if DEBUG
		TracyWrapper.Profiler.InitThread("Render Thread");
#endif

		var stopwatch = Stopwatch.StartNew();
		double lastFrameTime = stopwatch.Elapsed.TotalSeconds;
		double targetFrameTime = 1.0 / targetFPS;

		while (_isRunning)
		{
			double now = stopwatch.Elapsed.TotalSeconds;
			double delta = now - lastFrameTime;

			if (delta < targetFrameTime)
			{
				double remaining = targetFrameTime - delta;

				// Sleep only if remaining time is significant, otherwise just spin
				if (remaining > 0.002) // ~2 ms
					Thread.Sleep(1);
				else
					Thread.SpinWait(1);

				continue;
			}

			lastFrameTime = now;

			DeltaTime = (float)delta;
			FPS = 1f / DeltaTime;

			Time = _clock.ElapsedMilliseconds / 1000f;
			Render();

#if DEBUG
			TracyWrapper.Profiler.HeartBeat();
#endif
		}
	}

	public void StopRenderLoop()
	{
		if (_isRunning)
		{
			_isRunning = false;
			_renderThread?.Join();
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

		CreateStates(new(0, 0, 0, 0));
		if (DisplayPass == RenderPass.final)
			RenderPostProcess();

		if (DisplayPass > RenderPass.final_combine_no_pp)
			RenderGlobalPipeline(DisplayPass.ToString());

		if (Viewport.ShowGrid)
			RenderGrid();

		CreateStates(new(0, 0, 0, 0));
		// Blits to final RT/Correct format for WPF cus it hates everything
		BlitToWPF(GBuffers.Shading);
		BlitFinal();
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
		if (Camera is null || !IsAppFocused() || !Viewport.ViewportContainer.IsMouseOver)
			return;

		Camera.Update(world, KeyboardState, MouseState);
	}

	public void OnSizeChanged()
	{
		int newWidth = Math.Max(1, (int)Viewport.ActualWidth);
		int newHeight = Math.Max(1, (int)Viewport.ActualHeight);
		if (newWidth != _width || newHeight != _height)
		{
			StopRenderLoop();

			_width = newWidth;
			_height = newHeight;

			Context?.Flush();
			//Context?.ClearState();
			if (Camera is not null)
				Camera.Viewport = new(_width, _height);

			InitializeRenderTargets(newWidth, newHeight);

			DisposeWPF();
			wpfRT = new WpfRenderTarget(Device, newWidth, newHeight, _rtFinal_Clone, Viewport);

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
		_clock?.Stop();
		_clock = null;

		DisposeMesh();

		AssetManager?.Dispose();
		AssetManager = null;
	}

	public void DisposeMesh()
	{
		World?.Dispose();
		AssetManager?.DisposeTextures();
	}

	public void DisposeRenderingResources()
	{
		Utilities.Dispose(ref GBuffers);
		Utilities.Dispose(ref _rtFinal);
		Utilities.Dispose(ref _rtFinal_Clone);
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
		StopRenderLoop();

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
