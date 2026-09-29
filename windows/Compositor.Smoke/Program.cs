using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Compositor.Core;

if (args.Length is not (2 or 3)) throw new ArgumentException("Usage: Compositor.Smoke <fixtures directory> <new output directory> [native library]");
string fixtures = Path.GetFullPath(args[0]);
string fixture = Path.Combine(fixtures, "F01.comp");
string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Output directory already exists.");
Directory.CreateDirectory(output);
var session = ProjectStore.Open(fixture);
if (!session.CanEdit || session.IsDirty) throw new Exception("Fixture must be editable and clean.");
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
Console.WriteLine("PASS: edit, undo, redo, safe save, rejected-save protection, reopen, export, backup recovery, v1-v8 recognition and write protection, tile snapshots" +
    (args.Length == 3 ? ", native C pixels" : ""));
