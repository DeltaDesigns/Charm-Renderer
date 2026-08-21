using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpDX;
using SharpDX.Direct3D11;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    private volatile string _savePath;
    public void RequestScreenshot(string path)
    {
        _savePath = path;
    }

    private void CheckScreenshot()
    {
        var path = _savePath;
        if (path is null || path == string.Empty)
            return;

        _savePath = null;
        CaptureScreenshot(path);
    }

    private void CaptureScreenshot(string path)
    {
        var desc = _rtFinal.Texture.Description;
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

        using var staging = new Texture2D(Device, stagingDesc);
        Device.ImmediateContext.CopyResource(_rtFinal.Texture, staging);

        var dataBox = Device.ImmediateContext.MapSubresource(staging, 0, MapMode.Read, MapFlags.None, out var dataStream);
        try
        {
            Save(dataStream, desc.Width, desc.Height, dataBox.RowPitch, path);
        }
        finally
        {
            Device.ImmediateContext.UnmapSubresource(staging, 0);
            dataStream.Dispose();
        }
    }

    private static void Save(DataStream data, int width, int height, int rowPitch, string path)
    {
        var bitmap = BitmapSource.Create(
            width, height, 96, 96,
            PixelFormats.Bgra32, null,
            data.DataPointer, rowPitch * height, rowPitch);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(fileStream);
    }
}
