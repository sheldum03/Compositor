using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

// The editor's pixel boundary is 8-bit premultiplied RGBA in sRGB.
public static class ImageCodec
{
    public static unsafe TileRaster Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 512L * 1024 * 1024)
            throw new InvalidDataException("Image exceeds 512 MiB.");
        using var codec = SKCodec.Create(stream)
            ?? throw new InvalidDataException("Cannot decode image.");
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg))
            throw new NotSupportedException("Only PNG and JPEG are supported.");
        int width = codec.Info.Width, height = codec.Info.Height;
        CheckDimensions(width, height);
        using var srgb = SKColorSpace.CreateSrgb();
        using var bitmap = new SKBitmap(new SKImageInfo(width, height,
            SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, SKCodecOptions.Default);
        if (result != SKCodecResult.Success)
            throw new InvalidDataException($"Image decoding failed: {result}.");

        int orientation = (int)codec.EncodedOrigin;
        if (orientation is < 1 or > 8) throw new InvalidDataException("Invalid image orientation.");
        var raster = new TileRaster(orientation >= 5 ? height : width, orientation >= 5 ? width : height);
        var pixels = (byte*)bitmap.GetPixels();
        for (int row = 0; row * TileRaster.TileSize < raster.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < raster.Width; column++)
        {
            var size = raster.TileDimensions(column, row);
            var tile = new byte[size.Width * size.Height * 4];
            for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
            {
                int dx = column * TileRaster.TileSize + x, dy = row * TileRaster.TileSize + y;
                var (sx, sy) = orientation switch
                {
                    2 => (width - 1 - dx, dy),
                    3 => (width - 1 - dx, height - 1 - dy),
                    4 => (dx, height - 1 - dy),
                    5 => (dy, dx),
                    6 => (dy, height - 1 - dx),
                    7 => (width - 1 - dy, height - 1 - dx),
                    8 => (width - 1 - dy, dx),
                    _ => (dx, dy)
                };
                new ReadOnlySpan<byte>(pixels + sy * bitmap.RowBytes + sx * 4, 4)
                    .CopyTo(tile.AsSpan((y * size.Width + x) * 4, 4));
            }
            raster = raster.ReplaceTile(column, row, tile);
        }
        return raster;
    }

    public static void SavePng(TileRaster raster, string output) =>
        Save(raster, output, SKEncodedImageFormat.Png, 100, null);

    // JPEG has no alpha; callers must explicitly choose an opaque background.
    public static void SaveJpeg(TileRaster raster, string output, int quality,
        (byte R, byte G, byte B) background)
    {
        if (quality is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(quality));
        Save(raster, output, SKEncodedImageFormat.Jpeg, quality, background);
    }

    private static unsafe void Save(TileRaster raster, string output, SKEncodedImageFormat format,
        int quality, (byte R, byte G, byte B)? background)
    {
        CheckDimensions(raster.Width, raster.Height);
        using var srgb = SKColorSpace.CreateSrgb();
        using var bitmap = new SKBitmap(new SKImageInfo(raster.Width, raster.Height,
            SKColorType.Rgba8888, background.HasValue ? SKAlphaType.Opaque : SKAlphaType.Premul, srgb));
        var pixels = (byte*)bitmap.GetPixels();
        for (int row = 0; row * TileRaster.TileSize < raster.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < raster.Width; column++)
        {
            var size = raster.TileDimensions(column, row);
            var tile = raster.ReadTileCopy(column, row);
            for (int i = 0; i < tile.Length; i += 4)
            {
                int alpha = tile[i + 3];
                if (tile[i] > alpha || tile[i + 1] > alpha || tile[i + 2] > alpha)
                    throw new InvalidDataException("Invalid premultiplied pixel.");
                if (background is { } color)
                {
                    tile[i] += (byte)((color.R * (255 - alpha) + 127) / 255);
                    tile[i + 1] += (byte)((color.G * (255 - alpha) + 127) / 255);
                    tile[i + 2] += (byte)((color.B * (255 - alpha) + 127) / 255);
                    tile[i + 3] = 255;
                }
            }
            for (int y = 0; y < size.Height; y++)
                tile.AsSpan(y * size.Width * 4, size.Width * 4).CopyTo(new Span<byte>(
                    pixels + (row * TileRaster.TileSize + y) * bitmap.RowBytes + column * TileRaster.TileSize * 4,
                    size.Width * 4));
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality)
            ?? throw new IOException("Image encoding failed.");
        string destination = Path.GetFullPath(output);
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                data.SaveTo(stream);
                stream.Flush(flushToDisk: true);
            }
            // The caller must handle overwrite explicitly; never truncate an existing file here.
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void CheckDimensions(int width, int height)
    {
        if (width is < 1 or > 30000 || height is < 1 or > 30000 || (long)width * height > 100_000_000)
            throw new InvalidDataException("Invalid image dimensions.");
    }
}
