using System.Runtime.InteropServices;
using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

// Same sRGB premultiplied Skia blend modes and byte opacity as the fixed M1 CPU path.
public static class LayerCompositor
{
    public static TileRaster Composite(TileRaster bottom, TileRaster top, double opacity, string mode)
    {
        if (bottom.Width != top.Width || bottom.Height != top.Height)
            throw new ArgumentException("Layer dimensions must match.");
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(opacity));
        SKBlendMode blend = mode switch
        {
            "Normal" => SKBlendMode.SrcOver, "Multiply" => SKBlendMode.Multiply,
            "Screen" => SKBlendMode.Screen, "Overlay" => SKBlendMode.Overlay,
            "Darken" => SKBlendMode.Darken, "Lighten" => SKBlendMode.Lighten,
            "Difference" => SKBlendMode.Difference, "Color Dodge" => SKBlendMode.ColorDodge,
            "Color Burn" => SKBlendMode.ColorBurn, "Hue" => SKBlendMode.Hue,
            "Saturation" => SKBlendMode.Saturation, "Color" => SKBlendMode.Color,
            "Luminosity" => SKBlendMode.Luminosity,
            _ => throw new ArgumentException("Unknown blend mode.", nameof(mode))
        };
        if (mode == "Normal")
            return RasterCompositor.SourceOver(bottom, opacity == 1 ? top : ScalePremultiplied(top, opacity));
        using var srgb = SKColorSpace.CreateSrgb();
        using var paint = new SKPaint { BlendMode = blend, FilterQuality = SKFilterQuality.None };
        paint.ColorF = new SKColorF(1, 1, 1, (float)opacity);
        var result = new TileRaster(bottom.Width, bottom.Height);
        for (int row = 0; row * TileRaster.TileSize < bottom.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < bottom.Width; column++)
        {
            var size = bottom.TileDimensions(column, row);
            var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb);
            using var lower = new SKBitmap(info);
            using var upper = new SKBitmap(info);
            CopyToBitmap(bottom.ReadTileCopy(column, row), lower);
            CopyToBitmap(top.ReadTileCopy(column, row), upper);
            using var canvas = new SKCanvas(lower);
            canvas.DrawBitmap(upper, 0, 0, paint);
            canvas.Flush();
            var pixels = new byte[size.Width * size.Height * 4];
            for (int y = 0; y < size.Height; y++)
                Marshal.Copy(lower.GetPixels() + y * lower.RowBytes, pixels, y * size.Width * 4, size.Width * 4);
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    private static void CopyToBitmap(byte[] pixels, SKBitmap bitmap)
    {
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i] > pixels[i + 3] || pixels[i + 1] > pixels[i + 3] || pixels[i + 2] > pixels[i + 3])
                throw new InvalidDataException("Layer contains invalid premultiplied RGBA.");
        for (int y = 0; y < bitmap.Height; y++)
            Marshal.Copy(pixels, y * bitmap.Width * 4, bitmap.GetPixels() + y * bitmap.RowBytes, bitmap.Width * 4);
    }

    private static TileRaster ScalePremultiplied(TileRaster source, double opacity)
    {
        var result = new TileRaster(source.Width, source.Height);
        for (int row = 0; row * TileRaster.TileSize < source.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < source.Width; column++)
        {
            byte[] tile = source.ReadTileCopy(column, row);
            for (int i = 0; i < tile.Length; i++)
                tile[i] = (byte)Math.Round(tile[i] * opacity, MidpointRounding.AwayFromZero);
            result = result.ReplaceTile(column, row, tile);
        }
        return result;
    }
}
