using SkiaSharp;
using System.Security.Cryptography;
internal static class CoverageRegression
{
    public static void Run()
    {
        var random = new Random(521094);
        var source = new TiledRaster(300, 270);
        var tiles = new Dictionary<int, byte[]>();
        for (int key = 0; key < 4; key++)
        {
            var bounds = source.Bounds(key);
            var bytes = new byte[bounds.Width * bounds.Height * 4];
            for (int p = 0; p < bytes.Length; p += 4)
            {
                bytes[p + 3] = (byte)random.Next(256);
                for (int c = 0; c < 3; c++) bytes[p + c] = (byte)random.Next(bytes[p + 3] + 1);
            }
            tiles[key] = bytes;
        }
        source = source.Replacing(tiles);
        string initial = source.Digest();
        int cases = 0, previews = 0;
        foreach (int diameter in new[] { 1, 31, 120 })
        foreach (double opacity in new[] { .01, .37, .4, .999, 1 })
        foreach (double[] color in new[] { new[] { 0.0, 0, 0 }, new[] { 1.0, 1, 1 }, new[] { .13, .57, .91 }, new[] { 1.0, .001, .4999 } })
        {
            var settings = new SoftBrushSettings(diameter, opacity, color);
            var candidate = new SoftBrushStroke(source, settings);
            var baseline = new BaselineSoftBrushStroke(source, settings);
            for (int i = 0; i < 12; i++)
            {
                var point = new BrushPoint(-20 + i * 32.1, 242 + Math.Sin(i * .9) * 58);
                candidate.Append(point); baseline.Append(point);
                if (Raster(candidate.Paint) != Raster(baseline.Paint)) throw new Exception($"Preview differs case {cases}, update {i}");
                previews++;
            }
            candidate.Flush(); baseline.Flush();
            if (Raster(candidate.Paint) != Raster(baseline.Paint)) throw new Exception("Flush differs");
            if (!candidate.Commit().HasSamePixels(baseline.Commit())) throw new Exception("Committed tiles differ");
            if (source.Digest() != initial) throw new Exception("Source changed");
            cases++;
        }
        Console.WriteLine($"PASS {cases} cases, {previews} per-update comparisons, flush/commit/source identity; baseline 8cf1253");
    }
    private static string Raster(Func<SKCanvas, long> paint)
    {
        using var surface = SKSurface.Create(new SKImageInfo(300, 270, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        paint(surface.Canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToHexString(SHA256.HashData(data.ToArray()));
    }
}
