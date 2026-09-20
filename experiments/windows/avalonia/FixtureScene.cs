using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;

// A deliberately restricted specimen reader, not the production ProjectIO implementation.
internal sealed class FixtureScene : IDisposable
{
    private readonly JsonObject manifest;
    private readonly List<Raster> rasters = [];
    private sealed record Raster(string File, byte[] Png, SKBitmap Bitmap, SKRect Destination,
        SKFilterQuality Sampling, float Opacity, SKBlendMode Blend, bool Visible);
    public int Width { get; }
    public int Height { get; }
    public string ActiveID => manifest["activeLayerID"]!.GetValue<string>();

    private FixtureScene(JsonObject manifest)
    {
        this.manifest = manifest;
        Width = manifest["width"]!.GetValue<int>();
        Height = manifest["height"]!.GetValue<int>();
    }

    public static FixtureScene Read(string directory)
    {
        CheckPlainPath(directory);
        CheckPlainPath(Path.Combine(directory, "images"));
        var manifestPath = Path.Combine(directory, "manifest.json");
        CheckPlainPath(manifestPath);
        Require(new FileInfo(manifestPath).Length <= 4 * 1024 * 1024, "Manifest exceeds 4 MiB");
        var root = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidDataException("Missing manifest");
        Fields(root, "activeLayerID colorSpace documentID format height layers resolution version width");
        Require(root["format"]?.GetValue<string>() == "com.compositor.project", "Invalid format");
        int version = root["version"]!.GetValue<int>();
        Require(version is >= 1 and <= 8, "Unsupported project version");
        Require(root["colorSpace"]?.GetValue<string>() == "sRGB", "Expected sRGB");
        Require(Guid.TryParse(root["documentID"]?.GetValue<string>(), out _), "Invalid document ID");
        double resolution = Number(root, "resolution", 72);
        Require(resolution > 0, "Invalid resolution");
        var scene = new FixtureScene(root);
        try
        {
            Require(scene.Width is >= 1 and <= 30000 && scene.Height is >= 1 and <= 30000 &&
                (long)scene.Width * scene.Height <= 100_000_000, "Canvas exceeds pixel budget");
            var layers = root["layers"]!.AsArray();
            Require(layers.Count is >= 1 and <= 10000, "Invalid layer count");
            // Parents must precede their children in this restricted bottom-to-top corpus.
            var groups = new Dictionary<string, bool>();
            var ids = new HashSet<string>();
            long sourcePixels = 0;
            foreach (var node in layers)
            {
                var layer = node!.AsObject();
                Fields(layer, "id name isVisible isGroup parentID imageFile transform opacity blendMode");
                string id = layer["id"]!.GetValue<string>();
                Require(Guid.TryParse(id, out _) && ids.Add(id), "Invalid or duplicate layer ID");
                _ = layer["name"]!.GetValue<string>();
                bool visible = layer["isVisible"]!.GetValue<bool>();
                if (layer["parentID"] is { } parent)
                {
                    Require(version >= 2 && groups.TryGetValue(parent.GetValue<string>(), out _),
                        "Parent must be an earlier group");
                    visible &= groups[parent.GetValue<string>()];
                }
                double opacity = Number(layer, "opacity", 1);
                Require(opacity is >= 0 and <= 1, "Invalid opacity");
                var blend = Blend(layer["blendMode"]?.GetValue<string>() ?? "Normal");
                Require(version >= 3 || (opacity == 1 && blend == SKBlendMode.SrcOver),
                    "Appearance requires version 3");
                var transform = layer["transform"]!.AsObject();
                Fields(transform, "origin size rotation flipX flipY sampling");
                var origin = Pair(transform, "origin");
                var size = Pair(transform, "size");
                Require(Math.Abs(origin.X) <= 1_000_000 && Math.Abs(origin.Y) <= 1_000_000 &&
                    size.X is >= 1 and <= 300000 && size.Y is >= 1 and <= 300000, "Invalid transform");
                if (Number(transform, "rotation", 0) != 0 ||
                    (transform["flipX"]?.GetValue<bool>() ?? false) ||
                    (transform["flipY"]?.GetValue<bool>() ?? false) ||
                    origin.X != MathF.Truncate(origin.X) || origin.Y != MathF.Truncate(origin.Y))
                    throw new NotSupportedException("Rotation, flips and fractional origins are not implemented");
                Require(transform["sampling"]?.GetValue<string>() is "Nearest" or "Smooth" or "High quality",
                    "Unknown sampling mode");
                if (layer["isGroup"]?.GetValue<bool>() ?? false)
                {
                    Require(version >= 2 && layer["imageFile"] is null && opacity == 1 &&
                        blend == SKBlendMode.SrcOver && origin == SKPoint.Empty &&
                        size == new SKPoint(scene.Width, scene.Height), "Only pass-through groups supported");
                    groups.Add(id, visible);
                    continue;
                }
                string file = layer["imageFile"]!.GetValue<string>();
                Require(file == id + ".png", "Expected layer-ID PNG asset");
                var path = Path.Combine(directory, "images", file);
                CheckPlainPath(path);
                Require(new FileInfo(path).Length <= 512L * 1024 * 1024, "Asset exceeds 512 MiB");
                byte[] png = File.ReadAllBytes(path);
                using var stream = new SKMemoryStream(png);
                using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Invalid PNG");
                Require(codec.EncodedFormat == SKEncodedImageFormat.Png, "Expected PNG encoding");
                sourcePixels += (long)codec.Info.Width * codec.Info.Height;
                Require(sourcePixels <= 100_000_000, "Sources exceed pixel budget");
                using var srgb = SKColorSpace.CreateSrgb();
                var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height,
                    SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
                if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
                {
                    bitmap.Dispose();
                    throw new InvalidDataException("Incomplete PNG decode");
                }
                var sampling = transform["sampling"]!.GetValue<string>() switch
                {
                    "Nearest" => SKFilterQuality.None, "Smooth" => SKFilterQuality.Low,
                    _ => SKFilterQuality.High
                };
                scene.rasters.Add(new Raster(file, png, bitmap,
                    SKRect.Create(origin.X, origin.Y, size.X, size.Y), sampling, (float)opacity, blend, visible));
            }
            Require(ids.Contains(scene.ActiveID), "Missing active layer");
            return scene;
        }
        catch { scene.Dispose(); throw; }
    }

    public void Paint(SKCanvas canvas)
    {
        foreach (var raster in rasters.Where(r => r.Visible))
        {
            using var paint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)Math.Round(raster.Opacity * 255)),
                BlendMode = raster.Blend,
                FilterQuality = raster.Sampling
            };
            canvas.DrawBitmap(raster.Bitmap, raster.Destination, paint);
        }
    }

    public void Export(string path)
    {
        using var srgb = SKColorSpace.CreateSrgb();
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height,
            SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
        surface.Canvas.Clear(SKColors.Transparent);
        Paint(surface.Canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        data.SaveTo(output);
    }

    public void RenameActiveAndSaveCopy(string destination, string name)
    {
        destination = Path.GetFullPath(destination);
        Require(!Path.Exists(destination), "Destination already exists");
        var copy = manifest.DeepClone().AsObject();
        copy["layers"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == ActiveID)!["name"] = name;
        copy["version"] = 8;
        copy["resolution"] ??= 72;
        // Only publish a new directory; replacement/recovery is a later production gate.
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(temporary, "images"));
        try
        {
            foreach (var raster in rasters)
                File.WriteAllBytes(Path.Combine(temporary, "images", raster.File), raster.Png);
            File.WriteAllText(Path.Combine(temporary, "manifest.json"),
                copy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            using var verified = Read(temporary);
            Directory.Move(temporary, destination);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
    }

    public void Dispose() { foreach (var raster in rasters) raster.Bitmap.Dispose(); }

    private static SKBlendMode Blend(string value) => value switch
    {
        "Normal" => SKBlendMode.SrcOver, "Multiply" => SKBlendMode.Multiply,
        "Screen" => SKBlendMode.Screen, "Overlay" => SKBlendMode.Overlay,
        "Darken" => SKBlendMode.Darken, "Lighten" => SKBlendMode.Lighten,
        "Difference" => SKBlendMode.Difference, "Color Dodge" => SKBlendMode.ColorDodge,
        "Color Burn" => SKBlendMode.ColorBurn, "Hue" => SKBlendMode.Hue,
        "Saturation" => SKBlendMode.Saturation, "Color" => SKBlendMode.Color,
        "Luminosity" => SKBlendMode.Luminosity,
        _ => throw new InvalidDataException("Unknown blend mode")
    };
    private static void Fields(JsonObject node, string allowed)
    {
        foreach (var field in node)
            if (!allowed.Split(' ').Contains(field.Key))
                throw new NotSupportedException("Unsupported field: " + field.Key);
    }
    private static double Number(JsonObject node, string key, double fallback)
    {
        double value = node[key]?.GetValue<double>() ?? fallback;
        Require(double.IsFinite(value), "Non-finite " + key);
        return value;
    }
    private static SKPoint Pair(JsonObject node, string key)
    {
        var array = node[key]!.AsArray();
        Require(array.Count == 2, "Expected coordinate pair");
        double x = array[0]!.GetValue<double>(), y = array[1]!.GetValue<double>();
        Require(double.IsFinite(x) && double.IsFinite(y), "Non-finite coordinate");
        return new SKPoint((float)x, (float)y);
    }
    private static void CheckPlainPath(string path) => Require(
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Linked assets unsupported");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
