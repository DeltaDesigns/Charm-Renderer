using System.Windows;
using System.Windows.Interop;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.Direct3D9;
using SharpDX.DXGI;
using static Charm.Renderer.CharmRenderer;
using Device = SharpDX.Direct3D11.Device;

namespace Charm.Renderer;

public class WpfRenderTarget : IDisposable
{
    public RenderTarget2D SceneColor;
    public Texture2D SharedWpfTexture;
    private RendererViewport currentViewport;

    public D3DImage D3DBackBuffer;
    public int Width { get; }
    public int Height { get; }

    private SharpDX.Direct3D9.DeviceEx device9;
    private SharpDX.Direct3D9.Texture sharedTex9;

    public WpfRenderTarget(Device device11, int width, int height, RenderTarget2D sceneColor, RendererViewport viewport)
    {
        Width = width;
        Height = height;
        SceneColor = sceneColor;
        currentViewport = viewport;

        var sharedTexDesc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm, // WPF requires BGRA8
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.Shared
        };
        SharedWpfTexture = new Texture2D(device11, sharedTexDesc);

        var windowHandle = new WindowInteropHelper(Window.GetWindow(viewport)).Handle;
        var presentParams = new SharpDX.Direct3D9.PresentParameters
        {
            Windowed = true,
            SwapEffect = SharpDX.Direct3D9.SwapEffect.Discard,
            DeviceWindowHandle = windowHandle,
            PresentationInterval = SharpDX.Direct3D9.PresentInterval.One,
        };

        var direct3DEx = new SharpDX.Direct3D9.Direct3DEx();
        device9 = new SharpDX.Direct3D9.DeviceEx(direct3DEx, 0, SharpDX.Direct3D9.DeviceType.Hardware, IntPtr.Zero, CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded, presentParams);
        using (var dxgiResource = SharedWpfTexture.QueryInterface<SharpDX.DXGI.Resource1>())
        {
            IntPtr sharedHandle = dxgiResource.SharedHandle;
            sharedTex9 = new SharpDX.Direct3D9.Texture(device9, Width, Height, 1, SharpDX.Direct3D9.Usage.RenderTarget, SharpDX.Direct3D9.Format.A8R8G8B8, SharpDX.Direct3D9.Pool.Default, ref sharedHandle);
        }

        D3DBackBuffer = new D3DImage();
        currentViewport.RT0.Source = D3DBackBuffer;

        UpdateBackBuffer();
    }

    public void UpdateBackBuffer()
    {
        using (var surface = sharedTex9.GetSurfaceLevel(0))
        {
            D3DBackBuffer.Lock();
            D3DBackBuffer.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface.NativePointer);
            D3DBackBuffer.AddDirtyRect(new Int32Rect(0, 0, Width, Height));
            D3DBackBuffer.Unlock();
        }
    }

    /// <summary>
    /// Call each frame after rendering SceneColor
    /// </summary>
    public void Present(DeviceContext context, System.Windows.Controls.Image imageHost)
    {
        context!.CopyResource(SceneColor.Texture, SharedWpfTexture);
        context!.Flush();

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            D3DBackBuffer?.Lock();
            D3DBackBuffer?.AddDirtyRect(new Int32Rect(0, 0, Width, Height));
            D3DBackBuffer?.Unlock();
            if (imageHost.Source is null)
                imageHost.Source = D3DBackBuffer;

        }, System.Windows.Threading.DispatcherPriority.Send);
    }

    public void Dispose()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            D3DBackBuffer.Lock();
            D3DBackBuffer.SetBackBuffer(
                D3DResourceType.IDirect3DSurface9,
                IntPtr.Zero);
            D3DBackBuffer.Unlock();
            D3DBackBuffer = null;
            currentViewport.RT0.Source = null;
        }, System.Windows.Threading.DispatcherPriority.Send);

        Utilities.Dispose(ref SceneColor);
        Utilities.Dispose(ref SharedWpfTexture);
        Utilities.Dispose(ref sharedTex9);
        Utilities.Dispose(ref device9);
    }
}
