using SharpDX;
using SharpDX.Direct3D11;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Tiger;
using Tiger.Schema;
using Device = SharpDX.Direct3D11.Device;
using Arithmic;



#if DEBUG
using TracyWrapper;
using Evergine.Bindings.RenderDoc;
#endif

// Please do not look at this. It is an absolute mess and unoptimized and ugly and im ashamed yet proud at the same time.
// This in its current state *could* handle maps but with very low FPS.
// Massive credits to Cohae cus everything learned making this came from Alkahest.

// Realistically, I shouldn't bother with actual map rendering as thats Alkahest's job and it does it far better than I ever could.
// Charm's renderer should just focus on rendering individual assets, maybe with some more in-depth features for that, idk.

namespace Charm.Renderer;

public partial class CharmRenderer : IDisposable
{
	private int _width;
	private int _height;

	public FirstPersonCamera Camera;
	public TempScopes TempScopes;
	public Externs Externs;
	public MatCap MatCapRenderer;

	public RenderWorld World = new();
	public GroupVisibility GroupVisibility { get; } = new(64);

	public RendererViewport Viewport;
	public GPU _GPU { get; set; }

	public Device Device => _GPU?.Device;
	public DeviceContext Context; //=> _GPU?.Context;
	public CommandList CMD; //=> _GPU?.CMD;

	private volatile bool _isRunning = false;
	private Thread _renderThread;
	private ManualResetEventSlim _renderGate = new(true);
	private ManualResetEventSlim _pausedEvent = new(false);
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

			Context = new(Device);
			Load(width, height);

			CMD = new(_GPU);
			MatCapRenderer ??= new MatCap(Context);
		}
	}

	private void Load(int width, int height)
	{
		_width = width;
		_height = height;

		CreateRenderingResources(width, height);
		TempScopes?.Dispose();
		TempScopes = new();
		Externs = new(this);


		World.CreateWorld(this, FileResourcer.Get().GetSchemaTag<SBubbleParent>(new(0x81141179)));

		Camera = new(new HelixToolkit.Maths.Viewport(0, 0, width, height)); // Should be last
		Camera.ResetCameraTransform();

		Application.Current.Dispatcher.BeginInvoke(() =>
		{
			var width = (int)Viewport.ActualWidth;
			var height = (int)Viewport.ActualHeight;
			System.Windows.Point p = Viewport.TranslatePoint(
				new System.Windows.Point(0, 0),
				Application.Current.MainWindow
			);
			Camera.Viewport = new((int)p.X, (int)p.Y, width, height);
		}, DispatcherPriority.Send);
	}


	public void Start()
	{
		if (_isRunning)
			return;

		_isRunning = true;
		_renderThread = new Thread(RenderLoop)
		{
			IsBackground = true
		};
		_renderThread.Start();
		Log.Debug($"Renderer {GetHashCode()} started");
	}

	public void Stop()
	{
		if (_isRunning)
		{
			_isRunning = false;
			_renderThread?.Join();
			Log.Debug($"Renderer {GetHashCode()} stopped");
		}
	}

	public void Resume()
	{
		_pausedEvent.Reset();
		_renderGate.Set();
	}

	public void Pause()
	{
		_renderGate.Reset();
		_frameCompleteEvent.WaitOne();
		_pausedEvent.Set();
	}

	public float Time { get; private set; }
	public float DeltaTime { get; private set; }
	private const float MaxDeltaTime = 0.1f;

	private int _frameCounter = 0;
	public float FPS { get; private set; } = 0;
	private float TargetFPS = 200f;

	private void RenderLoop()
	{
#if DEBUG
		Profiler.InitThread("Render Thread");
#endif

		var stopwatch = Stopwatch.StartNew();
		double lastTime = stopwatch.Elapsed.TotalSeconds;

		double fpsTimer = 0.0;
		int fpsFrames = 0;

		while (_isRunning)
		{
			_renderGate.Wait();
			_pausedEvent.Reset();

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

			Render();

			fpsFrames++;
			fpsTimer += delta;
			if (fpsTimer >= 0.5)
			{
				FPS = fpsFrames / (float)fpsTimer;
				fpsFrames = 0;
				fpsTimer = 0;
			}

			_frameCounter++;
			_frameCompleteEvent.Set();
#if DEBUG
			Profiler.HeartBeat();
#endif
		}

		_pausedEvent.Set();
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

		HandleRenderDoc(true);

		int newWidth = Math.Max(1, (int)Viewport.ActualWidth);
		int newHeight = Math.Max(1, (int)Viewport.ActualHeight);
		Context.Rasterizer.SetViewport(0, 0, newWidth, newHeight, 0.0f, 1f);

		UpdateCamera();
		UpdateExterns();
		UpdateGlobalChannels();
		UpdateScopes();

		RenderGBuffer();
		RenderAtmosphere();
		RenderLighting();
		RenderShading();
		RenderTransparent();
		RenderPostProcess();

		if (Viewport.DisplayPass > RenderPass.final_color_grade)
			RenderGlobalPipeline(Viewport.DisplayPass.ToString());
		else
			RenderLuminance();

		var blitRT = Viewport.DisplayPass == RenderPass.final ? GBuffers.Shading : GBuffers.FXAA;
		if (Viewport.ShowGrid)
		{
			Context.OutputMerger.SetTargets(GBuffers.Depth.DSV, blitRT.RTV);
			RenderGrid();
		}

		if (Viewport.ShowSkele || Viewport.ShowBB)
		{
			Context.OutputMerger.SetTargets(blitRT.RTV);
			if (Viewport.ShowSkele)
				RenderSkeleton();

			if (Viewport.ShowBB)
			{
				RenderBoundingBoxes();
				if (World.OverrideMainBB is not null)
					RenderBoundingBox(World.OverrideMainBB.Value, new(1, 0, 0, 1));
			}
		}

		// Blits to final RT/Correct format for WPF cus it hates everything
		BlitToWPF(blitRT);

		//if (World.OverrideMainBB is not null)
		//{
		//	var bb = World.OverrideMainBB.Value;
		//	Console.WriteLine($"In Camera Frustum? {Camera.Frustum.Intersects(ref bb)}");
		//}

		//if (MouseState.Buttons[1])
		//	Camera.Pick(Camera.GetMouseRay(Camera.Viewport, Externs.View), World);

		HandleRenderDoc(false);
	}

	private void UpdateCamera()
	{
		if (Camera is null || !IsAppFocused())
			return;

		RenderHelpers.Profile("Update Camera");

		Camera.FOV = Viewport.FOV;
		Camera.UpdateProjectionMatrix();
		if (Viewport.ViewportContainer.IsMouseOver)
			Camera.Update(World, KeyboardState, MouseState);

		RenderHelpers.EndProfile();
	}

	private void UpdateExterns()
	{
		var near = Camera.Near;
		var far = Camera.Far;
		Externs.Deferred.DepthConstants = new(1.0f / far, (far - near) / (far * near), 0, 0);
		Externs.Decal.DepthConstants = Externs.Deferred.DepthConstants;
		Externs.Frame.ExposureIllumRelative = Viewport.ExposureIllum;
		Externs.Frame.Unk10 = Viewport.TimeOfDay;
		Externs.Update(this);
	}

	private void UpdateGlobalChannels()
	{
		World.EvaluateGlobalChannels(Externs);
		World.GlobalChannels.Set("sky_snapshot_rotation", new(Viewport.AtmosRotation * 360f));
		World.GlobalChannels.Set("sky_snapshot_intensity", new(Viewport.AtmosIntensity));
	}

	private void UpdateScopes()
	{
		TfxScopes[Tiger.TfxScope.VIEW].Bind(this);
		TfxScopes[Tiger.TfxScope.FRAME].Bind(this);
		TempScopes.UpdateFrameScope(this);
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
			System.Windows.Point p = Viewport.TranslatePoint(
				new System.Windows.Point(0, 0),
				Application.Current.MainWindow
			);

			Context?.Flush();
			//Context?.ClearState();
			if (Camera is not null)
				Camera.Viewport = new((int)p.X, (int)p.Y, _width, _height);

			InitializeRenderTargets(newWidth, newHeight);

			DisposeWPF();
			wpfRT = new WpfRenderTarget(Device, newWidth, newHeight, _rtFinal, Viewport);

			Context.Rasterizer.SetViewport(0, 0, newWidth, newHeight, 0.0f, 1f);

			Start();
		}
	}

	private void HandleRenderDoc(bool capture)
	{
#if DEBUG
		if (_renderDoc is null)
			RenderDoc.Load(out _renderDoc);

		if (capture)
		{
			if (!_captured & KeyboardState.IsPressed(SharpDX.DirectInput.Key.F12))
			{
				Log.Debug("Starting Renderdoc Capture");
				_renderDoc.API.StartFrameCapture(Device.NativePointer, IntPtr.Zero);
			}
			return;
		}

		if (!_captured & KeyboardState.IsPressed(SharpDX.DirectInput.Key.F12))
		{
			_renderDoc.API.EndFrameCapture(Device.NativePointer, IntPtr.Zero);
			_captured = true;
			Log.Debug("Renderdoc Capture Complete");
		}

		if (_captured & !KeyboardState.IsPressed(SharpDX.DirectInput.Key.F12))
			_captured = false;
#endif
	}

	public void DisposeMesh()
	{
		World?.DisposeAll();
		//AssetManager?.DisposeTextures();
	}

	public void DisposeRenderingResources()
	{
		Utilities.Dispose(ref GBuffers);
		Utilities.Dispose(ref MatCapRenderer);
		Utilities.Dispose(ref _rtFinal);
		Utilities.Dispose(ref _blitVS);
		Utilities.Dispose(ref _blitPS);
		Utilities.Dispose(ref _blitPS_Linear);
		Utilities.Dispose(ref _gridShaderVS);
		Utilities.Dispose(ref _gridShaderPS);
		Utilities.Dispose(ref _pointSampler);
		Utilities.Dispose(ref _wireframeRS);
		Utilities.Dispose(ref _debugPSCB);
		Utilities.Dispose(ref _debugLinesPS);
		Utilities.Dispose(ref _debugLinesVS);
		Utilities.Dispose(ref _debugLinesLayout);
		Utilities.Dispose(ref _clearAOVS);
		Utilities.Dispose(ref _clearAOPS);
		Utilities.Dispose(ref _fullHemiSkyTempVS);
		Utilities.Dispose(ref _fullHemiSkyTempPS);
		Utilities.Dispose(ref Annotation);

#if DEBUG
		_renderDoc = null;
#endif
	}

	public void Destroy(bool fullyDestroy = false)
	{
		Dispose();
		if (fullyDestroy)
		{
			AssetManager.Dispose();
			_GPU?.Dispose();
			_GPU = null;
		}
	}

	public void Dispose()
	{
		Stop();
		DisposeWPF();
		DisposeMesh();
		DisposeRenderingResources();

		foreach (var pipeline in _pipelineCache.Values)
		{
			pipeline?.Dispose();
		}
		_pipelineCache.Clear();

		// I don't think Externs needs disposed since all its SRVs are assigned from GBuffers/AssetManager which are disposed above
		//Externs?.Dispose();
		TempScopes?.Dispose();

		foreach (var scope in TfxScopes.Values)
		{
			scope.Dispose();
		}
		TfxScopes?.Clear();

		Input.Dispose();
		Keyboard.Dispose();
		Mouse.Dispose();

		CMD?.Dispose();
	}

	public void DisposeWPF()
	{
		wpfRT?.Dispose();
		wpfRT = null;
	}
}
