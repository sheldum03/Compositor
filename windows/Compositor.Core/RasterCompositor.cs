namespace Compositor.Core;

public static class RasterCompositor
{
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
