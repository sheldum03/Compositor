using System.Text.Json.Nodes;
using Compositor.Core;
using Compositor.Imaging;

if (args.Length is not (2 or 3)) throw new ArgumentException("Usage: Compositor.Workflow.Checks <image fixtures> <new output directory> [Mac-produced projects]");
string fixtures = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Output directory already exists.");
Directory.CreateDirectory(output);

string sourcePng = Path.Combine(fixtures, "alpha-tiles.png");
string project = Path.Combine(output, "Image.comp");
var session = ImageProjectWorkflow.Import(sourcePng, project);
var importedManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(project, "manifest.json")))!;
string importedId = importedManifest["layers"]![0]!["id"]!.GetValue<string>();
if (!session.CanEdit || session.IsDirty || session.Raster is null ||
    importedManifest["version"]!.GetValue<int>() != 8 ||
    importedManifest["layers"]![0]!["imageFile"]!.GetValue<string>() !=
        Guid.Parse(importedId).ToString("D").ToUpperInvariant() + ".png")
    throw new Exception("PNG import did not create a clean editable v8 project.");
AssertRaster(ImageCodec.Load(sourcePng), session.Raster);
try
{
    session.RenameLayer("  ");
    throw new Exception("Blank layer name was accepted.");
}
catch (ArgumentException) { }
if (session.IsDirty) throw new Exception("Rejected layer rename changed the save point.");

byte[] changedTile = session.Raster.ReadTileCopy(0, 0);
changedTile[0] = 17;
changedTile[1] = 19;
changedTile[2] = 23;
changedTile[3] = 255;
TileRaster edited = session.Raster.ReplaceTile(0, 0, changedTile);
changedTile[0] = 0;
session.ReplaceRaster(edited);
session.RenameLayer("Edited image");
if (!session.IsDirty || session.LayerName != "Edited image" || session.Raster!.ReadTileCopy(0, 0)[0] != 17)
    throw new Exception("Pixel transaction did not own its tile or mark the project dirty.");
if (!session.Undo() || session.LayerName != "Image" || !ReferenceEquals(session.Raster, edited) ||
    !session.Undo() || session.IsDirty || session.Raster!.ReadTileCopy(0, 0)[0] == 17 ||
    !session.Redo() || !session.Redo() || session.LayerName != "Edited image")
    throw new Exception("Pixel and metadata history did not undo and redo together.");

string beforeSavePng = Path.Combine(output, "before-save.png");
ImageProjectWorkflow.ExportPng(session, beforeSavePng);
AssertRaster(edited, ImageCodec.Load(beforeSavePng));
if (!session.IsDirty) throw new Exception("Export changed the project save point.");
try
{
    ProjectStore.ExportPng(session, Path.Combine(output, "unsafe-raw-export.png"));
    throw new Exception("Raw export ignored unsaved pixel edits.");
}
catch (NotSupportedException) { }

ImageProjectWorkflow.Save(session, project);
if (session.IsDirty) throw new Exception("Save did not advance the save point.");
if (!session.Undo() || !session.IsDirty || !session.Redo() || session.IsDirty)
    throw new Exception("Undo/redo did not return to the saved pixel and metadata snapshot.");
var reopened = ImageProjectWorkflow.OpenEditable(project);
if (reopened.LayerName != "Edited image" || reopened.IsDirty || reopened.Raster is null)
    throw new Exception("Saved project did not reopen with its metadata and pixels.");
AssertRaster(edited, reopened.Raster);
ImageProjectWorkflow.Save(reopened, Path.Combine(output, "Edited.comp"));
string exportedPng = Path.Combine(output, "export.png");
ImageProjectWorkflow.ExportPng(reopened, exportedPng);
AssertRaster(edited, ImageCodec.Load(exportedPng));
string exportedJpeg = Path.Combine(output, "export.jpg");
ImageProjectWorkflow.ExportJpeg(reopened, exportedJpeg, 100, (20, 40, 60));
if (ImageCodec.Load(exportedJpeg).Width != edited.Width || reopened.IsDirty)
    throw new Exception("JPEG export changed dimensions or project state.");

session.Undo();
session.Undo();
if (!session.IsDirty) throw new Exception("Undo after save did not restore a dirty prior pixel snapshot.");
ImageProjectWorkflow.Save(session, project);
AssertRaster(ImageCodec.Load(sourcePng), ImageProjectWorkflow.OpenEditable(project).Raster!);
byte[] branchTile = session.Raster!.ReadTileCopy(0, 0);
branchTile[0] = 5;
branchTile[1] = 7;
branchTile[2] = 9;
branchTile[3] = 255;
session.ReplaceRaster(session.Raster.ReplaceTile(0, 0, branchTile));
if (session.Redo() || !session.IsDirty)
    throw new Exception("Editing after undo retained the abandoned redo branch.");

string sourceJpeg = Path.Combine(fixtures, "orientation-6.jpg");
string jpegProject = Path.Combine(output, "Oriented.comp");
var jpegSession = ImageProjectWorkflow.Import(sourceJpeg, jpegProject);
AssertRaster(ImageCodec.Load(sourceJpeg), jpegSession.Raster!);
AssertRaster(jpegSession.Raster!, ImageProjectWorkflow.OpenEditable(jpegProject).Raster!);
ImageProjectWorkflow.ExportPng(jpegSession, Path.Combine(output, "oriented-export.png"));

byte[] manifestBefore = File.ReadAllBytes(Path.Combine(project, "manifest.json"));
try
{
    ImageProjectWorkflow.Import(sourceJpeg, project);
    throw new Exception("Import replaced an existing project.");
}
catch (IOException) { }
if (!File.ReadAllBytes(Path.Combine(project, "manifest.json")).SequenceEqual(manifestBefore) ||
    Directory.GetDirectories(output, "Image.comp.import-*").Length != 0)
    throw new Exception("Rejected import changed the existing project or left temporary files.");
try
{
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "truncated.png"), Path.Combine(output, "Invalid.comp"));
    throw new Exception("Truncated image was imported.");
}
catch (InvalidDataException) { }
if (Directory.Exists(Path.Combine(output, "Invalid.comp")))
    throw new Exception("Rejected image import left a formal project.");

string mutable = Path.Combine(output, "Mutable.comp");
Directory.CreateDirectory(Path.Combine(mutable, "images"));
File.Copy(Path.Combine(project, "manifest.json"), Path.Combine(mutable, "manifest.json"));
string imageName = importedManifest["layers"]![0]!["imageFile"]!.GetValue<string>();
string mutableImage = Path.Combine(mutable, "images", imageName);
File.Copy(Path.Combine(project, "images", imageName), mutableImage);
var mutableSession = ImageProjectWorkflow.OpenEditable(mutable);
TileRaster memorySnapshot = mutableSession.Raster!;
using (var stream = new FileStream(mutableImage, FileMode.Append)) stream.WriteByte(1);
ImageProjectWorkflow.ExportPng(mutableSession, Path.Combine(output, "memory-export.png"));
AssertRaster(memorySnapshot, ImageCodec.Load(Path.Combine(output, "memory-export.png")));
mutableSession.ReplaceRaster(memorySnapshot.ReplaceTile(0, 0, edited.ReadTileCopy(0, 0)));
try
{
    ImageProjectWorkflow.Save(mutableSession, mutable);
    throw new Exception("Externally changed source asset was overwritten.");
}
catch (IOException) { }
if (!mutableSession.IsDirty || Directory.GetDirectories(output, "Mutable.comp.tmp-*").Length != 0)
    throw new Exception("Rejected changed-asset save altered the session or left temporary files.");

CheckCompositing(output);
if (args.Length == 3) CheckMacProduced(Path.GetFullPath(args[2]), output);
Console.WriteLine("PASS: v8 PNG/JPEG import, pixel and metadata history, safe save, reopen, PNG/JPEG export, prior snapshot restore, changed-asset isolation, rejected import, flat Normal project composition" +
    (args.Length == 3 ? ", Mac-produced v8 continuation and non-default protection" : ""));

static void AssertRaster(TileRaster expected, TileRaster actual)
{
    if (expected.Width != actual.Width || expected.Height != actual.Height)
        throw new Exception("Raster dimensions differ.");
    for (int row = 0; row * TileRaster.TileSize < expected.Height; row++)
    for (int column = 0; column * TileRaster.TileSize < expected.Width; column++)
        if (!expected.ReadTileCopy(column, row).SequenceEqual(actual.ReadTileCopy(column, row)))
            throw new Exception($"Raster differs at tile {column},{row}.");
}

static void CheckCompositing(string output)
{
    var bottom = new TileRaster(300, 300);
    for (int row = 0; row < 2; row++)
    for (int column = 0; column < 2; column++)
    {
        var size = bottom.TileDimensions(column, row);
        var tile = new byte[size.Width * size.Height * 4];
        for (int i = 0; i < tile.Length; i += 4)
        {
            tile[i] = 40; tile[i + 1] = 80; tile[i + 2] = 120; tile[i + 3] = 200;
        }
        bottom = bottom.ReplaceTile(column, row, tile);
    }
    var top = new TileRaster(300, 300);
    byte[] left = top.ReadTileCopy(0, 0);
    new byte[] { 80, 20, 40, 128 }.CopyTo(left, 0);
    new byte[] { 255, 0, 0, 255 }.CopyTo(left, 8);
    top = top.ReplaceTile(0, 0, left);
    byte[] right = top.ReadTileCopy(1, 0);
    new byte[] { 0, 128, 0, 128 }.CopyTo(right, 0);
    top = top.ReplaceTile(1, 0, right);
    byte[] edge = top.ReadTileCopy(1, 1);
    new byte[] { 0, 0, 128, 128 }.CopyTo(edge, edge.Length - 4);
    top = top.ReplaceTile(1, 1, edge);

    TileRaster result = RasterCompositor.SourceOver(bottom, top);
    if (!Pixel(result, 0, 0).SequenceEqual(new byte[] { 100, 60, 100, 228 }) ||
        !Pixel(result, 1, 0).SequenceEqual(new byte[] { 40, 80, 120, 200 }) ||
        !Pixel(result, 2, 0).SequenceEqual(new byte[] { 255, 0, 0, 255 }) ||
        !Pixel(result, 256, 0).SequenceEqual(new byte[] { 20, 168, 60, 228 }) ||
        !Pixel(result, 299, 299).SequenceEqual(new byte[] { 20, 40, 188, 228 }) ||
        !Pixel(bottom, 0, 0).SequenceEqual(new byte[] { 40, 80, 120, 200 }) ||
        !Pixel(top, 0, 0).SequenceEqual(new byte[] { 80, 20, 40, 128 }))
        throw new Exception("Normal tile composition produced wrong pixels or changed an input.");

    string flat = Path.Combine(output, "Flat.comp");
    string images = Path.Combine(flat, "images");
    Directory.CreateDirectory(images);
    var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "Image.comp", "manifest.json")))!.AsObject();
    manifest["width"] = 300;
    manifest["height"] = 300;
    var baseLayer = manifest["layers"]![0]!.AsObject();
    baseLayer["transform"]!["size"]![0] = 300;
    baseLayer["transform"]!["size"]![1] = 300;
    var topLayer = (JsonObject)baseLayer.DeepClone();
    string topId = Guid.NewGuid().ToString("D");
    topLayer["id"] = topId;
    topLayer["imageFile"] = topId.ToUpperInvariant() + ".png";
    topLayer["name"] = "Top";
    manifest["layers"]!.AsArray().Add(topLayer);
    ImageCodec.SavePng(bottom, Path.Combine(images, baseLayer["imageFile"]!.GetValue<string>()));
    ImageCodec.SavePng(top, Path.Combine(images, topLayer["imageFile"]!.GetValue<string>()));
    string manifestPath = Path.Combine(flat, "manifest.json");
    string originalManifest = manifest.ToJsonString();
    File.WriteAllText(manifestPath, originalManifest);
    if (ProjectStore.Open(flat).CanEdit)
        throw new Exception("Multi-layer project was made editable without full write support.");
    AssertRaster(result, ImageProjectWorkflow.RenderFlatNormal(flat));
    try
    {
        topLayer["isVisible"] = false;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        AssertRaster(bottom, ImageProjectWorkflow.RenderFlatNormal(flat));
        topLayer["isVisible"] = true;
        topLayer["blendMode"] = "Multiply";
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        try
        {
            ImageProjectWorkflow.RenderFlatNormal(flat);
            throw new Exception("Unsupported blend mode was rendered as Normal.");
        }
        catch (NotSupportedException) { }
        topLayer.Remove("blendMode");
        topLayer["opacity"] = 0.5;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        try
        {
            ImageProjectWorkflow.RenderFlatNormal(flat);
            throw new Exception("Unsupported opacity was rendered as fully opaque.");
        }
        catch (NotSupportedException) { }
        topLayer.Remove("opacity");
        topLayer["transform"]!["origin"]![0] = 1;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        try
        {
            ImageProjectWorkflow.RenderFlatNormal(flat);
            throw new Exception("Unsupported transform was rendered at the origin.");
        }
        catch (NotSupportedException) { }
    }
    finally { File.WriteAllText(manifestPath, originalManifest); }

    string exported = Path.Combine(output, "composite.png");
    ImageCodec.SavePng(ImageProjectWorkflow.RenderFlatNormal(flat), exported);
    AssertRaster(result, ImageCodec.Load(exported));
    try
    {
        RasterCompositor.SourceOver(bottom, new TileRaster(1, 1));
        throw new Exception("Mismatched layer dimensions were accepted.");
    }
    catch (ArgumentException) { }
    byte[] invalidTile = new byte[256 * 256 * 4];
    invalidTile[0] = 200; invalidTile[3] = 100;
    try
    {
        RasterCompositor.SourceOver(bottom, new TileRaster(300, 300).ReplaceTile(0, 0, invalidTile));
        throw new Exception("Invalid premultiplied source was accepted.");
    }
    catch (InvalidDataException) { }
}

static byte[] Pixel(TileRaster raster, int x, int y)
{
    int column = x / TileRaster.TileSize, row = y / TileRaster.TileSize;
    var size = raster.TileDimensions(column, row);
    byte[] tile = raster.ReadTileCopy(column, row);
    int offset = ((y % TileRaster.TileSize) * size.Width + x % TileRaster.TileSize) * 4;
    return tile.AsSpan(offset, 4).ToArray();
}

static void CheckMacProduced(string macProjects, string output)
{
    foreach (string name in new[] { "Image", "Edited", "Oriented" })
    {
        string source = Path.Combine(macProjects, name + ".comp");
        var originalManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "manifest.json")))!;
        var originalLayer = originalManifest["layers"]![0]!;
        if (originalLayer["blendMode"]!.GetValue<string>() != "Normal" ||
            originalLayer["opacity"]!.GetValue<double>() != 1 ||
            originalLayer["isGroup"]!.GetValue<bool>())
            throw new Exception("Mac fixture does not contain explicit default layer fields.");
        var session = ImageProjectWorkflow.OpenEditable(source);
        TileRaster originalRaster = session.Raster!;
        string destination = Path.Combine(output, "MacContinued-" + name + ".comp");
        ImageProjectWorkflow.Save(session, destination);
        AssertRaster(originalRaster, ImageProjectWorkflow.OpenEditable(destination).Raster!);
        byte[] tile = originalRaster.ReadTileCopy(0, 0);
        tile[0] = 11; tile[1] = 13; tile[2] = 17; tile[3] = 255;
        TileRaster changed = originalRaster.ReplaceTile(0, 0, tile);
        session.ReplaceRaster(changed);
        session.RenameLayer("Windows continued " + name);
        string preview = Path.Combine(output, "MacContinued-" + name + ".png");
        ImageProjectWorkflow.ExportPng(session, preview);
        AssertRaster(changed, ImageCodec.Load(preview));
        ImageProjectWorkflow.Save(session, destination);
        var reopened = ImageProjectWorkflow.OpenEditable(destination);
        if (session.IsDirty || reopened.LayerName != "Windows continued " + name || reopened.IsDirty)
            throw new Exception("Mac-produced project continuation lost its save point or name.");
        AssertRaster(changed, reopened.Raster!);
        var savedManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "manifest.json")))!;
        var savedLayer = savedManifest["layers"]![0]!;
        if (savedManifest["documentID"]!.GetValue<string>() != originalManifest["documentID"]!.GetValue<string>() ||
            savedLayer["id"]!.GetValue<string>() != originalLayer["id"]!.GetValue<string>() ||
            savedLayer["blendMode"]!.GetValue<string>() != "Normal" ||
            savedLayer["opacity"]!.GetValue<double>() != 1 || savedLayer["isGroup"]!.GetValue<bool>())
            throw new Exception("Mac-produced project identity or default fields were lost.");
    }

    string baseProject = Path.Combine(macProjects, "Image.comp");
    var baseManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(baseProject, "manifest.json")))!;
    string imageName = baseManifest["layers"]![0]!["imageFile"]!.GetValue<string>();
    foreach (string field in new[] { "blendMode", "opacity", "isGroup" })
    {
        string target = Path.Combine(output, "NonDefault-" + field + ".comp");
        Directory.CreateDirectory(Path.Combine(target, "images"));
        File.Copy(Path.Combine(baseProject, "images", imageName), Path.Combine(target, "images", imageName));
        var manifest = baseManifest.DeepClone();
        manifest["layers"]![0]![field] = field switch
        {
            "blendMode" => "Multiply",
            "opacity" => 0.5,
            _ => true
        };
        File.WriteAllText(Path.Combine(target, "manifest.json"), manifest.ToJsonString());
        if (field == "isGroup")
        {
            try
            {
                ProjectStore.Open(target);
                throw new Exception("Group with image asset was accepted.");
            }
            catch (InvalidDataException) { }
        }
        else if (ProjectStore.Open(target).CanEdit)
            throw new Exception($"Non-default {field} layer was made editable without rendering support.");
    }
}
