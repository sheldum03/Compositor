using System.Runtime.InteropServices;
using SkiaSharp;

// Owned by one view. Each SKImage owns a pixel copy, including mutable stroke tiles.
internal sealed class TileImageCache : IDisposable
{
    private readonly Dictionary<int, (byte[] Source, long Version, SKMatrix? SamplingMatrix, SKImage Image)> entries = [];
    private SKSurface? scaledSurface;
    public long Draw(SKCanvas canvas, SKRectI bounds, int key, byte[] pixels, long version, SKPaint paint)
    {
        long copied = 0;
        int width = bounds.Width, height = bounds.Height;
        SKRectI deviceBounds = default;
        var clip = canvas.DeviceClipBounds;
        var matrix = canvas.TotalMatrix;
        if (matrix.ScaleX > 0 && matrix.ScaleX < 1 && matrix.ScaleY > 0 && matrix.ScaleY < 1 &&
            matrix.SkewX == 0 && matrix.SkewY == 0 && matrix.Persp0 == 0 && matrix.Persp1 == 0 && matrix.Persp2 == 1)
        {
            var device = matrix.MapRect(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
            var aligned = SKRectI.Round(device);
            if (device.Left == aligned.Left && device.Top == aligned.Top && device.Right == aligned.Right && device.Bottom == aligned.Bottom &&
                aligned.Width > 0 && aligned.Height > 0 && aligned.Left >= 0 && aligned.Top >= 0 &&
                aligned.Right <= clip.Right && aligned.Bottom <= clip.Bottom)
            { width = aligned.Width; height = aligned.Height; deviceBounds = aligned; }
        }
        SKMatrix? samplingMatrix = width != bounds.Width || height != bounds.Height ? matrix : null;
        if (!entries.TryGetValue(key, out var entry) || !ReferenceEquals(entry.Source, pixels) || entry.Version != version ||
            !Nullable.Equals(entry.SamplingMatrix, samplingMatrix) ||
            entry.Image.Width != width || entry.Image.Height != height)
        {
            using var srgb = SKColorSpace.CreateSrgb();
            var info = new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb);
            using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul, srgb));
            var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                using var rgba = new SKPixmap(info, pin.AddrOfPinnedObject());
                if (width == bounds.Width && height == bounds.Height)
                {
                    if (!rgba.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes))
                        throw new InvalidOperationException("Tile color conversion failed");
                }
                else
                {
                    if (scaledSurface is null || scaledSurface.Canvas.DeviceClipBounds.Right != clip.Right || scaledSurface.Canvas.DeviceClipBounds.Bottom != clip.Bottom)
                    {
                        scaledSurface?.Dispose();
                        scaledSurface = SKSurface.Create(new SKImageInfo(clip.Right, clip.Bottom, SKImageInfo.PlatformColorType, SKAlphaType.Premul, srgb));
                    }
                    // Keep device coordinates: relocating a tile changes nearest-sample rounding at ratios such as 3/8.
                    using var source = SKImage.FromPixels(rgba);
                    var target = scaledSurface.Canvas;
                    target.Save(); target.ResetMatrix();
                    target.ClipRect(new SKRect(deviceBounds.Left, deviceBounds.Top, deviceBounds.Right, deviceBounds.Bottom));
                    target.Clear(SKColors.Transparent); target.SetMatrix(matrix);
                    target.DrawImage(source, bounds.Left, bounds.Top); target.Restore();
                    if (!scaledSurface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, deviceBounds.Left, deviceBounds.Top))
                        throw new InvalidOperationException("Scaled tile readback failed");
                }
            }
            finally { pin.Free(); }
            bitmap.SetImmutable();
            var image = SKImage.FromBitmap(bitmap);
            entry.Image?.Dispose();
            entry = (pixels, version, samplingMatrix, image); entries[key] = entry;
            copied = bitmap.ByteCount;
        }
        canvas.DrawImage(entry.Image, new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), paint);
        return copied;
    }
    public void Clear()
    {
        foreach (var entry in entries.Values) entry.Image.Dispose();
        entries.Clear();
        scaledSurface?.Dispose(); scaledSurface = null;
    }
    public void Dispose() => Clear();
}
