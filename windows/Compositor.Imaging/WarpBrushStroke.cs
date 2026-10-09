using Compositor.Core;

namespace Compositor.Imaging;

public enum WarpBrushMode { Smudge, Liquify }

public sealed class WarpBrushStroke
{
    private readonly TileRaster source;
    private readonly GrayTileRaster? selection;
    private readonly SoftBrushSettings settings;
    private readonly WarpBrushMode mode;
    private readonly Dictionary<int, byte[]> pixels = [];
    private readonly Dictionary<int, byte[]> selectionTiles = [];
    private float[] carried = [];
    private BrushPoint? last;
    private bool finished;
    private int Columns => (source.Width + TileRaster.TileSize - 1) / TileRaster.TileSize;
    private int Radius => (int)Math.Ceiling(settings.Diameter / 2d);

    public WarpBrushStroke(TileRaster source, SoftBrushSettings settings, WarpBrushMode mode, GrayTileRaster? selection = null)
    {
        if (settings.Diameter is < 1 or > 2000 || !double.IsFinite(settings.Opacity) || settings.Opacity is < 0.01 or > 1 ||
            !double.IsFinite(settings.Hardness) || settings.Hardness is < 0 or > 1 || !Enum.IsDefined(mode) ||
            selection is not null && (selection.Width != source.Width || selection.Height != source.Height))
            throw new ArgumentException("Invalid warp brush settings.");
        this.source = source;
        this.selection = selection;
        this.settings = settings with { Diameter = Math.Max(2, settings.Diameter), Hardness = Math.Min(0.98, settings.Hardness) };
        this.mode = mode;
    }

    public void Append(BrushPoint point)
    {
        EnsureActive();
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Pressure) ||
            point.Pressure is < 0 or > 1 || Math.Abs(point.X) > 10000000 || Math.Abs(point.Y) > 10000000)
            throw new ArgumentException("Invalid pointer coordinate.");
        if (last is not { } from)
        {
            last = point;
            if (mode == WarpBrushMode.Smudge) PickUp(point);
            return;
        }
        double distance = Math.Sqrt(Math.Pow(point.X - from.X, 2) + Math.Pow(point.Y - from.Y, 2));
        double spacing = Math.Max(1, settings.Diameter * (mode == WarpBrushMode.Smudge ? 0.08 : 0.025));
        if (distance < spacing) return;
        int steps = (int)Math.Ceiling(distance / spacing);
        BrushPoint previous = from;
        for (int step = 1; step <= steps; step++)
        {
            double amount = step / (double)steps;
            var next = new BrushPoint(from.X + (point.X - from.X) * amount,
                from.Y + (point.Y - from.Y) * amount, from.Pressure + (point.Pressure - from.Pressure) * amount);
            if (mode == WarpBrushMode.Smudge) Smudge(next);
            else Push(previous, next);
            previous = next;
        }
        last = point;
    }

    public TileRaster Snapshot()
    {
        EnsureActive();
        TileRaster result = source;
        foreach (var tile in pixels) result = result.ReplaceTile(tile.Key % Columns, tile.Key / Columns, tile.Value);
        return result;
    }

    public TileRaster Commit()
    {
        TileRaster result = Snapshot();
        finished = true;
        return result;
    }

    public void Cancel() { EnsureActive(); finished = true; }

    private byte[] Tile(int x, int y, out int offset)
    {
        int column = x / TileRaster.TileSize, row = y / TileRaster.TileSize, key = row * Columns + column;
        if (!pixels.TryGetValue(key, out byte[]? tile)) pixels[key] = tile = source.ReadTileCopy(column, row);
        var size = source.TileDimensions(column, row);
        offset = ((y % TileRaster.TileSize) * size.Width + x % TileRaster.TileSize) * 4;
        return tile;
    }

    private double Weight(int x, int y, int dx, int dy, double pressure)
    {
        double u = Math.Sqrt((double)dx * dx + (double)dy * dy) / (settings.Diameter / 2d);
        if (u >= 1) return 0;
        double t = u <= settings.Hardness ? 1 : (1 - u) / (1 - settings.Hardness);
        double weight = t * t * (3 - 2 * t) * pressure;
        if (selection is null) return weight;
        int column = x / TileRaster.TileSize, row = y / TileRaster.TileSize, key = row * Columns + column;
        if (!selectionTiles.TryGetValue(key, out byte[]? tile)) selectionTiles[key] = tile = selection.ReadTileCopy(column, row);
        var size = selection.TileDimensions(column, row);
        return weight * tile[(y % TileRaster.TileSize) * size.Width + x % TileRaster.TileSize] / 255d;
    }

    private void PickUp(BrushPoint center)
    {
        int radius = Radius, side = 2 * radius + 1;
        carried = new float[side * side * 4];
        int cx = (int)Math.Round(center.X, MidpointRounding.AwayFromZero), cy = (int)Math.Round(center.Y, MidpointRounding.AwayFromZero);
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int x = cx + dx, y = cy + dy;
            if ((uint)x >= (uint)source.Width || (uint)y >= (uint)source.Height) continue;
            byte[] tile = Tile(x, y, out int offset);
            int carriedOffset = ((dy + radius) * side + dx + radius) * 4;
            for (int channel = 0; channel < 4; channel++) carried[carriedOffset + channel] = tile[offset + channel];
        }
    }

    private void Smudge(BrushPoint center)
    {
        int radius = Radius, side = 2 * radius + 1;
        int cx = (int)Math.Round(center.X, MidpointRounding.AwayFromZero), cy = (int)Math.Round(center.Y, MidpointRounding.AwayFromZero);
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int x = cx + dx, y = cy + dy;
            if ((uint)x >= (uint)source.Width || (uint)y >= (uint)source.Height) continue;
            double weight = Weight(x, y, dx, dy, center.Pressure);
            if (weight == 0) continue;
            byte[] tile = Tile(x, y, out int offset);
            int carriedOffset = ((dy + radius) * side + dx + radius) * 4;
            for (int channel = 0; channel < 4; channel++)
            {
                double under = tile[offset + channel];
                double painted = under + (carried[carriedOffset + channel] - under) * weight;
                tile[offset + channel] = (byte)Math.Clamp(Math.Round(painted, MidpointRounding.AwayFromZero), 0, 255);
                carried[carriedOffset + channel] = (float)(painted + (carried[carriedOffset + channel] - painted) * settings.Opacity);
            }
        }
    }

    private void Push(BrushPoint from, BrushPoint to)
    {
        int radius = Radius;
        double moveX = (to.X - from.X) * settings.Opacity, moveY = (to.Y - from.Y) * settings.Opacity;
        int margin = (int)Math.Ceiling(Math.Max(Math.Abs(moveX), Math.Abs(moveY))) + 2;
        int cx = (int)Math.Round(to.X, MidpointRounding.AwayFromZero), cy = (int)Math.Round(to.Y, MidpointRounding.AwayFromZero);
        int left = Math.Max(0, cx - radius - margin), right = Math.Min(source.Width - 1, cx + radius + margin);
        int top = Math.Max(0, cy - radius - margin), bottom = Math.Min(source.Height - 1, cy + radius + margin);
        if (left > right || top > bottom) return;
        int width = right - left + 1, height = bottom - top + 1;
        byte[] scratch = new byte[width * height * 4];
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            byte[] tile = Tile(x, y, out int offset);
            tile.AsSpan(offset, 4).CopyTo(scratch.AsSpan(((y - top) * width + x - left) * 4, 4));
        }
        for (int y = Math.Max(top, cy - radius); y <= Math.Min(bottom, cy + radius); y++)
        for (int x = Math.Max(left, cx - radius); x <= Math.Min(right, cx + radius); x++)
        {
            double weight = Weight(x, y, x - cx, y - cy, to.Pressure);
            if (weight == 0) continue;
            double sx = Math.Clamp(x - left - moveX * weight, 0, width - 1);
            double sy = Math.Clamp(y - top - moveY * weight, 0, height - 1);
            int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(width - 1, x0 + 1), y1 = Math.Min(height - 1, y0 + 1);
            double fx = sx - x0, fy = sy - y0;
            byte[] tile = Tile(x, y, out int offset);
            for (int channel = 0; channel < 4; channel++)
            {
                double upper = scratch[(y0 * width + x0) * 4 + channel] * (1 - fx) + scratch[(y0 * width + x1) * 4 + channel] * fx;
                double lower = scratch[(y1 * width + x0) * 4 + channel] * (1 - fx) + scratch[(y1 * width + x1) * 4 + channel] * fx;
                tile[offset + channel] = (byte)Math.Clamp(Math.Round(upper * (1 - fy) + lower * fy, MidpointRounding.AwayFromZero), 0, 255);
            }
        }
    }

    private void EnsureActive()
    {
        if (finished) throw new InvalidOperationException("Warp stroke is already committed or canceled.");
    }
}
