using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;

// A deliberately restricted specimen reader, not the production ProjectIO implementation.
internal sealed class FixtureScene : IDisposable
{
    private readonly JsonObject manifest;
    private readonly List<Asset> assets = [];
    private readonly List<SceneLayer> layers = [];
    private sealed record Asset(string File, byte[] Png, SKBitmap Bitmap);
    private sealed record Group(bool Visible, MaskPlacement[] Masks);
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
            var records = root["layers"]!.AsArray();
            Require(records.Count is >= 1 and <= 10000, "Invalid layer count");
            // Parents must precede their children in this restricted bottom-to-top corpus.
            var groups = new Dictionary<string, Group>();
            var ids = new HashSet<string>();
            long sourcePixels = 0;
            long maskPixels = 0;
            foreach (var node in records)
            {
                var layer = node!.AsObject();
                Fields(layer, "id name isVisible isGroup parentID imageFile transform opacity blendMode maskFile maskEnabled maskSourceID adjustment");
                string id = layer["id"]!.GetValue<string>();
                Require(Guid.TryParse(id, out _) && ids.Add(id), "Invalid or duplicate layer ID");
                _ = layer["name"]!.GetValue<string>();
                bool visible = layer["isVisible"]!.GetValue<bool>();
                MaskPlacement[] folderMasks = [];
                string? parentID = layer["parentID"]?.GetValue<string>();
                if (layer["parentID"] is { } parent)
                {
                    Require(version >= 2 && groups.TryGetValue(parent.GetValue<string>(), out _),
                        "Parent must be an earlier group");
                    var group = groups[parent.GetValue<string>()];
                    visible &= group.Visible;
                    folderMasks = group.Masks;
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
                var sampling = transform["sampling"]!.GetValue<string>() switch
                {
                    "Nearest" => SKFilterQuality.None, "Smooth" => SKFilterQuality.Low,
                    _ => SKFilterQuality.High
                };
                var destination = SKRect.Create(origin.X, origin.Y, size.X, size.Y);
                SKBitmap? mask = null;
                if (layer["maskFile"] is { } maskFile)
                {
                    Require(version >= 4, "Masks require version 4");
                    var asset = scene.ReadAsset(directory, maskFile.GetValue<string>(), id + ".mask.png", true, ref maskPixels);
                    if (layer["maskEnabled"]?.GetValue<bool>() ?? true) mask = asset.Bitmap;
                }
                string? clipSource = layer["maskSourceID"]?.GetValue<string>();
                Require(clipSource is null || version >= 5, "Clipping requires version 5");
                if (layer["isGroup"]?.GetValue<bool>() ?? false)
                {
                    Require(version >= 2 && layer["imageFile"] is null && opacity == 1 &&
                        blend == SKBlendMode.SrcOver && origin == SKPoint.Empty &&
                        size == new SKPoint(scene.Width, scene.Height) && clipSource is null &&
                        layer["adjustment"] is null, "Only pass-through groups supported");
                    Require(layer["maskFile"] is null || version >= 6, "Group masks require version 6");
                    if (mask is not null) folderMasks = [.. folderMasks, new MaskPlacement(mask, destination, sampling)];
                    groups.Add(id, new Group(visible, folderMasks));
                    continue;
                }
                double? saturation = null;
                SKBitmap? image = null;
                if (layer["adjustment"] is { } adjustment)
                {
                    Require(version >= 7 && layer["imageFile"] is null, "Invalid adjustment record");
                    var settings = adjustment.AsObject();
                    Fields(settings, "kind hue saturation lightness colorize levels curves");
                    saturation = Number(settings, "saturation", 0);
                    if (settings["kind"]?.GetValue<string>() != "Hue/Saturation" ||
                        Number(settings, "hue", 0) != 0 || Number(settings, "lightness", 0) != 0 ||
                        (settings["colorize"]?.GetValue<bool>() ?? false) || saturation is < -100 or > 0 ||
                        clipSource is null || blend != SKBlendMode.SrcOver || layer["maskFile"] is not null)
                        throw new NotSupportedException("Only normal, clipped master desaturation is implemented");
                }
                else
                {
                    image = scene.ReadAsset(directory, layer["imageFile"]!.GetValue<string>(),
                        id + ".png", false, ref sourcePixels).Bitmap;
                }
                scene.layers.Add(new SceneLayer(id, parentID, image,
                    mask is null ? null : new MaskPlacement(mask, destination, sampling), destination,
                    sampling, (float)opacity, blend, visible, clipSource, saturation, folderMasks));
            }
            Require(ids.Contains(scene.ActiveID), "Missing active layer");
            SceneLayer? stackBase = null;
            foreach (var layer in scene.layers)
            {
                if (layer.ClipSource is null) { stackBase = layer; continue; }
                if (stackBase is null || stackBase.Id != layer.ClipSource || stackBase.Parent != layer.Parent ||
                    stackBase.Image is null || (layer.Visible && !stackBase.Visible))
                    throw new NotSupportedException("Only contiguous, same-parent clipping stacks with visible bases are implemented");
            }
            return scene;
        }
        catch { scene.Dispose(); throw; }
    }

    private unsafe Asset ReadAsset(string directory, string file, string expected, bool mask, ref long pixels)
    {
        Require(file == expected, "Expected layer-ID PNG asset");
        var path = Path.Combine(directory, "images", file);
        CheckPlainPath(path);
        Require(new FileInfo(path).Length <= 512L * 1024 * 1024, "Asset exceeds 512 MiB");
        byte[] png = File.ReadAllBytes(path);
        using var stream = new SKMemoryStream(png);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Invalid PNG");
        Require(codec.EncodedFormat == SKEncodedImageFormat.Png, "Expected PNG encoding");
        pixels += (long)codec.Info.Width * codec.Info.Height;
        Require(pixels <= 100_000_000, "Sources or masks exceed pixel budget");
        Require(!mask || (png.Length >= 26 && png[24] == 8 && png[25] == 0 &&
            codec.Info.ColorType == SKColorType.Gray8 && codec.Info.AlphaType == SKAlphaType.Opaque),
            "Mask must be 8-bit grayscale without alpha");
        using var srgb = SKColorSpace.CreateSrgb();
        // Coverage bypasses color conversion. Convert raw Gray8 bytes into alpha for Skia masking.
        using var decoded = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height,
            mask ? SKColorType.Gray8 : SKColorType.Rgba8888,
            mask ? SKAlphaType.Opaque : SKAlphaType.Premul, mask ? null : srgb));
        Require(codec.GetPixels(decoded.Info, decoded.GetPixels()) == SKCodecResult.Success, "Incomplete PNG decode");
        SKBitmap bitmap;
        if (mask)
        {
            bitmap = new SKBitmap(decoded.Width, decoded.Height, SKColorType.Alpha8, SKAlphaType.Premul);
            for (int y = 0; y < decoded.Height; y++)
                new ReadOnlySpan<byte>((byte*)decoded.GetPixels() + y * decoded.RowBytes, decoded.Width)
                    .CopyTo(new Span<byte>((byte*)bitmap.GetPixels() + y * bitmap.RowBytes, bitmap.Width));
        }
        else bitmap = decoded.Copy();
        var asset = new Asset(file, png, bitmap);
        assets.Add(asset);
        return asset;
    }

    public void Paint(SKCanvas canvas) => Composite.Paint(layers, Width, Height, canvas);

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
            foreach (var asset in assets)
                File.WriteAllBytes(Path.Combine(temporary, "images", asset.File), asset.Png);
            File.WriteAllText(Path.Combine(temporary, "manifest.json"),
                copy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            using var verified = Read(temporary);
            Directory.Move(temporary, destination);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
    }

    public void Dispose() { foreach (var asset in assets) asset.Bitmap.Dispose(); }

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
