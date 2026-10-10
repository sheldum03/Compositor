using System.Reflection;
using System.Text.Json;
using SkiaSharp;

if (args.Length != 1 || Path.Exists(args[0])) throw new ArgumentException("Provide a new output directory");
Directory.CreateDirectory(args[0]);
var compare = Assembly.Load("Compositor.AvaloniaProbe").GetType("Program")!
    .GetMethod("Compare", BindingFlags.NonPublic | BindingFlags.Static)!;
object Compare(string a, string b, string? heatmap = null) => compare.Invoke(null, [a, b, heatmap])!;
string Png(string name, int width, int height, Action<SKBitmap>? modify = null)
{
    using var srgb = SKColorSpace.CreateSrgb();
    using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
    bitmap.Erase(SKColors.Black); modify?.Invoke(bitmap);
    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    string path = Path.Combine(args[0], name + ".png");
    using var output = File.Create(path); data.SaveTo(output);
    return path;
}
void Check(object result, int pixels, int maximum, double mean, int alpha, int above1)
{
    var r = JsonSerializer.SerializeToElement(result);
    if (r.GetProperty("DifferentPixels").GetInt32() != pixels ||
        r.GetProperty("MaximumChannelError").GetInt32() != maximum ||
        r.GetProperty("MeanAbsoluteChannelError").GetDouble() != mean ||
        r.GetProperty("MaximumAlphaError").GetInt32() != alpha ||
        r.GetProperty("PixelsWithErrorAbove1").GetInt32() != above1)
        throw new Exception("Difference statistics changed: " + r);
}
string first = Png("first", 3, 2), second = Png("second", 3, 2, b =>
{
    b.SetPixel(0, 0, new SKColor(1, 0, 0));
    b.SetPixel(1, 0, new SKColor(64, 32, 16));
    b.SetPixel(2, 1, SKColors.Transparent);
});
Check(Compare(first, first), 0, 0, 0, 0, 0);
string heatmap = Path.Combine(args[0], "heatmap.png");
Check(Compare(first, second, heatmap), 3, 255, 368 / 24d, 255, 2);
Check(Compare(second, first), 3, 255, 368 / 24d, 255, 2);
using (var image = SKBitmap.Decode(heatmap))
{
    for (int y = 0; y < 2; y++)
    for (int x = 0; x < 3; x++)
    {
        var expected = (x, y) switch { (0, 0) => new SKColor(32, 0, 0), (1, 0) => SKColors.Red, (2, 1) => SKColors.Blue, _ => SKColors.Black };
        if (image.GetPixel(x, y) != expected) throw new Exception("Heatmap pixel changed");
    }
}
string transparent = Png("transparent", 1, 1, b => b.Erase(SKColors.Transparent));
string partial = Png("partial", 1, 1, b => b.SetPixel(0, 0, new SKColor(128, 64, 32, 128)));
Check(Compare(transparent, partial), 1, 128, 60, 128, 1);
string otherSize = Png("other-size", 2, 2);
try { Compare(first, otherSize); throw new Exception("Dimension mismatch accepted"); }
catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
Console.WriteLine("PASS: exact/one-channel/alpha/premultiplied statistics, odd-width image, heatmap, dimension mismatch");
string large = Png("large", 4000, 4000);
long before = GC.GetAllocatedBytesForCurrentThread();
object largeResult = Compare(large, large);
long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
Check(largeResult, 0, 0, 0, 0, 0);
Console.WriteLine(JsonSerializer.Serialize(new { width = 4000, height = 4000, allocatedBytes = allocated, budgetBytes = 1024 * 1024 }));
if (allocated >= 1024 * 1024) throw new Exception("Pixel comparison still allocates full managed image copies");
