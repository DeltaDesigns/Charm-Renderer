using System.IO;
using System.Windows.Media.Imaging;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    private volatile string _savePath;
    private volatile float _scale = 1f;
    public void RequestScreenshot(string path, float scale = 1f)
    {
        _savePath = path;
        _scale = scale;
    }

    private void CheckScreenshot()
    {
        var path = _savePath;
        if (path is null || path == string.Empty)
            return;

        _savePath = null;

        var scale = _scale;
        if (scale <= 1)
            CaptureScreenshot(path, _rtFinal);
        else
            CaptureUpscaledScreenshot(path, scale);
    }

    private void CaptureScreenshot(string path, RenderTarget2D source)
    {
        RenderHelpers.Profile("Capture Screenshot");
        Submit();
        SaveRenderTargetToPng(source, path);
        RenderHelpers.EndProfile();
    }

    private void CaptureUpscaledScreenshot(string path, float scale)
    {
        RenderHelpers.Profile("Capture Screenshot (Supersampled)");

        int capWidth = Math.Max(1, (int)(_width * scale));
        int capHeight = Math.Max(1, (int)(_height * scale));

        var curGBuffers = GBuffers;
        var curViewport = Camera.Viewport;

        using var capGBuffers = new GBuffer(Device, capWidth, capHeight);
        using var capFinal = new RenderTarget2D(Device, capWidth, capHeight,
            Format.B8G8R8A8_UNorm_SRgb, resourceOptionFlags: ResourceOptionFlags.Shared,
            debugName: "RT Final (Capture)");

        try
        {
            GBuffers = capGBuffers;
            Camera.Viewport = new(0, 0, capWidth, capHeight);
            Camera.UpdateProjectionMatrix();
            Context.Rasterizer.SetViewport(0, 0, capWidth, capHeight, 0.0f, 1f);

            UpdateCamera();
            UpdateExterns(scale);
            UpdateGlobalChannels();
            UpdateScopes();
            RenderPasses();

            var blitRT = Viewport.FXAA ? GBuffers.FXAA : GBuffers.PostProcessResult;
            BlitTo(blitRT, capFinal);

            Submit();
            SaveRenderTargetToPng(capFinal, path);
        }
        finally
        {
            GBuffers = curGBuffers;
            Camera.Viewport = curViewport;
            Camera.UpdateProjectionMatrix();
            Context.Rasterizer.SetViewport(0, 0, _width, _height, 0.0f, 1f);
        }

        RenderHelpers.EndProfile();
    }

    private static void SaveRenderTargetToPng(RenderTarget2D target, string path)
    {
        var desc = target.Texture.Description;
        var stagingDesc = new Texture2DDescription
        {
            Width = desc.Width,
            Height = desc.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = desc.Format,
            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None
        };

        using var staging = new Texture2D(GPU.Instance.Device, stagingDesc);
        GPU.Instance.ImmediateContext.CopyResource(target.Texture, staging);

        var dataBox = GPU.Instance.ImmediateContext.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out var dataStream);
        try
        {
            var bitmap = BitmapSource.Create(
                desc.Width, desc.Height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null,
                dataStream.DataPointer, dataBox.RowPitch * desc.Height, dataBox.RowPitch);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            encoder.Save(fileStream);
        }
        finally
        {
            GPU.Instance.ImmediateContext.UnmapSubresource(staging, 0);
            dataStream.Dispose();
        }
    }

    private void Submit()
    {
        using (var commandList = Context.FinishCommandList(false))
        {
            GPU.Instance.ImmediateContext.ExecuteCommandList(commandList, true);
        }
    }
}
