using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Media.Imaging;

internal static class BrushProbe
{
    public static void Run(string fixtures, string output)
    {
        Verify(fixtures);
        var input = JsonNode.Parse(File.ReadAllText(Path.Combine(fixtures, "soft-crossing-4k.json")))!;
        int width = input["width"]!.GetValue<int>(), height = input["height"]!.GetValue<int>();
        Check(width == 4000 && height == 4000 && input["hardness"]!.GetValue<double>() == 0,
            "This probe uses the fixed untransformed 4K soft-brush corpus");
        var settings = new SoftBrushSettings(input["diameter"]!.GetValue<int>(), input["opacity"]!.GetValue<double>(),
            input["color"]!.AsArray().Select(c => c!.GetValue<double>()).ToArray());
        var paths = input["strokes"]!.AsArray();
        Check(paths.Count == 2 && paths.All(p => p!.AsArray().Count == 121), "Expected two 121-event strokes");
        var initial = new TiledRaster(width, height);
        var session = new BrushSession(initial);
        var snapshots = new List<TiledRaster>();
        var timings = new List<object>();
        string? firstDigest = null;
        long highWater = 0;
        using var process = Process.GetCurrentProcess();
        foreach (var path in paths)
        {
            var stroke = session.Begin(settings);
            var appendTimes = new List<double>();
            var previewTimes = new List<double>();
            long copiedForPreview = 0, allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int[] gcBefore = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            var control = new SceneControl(1000, 1000, canvas =>
            {
                canvas.Save();
                canvas.Scale(0.25f);
                copiedForPreview += stroke.Paint(canvas);
                canvas.Restore();
            }) { Width = 1000, Height = 1000 };
            foreach (var coordinates in path!.AsArray())
            {
                long start = Stopwatch.GetTimestamp();
                stroke.Append(new BrushPoint(coordinates![0]!.GetValue<double>(), coordinates[1]!.GetValue<double>()));
                appendTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                start = Stopwatch.GetTimestamp();
                Preview(control);
                previewTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                process.Refresh();
                highWater = Math.Max(highWater, process.WorkingSet64);
            }
            long commitStart = Stopwatch.GetTimestamp();
            session.Commit();
            double commitMs = Stopwatch.GetElapsedTime(commitStart).TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Check(session.Active is null && session.UndoCount == snapshots.Count + 1, "One stroke, one committed history entry");
            Check(initial.FullRasterExports == 0 && session.Current.FullRasterExports == 0 &&
                snapshots.All(s => s.FullRasterExports == 0), "No full raster export during update/commit/next stroke");
            Check(stroke.TouchedTiles < SourceTileCount(width, height) && stroke.CommitCopiedBytes < (long)width * height * 4,
                "Commit must copy only local tiles, not a contiguous 4K layer");
            Check(control.DrawCalls == 121, "Every pointer update must reach a real Avalonia Skia callback");
            timings.Add(new
            {
                stroke = snapshots.Count, appendMilliseconds = appendTimes, previewMilliseconds = previewTimes,
                commitMilliseconds = commitMs, touchedTiles = stroke.TouchedTiles, storedTiles = session.Current.TileCount,
                commitCopiedBytes = stroke.CommitCopiedBytes, publishedPixelBytes = stroke.PublishedPixelBytes,
                tailBackupBytes = stroke.TailBackupBytes, previewNativeCopyBytes = copiedForPreview,
                managedAllocatedBytes = allocated,
                gcCollections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gcBefore[i]).ToArray(),
                appendP95 = Percentile(appendTimes), previewP95 = Percentile(previewTimes),
                updateAndPreviewP95 = Percentile(appendTimes.Zip(previewTimes, (a, b) => a + b).ToList())
            });
            snapshots.Add(session.Current);
            if (firstDigest is null) firstDigest = session.Current.Digest();
            else Check(snapshots[0].Digest() == firstDigest, "Second stroke must not mutate the first snapshot");
        }
        Check(snapshots[0].SharedTiles(snapshots[1]) > 0, "Untouched tiles must be shared across history");
        var settled = initial;
        for (int index = 0; index < paths.Count; index++)
        {
            var points = paths[index]!.AsArray().Select(p => new BrushPoint(p![0]!.GetValue<double>(), p[1]!.GetValue<double>())).ToArray();
            settled = SoftBrushStroke.ReplaySettled(settled, settings, points);
            Check(settled.HasSamePixels(snapshots[index]), "Removing/replacing provisional tails must match a no-tail replay exactly");
        }
        var first = snapshots[0];
        var final = snapshots[1];
        session.Undo();
        Check(ReferenceEquals(session.Current, first), "Undo returns the original snapshot object");
        session.Redo();
        Check(ReferenceEquals(session.Current, final), "Redo returns the second snapshot object");
        SessionChecks(session, settings, final);

        var comparisons = new List<object>();
        foreach (var (name, raster) in new[] { ("first", first), ("final", final) })
        {
            string export = Path.Combine(output, name + ".png");
            raster.Export(export);
            var control = new SceneControl(width, height, canvas => raster.Paint(canvas)) { Width = width, Height = height };
            string preview = Path.Combine(output, name + "-preview.png");
            Preview(control, preview);
            Check(Program.Compare(export, preview).DifferentPixels == 0, "Full-resolution preview/export must match");
            var values = Program.Pixels(export);
            Check(values.Any(v => v != 0), "A painted result must not be blank");
            if (name == "first")
                for (int p = 3; p < values.Length; p += 4) Check(values[p] <= 102, "Within-stroke opacity cap is 40 percent");
            comparisons.Add(new
            {
                stage = name,
                cpuReference = Program.Compare(export, Path.Combine(fixtures, name + "-cpu.png"), Path.Combine(output, name + "-cpu-diff.png")),
                metalReference = Program.Compare(export, Path.Combine(fixtures, name + "-metal.png"))
            });
        }
        Check(Program.Compare(Path.Combine(output, "first.png"), Path.Combine(output, "final.png")).DifferentPixels > 0,
            "The second stroke must change actual pixels");
        string project = WriteProject(fixtures, output, width, height);
        using (var reopened = FixtureScene.Read(project)) reopened.Export(Path.Combine(output, "reopened.png"));
        Check(Program.Compare(Path.Combine(output, "final.png"), Path.Combine(output, "reopened.png")).DifferentPixels == 0,
            "Saved project reopens with identical pixels");
        Check(first.Digest() == firstDigest, "Export/save must not mutate history tiles");
        Verify(fixtures);
        var report = new
        {
            status = "preparation checks passed; reference differences and timings are observations",
            platform = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription, windowsExecuted = OperatingSystem.IsWindows(),
            algorithm = "CPU dabs; 24-stop Gaussian tip, 2.5 percent spacing, centripetal Catmull-Rom, provisional tail restoration",
            scope = "Two 40%-opacity strokes; synchronous headless CPU previews; not S02 or Windows acceptance",
            samplesPerStroke = 121, customControlUpdates = 242, tileSize = TiledRaster.TileSize,
            sharedTilesAcrossSecondStroke = first.SharedTiles(final), sampledWorkingSetHighWaterBytes = highWater,
            fullRasterExportsBeforeSecondCommit = 0, previewViewport = "1000x1000 at 25%; final correctness preview at 4000x4000",
            samplingNotes = "Working set sampled after each preview; digest correctness checks outside timings; no peak private RAM/VRAM claim",
            sessionChecks = new[] { "immediate next stroke", "one history entry per stroke", "shared untouched tiles",
                "prior snapshot unchanged", "undo/redo identity", "cancel preserves redo", "new edit replaces redo",
                "provisional tails equal no-tail replay", "empty stroke leaves history unchanged", "repeated flush stable",
                "committed stroke rejects writes", "full preview/export equal", "save/reopen equal" },
            timings, comparisons
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "brush-report.json"), json + "\n");
        Console.WriteLine(json);
    }

    private static void SessionChecks(BrushSession session, SoftBrushSettings settings, TiledRaster final)
    {
        session.Undo();
        var before = session.Current;
        string digest = before.Digest();
        var canceled = session.Begin(settings);
        canceled.Append(new BrushPoint(100, 100));
        session.Cancel();
        Check(ReferenceEquals(session.Current, before) && before.Digest() == digest, "Cancel leaves history/data unchanged");
        session.Redo();
        Check(ReferenceEquals(session.Current, final), "Cancel must preserve redo");
        session.Begin(settings);
        session.Commit();
        Check(ReferenceEquals(session.Current, final), "An empty stroke must not add history");
        session.Undo();
        var branch = session.Begin(settings);
        branch.Append(new BrushPoint(100, 100));
        branch.Append(new BrushPoint(140, 100));
        branch.Flush();
        // Pixel comparisons use a small rendered crop without exporting/materializing a full source raster.
        string firstFlush = PreviewDigest(branch);
        branch.Flush();
        Check(PreviewDigest(branch) == firstFlush, "Flush must be idempotent");
        session.Commit();
        var newBranch = session.Current;
        session.Redo();
        Check(ReferenceEquals(session.Current, newBranch) && !ReferenceEquals(newBranch, final), "New edit discards redo");
        try { branch.Append(new BrushPoint(160, 100)); throw new Exception("Committed stroke accepted a write"); }
        catch (InvalidOperationException) { }
        Check(before.Digest() == digest, "A branch must not mutate the source history");
    }
    private static string PreviewDigest(SoftBrushStroke stroke)
    {
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(512, 512));
        surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
        stroke.Paint(surface.Canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return Convert.ToHexString(SHA256.HashData(data.ToArray()));
    }
    internal static string WriteProject(string fixtures, string output, int width, int height)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(fixtures, "soft-crossing-4k-cpu.comp/manifest.json")))!;
        var layer = manifest["layers"]![0]!;
        layer["transform"]!["origin"] = new JsonArray(0, 0);
        layer["transform"]!["size"] = new JsonArray(width, height);
        string project = Path.Combine(output, "brush.comp");
        Directory.CreateDirectory(Path.Combine(project, "images"));
        File.Copy(Path.Combine(output, "final.png"), Path.Combine(project, "images", layer["imageFile"]!.GetValue<string>()));
        File.WriteAllText(Path.Combine(project, "manifest.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return project;
    }
    private static void Preview(SceneControl control, string? path = null)
    {
        control.Measure(new Size(control.Width, control.Height));
        control.Arrange(new Rect(0, 0, control.Width, control.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)control.Width, (int)control.Height), new Vector(96, 96));
        bitmap.Render(control);
        if (path is not null) bitmap.Save(path);
    }
    private static int SourceTileCount(int width, int height) => ((width + 255) / 256) * ((height + 255) / 256);
    private static double Percentile(List<double> values) => values.Order().ElementAt((int)Math.Ceiling(values.Count * 0.95) - 1);
    private static void Verify(string root)
    {
        var checksums = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "checksums.json")))!.AsArray();
        Check(checksums.Count == 9, "Expected the nine-file brush corpus");
        foreach (var row in checksums)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, row!["path"]!.GetValue<string>()));
            Check(bytes.Length == row["bytes"]!.GetValue<int>() &&
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == row["sha256"]!.GetValue<string>(), "Corpus changed");
        }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
