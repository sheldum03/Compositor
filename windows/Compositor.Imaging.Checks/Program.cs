using System.IO.Compression;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

if (args.Length != 2) throw new ArgumentException("Usage: <fixtures> <new-output-directory>");
string fixtures = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must not exist.");
Directory.CreateDirectory(output);
var results = new List<object>();
using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "cases.json")));
foreach (var item in cases.RootElement.EnumerateArray())
{
    string name = item.GetProperty("file").GetString()!;
    int width = item.GetProperty("width").GetInt32(), height = item.GetProperty("height").GetInt32();
    using var file = File.OpenRead(Path.Combine(fixtures, item.GetProperty("expected").GetString()!));
    using var gzip = new GZipStream(file, CompressionMode.Decompress);
    using var expected = new MemoryStream();
    gzip.CopyTo(expected);
    var raster = ImageCodec.Load(Path.Combine(fixtures, name));
    Require(raster.Width == width && raster.Height == height, $"{name}: dimensions");
    var actual = Flatten(raster);
    Require(actual.Length == expected.Length, $"{name}: reference size");
    int maximum = Difference(actual, expected.ToArray());
    Require(maximum <= item.GetProperty("tolerance").GetInt32(), $"{name}: maximum channel error {maximum}");
    string exported = Path.Combine(output, name + ".png");
    ImageCodec.SavePng(raster, exported);
    Require(Flatten(ImageCodec.Load(exported)).SequenceEqual(actual), $"{name}: PNG premultiplied roundtrip");
    results.Add(new { file = name, width, height, maximum, pngRoundtripExact = true });
}

foreach (string name in new[] { "truncated.png", "truncated.jpg", "too-wide.png", "too-many-pixels.png" })
    Reject<InvalidDataException>(() => ImageCodec.Load(Path.Combine(fixtures, name)), name);
Reject<NotSupportedException>(() => ImageCodec.Load(Path.Combine(fixtures, "unsupported.gif")), "GIF");

var transparent = new TileRaster(32, 32);
var halfRed = transparent.ReplaceTile(0, 0,
    Enumerable.Range(0, 32 * 32).SelectMany(_ => new byte[] { 128, 0, 0, 128 }).ToArray());
foreach (var (name, raster, expected) in new[]
{
    ("transparent.jpg", transparent, new byte[] { 20, 40, 60, 255 }),
    ("half-red.jpg", halfRed, new byte[] { 138, 20, 30, 255 })
})
{
    string path = Path.Combine(output, name);
    var original = Flatten(raster);
    ImageCodec.SaveJpeg(raster, path, 100, (20, 40, 60));
    Require(Flatten(raster).SequenceEqual(original), $"{name}: export changed input snapshot");
    var loaded = ImageCodec.Load(path);
    Require(loaded.Width == 32 && loaded.Height == 32, $"{name}: dimensions");
    var decoded = Flatten(loaded);
    int maximum = Difference(decoded, Enumerable.Range(0, 32 * 32).SelectMany(_ => expected).ToArray());
    Require(maximum <= 3, $"{name}: explicit background error {maximum}");
    results.Add(new { file = name, maximum, explicitBackground = true });
}

string unchanged = Path.Combine(output, "existing.png");
byte[] sentinel = [1, 2, 3, 4];
File.WriteAllBytes(unchanged, sentinel);
Reject<IOException>(() => ImageCodec.SavePng(transparent, unchanged), "existing output");
Require(File.ReadAllBytes(unchanged).SequenceEqual(sentinel), "Existing output was changed");
Reject<ArgumentOutOfRangeException>(() => ImageCodec.SaveJpeg(transparent,
    Path.Combine(output, "invalid-quality.jpg"), 0, (0, 0, 0)), "quality");
var invalid = new TileRaster(1, 1).ReplaceTile(0, 0, new byte[] { 255, 0, 0, 128 });
Reject<InvalidDataException>(() => ImageCodec.SavePng(invalid, Path.Combine(output, "invalid.png")), "premultiplication");
Require(!File.Exists(Path.Combine(output, "invalid.png")) &&
    !File.Exists(Path.Combine(output, "invalid-quality.jpg")), "Rejected output was published");
Require(!Directory.EnumerateFiles(output, "*.tmp-*").Any(), "Temporary output leaked");
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
{
    platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    cases = results, rejectionChecks = 8, passed = true
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PASS: PNG pixels/alpha/tiles, JPEG EXIF 1-8, linear ICC to sRGB, PNG roundtrip, explicit JPEG background, rejected input/output protection");

static byte[] Flatten(TileRaster raster)
{
    var pixels = new byte[raster.Width * raster.Height * 4];
    for (int row = 0; row * TileRaster.TileSize < raster.Height; row++)
    for (int column = 0; column * TileRaster.TileSize < raster.Width; column++)
    {
        var size = raster.TileDimensions(column, row);
        var tile = raster.ReadTileCopy(column, row);
        for (int y = 0; y < size.Height; y++)
            tile.AsSpan(y * size.Width * 4, size.Width * 4).CopyTo(pixels.AsSpan(
                ((row * TileRaster.TileSize + y) * raster.Width + column * TileRaster.TileSize) * 4));
    }
    return pixels;
}

static int Difference(byte[] actual, byte[] expected) =>
    actual.Zip(expected, (a, b) => Math.Abs(a - b)).Max();

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Reject<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"{name} was not rejected");
}
