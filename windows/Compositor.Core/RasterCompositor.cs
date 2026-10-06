namespace Compositor.Core;

public static class RasterCompositor
{
    public static TileRaster ApplyExposure(TileRaster image, ExposureSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        double scale = Math.Pow(2, settings.Exposure);
        var table = new byte[256];
        for (int index = 0; index < table.Length; index++)
        {
            double encoded = index / 255d;
            double linear = encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);
            linear = Math.Pow(Math.Max(0, linear * scale + settings.Offset), 1 / settings.Gamma);
            double output = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
            table[index] = (byte)Math.Round(Math.Clamp(output, 0, 1) * 255, MidpointRounding.AwayFromZero);
        }
        var result = new TileRaster(image.Width, image.Height);
        for (int row = 0; row * TileRaster.TileSize < image.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < image.Width; column++)
        {
            byte[] pixels = image.ReadTileCopy(column, row);
            for (int pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                int alpha = pixels[pixel + 3];
                if (alpha == 0) continue;
                for (int channel = 0; channel < 3; channel++)
                {
                    double straight = pixels[pixel + channel] * 255d / alpha;
                    int low = Math.Clamp((int)straight, 0, 255);
                    int high = Math.Min(255, low + 1);
                    double mapped = table[low] + (table[high] - table[low]) * (straight - low);
                    pixels[pixel + channel] = (byte)Math.Clamp(
                        Math.Round(mapped * alpha / 255d, MidpointRounding.AwayFromZero), 0, alpha);
                }
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    public static TileRaster ApplyMask(TileRaster image, GrayTileRaster mask)
    {
        if (image.Width != mask.Width || image.Height != mask.Height)
            throw new ArgumentException("Image and mask dimensions must match.");
        var result = new TileRaster(image.Width, image.Height);
        for (int row = 0; row * TileRaster.TileSize < image.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < image.Width; column++)
        {
            byte[] pixels = image.ReadTileCopy(column, row);
            byte[] coverage = mask.ReadTileCopy(column, row);
            for (int pixel = 0; pixel < coverage.Length; pixel++)
            {
                int i = pixel * 4, alpha = pixels[i + 3];
                if (pixels[i] > alpha || pixels[i + 1] > alpha || pixels[i + 2] > alpha)
                    throw new InvalidDataException("Layer contains invalid premultiplied RGBA.");
                for (int channel = 0; channel < 4; channel++)
                    pixels[i + channel] = (byte)((pixels[i + channel] * coverage[pixel] + 127) / 255);
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    public static TileRaster ApplyAlphaMask(TileRaster image, TileRaster source, double sourceOpacity = 1)
    {
        if (image.Width != source.Width || image.Height != source.Height)
            throw new ArgumentException("Image and source dimensions must match.");
        if (!double.IsFinite(sourceOpacity) || sourceOpacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(sourceOpacity));
        var result = new TileRaster(image.Width, image.Height);
        for (int row = 0; row * TileRaster.TileSize < image.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < image.Width; column++)
        {
            byte[] pixels = image.ReadTileCopy(column, row);
            byte[] sourcePixels = source.ReadTileCopy(column, row);
            for (int pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                int alpha = (int)Math.Round(sourcePixels[pixel + 3] * sourceOpacity, MidpointRounding.AwayFromZero);
                if (pixels[pixel] > pixels[pixel + 3] || pixels[pixel + 1] > pixels[pixel + 3] || pixels[pixel + 2] > pixels[pixel + 3] ||
                    sourcePixels[pixel] > sourcePixels[pixel + 3] || sourcePixels[pixel + 1] > sourcePixels[pixel + 3] || sourcePixels[pixel + 2] > sourcePixels[pixel + 3])
                    throw new InvalidDataException("Layer contains invalid premultiplied RGBA.");
                for (int channel = 0; channel < 4; channel++)
                    pixels[pixel + channel] = (byte)((pixels[pixel + channel] * alpha + 127) / 255);
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    public static TileRaster SourceOver(TileRaster bottom, TileRaster top)
    {
        if (bottom.Width != top.Width || bottom.Height != top.Height)
            throw new ArgumentException("Layer dimensions must match.");
        var result = new TileRaster(bottom.Width, bottom.Height);
        for (int row = 0; row * TileRaster.TileSize < bottom.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < bottom.Width; column++)
        {
            byte[] lower = bottom.ReadTileCopy(column, row);
            byte[] upper = top.ReadTileCopy(column, row);
            for (int i = 0; i < lower.Length; i += 4)
            {
                int bottomAlpha = lower[i + 3], topAlpha = upper[i + 3];
                if (lower[i] > bottomAlpha || lower[i + 1] > bottomAlpha || lower[i + 2] > bottomAlpha ||
                    upper[i] > topAlpha || upper[i + 1] > topAlpha || upper[i + 2] > topAlpha)
                    throw new InvalidDataException("Layer contains invalid premultiplied RGBA.");
                int inverse = 255 - topAlpha;
                for (int channel = 0; channel < 3; channel++)
                    lower[i + channel] = (byte)(upper[i + channel] + (lower[i + channel] * inverse + 127) / 255);
                lower[i + 3] = (byte)(topAlpha + (bottomAlpha * inverse + 127) / 255);
            }
            result = result.ReplaceTile(column, row, lower);
        }
        return result;
    }
}
