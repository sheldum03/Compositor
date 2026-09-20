using System.Security.Cryptography;
using SkiaSharp;

// Fixed document grid for the M1 brush specimen. Arrays in a snapshot are never exposed for writing.
internal sealed class TiledRaster(int width, int height)
{
    public const int TileSize = 256;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Columns => (Width + TileSize - 1) / TileSize;
    private readonly Dictionary<int, byte[]> tiles = [];
    public int TileCount => tiles.Count;
    public int FullRasterExports { get; private set; }
    public long StoredBytes => tiles.Values.Sum(t => (long)t.Length);

    public SKRectI Bounds(int key)
    {
        int x = key % Columns * TileSize, y = key / Columns * TileSize;
        return new SKRectI(x, y, Math.Min(x + TileSize, Width), Math.Min(y + TileSize, Height));
    }
    public byte[] CopyTile(int key) => tiles.TryGetValue(key, out var tile)
        ? (byte[])tile.Clone() : new byte[Bounds(key).Width * Bounds(key).Height * 4];

    public TiledRaster Replacing(IEnumerable<KeyValuePair<int, byte[]>> replacements)
    {
        var result = new TiledRaster(Width, Height);
        foreach (var pair in tiles) result.tiles.Add(pair.Key, pair.Value);
        foreach (var pair in replacements) result.tiles[pair.Key] = (byte[])pair.Value.Clone();
        return result;
    }
    public int SharedTiles(TiledRaster other) => tiles.Count(pair =>
        other.tiles.TryGetValue(pair.Key, out var tile) && ReferenceEquals(pair.Value, tile));

    public bool HasSamePixels(TiledRaster other)
    {
        if (Width != other.Width || Height != other.Height) return false;
        foreach (int key in tiles.Keys.Union(other.tiles.Keys))
        {
            tiles.TryGetValue(key, out var a);
            other.tiles.TryGetValue(key, out var b);
            if (a is null) { if (b!.Any(value => value != 0)) return false; }
            else if (b is null) { if (a.Any(value => value != 0)) return false; }
            else if (!a.AsSpan().SequenceEqual(b)) return false;
        }
        return true;
    }

    public string Digest()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var pair in tiles.OrderBy(p => p.Key))
        {
            hash.AppendData(BitConverter.GetBytes(pair.Key));
            hash.AppendData(pair.Value);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public long Paint(SKCanvas canvas, IReadOnlyDictionary<int, byte[]>? replacements = null)
    {
        long copied = 0;
        foreach (var pair in tiles)
            if (replacements?.ContainsKey(pair.Key) != true) copied += DrawTile(canvas, Bounds(pair.Key), pair.Value);
        if (replacements is not null)
            foreach (var pair in replacements) copied += DrawTile(canvas, Bounds(pair.Key), pair.Value);
        return copied;
    }
    private static long DrawTile(SKCanvas canvas, SKRectI bounds, byte[] rgba)
    {
        using var srgb = SKColorSpace.CreateSrgb();
        using var image = SKImage.FromPixelCopy(new SKImageInfo(bounds.Width, bounds.Height,
            SKColorType.Rgba8888, SKAlphaType.Premul, srgb), rgba);
        // The native image owns its copy; the mutable stroke buffer is not lent to deferred drawing.
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src, FilterQuality = SKFilterQuality.None };
        canvas.DrawImage(image, bounds.Left, bounds.Top, paint);
        return rgba.Length;
    }
    public void Export(string path)
    {
        FullRasterExports++;
        using var srgb = SKColorSpace.CreateSrgb();
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
        surface.Canvas.Clear(SKColors.Transparent);
        Paint(surface.Canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        data.SaveTo(output);
    }
}
