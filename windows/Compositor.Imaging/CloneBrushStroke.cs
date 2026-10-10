using Compositor.Core;

namespace Compositor.Imaging;

// Clone from the immutable layer snapshot; coverage follows the existing soft brush path.
public sealed class CloneBrushStroke
{
    private readonly TileRaster source;
    private readonly SoftBrushStroke coverage;
    private readonly double opacity;
    private readonly int sourceOffsetX, sourceOffsetY;
    private readonly Dictionary<int, byte[]> sourceTiles = [];
    private bool finished;

    public CloneBrushStroke(TileRaster source, SoftBrushSettings settings, BrushPoint sourcePoint,
        BrushPoint targetPoint, GrayTileRaster? selection = null)
    {
        if (!double.IsFinite(settings.Opacity) || settings.Opacity is < 0.01 or > 1 ||
            !double.IsFinite(sourcePoint.X) || !double.IsFinite(sourcePoint.Y) ||
            sourcePoint.X < 0 || sourcePoint.Y < 0 || sourcePoint.X >= source.Width || sourcePoint.Y >= source.Height ||
            !double.IsFinite(targetPoint.X) || !double.IsFinite(targetPoint.Y) ||
            Math.Abs(targetPoint.X) > 10_000_000 || Math.Abs(targetPoint.Y) > 10_000_000)
            throw new ArgumentException("Invalid clone brush source or settings.");
        if (selection is not null && (selection.Width != source.Width || selection.Height != source.Height))
            throw new ArgumentException("Clone selection dimensions must match the source.", nameof(selection));
        this.source = source;
        opacity = settings.Opacity;
        sourceOffsetX = (int)Math.Round(sourcePoint.X - targetPoint.X, MidpointRounding.AwayFromZero);
        sourceOffsetY = (int)Math.Round(sourcePoint.Y - targetPoint.Y, MidpointRounding.AwayFromZero);
        coverage = new SoftBrushStroke(new TileRaster(source.Width, source.Height),
            settings with { Color = [1, 1, 1], Opacity = 1 }, selection);
    }

    public void Append(BrushPoint point)
    {
        EnsureActive();
        coverage.Append(point);
    }

    public TileRaster Commit()
    {
        EnsureActive();
        coverage.Flush();
        TileRaster result = Snapshot();
        finished = true;
        return result;
    }

    public void Cancel()
    {
        EnsureActive();
        coverage.Cancel();
        finished = true;
    }

    public TileRaster Snapshot()
    {
        coverage.Flush();
        TileRaster result = source;
        foreach (var tile in coverage.CoverageTiles)
        {
            var size = source.TileDimensions(tile.Column, tile.Row);
            byte[] pixels = source.ReadTileCopy(tile.Column, tile.Row);
            ReadOnlySpan<byte> mask = tile.Coverage.Span;
            for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
            {
                int index = y * size.Width + x;
                int amount = Math.Clamp((int)Math.Round(mask[index] * opacity, MidpointRounding.AwayFromZero), 0, 255);
                if (amount == 0) continue;
                int sourceX = tile.Column * TileRaster.TileSize + x + sourceOffsetX;
                int sourceY = tile.Row * TileRaster.TileSize + y + sourceOffsetY;
                if ((uint)sourceX >= (uint)source.Width || (uint)sourceY >= (uint)source.Height) continue;
                int column = sourceX / TileRaster.TileSize, row = sourceY / TileRaster.TileSize;
                int key = row * Columns + column;
                if (!sourceTiles.TryGetValue(key, out byte[]? sampled))
                    sourceTiles[key] = sampled = source.ReadTileCopy(column, row);
                var sourceSize = source.TileDimensions(column, row);
                int sampledOffset = ((sourceY - row * TileRaster.TileSize) * sourceSize.Width +
                    sourceX - column * TileRaster.TileSize) * 4;
                int alpha = (sampled[sampledOffset + 3] * amount + 127) / 255;
                int destinationOffset = index * 4;
                for (int channel = 0; channel < 4; channel++)
                    pixels[destinationOffset + channel] = (byte)Math.Clamp(
                        (sampled[sampledOffset + channel] * amount + 127) / 255 +
                        (pixels[destinationOffset + channel] * (255 - alpha) + 127) / 255, 0, 255);
            }
            result = result.ReplaceTile(tile.Column, tile.Row, pixels);
        }
        return result;
    }

    private int Columns => (source.Width + TileRaster.TileSize - 1) / TileRaster.TileSize;

    private void EnsureActive()
    {
        if (finished) throw new InvalidOperationException("Clone stroke is already committed or canceled.");
    }
}
