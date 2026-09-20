using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using SkiaSharp;

internal static class Program
{
    private static void Main(string[] args)
    {
        Check(args.Length == 3, "Usage: probe <fixed-fixtures-directory> <new-output-directory> <native-library-path>");
        string fixtures = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        string nativePath = Path.GetFullPath(args[2]);
        var library = NativeLibrary.Load(nativePath);
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly,
            (name, _, _) => name == "compositor_native" ? library : 0);
        Check(!Path.Exists(output), "Output directory must not exist");
        VerifyCorpus(fixtures);
        Directory.CreateDirectory(output);
        AppBuilder.Configure<Application>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        var results = new List<object>();
        var names = Enumerable.Range(1, 7).Select(i => $"F{i:00}")
            .Concat(Enumerable.Range(1, 13).Select(i => $"B{i:00}"));
        foreach (string name in names)
        {
            string source = Path.Combine(fixtures, name + ".comp");
            using var scene = FixtureScene.Read(source);
            string export = Path.Combine(output, name + "-export.png");
            scene.Export(export);
            var control = new SceneControl(scene) { Width = scene.Width, Height = scene.Height };
            control.Measure(new Size(scene.Width, scene.Height));
            control.Arrange(new Rect(0, 0, scene.Width, scene.Height));
            using var bitmap = new RenderTargetBitmap(new PixelSize(scene.Width, scene.Height), new Vector(96, 96));
            bitmap.Render(control);
            string preview = Path.Combine(output, name + "-preview.png");
            bitmap.Save(preview);
            Check(control.DrawCalls > 0, "Custom Skia control must actually draw");
            var previewDifference = Compare(preview, export);
            Check(previewDifference.DifferentPixels == 0, name + ": preview/export differ");
            Check(Pixels(export).Any(b => b != 0), name + ": nonempty specimen rendered blank");

            string saved = Path.Combine(output, name + ".comp");
            scene.RenameActiveAndSaveCopy(saved, "跨平台 renamed " + name);
            VerifySaved(source, saved, scene.ActiveID, "跨平台 renamed " + name);
            using var reopened = FixtureScene.Read(saved);
            string resaved = Path.Combine(output, name + "-reopened.png");
            reopened.Export(resaved);
            Check(Compare(export, resaved).DifferentPixels == 0, name + ": rename changed pixels");
            Reject<InvalidDataException>(() => scene.RenameActiveAndSaveCopy(saved, "overwrite"));
            VerifySaved(source, saved, scene.ActiveID, "跨平台 renamed " + name);
            results.Add(new
            {
                fixture = name, customControlDrawCalls = control.DrawCalls,
                previewExport = previewDifference, renamedCopy = "passed",
                macReference = Compare(export, Path.Combine(fixtures, name + "-mac.png"), Path.Combine(output, name + "-diff.png"))
            });
        }
        Reject<NotSupportedException>(() => { using var unused = FixtureScene.Read(Path.Combine(fixtures, "F08.comp")); });
        GuardChecks(fixtures, output);
        CombinationChecks(fixtures, output);
        VerifyCorpus(fixtures);
        var report = new
        {
            status = "preparation checks passed; Mac pixel differences are observations, not acceptance",
            platform = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription,
            avalonia = typeof(Application).Assembly.GetName().Version?.ToString(),
            skiaSharp = typeof(SKCanvas).Assembly.GetName().Version?.ToString(),
            nativeLibrarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nativePath))).ToLowerInvariant(),
            nativeRenderingFunctions = new[] { "layer_extract_alpha", "layer_unpremultiply_opaque", "layer_restore_alpha" },
            rendering = "CPU Skia; real Avalonia custom control at 96 dpi; explicit sRGB export",
            comparedFormat = "decoded sRGB, premultiplied RGBA8; error units 0–255 per channel",
            windowsExecuted = OperatingSystem.IsWindows(),
            corpusHashesVerified = 91, unsupportedFixturesRejected = 1,
            combinationChecks = new[] { "disabled layer mask", "clipping preserves base alpha", "adjustment preserves stack alpha",
                "disabled group mask", "group coverage applied once", "identity saturation", "zero adjustment opacity",
                "opaque cube result", "clip cycle rejected", "unsupported adjustment rejected", "RGBA mask rejected" },
            guardChecks = new[] { "hidden ancestor", "duplicate ID", "missing parent", "asset escape",
                "future version", "invalid opacity", "rotation unsupported", "unknown optional field",
                "truncated PNG", "existing destination preserved" },
            results
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "report.json"), json + "\n");
        Console.WriteLine(json);
    }

    private static void VerifyCorpus(string fixtures)
    {
        var checksums = JsonNode.Parse(File.ReadAllText(Path.Combine(fixtures, "checksums.json")))!.AsArray();
        Check(checksums.Count == 91, "Expected the fixed M0 corpus");
        foreach (var entry in checksums)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(fixtures, entry!["path"]!.GetValue<string>()));
            Check(bytes.Length == entry["bytes"]!.GetValue<int>() &&
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == entry["sha256"]!.GetValue<string>(),
                "Fixture hash mismatch: " + entry["path"]);
        }
    }

    private static void VerifySaved(string source, string saved, string active, string name)
    {
        var expected = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "manifest.json")))!;
        expected["version"] = 8;
        expected["resolution"] ??= 72;
        expected["layers"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == active)!["name"] = name;
        var actual = JsonNode.Parse(File.ReadAllText(Path.Combine(saved, "manifest.json")));
        Check(JsonNode.DeepEquals(expected, actual), "All untouched manifest values must survive");
        var images = Directory.GetFiles(Path.Combine(source, "images"));
        Check(images.Length == Directory.GetFiles(Path.Combine(saved, "images")).Length, "Asset count changed");
        foreach (string image in images)
            Check(File.ReadAllBytes(image).SequenceEqual(File.ReadAllBytes(Path.Combine(saved, "images", Path.GetFileName(image)))),
                "PNG bytes changed");
    }

    private static void GuardChecks(string fixtures, string output)
    {
        string scratch = Path.Combine(output, "guard.comp");
        using var baseline = FixtureScene.Read(Path.Combine(fixtures, "B01.comp"));
        baseline.RenameActiveAndSaveCopy(scratch, "guard");
        string path = Path.Combine(scratch, "manifest.json"), original = File.ReadAllText(path);
        void Mutate(Action<JsonNode> mutation)
        {
            var json = JsonNode.Parse(original)!;
            mutation(json);
            File.WriteAllText(path, json.ToJsonString());
        }
        void Read() { using var unused = FixtureScene.Read(scratch); }
        Mutate(j => j["layers"]![0]!["isVisible"] = false);
        using (var hidden = FixtureScene.Read(scratch))
        {
            string png = Path.Combine(output, "hidden.png");
            hidden.Export(png);
            Check(Pixels(png).All(b => b == 0), "Hidden group must hide its children");
            File.Delete(png);
        }
        Mutate(j => j["layers"]![2]!["id"] = j["layers"]![1]!["id"]!.DeepClone());
        Reject<InvalidDataException>(Read);
        Mutate(j => j["layers"]![1]!["parentID"] = "00000000-0000-4000-8000-000000000999");
        Reject<InvalidDataException>(Read);
        Mutate(j => j["layers"]![1]!["imageFile"] = "../outside.png");
        Reject<InvalidDataException>(Read);
        Mutate(j => j["version"] = 9);
        Reject<InvalidDataException>(Read);
        Mutate(j => j["layers"]![1]!["opacity"] = -0.1);
        Reject<InvalidDataException>(Read);
        Mutate(j => j["layers"]![1]!["transform"]!["rotation"] = 20);
        Reject<NotSupportedException>(Read);
        Mutate(j => j["layers"]![1]!["unknownFutureField"] = true);
        Reject<NotSupportedException>(Read);
        File.WriteAllText(path, original);
        File.WriteAllBytes(Directory.GetFiles(Path.Combine(scratch, "images"))[0], [137, 80, 78, 71]);
        Reject<InvalidDataException>(Read);
        Directory.Delete(scratch, recursive: true);
    }

    private sealed record Difference(int DifferentPixels, int MaximumChannelError, double MeanAbsoluteChannelError,
        int MaximumAlphaError, int PixelsWithErrorAbove1);

    private static void CombinationChecks(string fixtures, string output)
    {
        byte[] Variant(string name, Action<JsonNode> mutate)
        {
            string directory = Path.Combine(output, "combination.comp"), png = Path.Combine(output, "combination.png");
            using (var source = FixtureScene.Read(Path.Combine(fixtures, name + ".comp")))
                source.RenameActiveAndSaveCopy(directory, "combination");
            string path = Path.Combine(directory, "manifest.json");
            var root = JsonNode.Parse(File.ReadAllText(path))!;
            mutate(root);
            File.WriteAllText(path, root.ToJsonString());
            try
            {
                using var scene = FixtureScene.Read(directory);
                scene.Export(png);
                return Pixels(png);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
                if (File.Exists(png)) File.Delete(png);
            }
        }
        byte[] f03 = Pixels(Path.Combine(output, "F03-export.png"));
        byte[] f05 = Pixels(Path.Combine(output, "F05-export.png"));
        byte[] f06 = Pixels(Path.Combine(output, "F06-export.png"));
        byte[] f07 = Pixels(Path.Combine(output, "F07-export.png"));
        byte[] disabledLayer = Variant("F04", j => j["layers"]![1]!["maskEnabled"] = false);
        Check(disabledLayer.SequenceEqual(f03), "Disabled raster mask must preserve unmasked appearance");
        byte[] baseOnly = Variant("F05", j => j["layers"]!.AsArray().RemoveAt(2));
        for (int p = 3; p < f05.Length; p += 4)
        {
            Check(f05[p] == baseOnly[p], "Clipped overlay must not thicken base alpha");
            Check(f07[p] == f06[p], "Clipped adjustment must preserve stack alpha");
        }
        Check(Variant("F06", j => j["layers"]![0]!["maskEnabled"] = false).SequenceEqual(f05),
            "Disabled group mask must recover the pre-group-mask stack");
        using (var mask = SKBitmap.Decode(Path.Combine(fixtures, "F06.comp/images/00000000-0000-4000-8000-000000000002.mask.png")))
        {
            for (int y = 0; y < mask.Height; y++)
            for (int x = 0; x < mask.Width; x++)
            {
                int p = (y * mask.Width + x) * 4 + 3;
                int expected = (f05[p] * mask.GetPixel(x, y).Red + 127) / 255;
                Check(f06[p] == expected, "Group mask coverage must be applied once with rounded /255, after the stack");
            }
        }
        Check(Variant("F07", j => j["layers"]![3]!["adjustment"]!["saturation"] = 0).SequenceEqual(f06),
            "Identity saturation must preserve the stack");
        Check(Variant("F07", j => j["layers"]![3]!["opacity"] = 0).SequenceEqual(f06),
            "Zero-opacity adjustment must preserve the stack");
        byte[] gray = Variant("F07", j => { j["layers"]![3]!["adjustment"]!["saturation"] = -100; j["layers"]![3]!["opacity"] = 1; });
        for (int p = 0; p < gray.Length; p += 4)
            Check(gray[p] == gray[p + 1] && gray[p] == gray[p + 2] && gray[p + 3] == f06[p + 3],
                "Full desaturation produces neutral gray without alpha changes");
        Reject<NotSupportedException>(() => Variant("F05", j => j["layers"]![2]!["maskSourceID"] = j["layers"]![2]!["id"]!.DeepClone()));
        Reject<NotSupportedException>(() => Variant("F07", j => j["layers"]![3]!["adjustment"]!["hue"] = 20));
        Reject<InvalidDataException>(() => Variant("F04", _ =>
            File.Copy(Path.Combine(fixtures, "F01.comp/images/00000000-0000-4000-8000-000000000001.png"),
                Path.Combine(output, "combination.comp/images/00000000-0000-4000-8000-000000000001.mask.png"), overwrite: true)));
    }

    private static Difference Compare(string first, string second, string? heatmap = null)
    {
        using var firstCodec = SKCodec.Create(first);
        using var secondCodec = SKCodec.Create(second);
        Check(firstCodec.Info.Width == secondCodec.Info.Width && firstCodec.Info.Height == secondCodec.Info.Height,
            "Image dimensions must match");
        using var difference = heatmap is null ? null : new SKBitmap(firstCodec.Info.Width, firstCodec.Info.Height);
        byte[] a = Pixels(first), b = Pixels(second);
        long sum = 0;
        int different = 0, maximum = 0, alphaMaximum = 0, above1 = 0;
        for (int pixel = 0; pixel < a.Length; pixel += 4)
        {
            bool changed = false;
            int pixelMaximum = 0, colorMaximum = 0;
            for (int channel = 0; channel < 4; channel++)
            {
                int delta = Math.Abs(a[pixel + channel] - b[pixel + channel]);
                sum += delta;
                maximum = Math.Max(maximum, delta);
                pixelMaximum = Math.Max(pixelMaximum, delta);
                if (channel < 3) colorMaximum = Math.Max(colorMaximum, delta);
                changed |= delta != 0;
            }
            if (changed) different++;
            if (pixelMaximum > 1) above1++;
            int alpha = Math.Abs(a[pixel + 3] - b[pixel + 3]);
            alphaMaximum = Math.Max(alphaMaximum, alpha);
            difference?.SetPixel(pixel / 4 % difference.Width, pixel / 4 / difference.Width,
                new SKColor((byte)Math.Min(255, colorMaximum * 32), 0, (byte)Math.Min(255, alpha * 32)));
        }
        if (difference is not null)
        {
            using var data = difference.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(heatmap!);
            data.SaveTo(stream);
        }
        return new Difference(different, maximum, sum / (double)a.Length, alphaMaximum, above1);
    }
    private static byte[] Pixels(string path)
    {
        using var codec = SKCodec.Create(path);
        using var srgb = SKColorSpace.CreateSrgb();
        using var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height,
            SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
        Check(codec.GetPixels(bitmap.Info, bitmap.GetPixels()) == SKCodecResult.Success, "PNG decode failed");
        byte[] bytes = new byte[bitmap.ByteCount];
        Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
        return bytes;
    }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
