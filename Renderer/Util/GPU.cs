using System.Windows;
using Arithmic;
using SharpDX;
using SharpDX.Diagnostics;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace Charm.Renderer;

public class GPU : IDisposable
{
    private static Lazy<GPU> _lazy = CreateLazy();
    private static Lazy<GPU> CreateLazy() => new(() => new GPU(), LazyThreadSafetyMode.ExecutionAndPublication);
    public static GPU Instance => _lazy.Value;

    public Device Device;
    public DeviceContext ImmediateContext;

    private readonly HashSet<IDisposable> _trackedResources = new();

    public GPU()
    {
        Log.Debug("Creating Device");
        Create();
        Log.Debug("Created Device");

        //Application.Current.Exit += OnAppExit;
    }

    private void Create()
    {
        if (Device != null)
            throw new Exception("GPU Device already exists! This shouldn't have been called!");

        var creationFlags = DeviceCreationFlags.BgraSupport;

        var featureLevels = new[]
        {
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_11_1,
        };

        //#if DEBUG
        //        Configuration.EnableObjectTracking = true;
        //#endif
        Configuration.EnableReleaseOnFinalizer = true; // I THINK this is helping with vram
        var device = new SharpDX.Direct3D11.Device(DriverType.Hardware, creationFlags, featureLevels);
        Device = device.QueryInterface<Device>();
        ImmediateContext = Device.ImmediateContext.QueryInterface<DeviceContext>();
    }

    public void RegisterResource(IDisposable resource)
    {
        lock (_trackedResources)
        {
            _trackedResources.Add(resource);
        }
    }

    public void UnregisterResource(IDisposable resource)
    {
        lock (_trackedResources)
        {
            _trackedResources.Remove(resource);
        }
    }

    private bool _disposed = false;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_trackedResources.Count > 0)
        {
            Log.Debug($"{_trackedResources.Count} Resources still registered");
            foreach (var resource in _trackedResources)
            {
                Log.Debug($"{resource}");
                if (resource is Constants consts)
                    Log.Debug($"{consts.DebugName}");

                resource?.Dispose();
            }
        }

        if (Configuration.EnableObjectTracking)
        {
            var live = ObjectTracker.FindActiveObjects();
            Log.Debug($"{live.Count} Still Alive!");
            foreach (var obj in live)
            {
                if (obj.Object.Target is DeviceChild test)
                    Log.Debug($"{obj.Object.Target} : {test?.DebugName}");
                else
                    Log.Debug($"{obj.Object.Target}");
            }
        }
    }

    private void OnAppExit(object sender, ExitEventArgs e)
    {
        DisposeFull();
    }

    public void DisposeFull()
    {
        Dispose();

        Utilities.Dispose(ref Device);
        Utilities.Dispose(ref ImmediateContext);
        _lazy = CreateLazy();

        if (Application.Current != null)
            Application.Current.Exit -= OnAppExit;
    }
}

public class GPUState
{
    public States States;
    public InputLayout CurrentInputLayout;
    public PrimitiveTopology CurrentTopology;
    public GPUStageState VSState;
    public GPUStageState PSState;
    public RawViewportF[] Viewports = new RawViewportF[8];
    public RenderTargetView[] RTVs = new RenderTargetView[8];
    public DepthStencilView DSV;

    public GPUState Backup(CommandList cmd)
    {
        States = cmd.States;
        CurrentInputLayout = cmd.ImmediateContext.InputAssembler.InputLayout;
        CurrentTopology = cmd.ImmediateContext.InputAssembler.PrimitiveTopology;
        RTVs = cmd.ImmediateContext.OutputMerger.GetRenderTargets(8, out DepthStencilView dsv);
        DSV = dsv;
        Viewports = cmd.ImmediateContext.Rasterizer.GetViewports<RawViewportF>();

        VSState = new GPUStageState()
        {
            SRVs = cmd.ImmediateContext.VertexShader.GetShaderResources(0, 128),
            Samplers = cmd.ImmediateContext.VertexShader.GetSamplers(0, 16),
            CBuffers = cmd.ImmediateContext.VertexShader.GetConstantBuffers(0, 14),
        };

        PSState = new GPUStageState()
        {
            SRVs = cmd.ImmediateContext.PixelShader.GetShaderResources(0, 128),
            Samplers = cmd.ImmediateContext.PixelShader.GetSamplers(0, 16),
            CBuffers = cmd.ImmediateContext.PixelShader.GetConstantBuffers(0, 14),
        };

        return this;
    }

    public void Restore(CommandList cmd)
    {
        cmd.States.CreateStates(cmd.ImmediateContext, States.CurrentState);
        cmd.ImmediateContext.InputAssembler.InputLayout = CurrentInputLayout;
        cmd.ImmediateContext.InputAssembler.PrimitiveTopology = CurrentTopology;
        cmd.ImmediateContext.OutputMerger.SetRenderTargets(DSV, RTVs);
        cmd.ImmediateContext.Rasterizer.SetViewports(Viewports);
        cmd.ImmediateContext.VertexShader.SetShaderResources(0, VSState.SRVs);
        cmd.ImmediateContext.VertexShader.SetSamplers(0, VSState.Samplers);
        cmd.ImmediateContext.VertexShader.SetConstantBuffers(0, VSState.CBuffers);
        cmd.ImmediateContext.PixelShader.SetShaderResources(0, PSState.SRVs);
        cmd.ImmediateContext.PixelShader.SetSamplers(0, PSState.Samplers);
        cmd.ImmediateContext.PixelShader.SetConstantBuffers(0, PSState.CBuffers);
    }
}

public struct GPUStageState
{
    public GPUStageState()
    {
    }

    public ShaderResourceView[] SRVs = new ShaderResourceView[128];
    public SamplerState[] Samplers = new SamplerState[16];
    public Buffer[] CBuffers = new Buffer[14];
}

public class CommandList : IDisposable
{
    public CommandList(GPU gpu)
    {
        Parent = gpu;
        States = new States();
        GpuState = new();
    }

    public CommandList() { }

    public GPU Parent;
    public GPUState GpuState;
    public DeviceContext ImmediateContext => Parent.ImmediateContext;
    public DeviceContext DeferredContext;

    public States States;
    public StateSelection CurrentState => States.CurrentState;

    public void Dispose()
    {
        States?.DisposeStates();
    }
}


public abstract class GpuResource : IDisposable
{
    protected GpuResource()
    {
        GPU.Instance?.RegisterResource(this);
    }

    public virtual void Dispose()
    {
        GPU.Instance?.UnregisterResource(this);
    }
}
