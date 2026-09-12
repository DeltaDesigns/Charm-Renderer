using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;
using SharpDX.Direct3D11;
using Tiger;
using Tiger.Schema;
using Tiger.Schema.Entity;
using BitmapSource = System.Windows.Media.Imaging.BitmapSource;
using Format = SharpDX.DXGI.Format;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace Charm.Renderer;

public partial class CharmRenderer
{
    private const int ThumbnailSize = 256;
    private GBuffer _thumbnailGBuffers;
    private RenderTarget2D _thumbnailFinal;

    private static string ThumbnailCacheDirectory =>
       Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Charm", "ThumbnailCache");

    private static string GetThumbnailCachePath(FileHash hash) =>
       Path.Combine(ThumbnailCacheDirectory, $"{Strategy.CurrentStrategy}_{hash}.png");

    private void CreateThumbnailGBuffers()
    {
        if (_thumbnailGBuffers != null)
            return;

        _thumbnailGBuffers = new GBuffer(Device, ThumbnailSize, ThumbnailSize);
        _thumbnailFinal = new RenderTarget2D(Device, ThumbnailSize, ThumbnailSize,
            Format.B8G8R8A8_UNorm_SRgb, resourceOptionFlags: ResourceOptionFlags.Shared,
            debugName: "RT Final (Thumbnail)");
    }

    // reusing TfxFeatureRenderer cus i dont wanna make a new enum just for this
    public BitmapSource GenerateThumbnail(FileHash hash, TfxFeatureRenderer type)
    {
        CreateThumbnailGBuffers();
        DefaultWorld.DisposeAll();

        RenderObject obj = new();
        ObjectChannels channels = null;
        if (type == TfxFeatureRenderer.StaticObjects)
        {
            var staticMesh = FileResourcer.Get().GetFile<StaticMesh>(hash, shouldCache: false);
            obj.Create(Context, DefaultWorld, staticMesh);
        }
        else
        {
            var ent = FileResourcer.Get().GetFile<Entity>(hash, shouldCache: false);
            obj.Create(Context, DefaultWorld, ent, renderType: RenderType.Minimal);
            channels = new(ent);
        }


        //var children = entity.GetEntityChildren();
        //foreach (var child in children)
        //{
        //    if (child.Model is null)
        //        continue;

        //    RenderObject childObj = new();
        //    childObj.TransformOffset = new Transform
        //    {
        //        Quaternion = child.Model.RotationOffset,
        //        Position = child.Model.TranslationOffset.ToVec3()
        //    };

        //    childObj.Create(Context, DefaultWorld, child, renderType: RenderType.Minimal);
        //    childObj.IsChild = true;
        //}

        var curGBuffers = GBuffers;
        var curViewport = Camera.Viewport;
        var curPosition = Camera.Position;
        var curPitch = Camera.Pitch;
        var curYaw = Camera.Yaw;
        var curExposure = Externs.Frame.ExposureScale;
        var curObjectChannels = EntityObjectChannels;

        try
        {
            RenderType = RenderType.Minimal;
            GBuffers = _thumbnailGBuffers;
            EntityObjectChannels = channels;

            LookAtBoundingBox(obj.BoundingBox, distanceX: 1f);
            Camera.Viewport = new(0, 0, ThumbnailSize, ThumbnailSize);
            Camera.UpdateViewMatrix();
            Camera.UpdateProjectionMatrix();

            Externs.Frame.Update(this);
            Externs.View.Update(this);
            Externs.UberDepth.Update(this);
            Externs.Frame.ExposureScale = 1f;
            UpdateScopes();

            Context.Rasterizer.SetViewport(0, 0, ThumbnailSize, ThumbnailSize, 0.0f, 1f);

            RenderPasses(DefaultWorld);
            BlitTo(GBuffers.FXAA, _thumbnailFinal);

            using var commandList = Context.FinishCommandList(false);
            GPU.Instance.ImmediateContext.ExecuteCommandList(commandList, true);

            var bitmap = RtToBitmap(_thumbnailFinal);
            Task.Run(() => SaveThumbnailToCache(bitmap, hash));

            return bitmap;
        }
        finally
        {
            RenderType = RenderType.Full;
            GBuffers = curGBuffers;
            EntityObjectChannels = curObjectChannels;
            Camera.Viewport = curViewport;
            Camera.Position = curPosition;
            Camera.Pitch = curPitch;
            Camera.Yaw = curYaw;
            Camera.UpdateViewMatrix();
            Camera.UpdateProjectionMatrix();
            Camera.UpdateVectors();
            Externs.Frame.Update(this);
            Externs.View.Update(this);
            Externs.UberDepth.Update(this);

            Externs.Frame.ExposureScale = curExposure;
            UpdateScopes();

            Context.Rasterizer.SetViewport(0, 0, _width, _height, 0.0f, 1f);
        }
    }

    private static BitmapSource RtToBitmap(RenderTarget2D target)
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

        var dataBox = GPU.Instance.ImmediateContext.MapSubresource(staging, 0, MapMode.Read, MapFlags.None, out var dataStream);
        try
        {
            var bitmap = BitmapSource.Create(
                desc.Width, desc.Height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null,
                dataStream.DataPointer, dataBox.RowPitch * desc.Height, dataBox.RowPitch);

            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            GPU.Instance.ImmediateContext.UnmapSubresource(staging, 0);
            dataStream.Dispose();
        }
    }

    private readonly ConcurrentQueue<(FileHash hash, TfxFeatureRenderer type, CancellationToken token, Action<BitmapSource> callback)> _pendingThumbnails = new();
    public void RequestThumbnail(FileHash hash, TfxFeatureRenderer type, CancellationToken token, Action<BitmapSource> onComplete)
    {
        _pendingThumbnails.Enqueue((hash, type, token, onComplete));
    }

    private void CheckThumbnailRequest()
    {
        while (_pendingThumbnails.TryDequeue(out var request))
        {
            if (request.token.IsCancellationRequested)
                continue;

            var bitmap = GenerateThumbnail(request.hash, request.type);
            var callback = request.callback;
            Viewport.Dispatcher.InvokeAsync(() => callback?.Invoke(bitmap));
            break;
        }
    }

    private static void SaveThumbnailToCache(BitmapSource bitmap, FileHash hash)
    {
        try
        {
            Directory.CreateDirectory(ThumbnailCacheDirectory);
            var path = GetThumbnailCachePath(hash);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            encoder.Save(fileStream);
        }
        catch (IOException)
        {
        }
    }
}
