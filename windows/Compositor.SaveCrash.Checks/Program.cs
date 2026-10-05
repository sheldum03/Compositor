using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;
using Compositor.Imaging;

if (args.Length == 4 && args[0] == "--child")
{
    string source = args[1], target = args[2], stop = args[3];
    var childSession = ImageProjectWorkflow.OpenEditable(source);
    Edit(childSession);
    ProjectStore.Save(childSession, target, backup =>
    {
        if (stop == "BackupCleanup")
        {
            File.Delete(Path.Combine(backup, "manifest.json"));
            Ready(stop);
        }
        Directory.Delete(backup, recursive: true);
    }, stage => { if (stage.ToString() == stop) Ready(stop); }, encodeRaster: (raster, path) =>
    {
        if (stop == "Encoding")
        {
            using var partial = File.Create(path);
            partial.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            partial.Flush(flushToDisk: true);
            Ready(stop);
        }
        ImageCodec.SavePng(raster, path);
        _ = ImageCodec.Load(path);
    });
    throw new Exception("Child save returned without reaching the requested kill point.");
}

if (args.Length != 2) throw new ArgumentException("Usage: <image fixtures> <new output directory>");
string fixture = Path.GetFullPath(Path.Combine(args[0], "alpha-tiles.png"));
string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must not exist.");
Directory.CreateDirectory(output);
var records = new List<object>();
foreach (bool multilayer in new[] { false, true })
foreach (bool existing in new[] { true, false })
foreach (string stage in existing
    ? new[] { "Encoding", "Prepared", "OldMoved", "NewMoved", "BackupCleanup" }
    : new[] { "Encoding", "Prepared", "NewMoved" })
{
    if (multilayer && stage == "Encoding") continue;
    string name = (multilayer ? "multi-" : "") + (existing ? "replace-" : "first-") + stage;
    string directory = Path.Combine(output, name);
    Directory.CreateDirectory(directory);
    string source = Path.Combine(directory, "Source.comp");
    string target = Path.Combine(directory, "Target.comp");
    var original = ImageProjectWorkflow.Import(fixture, source);
    if (multilayer)
    {
        AddSecondLayer(source, original.Raster!);
        original = ImageProjectWorkflow.OpenEditable(source);
    }
    var sourceHashes = Hashes(source);
    var oldState = Capture(original);
    string documentId = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "manifest.json")))!["documentID"]!.GetValue<string>();
    if (existing) ImageProjectWorkflow.Save(original, target);
    var expected = ImageProjectWorkflow.OpenEditable(source);
    Edit(expected);
    var newState = Capture(expected);

    var start = new ProcessStartInfo(Environment.ProcessPath!)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    foreach (string argument in new[] { "--child", source, target, stage }) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new Exception("Child process did not start.");
    var errors = process.StandardError.ReadToEndAsync();
    string? ready = null;
    try
    {
        ready = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
        if (ready != "READY " + stage || process.HasExited)
            throw new Exception($"{name}: child did not reach the requested live save boundary.");
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        if (process.ExitCode == 0) throw new Exception($"{name}: child exited successfully instead of being killed.");
    }
    finally
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        File.WriteAllText(Path.Combine(directory, "child.stdout.txt"), ready + "\n" + await process.StandardOutput.ReadToEndAsync());
        File.WriteAllText(Path.Combine(directory, "child.stderr.txt"), await errors);
    }

    string backup = target + ".backup";
    string[] temporary = Directory.GetDirectories(directory, "Target.comp.tmp-*");
    bool newAtTarget = stage is "NewMoved" or "BackupCleanup";
    bool targetBeforeOpen = Directory.Exists(target);
    bool backupBeforeOpen = Directory.Exists(backup);
    if (targetBeforeOpen != (newAtTarget || existing && stage is "Encoding" or "Prepared") ||
        backupBeforeOpen != (existing && stage is "OldMoved" or "NewMoved" or "BackupCleanup") ||
        temporary.Length != (newAtTarget ? 0 : 1))
        throw new Exception($"{name}: on-disk state does not match an abrupt save interruption.");

    if (newAtTarget || existing)
    {
        var recovered = ImageProjectWorkflow.OpenEditable(target);
        Check(recovered, newAtTarget ? newState : oldState);
        string actualId = JsonNode.Parse(File.ReadAllText(Path.Combine(target, "manifest.json")))!["documentID"]!.GetValue<string>();
        if (actualId != documentId) throw new Exception($"{name}: recovered the wrong document.");
        if (stage == "OldMoved" && Directory.Exists(backup))
            throw new Exception($"{name}: backup was not restored to the missing target.");
        if (backupBeforeOpen && stage != "OldMoved")
        {
            recovered.RenameLayer("Must not overwrite unresolved backup");
            var savedHashes = Hashes(target);
            try
            {
                ImageProjectWorkflow.Save(recovered, target);
                throw new Exception($"{name}: allowed another save over an unresolved backup.");
            }
            catch (IOException) { }
            if (!recovered.IsDirty || !SameHashes(savedHashes, Hashes(target)))
                throw new Exception($"{name}: rejected save changed the recovered project.");
        }
    }
    else if (Directory.Exists(target)) throw new Exception($"{name}: incomplete first save became a formal project.");

    if (stage is "Prepared" or "OldMoved")
        Check(ImageProjectWorkflow.OpenEditable(temporary.Single()), newState);
    if (stage == "NewMoved" && existing)
        Check(ImageProjectWorkflow.OpenEditable(backup), oldState);
    if (stage == "Encoding")
    {
        string partial = Directory.GetFiles(Path.Combine(temporary.Single(), "images")).Single();
        if (new FileInfo(partial).Length != 8 || File.Exists(Path.Combine(temporary.Single(), "manifest.json")))
            throw new Exception($"{name}: partial write boundary was not exercised.");
    }
    if (!SameHashes(sourceHashes, Hashes(source))) throw new Exception($"{name}: original source was changed.");
    records.Add(new { name, layerCount = oldState.Layers.Count, killedPid = process.Id, childExitCode = process.ExitCode,
        targetBeforeOpen, backupBeforeOpen, temporaryDirectories = temporary.Length,
        recovered = newAtTarget ? "complete-new" : existing ? "complete-old" : "no-formal-target",
        preservedSource = true });
    Console.WriteLine("PASS: " + name);
}
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
{
    platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    passed = true, cases = records,
    limits = "Process termination at controlled boundaries only; not power-loss or Windows evidence unless executed on Windows."
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PASS: fourteen real child-process save interruptions, single/multilayer recovery, backup protection and source integrity");

static void Ready(string stage)
{
    Console.WriteLine("READY " + stage);
    Console.Out.Flush();
    Thread.Sleep(Timeout.Infinite);
}

static void Edit(ProjectSession session)
{
    if (session.Layers.Count > 1)
    {
        Guid top = session.Layers[^1].Id;
        session.RenameLayer(top, "Interrupted hidden layer");
        session.SetLayerVisible(top, false);
        session.MoveLayer(top, 0);
        return;
    }
    byte[] pixels = session.Raster!.ReadTileCopy(0, 0);
    new byte[] { 17, 19, 23, 255 }.CopyTo(pixels, 0);
    session.ReplaceRaster(session.Raster.ReplaceTile(0, 0, pixels));
    session.RenameLayer("Interrupted pixels");
}

static ExpectedProject Capture(ProjectSession session) => new(
    ImageProjectWorkflow.RenderFlatNormal(session), session.Layers,
    session.Current["documentID"]!.GetValue<string>(),
    session.Layers.Count > 1 ? Hashes(Path.Combine(session.SourceDirectory, "images")) : null);

static void Check(ProjectSession session, ExpectedProject expected)
{
    if (!session.CanEdit || session.IsDirty || !session.Layers.SequenceEqual(expected.Layers) ||
        session.Current["documentID"]!.GetValue<string>() != expected.DocumentId)
        throw new Exception("Recovered document state differs from the expected complete version.");
    var actual = ImageProjectWorkflow.RenderFlatNormal(session);
    if (actual.Width != expected.Pixels.Width || actual.Height != expected.Pixels.Height)
        throw new Exception("Recovered image dimensions differ from the expected version.");
    for (int row = 0; row * TileRaster.TileSize < expected.Pixels.Height; row++)
    for (int column = 0; column * TileRaster.TileSize < expected.Pixels.Width; column++)
        if (!actual.ReadTileCopy(column, row).SequenceEqual(expected.Pixels.ReadTileCopy(column, row)))
            throw new Exception("Recovered image pixels differ from the expected complete version.");
    if (expected.AssetHashes is { } hashes && !SameHashes(hashes, Hashes(Path.Combine(session.SourceDirectory, "images"))))
        throw new Exception("Recovered multilayer assets differ, including the hidden layer.");
}

static void AddSecondLayer(string directory, TileRaster source)
{
    string manifestPath = Path.Combine(directory, "manifest.json");
    var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
    var layers = manifest["layers"]!.AsArray();
    var layer = layers[0]!.DeepClone();
    string id = Guid.NewGuid().ToString("D");
    string imageName = id.ToUpperInvariant() + ".png";
    layer["id"] = id;
    layer["name"] = "Top";
    layer["imageFile"] = imageName;
    layers.Add(layer);
    var top = new TileRaster(source.Width, source.Height);
    for (int row = 0; row * TileRaster.TileSize < top.Height; row++)
    for (int column = 0; column * TileRaster.TileSize < top.Width; column++)
    {
        var size = top.TileDimensions(column, row);
        byte[] pixels = new byte[size.Width * size.Height * 4];
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 7; pixels[offset + 1] = 11; pixels[offset + 2] = 211; pixels[offset + 3] = 255;
        }
        top = top.ReplaceTile(column, row, pixels);
    }
    ImageCodec.SavePng(top, Path.Combine(directory, "images", imageName));
    File.WriteAllText(manifestPath, manifest.ToJsonString());
}

static Dictionary<string, string> Hashes(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
    .ToDictionary(path => Path.GetRelativePath(directory, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

static bool SameHashes(Dictionary<string, string> first, Dictionary<string, string> second) =>
    first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out string? value) && value == pair.Value);

internal sealed record ExpectedProject(TileRaster Pixels, IReadOnlyList<FlatLayerInfo> Layers,
    string DocumentId, Dictionary<string, string>? AssetHashes);
