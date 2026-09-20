using SkiaSharp;

internal readonly record struct BrushPoint(double X, double Y);
internal sealed record SoftBrushSettings(int Diameter, double Opacity, double[] Color);

// Untransformed, unselected color painting only. CPU dabs follow Mac BrushStroke's event/tail rules.
internal sealed class SoftBrushStroke
{
    private readonly TiledRaster source;
    private readonly SoftBrushSettings settings;
    private readonly byte[] tip;
    private readonly Dictionary<int, byte[]> coverage = [];
    private readonly Dictionary<int, byte[]> original = [];
    private readonly Dictionary<int, byte[]> pixels = [];
    private readonly Dictionary<int, SKRectI> dirty = [];
    private readonly Dictionary<int, byte[]?> tail = [];
    private readonly List<BrushPoint> samples = [];
    private BrushPoint? previous;
    private double distanceToNext;
    private bool finished;
    public int TouchedTiles => pixels.Count;
    public long PublishedPixelBytes { get; private set; }
    public long TailBackupBytes { get; private set; }
    public long CommitCopiedBytes { get; private set; }

    public SoftBrushStroke(TiledRaster source, SoftBrushSettings settings)
    {
        if (settings.Diameter is < 1 or > 2000 || !double.IsFinite(settings.Opacity) ||
            settings.Opacity is < 0.01 or > 1 || settings.Color.Length != 3 ||
            settings.Color.Any(c => !double.IsFinite(c) || c is < 0 or > 1))
            throw new ArgumentException("Invalid soft brush settings");
        this.source = source;
        this.settings = settings with { Color = (double[])settings.Color.Clone() };
        // Match the 24-stop normalized Gaussian tip used by the CPU reference; hardness is zero.
        tip = new byte[settings.Diameter * settings.Diameter];
        var stops = Enumerable.Range(0, 25).Select(i =>
            Math.Max(0, (Math.Exp(-2.5 * i * i / (24.0 * 24)) - Math.Exp(-2.5)) / (1 - Math.Exp(-2.5)))).ToArray();
        double radius = settings.Diameter / 2.0;
        for (int y = 0; y < settings.Diameter; y++)
        for (int x = 0; x < settings.Diameter; x++)
        {
            double u = Math.Sqrt(Math.Pow(x + 0.5 - radius, 2) + Math.Pow(y + 0.5 - radius, 2)) / radius;
            if (u >= 1) continue;
            int stop = Math.Min(23, (int)(u * 24));
            tip[y * settings.Diameter + x] = Round255((stops[stop] + (stops[stop + 1] - stops[stop]) * (u * 24 - stop)) * 255);
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
    public TiledRaster Commit()
    {
        Flush();
        var result = source.Replacing(pixels);
        CommitCopiedBytes = pixels.Values.Sum(p => (long)p.Length);
        finished = true;
        return result;
    }
    public void Cancel() { EnsureActive(); finished = true; }
    public long Paint(SKCanvas canvas) => source.Paint(canvas, pixels);
    // Test oracle for provisional-tail replacement: render the known complete path without any tails.
    internal static TiledRaster ReplaySettled(TiledRaster source, SoftBrushSettings settings, BrushPoint[] points)
    {
        var stroke = new SoftBrushStroke(source, settings);
        if (points.Length > 0) stroke.Walk(points[0]);
        for (int i = 0; i + 1 < points.Length; i++)
            stroke.Curve(points[i], points[i + 1], points[Math.Max(0, i - 1)], points[Math.Min(points.Length - 1, i + 2)]);
        stroke.Publish();
        return source.Replacing(stroke.pixels);
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
        for (int y = top / TiledRaster.TileSize; y <= (bottom - 1) / TiledRaster.TileSize; y++)
        for (int x = left / TiledRaster.TileSize; x <= (right - 1) / TiledRaster.TileSize; x++)
            yield return y * source.Columns + x;
    }
    private void DrawTail(BrushPoint start, BrushPoint end)
    {
        double reach = settings.Diameter / 2.0 + 2;
        var bounds = new SKRectI((int)Math.Floor(Math.Min(start.X, end.X) - reach),
            (int)Math.Floor(Math.Min(start.Y, end.Y) - reach), (int)Math.Ceiling(Math.Max(start.X, end.X) + reach),
            (int)Math.Ceiling(Math.Max(start.Y, end.Y) + reach));
        foreach (int key in Keys(bounds))
        {
            tail[key] = coverage.TryGetValue(key, out var mask) ? (byte[])mask.Clone() : null;
            TailBackupBytes += tail[key]?.Length ?? 0;
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
            if (pair.Value is { } backup) backup.CopyTo(mask, 0);
            else Array.Clear(mask);
            dirty[pair.Key] = source.Bounds(pair.Key);
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
            var tile = source.Bounds(key);
            var touched = SKRectI.Intersect(stamp, tile);
            if (!coverage.TryGetValue(key, out var mask))
            {
                mask = new byte[tile.Width * tile.Height];
                coverage.Add(key, mask);
                original.Add(key, source.CopyTile(key));
                pixels.Add(key, (byte[])original[key].Clone());
            }
            dirty[key] = dirty.TryGetValue(key, out var previousDirty) ? SKRectI.Union(previousDirty, touched) : touched;
            for (int y = touched.Top; y < touched.Bottom; y++)
            for (int x = touched.Left; x < touched.Right; x++)
            {
                int offset = (y - tile.Top) * tile.Width + x - tile.Left;
                int deposited = tip[(y - top) * settings.Diameter + x - left];
                mask[offset] = (byte)(mask[offset] + (deposited * (255 - mask[offset]) + 127) / 255);
            }
        }
    }
    private void Publish()
    {
        foreach (var pair in dirty)
        {
            var tile = source.Bounds(pair.Key);
            byte[] mask = coverage[pair.Key], baseline = original[pair.Key], result = pixels[pair.Key];
            for (int y = pair.Value.Top; y < pair.Value.Bottom; y++)
            for (int x = pair.Value.Left; x < pair.Value.Right; x++)
            {
                int offset = (y - tile.Top) * tile.Width + x - tile.Left;
                int alpha = Round255(mask[offset] * settings.Opacity);
                for (int c = 0; c < 3; c++)
                    result[offset * 4 + c] = (byte)(Round255(settings.Color[c] * alpha) +
                        (baseline[offset * 4 + c] * (255 - alpha) + 127) / 255);
                result[offset * 4 + 3] = (byte)(alpha + (baseline[offset * 4 + 3] * (255 - alpha) + 127) / 255);
            }
            PublishedPixelBytes += (long)pair.Value.Width * pair.Value.Height * 4;
        }
        dirty.Clear();
    }
    private static byte Round255(double value) => (byte)Math.Clamp(Math.Floor(value + 0.5), 0, 255);
}

internal sealed class BrushSession(TiledRaster initial)
{
    private readonly List<TiledRaster> history = [initial];
    private int position;
    public TiledRaster Current => history[position];
    public int UndoCount => position;
    public SoftBrushStroke? Active { get; private set; }
    public SoftBrushStroke Begin(SoftBrushSettings settings)
    {
        if (Active is not null) throw new InvalidOperationException("A stroke is already active");
        return Active = new SoftBrushStroke(Current, settings);
    }
    public void Commit()
    {
        var stroke = Active ?? throw new InvalidOperationException("No active stroke");
        var next = stroke.Commit();
        Active = null;
        if (stroke.TouchedTiles == 0) return;
        history.RemoveRange(position + 1, history.Count - position - 1);
        history.Add(next);
        position++;
    }
    public void Cancel() { Active?.Cancel(); Active = null; }
    public void Undo()
    {
        if (Active is not null) throw new InvalidOperationException("Finish the active stroke first");
        if (position > 0) position--;
    }
    public void Redo()
    {
        if (Active is not null) throw new InvalidOperationException("Finish the active stroke first");
        if (position + 1 < history.Count) position++;
    }
}
