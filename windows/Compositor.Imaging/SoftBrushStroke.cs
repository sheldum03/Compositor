using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using SkiaSharp;
using Compositor.Core;

namespace Compositor.Imaging;

public readonly record struct BrushPoint(double X, double Y);
public sealed record SoftBrushSettings(int Diameter, double Opacity, double[] Color, double Hardness = 0);

// Untransformed, unselected color painting only. CPU dabs follow Mac BrushStroke's event/tail rules.
public sealed class SoftBrushStroke
{
    private readonly TileRaster source;
    private readonly GrayTileRaster? selection;
    private readonly SoftBrushSettings settings;
    private readonly byte[] tip;
    private readonly byte[] coverageColors = new byte[256 * 4];
    private readonly Dictionary<int, byte[]> coverage = [];
    private readonly Dictionary<int, ReadOnlyMemory<byte>> original = [];
    private readonly Dictionary<int, byte[]> pixels = [];
    private readonly Dictionary<int, SKRectI> dirty = [];
    private readonly Dictionary<int, (byte[]? Pixels, SKRectI Bounds)> tail = [];
    private readonly Dictionary<int, byte[]> tailBackups = [];
    private readonly List<BrushPoint> samples = [];
    private BrushPoint? previous;
    private double distanceToNext;
    private bool finished;
    public int TouchedTiles => pixels.Count;

    public SoftBrushStroke(TileRaster source, SoftBrushSettings settings, GrayTileRaster? selection = null)
    {
        if (settings.Diameter is < 1 or > 2000 || !double.IsFinite(settings.Opacity) ||
            settings.Opacity is < 0.01 or > 1 || settings.Color.Length != 3 ||
            settings.Color.Any(c => !double.IsFinite(c) || c is < 0 or > 1) ||
            !double.IsFinite(settings.Hardness) || settings.Hardness is < 0 or > 1)
            throw new ArgumentException("Invalid soft brush settings");
        this.source = source;
        this.selection = selection;
        this.settings = settings with { Color = (double[])settings.Color.Clone() };
        // Coverage is one byte, so its premultiplied paint color has only 256 possible values.
        for (int coverageValue = 0; coverageValue < 256; coverageValue++)
        {
            int alpha = Round255(coverageValue * this.settings.Opacity);
            for (int c = 0; c < 3; c++)
                coverageColors[coverageValue * 4 + c] = Round255(this.settings.Color[c] * alpha);
            coverageColors[coverageValue * 4 + 3] = (byte)alpha;
        }
        // Match the 24-stop normalized Gaussian tip used by the CPU reference at zero hardness.
        tip = new byte[settings.Diameter * settings.Diameter];
        var stops = Enumerable.Range(0, 25).Select(i =>
            Math.Max(0, (Math.Exp(-2.5 * i * i / (24.0 * 24)) - Math.Exp(-2.5)) / (1 - Math.Exp(-2.5)))).ToArray();
        double radius = settings.Diameter / 2.0;
        for (int y = 0; y < settings.Diameter; y++)
        for (int x = 0; x < settings.Diameter; x++)
        {
            double u = Math.Sqrt(Math.Pow(x + 0.5 - radius, 2) + Math.Pow(y + 0.5 - radius, 2)) / radius;
            if (u >= 1) continue;
            double coverage = settings.Hardness >= 1
                ? 1
                : (stops[Math.Min(23, (int)(u * 24))] +
                    (stops[Math.Min(23, (int)(u * 24)) + 1] - stops[Math.Min(23, (int)(u * 24))]) *
                    (u * 24 - Math.Min(23, (int)(u * 24))));
            tip[y * settings.Diameter + x] = Round255(coverage * 255);
        }
    }

    public void Append(BrushPoint point)
    {
        EnsureActive();
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > 10_000_000 || Math.Abs(point.Y) > 10_000_000)
            throw new ArgumentException("Invalid pointer coordinate");
        if (samples.Count > 0 && samples[^1] == point) return;
        RemoveTail();
        samples.Add(point);
        if (samples.Count > 4) samples.RemoveAt(0);
        int count = samples.Count;
        if (count == 1) Walk(point);
        else if (count >= 3) Curve(samples[count - 3], samples[count - 2], samples[Math.Max(0, count - 4)], point);
        if (count >= 2) DrawTail(samples[count - 2], point);
        Publish();
    }
    public void Flush()
    {
        EnsureActive();
        RemoveTail();
        int count = samples.Count;
        if (count >= 2)
        {
            Curve(samples[count - 2], samples[^1], samples[Math.Max(0, count - 3)], samples[^1]);
            var last = samples[^1];
            samples.Clear();
            samples.Add(last);
        }
        Publish();
    }
    public TileRaster Commit()
    {
        Flush();
        var result = Snapshot();
        finished = true;
        return result;
    }
    public void Cancel() { EnsureActive(); finished = true; }
    public TileRaster Snapshot()
    {
        var result = source;
        foreach (var pair in pixels)
            result = result.ReplaceTile(pair.Key % Columns, pair.Key / Columns, pair.Value);
        return result;
    }

    private int Columns => (source.Width + TileRaster.TileSize - 1) / TileRaster.TileSize;
    private SKRectI Bounds(int key)
    {
        int column = key % Columns, row = key / Columns;
        var size = source.TileDimensions(column, row);
        int left = column * TileRaster.TileSize, top = row * TileRaster.TileSize;
        return new SKRectI(left, top, left + size.Width, top + size.Height);
    }

    private void EnsureActive()
    {
        if (finished) throw new InvalidOperationException("Stroke is already committed or canceled");
    }

    private IEnumerable<int> Keys(SKRectI bounds)
    {
        int left = Math.Max(0, bounds.Left), top = Math.Max(0, bounds.Top);
        int right = Math.Min(source.Width, bounds.Right), bottom = Math.Min(source.Height, bounds.Bottom);
        if (left >= right || top >= bottom) yield break;
        for (int y = top / TileRaster.TileSize; y <= (bottom - 1) / TileRaster.TileSize; y++)
        for (int x = left / TileRaster.TileSize; x <= (right - 1) / TileRaster.TileSize; x++)
            yield return y * Columns + x;
    }
    private void DrawTail(BrushPoint start, BrushPoint end)
    {
        double reach = settings.Diameter / 2.0 + 2;
        var bounds = new SKRectI((int)Math.Floor(Math.Min(start.X, end.X) - reach),
            (int)Math.Floor(Math.Min(start.Y, end.Y) - reach), (int)Math.Ceiling(Math.Max(start.X, end.X) + reach),
            (int)Math.Ceiling(Math.Max(start.Y, end.Y) + reach));
        foreach (int key in Keys(bounds))
        {
            byte[]? backup = null;
            if (coverage.TryGetValue(key, out var mask))
            {
                if (!tailBackups.TryGetValue(key, out backup)) tailBackups[key] = backup = new byte[mask.Length];
                mask.CopyTo(backup, 0);
            }
            tail[key] = (backup, SKRectI.Intersect(Bounds(key), bounds));
        }
        var saved = (previous, distanceToNext);
        Walk(end);
        (previous, distanceToNext) = saved;
    }
    private void RemoveTail()
    {
        foreach (var pair in tail)
        {
            if (!coverage.TryGetValue(pair.Key, out var mask)) continue;
            if (pair.Value.Pixels is { } backup) backup.CopyTo(mask, 0);
            else Array.Clear(mask);
            dirty[pair.Key] = pair.Value.Bounds;
        }
        tail.Clear();
    }
    private static double Distance(BrushPoint a, BrushPoint b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
    private void Curve(BrushPoint start, BrushPoint end, BrushPoint before, BrushPoint after)
    {
        static double Knot(double t, BrushPoint a, BrushPoint b) => t + Math.Max(0.0001, Math.Sqrt(Distance(a, b)));
        static BrushPoint Mix(BrushPoint a, BrushPoint b, double ta, double tb, double t) =>
            new(a.X * ((tb - t) / (tb - ta)) + b.X * ((t - ta) / (tb - ta)),
                a.Y * ((tb - t) / (tb - ta)) + b.Y * ((t - ta) / (tb - ta)));
        double t0 = 0, t1 = Knot(t0, before, start), t2 = Knot(t1, start, end), t3 = Knot(t2, end, after);
        int pieces = Math.Max(1, (int)Math.Ceiling(Distance(start, end) / 2));
        for (int index = 1; index <= pieces; index++)
        {
            double t = t1 + (t2 - t1) * index / pieces;
            var a = Mix(before, start, t0, t1, t);
            var b = Mix(start, end, t1, t2, t);
            var c = Mix(end, after, t2, t3, t);
            Walk(index == pieces ? end : Mix(Mix(a, b, t0, t2, t), Mix(b, c, t1, t3, t), t1, t2, t));
        }
    }
    private void Walk(BrushPoint point)
    {
        double spacing = Math.Max(0.25, settings.Diameter * 0.025);
        if (previous is { } start)
        {
            double length = Distance(start, point);
            if (length > 0)
            {
                double distance = distanceToNext;
                while (distance <= length)
                {
                    Dab(new BrushPoint(start.X + (point.X - start.X) * distance / length,
                        start.Y + (point.Y - start.Y) * distance / length));
                    distance += spacing;
                }
                distanceToNext = distance - length;
            }
        }
        else { Dab(point); distanceToNext = spacing; }
        previous = point;
    }
    private void Dab(BrushPoint point)
    {
        int left = (int)Math.Round(point.X - settings.Diameter / 2.0, MidpointRounding.AwayFromZero);
        int top = (int)Math.Round(point.Y - settings.Diameter / 2.0, MidpointRounding.AwayFromZero);
        var stamp = new SKRectI(left, top, left + settings.Diameter, top + settings.Diameter);
        foreach (int key in Keys(stamp))
        {
            var tile = Bounds(key);
            var touched = SKRectI.Intersect(stamp, tile);
            if (!coverage.TryGetValue(key, out var mask))
            {
                mask = new byte[tile.Width * tile.Height];
                coverage.Add(key, mask);
                byte[] baseline = source.ReadTileCopy(key % Columns, key / Columns);
                original.Add(key, baseline);
                pixels.Add(key, (byte[])baseline.Clone());
            }
            dirty[key] = dirty.TryGetValue(key, out var previousDirty) ? SKRectI.Union(previousDirty, touched) : touched;
            if (selection is not { })
            {
                for (int y = touched.Top; y < touched.Bottom; y++)
                    AccumulateCoverage(mask.AsSpan((y - tile.Top) * tile.Width + touched.Left - tile.Left, touched.Width),
                        tip.AsSpan((y - top) * settings.Diameter + touched.Left - left, touched.Width));
            }
            else
            {
                byte[] selected = selection.ReadTileCopy(key % Columns, key / Columns);
                for (int y = touched.Top; y < touched.Bottom; y++)
                for (int x = touched.Left; x < touched.Right; x++)
                {
                    int offset = (y - tile.Top) * tile.Width + x - tile.Left;
                    int stampOffset = (y - top) * settings.Diameter + x - left;
                    byte allowed = (byte)((tip[stampOffset] * selected[offset] + 127) / 255);
                    mask[offset] = (byte)(mask[offset] + (allowed * (255 - mask[offset]) + 127) / 255);
                }
            }
        }
    }
    internal static void AccumulateCoverage(Span<byte> mask, ReadOnlySpan<byte> stamp)
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var full = new Vector<ushort>(255);
            var rounding = new Vector<ushort>(127);
            for (; i <= mask.Length - Vector<byte>.Count; i += Vector<byte>.Count)
            {
                Vector.Widen(new Vector<byte>(mask.Slice(i)), out var low, out var high);
                Vector.Widen(new Vector<byte>(stamp.Slice(i)), out var tipLow, out var tipHigh);
                var nLow = tipLow * (full - low) + rounding;
                var nHigh = tipHigh * (full - high) + rounding;
                // Exact n / 255 for this range (127..65152), with no ushort overflow.
                low += (nLow + Vector<ushort>.One + (nLow >> 8)) >> 8;
                high += (nHigh + Vector<ushort>.One + (nHigh >> 8)) >> 8;
                Vector.Narrow(low, high).CopyTo(mask.Slice(i));
            }
        }
        for (; i < mask.Length; i++)
            mask[i] = (byte)(mask[i] + (stamp[i] * (255 - mask[i]) + 127) / 255);
    }
    private void Publish()
    {
        foreach (var pair in dirty)
        {
            var tile = Bounds(pair.Key);
            byte[] mask = coverage[pair.Key], result = pixels[pair.Key];
            var baselinePixels = MemoryMarshal.Cast<byte, uint>(original[pair.Key].Span);
            var resultPixels = MemoryMarshal.Cast<byte, uint>(result.AsSpan());
            var colors = MemoryMarshal.Cast<byte, uint>(coverageColors);
            for (int y = pair.Value.Top; y < pair.Value.Bottom; y++)
            for (int x = pair.Value.Left; x < pair.Value.Right; x++)
            {
                int offset = (y - tile.Top) * tile.Width + x - tile.Left;
                uint baselinePixel = baselinePixels.IsEmpty ? 0 : baselinePixels[offset];
                // An untouched transparent pixel has no destination contribution.
                if (baselinePixel == 0)
                {
                    resultPixels[offset] = colors[mask[offset]];
                    continue;
                }
                int colorOffset = mask[offset] * 4;
                resultPixels[offset] = BlendPixel(baselinePixel, colors[mask[offset]],
                    (uint)(255 - coverageColors[colorOffset + 3]));
            }
        }
        dirty.Clear();
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint BlendPixel(uint baseline, uint color, uint inverseAlpha)
    {
        // Two independent 16-bit lanes; each intermediate stays below 65536.
        const uint lanes = 0x00ff00ff;
        uint rb = (baseline & lanes) * inverseAlpha + 0x007f007f;
        uint ga = ((baseline >> 8) & lanes) * inverseAlpha + 0x007f007f;
        rb = ((rb + 0x00010001 + ((rb >> 8) & lanes)) >> 8) & lanes;
        ga = ((ga + 0x00010001 + ((ga >> 8) & lanes)) >> 8) & lanes;
        return color + (rb | (ga << 8));
    }
    private static byte Round255(double value) => (byte)Math.Clamp(Math.Floor(value + 0.5), 0, 255);
}
