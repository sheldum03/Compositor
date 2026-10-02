using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Compositor.Core;

if (args.Length is not (2 or 3)) throw new ArgumentException("Usage: Compositor.Smoke <fixtures directory> <new output directory> [native library]");
string fixtures = Path.GetFullPath(args[0]);
string fixture = Path.Combine(fixtures, "F01.comp");
string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Output directory already exists.");
Directory.CreateDirectory(output);
var session = ProjectStore.Open(fixture);
if (!session.CanEdit || session.IsDirty) throw new Exception("Fixture must be editable and clean.");
string scaled = Path.Combine(output, "ScaledSource.comp");
Directory.CreateDirectory(Path.Combine(scaled, "images"));
File.Copy(Path.Combine(fixture, "images", session.ImageName), Path.Combine(scaled, "images", session.ImageName));
var scaledManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture, "manifest.json")))!.AsObject();
int scaledWidth = scaledManifest["width"]!.GetValue<int>() + 1;
scaledManifest["width"] = scaledWidth;
scaledManifest["layers"]![0]!["transform"]!["size"]![0] = scaledWidth;
File.WriteAllText(Path.Combine(scaled, "manifest.json"), scaledManifest.ToJsonString());
var scaledSession = ProjectStore.Open(scaled);
if (scaledSession.CanEdit) throw new Exception("Scaled source was made editable without rendering support.");
try
{
    ProjectStore.Save(scaledSession, Path.Combine(output, "Unexpected.comp"));
    throw new Exception("Scaled source was saved without rendering support.");
}
catch (NotSupportedException) { }
string mutable = Path.Combine(output, "Mutable.comp");
Directory.CreateDirectory(Path.Combine(mutable, "images"));
File.Copy(Path.Combine(fixture, "manifest.json"), Path.Combine(mutable, "manifest.json"));
string mutableImage = Path.Combine(mutable, "images", session.ImageName);
File.Copy(Path.Combine(fixture, "images", session.ImageName), mutableImage);
var mutableSession = ProjectStore.Open(mutable);
using (var stream = new FileStream(mutableImage, FileMode.Append)) stream.WriteByte(1);
string changedSave = Path.Combine(output, "Changed.comp");
try
{
    ProjectStore.Save(mutableSession, changedSave);
    throw new Exception("Externally changed image was saved.");
}
catch (IOException) { }
if (Directory.Exists(changedSave)) throw new Exception("Rejected changed image left a saved project.");
string changedExport = Path.Combine(output, "changed.png");
try
{
    ProjectStore.ExportPng(mutableSession, changedExport);
    throw new Exception("Externally changed image was exported.");
}
catch (IOException) { }
if (File.Exists(changedExport)) throw new Exception("Rejected changed image left an export.");
string before = session.LayerName;
session.RenameLayer("Windows smoke edit");
if (!session.IsDirty || session.LayerName != "Windows smoke edit") throw new Exception("Rename failed.");
if (!session.Undo() || session.LayerName != before || session.IsDirty) throw new Exception("Undo failed.");
if (!session.Redo() || session.LayerName != "Windows smoke edit") throw new Exception("Redo failed.");
string saved = Path.Combine(output, "Edited.comp");
ProjectStore.Save(session, saved);
if (session.IsDirty || ProjectStore.Open(saved).LayerName != session.LayerName) throw new Exception("Save/reopen failed.");
string manifestBeforeRejectedSave = File.ReadAllText(Path.Combine(saved, "manifest.json"));
Directory.CreateDirectory(saved + ".backup");
session.RenameLayer("Rejected edit");
try
{
    ProjectStore.Save(session, saved);
    throw new Exception("Save overwrote a project with a pre-existing backup.");
}
catch (IOException) { }
if (File.ReadAllText(Path.Combine(saved, "manifest.json")) != manifestBeforeRejectedSave || !session.IsDirty)
    throw new Exception("Rejected save changed the target or session state.");
Directory.Delete(saved + ".backup");
session.Undo();
string png = Path.Combine(output, "export.png");
ProjectStore.ExportPng(ProjectStore.Open(saved), png);
string originalPng = Path.Combine(fixture, "images", session.ImageName);
if (!SHA256.HashData(File.ReadAllBytes(png)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(originalPng))))
    throw new Exception("Export differs from source image.");
Directory.Move(saved, saved + ".backup");
if (ProjectStore.Open(saved).LayerName != "Windows smoke edit")
    throw new Exception("Interrupted-save recovery failed.");
string corruptTarget = Path.Combine(output, "CorruptBackup.comp");
string corruptBackup = corruptTarget + ".backup";
Directory.CreateDirectory(corruptBackup);
File.WriteAllText(Path.Combine(corruptBackup, "manifest.json"), "{\"format\":\"invalid\"}");
try
{
    ProjectStore.Open(corruptTarget);
    throw new Exception("Corrupt backup was accepted.");
}
catch (InvalidDataException) { }
if (Directory.Exists(corruptTarget) || !Directory.Exists(corruptBackup))
    throw new Exception("Corrupt backup was moved before validation.");
string cleanupTarget = Path.Combine(output, "Cleanup.comp");
ProjectStore.Save(ProjectStore.Open(fixture), cleanupTarget);
var cleanupSession = ProjectStore.Open(cleanupTarget);
cleanupSession.RenameLayer("After cleanup failure");
bool cleanupFailed = false;
try
{
    ProjectStore.Save(cleanupSession, cleanupTarget, backup =>
    {
        File.Delete(Path.Combine(backup, "manifest.json"));
        throw new IOException("Injected backup cleanup failure.");
    });
}
catch (IOException ex) when (ex.Message == "Injected backup cleanup failure.") { cleanupFailed = true; }
if (!cleanupFailed || cleanupSession.IsDirty ||
    ProjectStore.Open(cleanupTarget).LayerName != "After cleanup failure" ||
    !Directory.Exists(cleanupTarget + ".backup") ||
    Directory.GetDirectories(output, "Cleanup.comp.failed-*").Length != 0)
    throw new Exception("Cleanup failure damaged the committed project or session state.");
cleanupSession.RenameLayer("Later edit");
try
{
    ProjectStore.Save(cleanupSession, cleanupTarget);
    throw new Exception("Save ignored a partially removed backup.");
}
catch (IOException) { }
if (!cleanupSession.IsDirty || ProjectStore.Open(cleanupTarget).LayerName != "After cleanup failure")
    throw new Exception("Rejected retry changed the committed project or session state.");
for (int version = 2; version <= 8; version++)
    if (ProjectStore.Open(Path.Combine(fixtures, $"F{version:00}.comp")).CanEdit)
        throw new Exception($"Version {version} was made editable without its full semantics.");
var complex = ProjectStore.Open(Path.Combine(fixtures, "F04.comp"));
try
{
    ProjectStore.Save(complex, Path.Combine(output, "Complex.comp"));
    throw new Exception("Unsupported project was saved.");
}
catch (NotSupportedException) { }
if (Directory.Exists(Path.Combine(output, "Complex.comp"))) throw new Exception("Rejected complex save created output.");
var emptyTiles = new TileRaster(300, 300);
byte[] corner = new byte[44 * 44 * 4];
corner[0] = 17;
var firstTiles = emptyTiles.ReplaceTile(1, 1, corner);
corner[0] = 0;
if (firstTiles.ReadTileCopy(1, 1)[0] != 17 || emptyTiles.ReadTileCopy(1, 1)[0] != 0)
    throw new Exception("Tile replacement did not own its bytes.");
var secondTiles = firstTiles.ReplaceTile(0, 0, new byte[256 * 256 * 4]);
if (secondTiles.SharedTileCount(firstTiles) != 1 || secondTiles.StoredBytes != 44L * 44 * 4 + 256L * 256 * 4)
    throw new Exception("Tile snapshots did not share untouched bytes.");
if (args.Length == 3)
{
    nint library = NativeLibrary.Load(Path.GetFullPath(args[2]));
    NativeLibrary.SetDllImportResolver(typeof(NativePixels).Assembly,
        (name, _, _) => name == "compositor_native" ? library : 0);
    byte[] rgba = [200, 40, 255, 100, 1, 2, 3, 4];
    NativePixels.ClampPremultiplied(rgba);
    if (!rgba.AsSpan().SequenceEqual(new byte[] { 100, 40, 100, 100, 1, 2, 3, 4 }) ||
        !NativePixels.ExtractAlpha(rgba, 2, 1, 8).AsSpan().SequenceEqual(new byte[] { 100, 4 }))
        throw new Exception("Native C pixel boundary failed.");
}
try
{
    ProjectStore.Open(Path.Combine(fixtures, "invalid", "future-version.comp"));
    throw new Exception("Future-version project was accepted.");
}
catch (NotSupportedException) { }
Console.WriteLine("PASS: edit, undo, redo, safe save, rejected-save protection, reopen, export, validated backup recovery, cleanup-failure commit, v1-v8 recognition and write protection, scaled-source write protection, changed-asset protection, tile snapshots" +
    (args.Length == 3 ? ", native C pixels" : ""));
