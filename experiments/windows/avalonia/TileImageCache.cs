using SkiaSharp;

// Owned by one view. Each SKImage owns a pixel copy, including mutable stroke tiles.
internal sealed class TileImageCache : IDisposable
{
    private readonly Dictionary<int, (byte[] Source, long Version, SKImage Image)> entries = [];
    public long Draw(SKCanvas canvas, SKRectI bounds, int key, byte[] pixels, long version, SKPaint paint)
    {
        long copied = 0;
        if (!entries.TryGetValue(key, out var entry) || !ReferenceEquals(entry.Source, pixels) || entry.Version != version)
        {
            using var srgb = SKColorSpace.CreateSrgb();
            var image = SKImage.FromPixelCopy(new SKImageInfo(bounds.Width, bounds.Height,
                SKColorType.Rgba8888, SKAlphaType.Premul, srgb), pixels);
            entry.Image?.Dispose();
            entry = (pixels, version, image); entries[key] = entry;
            copied = pixels.Length;
        }
        canvas.DrawImage(entry.Image, bounds.Left, bounds.Top, paint);
        return copied;
    }
    public void Clear()
    {
        foreach (var entry in entries.Values) entry.Image.Dispose();
        entries.Clear();
    }
    public void Dispose() => Clear();
}
