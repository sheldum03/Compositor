using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

// Exhaust every coverage/tip pair, including unaligned spans and scalar remainders.
foreach (int length in new[] { 1, 15, 16, 17, 31, 32, 33, 257, 65536 })
{
    var mask = new byte[length + 2]; var stamp = new byte[length + 2];
    for (int first = 0; first < 65536; first += length)
    {
        int count = Math.Min(length, 65536 - first);
        for (int i = 0; i < count; i++) { mask[i + 1] = (byte)((first + i) >> 8); stamp[i + 1] = (byte)(first + i); }
        var expected = (byte[])mask.Clone();
        for (int i = 1; i <= count; i++) expected[i] = (byte)(mask[i] + (stamp[i] * (255 - mask[i]) + 127) / 255);
        SoftBrushStroke.AccumulateCoverage(mask.AsSpan(1, count), stamp.AsSpan(1, count));
        if (!mask.AsSpan().SequenceEqual(expected)) throw new Exception("Coverage accumulation differs from scalar formula or overwrites span boundary");
    }
}
// Packed destination blending must preserve every scalar channel and alpha rounding.
for (uint alpha = 0; alpha < 256; alpha++)
for (uint value = 0; value < 256; value++)
{
    uint baseline = value | ((value ^ 85) << 8) | (((value + 91) & 255) << 16) | ((value ^ 173) << 24);
    uint color = alpha | ((alpha / 2) << 8) | ((alpha / 3) << 16) | (alpha << 24);
    uint expected = 0;
    for (int shift = 0; shift < 32; shift += 8)
        expected |= (((color >> shift) & 255) + (((baseline >> shift) & 255) * (255 - alpha) + 127) / 255) << shift;
    if (SoftBrushStroke.BlendPixel(baseline, color, 255 - alpha) != expected)
        throw new Exception("Packed destination blend differs from scalar channels");
}
// Revisiting an already allocated area must not clone every tail tile on each pointer update.
var allocationStroke = new SoftBrushStroke(new TiledRaster(4000, 4000), new(800, 1, [1, .3, .1]));
for (int i = 0; i < 12; i++) allocationStroke.Append(new(2000 + i % 2 * 50, 2000 + i % 3 * 40));
long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 32; i++) allocationStroke.Append(new(2000 + i % 2 * 50, 2000 + i % 3 * 40));
long tailUpdateAllocations = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
if (tailUpdateAllocations > 4 * 1024 * 1024)
    throw new Exception($"Repeated tail updates allocated {tailUpdateAllocations} bytes (budget 4 MiB)");
Console.WriteLine($"PASS: 32 repeated tail updates allocated {tailUpdateAllocations} bytes (budget 4 MiB)");
allocationStroke.Cancel();
CheckStrokeAllocations();

// Frozen pre-optimization raster hashes include provisional tails, clipping and nontransparent destinations.
var actual = new Dictionary<string, string>();
using var srgb = SKColorSpace.CreateSrgb();
using var surface = SKSurface.Create(new SKImageInfo(600, 450, SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
string Render(Action<SKCanvas> draw)
{
    surface.Canvas.Clear(SKColors.Transparent);
    surface.Canvas.Save(); surface.Canvas.Scale(.5f); draw(surface.Canvas); surface.Canvas.Restore();
    using var image = surface.Snapshot();
    using var bitmap = SKBitmap.FromImage(image);
    return Convert.ToHexString(SHA256.HashData(bitmap.Bytes));
}
string RenderBackdrop(Action<SKCanvas> draw, float scale, bool isolated, float offsetX = .3f, float offsetY = .7f)
{
    surface.Canvas.Clear(new SKColor(19, 51, 87, 143));
    surface.Canvas.ClipRect(new SKRect(0, 0, 600, 450));
    if (isolated) surface.Canvas.SaveLayer(); else surface.Canvas.Save();
    surface.Canvas.Translate(offsetX, offsetY); surface.Canvas.Scale(scale); draw(surface.Canvas); surface.Canvas.Restore();
    using var image = surface.Snapshot(); using var bitmap = SKBitmap.FromImage(image);
    return Convert.ToHexString(SHA256.HashData(bitmap.Bytes));
}
BrushPoint[] path = [new(-12, 30), new(20, 20), new(120, 80), new(260, 280), new(520, 470),
    new(270, 260), new(270, 260), new(700, 50), new(1100, 820), new(1250, 920)];
foreach (int diameter in new[] { 12, 40, 520, 800 })
foreach (double opacity in new[] { .01, .4, 1 })
{
    var empty = new TiledRaster(1200, 900);
    var underpaint = SoftBrushStroke.ReplaySettled(empty, new(520, .7, [.2, .8, .4]), path.Reverse().ToArray());
    foreach (var (name, initial) in new[] { ("empty", empty), ("existing", underpaint) })
    {
        string key = $"{diameter}/{opacity}/{name}";
        string initialHash = Render(c => initial.Paint(c));
        using var images = new TileImageCache();
        var session = new BrushSession(initial);
        var stroke = session.Begin(new(diameter, opacity, [1, .3, .1]));
        for (int i = 0; i < path.Length; i++)
        {
            stroke.Append(path[i]);
            actual[$"{key}/update-{i}"] = Render(c => stroke.Paint(c, images));
            long copied = -1;
            string repeated = Render(c => copied = stroke.Paint(c, images));
            if (copied != 0 || repeated != actual[$"{key}/update-{i}"]) throw new Exception("Unchanged frame copied pixels or changed output");
        }
        foreach (float scale in new[] { .175f, .25f, .375f })
            if (RenderBackdrop(c => stroke.Paint(c, images), scale, true) != RenderBackdrop(c => stroke.Paint(c, images), scale, false))
                throw new Exception("Direct brush composition differs from isolated layer over a backdrop");
        foreach (float scale in new[] { .125f, .25f, .375f, .5f, 1f, 1.5f, .175f, .375f })
            foreach (var (x, y) in new[] { (0f, 0f), (1f, 3f), (35f, 17f), (-8f, -6f), (.3f, .7f) })
                if (RenderBackdrop(c => stroke.Paint(c, images), scale, false, x, y) != RenderBackdrop(c => stroke.Paint(c), scale, false, x, y))
                    throw new Exception($"Cached viewport differs from full-resolution nearest sampling: {key}, scale={scale}, offset={x}/{y}");
        session.Commit(); actual[$"{key}/commit"] = Render(c => session.Current.Paint(c, images: images));
        images.Clear();
        long rebuilt = 0;
        if (Render(c => rebuilt = session.Current.Paint(c, images: images)) != actual[$"{key}/commit"] || rebuilt == 0)
            throw new Exception("Cleared cache did not rebuild the same pixels");
        session.Undo(); if (Render(c => session.Current.Paint(c, images: images)) != initialHash) throw new Exception("Undo changed source");
        session.Redo(); if (Render(c => session.Current.Paint(c, images: images)) != actual[$"{key}/commit"]) throw new Exception("Redo changed pixels");
        session.Begin(new(diameter, opacity, [.1, .2, .9])).Append(new(350, 350)); session.Cancel();
        if (Render(c => session.Current.Paint(c, images: images)) != actual[$"{key}/commit"]) throw new Exception("Cancel changed pixels");
    }
}
if (args.Length == 2 && args[0] == "--record")
    File.WriteAllText(args[1], JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true }) + "\n");
else
{
    var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(args[0]))!;
    if (actual.Count != expected.Count || actual.Any(pair => !expected.TryGetValue(pair.Key, out var hash) || hash != pair.Value))
        throw new Exception("Brush transient/committed raster differs from frozen pre-optimization output");
    Console.WriteLine($"PASS: 65536 coverage pairs across 9 span lengths; {actual.Count} transient/committed rasters; undo/redo/cancel; 4 diameters, 3 opacities, empty/existing layers");
}


static void CheckStrokeAllocations()
{
    var session = new BrushSession(new TiledRaster(4000, 4000));
    var digests = new List<string> { session.Current.Digest() };
    long appendAllocations = 0, committedPixelBytes = 0;
    for (int edit = 0; edit < 100; edit++)
    {
        var source = session.Current;
        string sourceDigest = digests[^1];
        var stroke = session.Begin(new(160, .4, [1, .3, .1]));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int point = 0; point <= 20; point++)
            stroke.Append(new(180 + edit % 10 * 360 + point * 6,
                180 + edit / 10 * 360 + 50 * Math.Sin(point * Math.PI / 10)));
        appendAllocations += GC.GetAllocatedBytesForCurrentThread() - before;
        if (source.Digest() != sourceDigest) throw new Exception("Active stroke mutated its source snapshot");
        session.Commit(); committedPixelBytes += stroke.CommitCopiedBytes;
        if (source.Digest() != sourceDigest) throw new Exception("Commit mutated its source snapshot");
        digests.Add(session.Current.Digest());
    }
    if (digests[^1] != "9f82875d4ea2fef45bfb642e257fdab9683cd21124fb52e92ce99bb3b5b73af7")
        throw new Exception("S05 fixed stroke trace changed");
    for (int edit = 0; edit < 100; edit++)
    {
        session.Undo();
        if (session.Current.Digest() != digests[session.UndoCount]) throw new Exception("Shared snapshot undo changed");
    }
    for (int edit = 0; edit < 100; edit++)
    {
        session.Redo();
        if (session.Current.Digest() != digests[session.UndoCount]) throw new Exception("Shared snapshot redo changed");
    }
    // Working RGBA pixels plus byte coverage/tail masks fit below two RGBA copies.
    // An additional full copy of the immutable source exceeds this bound on the fixed trace.
    Console.WriteLine($"Stroke append allocations: {appendAllocations}; budget: {2 * committedPixelBytes}");
    if (appendAllocations >= 2 * committedPixelBytes)
        throw new Exception("Stroke still allocates a full immutable-source copy per touched tile");
}
