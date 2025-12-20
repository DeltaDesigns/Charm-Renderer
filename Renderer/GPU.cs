using System.Windows;
using Arithmic;
using SharpDX;
using SharpDX.Diagnostics;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;

namespace Charm.Renderer;

public class GPU : IDisposable
{
    private static GPU _instance;
    public static GPU Instance
    {
        get
        {
            if (_instance == null)
                _instance = new GPU();

            return _instance;
        }
    }

    public Device Device;
    public DeviceContext Context;

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
        Context = Device.ImmediateContext.QueryInterface<DeviceContext>();
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
        Utilities.Dispose(ref Context);
        _instance = null;

        if (Application.Current != null)
            Application.Current.Exit -= OnAppExit;
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
