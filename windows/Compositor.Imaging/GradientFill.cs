using Compositor.Core;

namespace Compositor.Imaging;

public sealed record GradientFillSettings(double[] Foreground, double[] Background,
    bool Radial = false, bool ToTransparent = true, bool Reversed = false, double Opacity = 1);

public static class GradientFill
{
    public static TileRaster Apply(TileRaster source, GrayTileRaster? selection, BrushPoint start, BrushPoint end,
        GradientFillSettings settings, LayerTransformInfo transform, int canvasWidth, int canvasHeight)
    {
        var result = source;
        foreach (var tile in Paint(source.Width, source.Height, source.ReadTileCopy, false, selection,
            start, end, settings, transform, canvasWidth, canvasHeight))
            result = result.ReplaceTile(tile.Column, tile.Row, tile.Pixels);
        return result;
    }

    public static GrayTileRaster ApplyMask(GrayTileRaster source, GrayTileRaster? selection, BrushPoint start, BrushPoint end,
        GradientFillSettings settings, LayerTransformInfo transform, int canvasWidth, int canvasHeight)
    {
        var result = source;
        foreach (var tile in Paint(source.Width, source.Height, source.ReadTileCopy, true, selection,
            start, end, settings, transform, canvasWidth, canvasHeight))
            result = result.ReplaceTile(tile.Column, tile.Row, tile.Pixels);
        return result;
    }

    private static IEnumerable<(int Column, int Row, byte[] Pixels)> Paint(int width, int height,
        Func<int, int, byte[]> readTile, bool mask, GrayTileRaster? selection, BrushPoint start, BrushPoint end,
        GradientFillSettings settings, LayerTransformInfo transform, int canvasWidth, int canvasHeight)
    {
        if (settings.Foreground.Length != 3 || settings.Background.Length != 3 ||
            settings.Foreground.Concat(settings.Background).Any(value => !double.IsFinite(value) || value is < 0 or > 1) ||
            !double.IsFinite(settings.Opacity) || settings.Opacity is < 0.01 or > 1)
            throw new ArgumentException("Invalid gradient settings.", nameof(settings));
        if (!double.IsFinite(start.X) || !double.IsFinite(start.Y) || !double.IsFinite(end.X) || !double.IsFinite(end.Y))
            throw new ArgumentException("Invalid gradient endpoints.");
        if (selection is not null && (selection.Width != width || selection.Height != height))
            throw new ArgumentException("Gradient selection dimensions do not match the source.", nameof(selection));
        double dx = end.X - start.X, dy = end.Y - start.Y, lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 0.25) yield break;
        double length = Math.Sqrt(lengthSquared);
        double radians = transform.Rotation * Math.PI / 180, cos = Math.Cos(radians), sin = Math.Sin(radians);
        double scaleX = transform.Width / width * (transform.FlipX ? -1 : 1);
        double scaleY = transform.Height / height * (transform.FlipY ? -1 : 1);
        int channels = mask ? 1 : 4;
        for (int row = 0; row * TileRaster.TileSize < height; row++)
        for (int column = 0; column * TileRaster.TileSize < width; column++)
        {
            int tileWidth = Math.Min(TileRaster.TileSize, width - column * TileRaster.TileSize);
            int tileHeight = Math.Min(TileRaster.TileSize, height - row * TileRaster.TileSize);
            byte[] tile = readTile(column, row);
            byte[]? coverage = selection?.ReadTileCopy(column, row);
            bool changed = false;
            for (int y = 0; y < tileHeight; y++)
            for (int x = 0; x < tileWidth; x++)
            {
                int pixel = y * tileWidth + x, offset = pixel * channels;
                if (coverage is not null && coverage[pixel] == 0) continue;
                double localX = (column * TileRaster.TileSize + x + 0.5 - width / 2d) * scaleX;
                double localY = (row * TileRaster.TileSize + y + 0.5 - height / 2d) * scaleY;
                double documentX = transform.X + transform.Width / 2 + localX * cos - localY * sin;
                double documentY = transform.Y + transform.Height / 2 + localX * sin + localY * cos;
                if (documentX < 0 || documentY < 0 || documentX >= canvasWidth || documentY >= canvasHeight) continue;
                double fromX = documentX - start.X, fromY = documentY - start.Y;
                double amount = Math.Clamp(settings.Radial ? Math.Sqrt(fromX * fromX + fromY * fromY) / length
                    : (fromX * dx + fromY * dy) / lengthSquared, 0, 1);
                if (settings.Reversed) amount = 1 - amount;
                double alpha = settings.Opacity * (settings.ToTransparent ? 1 - amount : 1) *
                    (coverage is null ? 1 : coverage[pixel] / 255d);
                if (alpha == 0) continue;
                for (int channel = 0; channel < (mask ? 1 : 3); channel++)
                {
                    double value = settings.Foreground[channel];
                    if (!settings.ToTransparent) value += (settings.Background[channel] - value) * amount;
                    byte next = ToByte(value * alpha * 255 + tile[offset + channel] * (1 - alpha));
                    changed |= next != tile[offset + channel];
                    tile[offset + channel] = next;
                }
                if (!mask)
                {
                    byte next = ToByte(alpha * 255 + tile[offset + 3] * (1 - alpha));
                    changed |= next != tile[offset + 3];
                    tile[offset + 3] = next;
                }
            }
            if (changed) yield return (column, row, tile);
        }
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}
