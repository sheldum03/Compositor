using Compositor.Core;

namespace Compositor.Imaging;

public sealed class SpotHealingBrushStroke
{
    private readonly TileRaster source;
    private readonly SoftBrushStroke coverage;
    private bool finished;

    public SpotHealingBrushStroke(TileRaster source, SoftBrushSettings settings, GrayTileRaster? selection = null)
    {
        if (settings.Diameter is < 1 or > 2000 || !double.IsFinite(settings.Opacity) ||
            settings.Opacity is < 0.01 or > 1)
            throw new ArgumentException("Invalid healing brush settings.");
        if (selection is not null && (selection.Width != source.Width || selection.Height != source.Height))
            throw new ArgumentException("Healing selection dimensions must match the source.", nameof(selection));
        this.source = source;
        coverage = new SoftBrushStroke(source, settings with { Color = [1, 1, 1], Opacity = settings.Opacity }, selection);
    }

    public void Append(BrushPoint point)
    {
        EnsureActive();
        coverage.Append(point);
    }

    public TileRaster Snapshot()
    {
        EnsureActive();
        return RasterCompositor.ApplyContentFill(source, coverage.CoverageSnapshot());
    }

    public TileRaster Commit()
    {
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

    private void EnsureActive()
    {
        if (finished) throw new InvalidOperationException("Healing stroke is already committed or canceled.");
    }
}
