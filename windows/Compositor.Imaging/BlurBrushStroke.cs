using Compositor.Core;

namespace Compositor.Imaging;

// Localized blur brush: the blur is computed from the immutable stroke source.
public sealed class BlurBrushStroke
{
    private readonly TileRaster source;
    private readonly TileRaster blurred;
    private readonly SoftBrushStroke coverage;
    private readonly double opacity;
    private bool finished;

    public BlurBrushStroke(TileRaster source, SoftBrushSettings settings, GrayTileRaster? selection = null)
    {
        this.source = source;
        opacity = settings.Opacity;
        int radius = Math.Clamp(settings.Diameter / 8, 1, 32);
        blurred = RasterCompositor.ApplyGaussianBlur(source, new GaussianBlurSettings(radius));
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
            byte[] destination = source.ReadTileCopy(tile.Column, tile.Row);
            byte[] replacement = blurred.ReadTileCopy(tile.Column, tile.Row);
            ReadOnlySpan<byte> mask = tile.Coverage.Span;
            for (int pixel = 0; pixel < mask.Length; pixel++)
            {
                int amount = Math.Clamp((int)Math.Round(mask[pixel] * opacity, MidpointRounding.AwayFromZero), 0, 255);
                if (amount == 0) continue;
                int offset = pixel * 4;
                int alpha = (replacement[offset + 3] * amount + 127) / 255;
                for (int channel = 0; channel < 4; channel++)
                    destination[offset + channel] = (byte)Math.Clamp(
                        (replacement[offset + channel] * amount + 127) / 255 +
                        (destination[offset + channel] * (255 - alpha) + 127) / 255, 0, 255);
            }
            result = result.ReplaceTile(tile.Column, tile.Row, destination);
        }
        return result;
    }

    private void EnsureActive()
    {
        if (finished) throw new InvalidOperationException("Blur stroke is already committed or canceled.");
    }
}
