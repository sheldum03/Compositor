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
