using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Compositor.Workflow.Checks;

if (args.Length is not (2 or 3 or 4)) throw new ArgumentException("Usage: Compositor.Workflow.Checks <image fixtures> <new output directory> [Mac-produced projects] [Mac-produced flat project]");
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
string legacy = Path.Combine(output, "Legacy.comp");
Directory.CreateDirectory(Path.Combine(legacy, "images"));
File.Copy(Path.Combine(project, "images", importedManifest["layers"]![0]!["imageFile"]!.GetValue<string>()),
    Path.Combine(legacy, "images", importedManifest["layers"]![0]!["imageFile"]!.GetValue<string>()));
var legacyManifest = (JsonObject)importedManifest.DeepClone();
legacyManifest["version"] = 1;
File.WriteAllText(Path.Combine(legacy, "manifest.json"), legacyManifest.ToJsonString());
var legacySession = ImageProjectWorkflow.OpenEditable(legacy);
string legacyExport = Path.Combine(output, "legacy-export.png");
ImageProjectWorkflow.ExportPng(legacySession, legacyExport);
AssertRaster(session.Raster, ImageCodec.Load(legacyExport));
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
AssertRaster(edited, ImageProjectWorkflow.RenderFlatNormal(session, Guid.Parse(importedId), edited));
if (session.IsDirty || session.Undo()) throw new Exception("Temporary single-layer preview changed history.");
AssertRaster(ImageCodec.Load(sourcePng), ImageProjectWorkflow.RenderFlatNormal(session));
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

var historySession = ImageProjectWorkflow.OpenEditable(project);
for (int i = 1; i <= 105; i++) historySession.RenameLayer($"History {i}");
for (int i = 0; i < 100; i++)
    if (!historySession.Undo()) throw new Exception("History lost a retained undo step.");
if (historySession.Undo() || historySession.LayerName != "History 5" || !historySession.IsDirty)
    throw new Exception("History exceeded 100 undo steps or lost its saved-state marker.");
for (int i = 0; i < 100; i++)
    if (!historySession.Redo()) throw new Exception("History lost a retained redo step.");
if (historySession.Redo() || historySession.LayerName != "History 105" ||
    !historySession.Undo()) throw new Exception("History redo boundary is wrong.");
historySession.RenameLayer("New branch");
if (historySession.Redo() || historySession.LayerName != "New branch")
    throw new Exception("A new edit retained an abandoned redo branch.");

CheckCompositing(output, fixtures);
CheckCachedGroupRendering(output, fixtures);
CheckCachedGroupTransform(output, fixtures);
CheckGroupStructureCreation(output, fixtures);
CheckGroupedLayerViaCopy(output, fixtures);
CheckGroupedLeafLayerViaCopy(output, fixtures);
CheckGroupedClippingStackLayerViaCopy(output, fixtures);
CheckGroupedNestedClippingStackLayerViaCopy(output, fixtures);
CheckGroupedDiscontinuousClippingStackLayerViaCopy(output, fixtures);
CheckCrossParentGroupedLeafLayerViaCopy(output, fixtures);
CheckTransformedGroupedLeafLayerViaCopy(output, fixtures);
CheckEditableLayerTransform(output, fixtures);
CheckEditableGroupMask(output, fixtures);
CheckClippingMask(output);
CheckInvalidClippingRelationships(output);
CheckLayerStructure(output, sourcePng);
CheckNewCanvas(output);
CheckLayerSelection(output);
CheckExposureAdjustment(output);
CheckLevelsAdjustment(output);
CheckHueSaturationAdjustment(output);
CheckCurvesAdjustment(output);
CheckGradientMapAdjustment(output);
CheckGaussianBlurAdjustment(output);
CheckSelectionGaussianBlurFilter(output);
CheckSelectionMotionBlurFilter(output);
CheckMotionBlurAdjustment(output);
CheckNoiseAdjustment(output);
CheckLensCorrectionAdjustment(output);
CheckGrainAdjustment(output);
BlendChecks.Run(output, Path.GetFullPath(Path.Combine(fixtures, "..", "..", "..", "docs", "windows", "fixtures")));
TextChecks.Run(Path.GetFullPath(Path.Combine(fixtures, "..", "..", "..", "docs", "windows", "fixtures")), output);
FontLibraryChecks.Run(Path.GetFullPath(Path.Combine(fixtures, "..", "..", "..", "docs", "windows", "fixtures")), output);
if (args.Length == 3) CheckMacProduced(Path.GetFullPath(args[2]), output);
if (args.Length == 4)
{
    CheckMacProduced(Path.GetFullPath(args[2]), output);
    CheckMacFlatProduced(Path.GetFullPath(args[3]), output);
}
Console.WriteLine("PASS: v1 export, v8 PNG/JPEG import, pixel and metadata history with 100-step bound, safe save, reopen, PNG/JPEG export, prior snapshot restore, changed-asset isolation, rejected import, flat multi-layer pixel/metadata edit/save, layer add/duplicate/delete, empty project and deleted-asset undo, unsaved new canvas and first save, Normal and Gray8 mask composition, flat clipping-mask alpha composition" +
    (args.Length >= 3 ? ", Mac-produced v8 continuation and non-default protection" : "") +
    (args.Length == 4 ? ", Mac-produced flat layer pixel continuation" : ""));

static void AssertRaster(TileRaster expected, TileRaster actual)
{
    if (expected.Width != actual.Width || expected.Height != actual.Height)
        throw new Exception("Raster dimensions differ.");
    for (int row = 0; row * TileRaster.TileSize < expected.Height; row++)
    for (int column = 0; column * TileRaster.TileSize < expected.Width; column++)
        if (!expected.ReadTileCopy(column, row).SequenceEqual(actual.ReadTileCopy(column, row)))
            throw new Exception($"Raster differs at tile {column},{row}.");
}

static void CheckCachedGroupRendering(string output, string fixtures)
{
    string referenceRoot = Path.GetFullPath(Path.Combine(fixtures, "..", "..", "..", "docs", "windows", "fixtures"));
    foreach (string name in new[] { "F02", "F05", "F06" })
    {
        string project = Path.Combine(referenceRoot, name + ".comp");
        string reference = CachedReferencePath(referenceRoot, name);
        var session = ProjectStore.Open(project);
        if (session.CanEdit) throw new Exception($"Cached group fixture {name} became editable.");
        TileRaster actual = ImageProjectWorkflow.RenderFlatNormal(session);
        ImageCodec.SavePng(actual, Path.Combine(output, name + "-actual.png"));
        try { AssertRaster(ImageCodec.Load(reference), actual); }
        catch (Exception error) { throw new Exception($"Cached group fixture {name} differs: {error.Message}", error); }
    }
    Console.WriteLine("PASS: cached pass-through groups, child visibility, raster masks, clipping alpha and group masks match F02/F05/F06 references");
}

static string CachedReferencePath(string referenceRoot, string name) =>
    OperatingSystem.IsWindows() && name is ("F05" or "F06")
        ? Path.Combine(referenceRoot, "windows", name + ".png")
        : Path.Combine(referenceRoot, name + "-mac.png");

static void CheckCachedGroupTransform(string output, string fixtures)
{
    string referenceRoot = Path.GetFullPath(Path.Combine(fixtures, "..", "..", "..", "docs", "windows", "fixtures"));
    string source = Path.Combine(referenceRoot, "F02.comp");
    string transformed = Path.Combine(output, "F02-GroupFlip.comp");
    Directory.CreateDirectory(Path.Combine(transformed, "images"));
    foreach (string asset in Directory.GetFiles(Path.Combine(source, "images")))
        File.Copy(asset, Path.Combine(transformed, "images", Path.GetFileName(asset)));
    var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "manifest.json")))!.AsObject();
    manifest["layers"]![0]!["transform"]!["flipX"] = true;
    File.WriteAllText(Path.Combine(transformed, "manifest.json"), manifest.ToJsonString());

    TileRaster baseline = ImageProjectWorkflow.RenderFlatNormal(ProjectStore.Open(source));
    TileRaster actual = ImageProjectWorkflow.RenderFlatNormal(ProjectStore.Open(transformed));
    AssertRaster(HorizontalFlip(baseline), actual);
    Console.WriteLine("PASS: cached pass-through group horizontal transform preserves canvas geometry");
}

static TileRaster HorizontalFlip(TileRaster source)
{
    var result = new TileRaster(source.Width, source.Height);
    for (int row = 0; row * TileRaster.TileSize < source.Height; row++)
    for (int column = 0; column * TileRaster.TileSize < source.Width; column++)
    {
        var size = result.TileDimensions(column, row);
        var tile = new byte[size.Width * size.Height * 4];
        for (int y = 0; y < size.Height; y++)
        for (int x = 0; x < size.Width; x++)
        {
            int targetX = column * TileRaster.TileSize + x;
            int sourceX = source.Width - 1 - targetX;
            byte[] sourceTile = source.ReadTileCopy(sourceX / TileRaster.TileSize, (row * TileRaster.TileSize + y) / TileRaster.TileSize);
            int sourceWidth = source.TileDimensions(sourceX / TileRaster.TileSize, (row * TileRaster.TileSize + y) / TileRaster.TileSize).Width;
            int sourceLocalX = sourceX % TileRaster.TileSize;
            int sourceLocalY = (row * TileRaster.TileSize + y) % TileRaster.TileSize;
            sourceTile.AsSpan((sourceLocalY * sourceWidth + sourceLocalX) * 4, 4)
                .CopyTo(tile.AsSpan((y * size.Width + x) * 4, 4));
        }
        result = result.ReplaceTile(column, row, tile);
    }
    return result;
}

static void CheckEditableGroupMask(string output, string fixtures)
{
    string sourceRoot = Path.GetFullPath(Path.Combine(fixtures, "..", "..", "..", "docs", "windows", "fixtures", "F06.comp"));
    string project = Path.Combine(output, "EditableGroup.comp");
    Directory.CreateDirectory(Path.Combine(project, "images"));
    foreach (string asset in Directory.GetFiles(Path.Combine(sourceRoot, "images")))
        File.Copy(asset, Path.Combine(project, "images", Path.GetFileName(asset)));
    var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(sourceRoot, "manifest.json")))!.AsObject();
    manifest["version"] = 8;
    File.WriteAllText(Path.Combine(project, "manifest.json"), manifest.ToJsonString());

    var opened = ProjectStore.Open(project);
    if (!opened.CanEdit || !opened.HasGroups) throw new Exception("v8 group project was not accepted for editable loading.");
    var session = ImageProjectWorkflow.OpenEditable(project);
    Guid groupId = session.Layers.Single(layer => layer.IsGroup).Id;
    string referenceRoot = Path.GetFullPath(Path.Combine(fixtures, "..", "..", "..", "docs", "windows", "fixtures"));
    TileRaster editableInitial = ImageProjectWorkflow.RenderFlatNormal(session);
    AssertRaster(ImageProjectWorkflow.RenderFlatNormal(ProjectStore.Open(sourceRoot)), editableInitial);
    TileRaster reference = ImageCodec.Load(CachedReferencePath(referenceRoot, "F06"));
    AssertRaster(reference, editableInitial);
    ExpectNotSupported(() => session.AddBlankLayer("Rejected", 0), "adding a layer to a grouped project");
    ExpectNotSupported(() => session.DuplicateLayer(groupId, "Rejected"), "duplicating a group");
    ExpectNotSupported(() => session.DeleteLayer(groupId), "deleting a group");
    ExpectNotSupported(() => session.MoveLayer(groupId, 0), "reordering a grouped project");
    ExpectNotSupported(() => session.SetLayerMaskSource(groupId, null), "editing clipping structure in a grouped project");
    if (session.IsDirty) throw new Exception("Rejected grouped structure edit changed history.");
    session.SetLayerVisible(groupId, false);
    AssertRaster(new TileRaster(session.Width, session.Height), ImageProjectWorkflow.RenderFlatNormal(session));
    if (!session.Undo() || !SameRaster(reference, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Group visibility undo did not restore the preview.");
    session.SetLayerOpacity(groupId, 0.5);
    TileRaster halfOpacity = ImageProjectWorkflow.RenderFlatNormal(session);
    if (SameRaster(reference, halfOpacity)) throw new Exception("Group opacity did not affect the preview.");
    if (!session.Undo() || !SameRaster(reference, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Group opacity undo did not restore the preview.");
    session.SetLayerMaskEnabled(groupId, false);
    TileRaster disabled = ImageProjectWorkflow.RenderFlatNormal(session);
    if (SameRaster(reference, disabled)) throw new Exception("Disabling an editable group mask did not change the preview.");
    if (!session.Undo() || !SameRaster(reference, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Group mask undo did not restore the preview.");
    var replacement = GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width / 2, session.Height);
    session.ReplaceLayerMask(groupId, replacement);
    TileRaster edited = ImageProjectWorkflow.RenderFlatNormal(session);
    if (SameRaster(reference, edited) || !session.IsDirty) throw new Exception("Editable group mask replacement was not rendered or recorded.");
    string saved = Path.Combine(output, "EditableGroupSaved.comp");
    ImageProjectWorkflow.Save(session, saved);
    var reopened = ImageProjectWorkflow.OpenEditable(saved);
    AssertRaster(edited, ImageProjectWorkflow.RenderFlatNormal(reopened));
    if (reopened.IsDirty || !reopened.Layers.Single(layer => layer.IsGroup).HasMask)
        throw new Exception("Saved editable group mask did not reopen cleanly.");
    Console.WriteLine("PASS: v8 pass-through group mask editable load, toggle, replacement, undo and save/reopen");
}

static void CheckGroupStructureCreation(string output, string fixtures)
{
    string source = Path.Combine(output, "GroupSource.comp");
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "alpha-tiles.png"), source);
    var session = ImageProjectWorkflow.OpenEditable(source);
    Guid leafId = session.Layers.Single().Id;
    Guid secondId = session.AddBlankLayer("Second", 1);
    TileRaster baseline = ImageProjectWorkflow.RenderFlatNormal(session);
    Guid groupId = session.GroupLayers([leafId, secondId], "Pass-through");
    if (!session.HasGroups || session.Layers.Single(layer => layer.Id == leafId).ParentId != groupId ||
        session.Layers.Single(layer => layer.Id == secondId).ParentId != groupId ||
        session.Layers.Single(layer => layer.Id == groupId).HasMask ||
        !SameRaster(baseline, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Grouping a root raster did not preserve the pass-through render.");
    string grouped = Path.Combine(output, "GroupedCreated.comp");
    ImageProjectWorkflow.Save(session, grouped);
    var reopened = ImageProjectWorkflow.OpenEditable(grouped);
    if (!reopened.CanEdit || !reopened.HasGroups || !SameRaster(baseline, ImageProjectWorkflow.RenderFlatNormal(reopened)))
        throw new Exception("Created group did not survive save and reopen.");
    var transformed = ImageProjectWorkflow.OpenEditable(grouped);
    transformed.FlipGroup(groupId, horizontal: true);
    if (!SameRaster(HorizontalFlip(baseline), ImageProjectWorkflow.RenderFlatNormal(transformed)) || !transformed.IsDirty)
        throw new Exception("Editable group horizontal transform did not render or record.");
    if (!transformed.Undo() || !SameRaster(baseline, ImageProjectWorkflow.RenderFlatNormal(transformed)) || transformed.IsDirty)
        throw new Exception("Editable group transform undo did not restore the saved render.");
    if (!transformed.Redo() || !SameRaster(HorizontalFlip(baseline), ImageProjectWorkflow.RenderFlatNormal(transformed)))
        throw new Exception("Editable group transform redo did not restore the transformed render.");
    string transformedPath = Path.Combine(output, "GroupedFlipped.comp");
    ImageProjectWorkflow.Save(transformed, transformedPath);
    var transformedReopened = ImageProjectWorkflow.OpenEditable(transformedPath);
    if (!transformedReopened.CanEdit || !SameRaster(HorizontalFlip(baseline), ImageProjectWorkflow.RenderFlatNormal(transformedReopened)))
        throw new Exception("Editable group transform did not survive save and reopen.");
    var baked = ImageProjectWorkflow.OpenEditable(grouped);
    baked.RotateGroup90(groupId, clockwise: true);
    TileRaster bakedReference = ImageProjectWorkflow.RenderFlatNormal(baked);
    ImageProjectWorkflow.BakeGroupTransform(baked, groupId);
    if (baked.HasGroups || baked.Layers.Count != 1 || !SameRaster(bakedReference, ImageProjectWorkflow.RenderFlatNormal(baked)))
        throw new Exception("Baking a transformed group did not preserve the preview or flatten the subtree.");
    if (!baked.Undo() || !baked.HasGroups || !SameRaster(bakedReference, ImageProjectWorkflow.RenderFlatNormal(baked)))
        throw new Exception("Baked group undo did not restore the transformed group preview.");
    if (!baked.Redo() || baked.HasGroups || !SameRaster(bakedReference, ImageProjectWorkflow.RenderFlatNormal(baked)))
        throw new Exception("Baked group redo did not restore the flattened preview.");
    string bakedPath = Path.Combine(output, "BakedGroup.comp");
    ImageProjectWorkflow.Save(baked, bakedPath);
    var bakedReopened = ImageProjectWorkflow.OpenEditable(bakedPath);
    if (bakedReopened.HasGroups || !SameRaster(bakedReference, ImageProjectWorkflow.RenderFlatNormal(bakedReopened)))
        throw new Exception("Baked transformed group did not survive save and reopen.");
    var bakedMasked = ImageProjectWorkflow.OpenEditable(grouped);
    bakedMasked.EnsureLayerMask(groupId);
    bakedMasked.ReplaceLayerMask(groupId,
        GrayTileRaster.Rectangle(bakedMasked.Width, bakedMasked.Height, 0, 0, bakedMasked.Width / 2, bakedMasked.Height));
    bakedMasked.SetLayerOpacity(groupId, 0.5);
    bakedMasked.SetLayerBlendMode(groupId, "Multiply");
    bakedMasked.FlipGroup(groupId, horizontal: true);
    TileRaster bakedMaskedReference = ImageProjectWorkflow.RenderFlatNormal(bakedMasked);
    ImageProjectWorkflow.BakeGroupTransform(bakedMasked, groupId);
    if (bakedMasked.HasGroups || !SameRaster(bakedMaskedReference, ImageProjectWorkflow.RenderFlatNormal(bakedMasked)))
        throw new Exception("Baking a masked styled group did not preserve the preview.");
    var scaled = ImageProjectWorkflow.OpenEditable(grouped);
    scaled.ScaleGroup(groupId, 0.5);
    TileRaster scaledPreview = ImageProjectWorkflow.RenderFlatNormal(scaled);
    if (SameRaster(baseline, scaledPreview) || !scaled.IsDirty)
        throw new Exception("Editable group scale did not render or record.");
    if (!scaled.Undo() || !SameRaster(baseline, ImageProjectWorkflow.RenderFlatNormal(scaled)) || scaled.IsDirty)
        throw new Exception("Editable group scale undo did not restore the saved render.");
    if (!scaled.Redo() || !SameRaster(scaledPreview, ImageProjectWorkflow.RenderFlatNormal(scaled)))
        throw new Exception("Editable group scale redo did not restore the scaled render.");
    var rotated = ImageProjectWorkflow.OpenEditable(grouped);
    rotated.RotateGroup90(groupId, clockwise: true);
    TileRaster rotatedPreview = ImageProjectWorkflow.RenderFlatNormal(rotated);
    if (SameRaster(baseline, rotatedPreview) || !rotated.IsDirty)
        throw new Exception("Editable group rotation did not render or record.");
    if (!rotated.Undo() || !SameRaster(baseline, ImageProjectWorkflow.RenderFlatNormal(rotated)) || rotated.IsDirty)
        throw new Exception("Editable group rotation undo did not restore the saved render.");
    var freelyRotated = ImageProjectWorkflow.OpenEditable(grouped);
    freelyRotated.RotateGroupTransform(groupId, 15);
    TileRaster freelyRotatedPreview = ImageProjectWorkflow.RenderFlatNormal(freelyRotated);
    if (SameRaster(baseline, freelyRotatedPreview) || !freelyRotated.IsDirty ||
        !freelyRotated.Undo() || !SameRaster(baseline, ImageProjectWorkflow.RenderFlatNormal(freelyRotated)) ||
        freelyRotated.IsDirty || !freelyRotated.Redo() ||
        !SameRaster(freelyRotatedPreview, ImageProjectWorkflow.RenderFlatNormal(freelyRotated)))
        throw new Exception("Editable group free rotation did not preserve preview or history.");
    var moved = ImageProjectWorkflow.OpenEditable(grouped);
    moved.MoveGroup(groupId, 12, -7);
    TileRaster movedPreview = ImageProjectWorkflow.RenderFlatNormal(moved);
    if (SameRaster(baseline, movedPreview) || !moved.IsDirty)
        throw new Exception("Editable group move did not render or record.");
    if (!moved.Undo() || !SameRaster(baseline, ImageProjectWorkflow.RenderFlatNormal(moved)) || moved.IsDirty)
        throw new Exception("Editable group move undo did not restore the saved render.");
    if (!moved.Redo() || !SameRaster(movedPreview, ImageProjectWorkflow.RenderFlatNormal(moved)))
        throw new Exception("Editable group move redo did not restore the moved render.");
    Guid innerGroupId = reopened.GroupLayer(leafId, "Inner");
    Guid outerGroupId = reopened.GroupLayer(groupId, "Outer");
    if (reopened.Layers.Single(layer => layer.Id == outerGroupId).ParentId is not null ||
        reopened.Layers.Single(layer => layer.Id == groupId).ParentId != outerGroupId ||
        reopened.Layers.Single(layer => layer.Id == innerGroupId).ParentId != groupId ||
        reopened.Layers.Single(layer => layer.Id == leafId).ParentId != innerGroupId ||
        !SameRaster(baseline, ImageProjectWorkflow.RenderFlatNormal(reopened)))
        throw new Exception("Nested pass-through groups changed hierarchy or render.");
    string nested = Path.Combine(output, "NestedGrouped.comp");
    ImageProjectWorkflow.Save(reopened, nested);
    var nestedReopened = ImageProjectWorkflow.OpenEditable(nested);
    if (nestedReopened.Layers.Single(layer => layer.Id == groupId).ParentId != outerGroupId ||
        nestedReopened.Layers.Single(layer => layer.Id == innerGroupId).ParentId != groupId ||
        !SameRaster(baseline, ImageProjectWorkflow.RenderFlatNormal(nestedReopened)))
        throw new Exception("Nested groups did not survive save and reopen.");
    nestedReopened.UngroupLayer(outerGroupId);
    if (nestedReopened.Layers.Single(layer => layer.Id == groupId).ParentId is not null ||
        !SameRaster(baseline, ImageProjectWorkflow.RenderFlatNormal(nestedReopened)))
        throw new Exception("Ungrouping a nested root group did not preserve hierarchy or render.");
    nestedReopened.UngroupLayer(groupId);
    nestedReopened.UngroupLayer(innerGroupId);
    if (nestedReopened.HasGroups || !SameRaster(baseline, ImageProjectWorkflow.RenderFlatNormal(nestedReopened)))
        throw new Exception("Ungrouping nested groups did not restore the flat render.");
    ImageProjectWorkflow.Save(nestedReopened, Path.Combine(output, "UngroupedCreated.comp"));
    var masked = ImageProjectWorkflow.OpenEditable(grouped);
    masked.EnsureLayerMask(groupId);
    masked.ReplaceLayerMask(groupId, GrayTileRaster.Rectangle(masked.Width, masked.Height, 0, 0, masked.Width / 2, masked.Height));
    TileRaster maskedReference = ImageProjectWorkflow.RenderFlatNormal(masked);
    masked.FlipGroup(groupId, horizontal: true);
    TileRaster maskedFlipped = ImageProjectWorkflow.RenderFlatNormal(masked);
    if (SameRaster(maskedReference, maskedFlipped) || !masked.IsDirty)
        throw new Exception("Enabled group mask did not follow the group transform.");
    if (!masked.Undo() || !SameRaster(maskedReference, ImageProjectWorkflow.RenderFlatNormal(masked)))
        throw new Exception("Enabled group mask transform undo did not restore the preview.");
    masked.SetLayerMaskEnabled(groupId, false);
    TileRaster disabledMaskReference = ImageProjectWorkflow.RenderFlatNormal(masked);
    if (SameRaster(maskedReference, disabledMaskReference))
        throw new Exception("Disabling an enabled group mask did not change the preview before ungroup.");
    masked.UngroupLayer(groupId);
    if (masked.HasGroups || !SameRaster(disabledMaskReference, ImageProjectWorkflow.RenderFlatNormal(masked)))
        throw new Exception("Ungrouping a disabled group mask did not preserve the preview.");
    string maskedUngrouped = Path.Combine(output, "MaskedUngrouped.comp");
    ImageProjectWorkflow.Save(masked, maskedUngrouped);
    var maskedReopened = ImageProjectWorkflow.OpenEditable(maskedUngrouped);
    if (!SameRaster(disabledMaskReference, ImageProjectWorkflow.RenderFlatNormal(maskedReopened)))
        throw new Exception("Disabled group-mask ungroup did not survive save and reopen.");
    var clipped = ImageProjectWorkflow.OpenEditable(source);
    Guid clippedBaseId = clipped.Layers.Single().Id;
    Guid clippedChildId = clipped.AddBlankLayer("Clipped", 1);
    clipped.SetLayerMaskSource(clippedChildId, clippedBaseId);
    Guid clippedGroupId = clipped.GroupLayers([clippedBaseId, clippedChildId], "Clipped group");
    clipped.EnsureLayerMask(clippedGroupId);
    clipped.ReplaceLayerMask(clippedGroupId,
        GrayTileRaster.Rectangle(clipped.Width, clipped.Height, 0, 0, clipped.Width / 2, clipped.Height));
    TileRaster clippedReference = ImageProjectWorkflow.RenderFlatNormal(clipped);
    clipped.UngroupLayer(clippedGroupId);
    if (clipped.HasGroups || !SameRaster(clippedReference, ImageProjectWorkflow.RenderFlatNormal(clipped)))
        throw new Exception("Ungrouping a masked clipping stack did not preserve the preview.");
    string clippedUngrouped = Path.Combine(output, "ClippedUngrouped.comp");
    ImageProjectWorkflow.Save(clipped, clippedUngrouped);
    var clippedReopened = ImageProjectWorkflow.OpenEditable(clippedUngrouped);
    if (!SameRaster(clippedReference, ImageProjectWorkflow.RenderFlatNormal(clippedReopened)))
        throw new Exception("Masked clipping-stack ungroup did not survive save and reopen.");
    Console.WriteLine("PASS: root, nested and transformed masked groups preserve render through group/ungroup and save/reopen");
}

static void CheckGroupedLayerViaCopy(string output, string fixtures)
{
    string source = Path.Combine(output, "GroupedLayerCopySource.comp");
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "alpha-tiles.png"), source);
    var session = ImageProjectWorkflow.OpenEditable(source);
    Guid baseId = session.Layers.Single().Id;
    Guid overlayId = session.AddBlankLayer("Group overlay", 1);
    TileRaster overlayRaster = session.GetLayerRaster(overlayId);
    var size = overlayRaster.TileDimensions(0, 0);
    byte[] tile = overlayRaster.ReadTileCopy(0, 0);
    int offset = (12 * size.Width + 12) * 4;
    tile[offset] = 150; tile[offset + 1] = 70; tile[offset + 2] = 30; tile[offset + 3] = 180;
    session.ReplaceLayerRaster(overlayId, overlayRaster.ReplaceTile(0, 0, tile));
    session.SetLayerOpacity(baseId, 0.72);
    session.SetLayerBlendMode(baseId, "Multiply");
    session.SetLayerOpacity(overlayId, 0.63);
    session.SetLayerBlendMode(overlayId, "Screen");
    session.SetLayerMaskSource(overlayId, baseId);
    Guid innerGroupId = session.GroupLayers([baseId, overlayId], "Visible group");
    session.SetLayerOpacity(innerGroupId, 0.82);
    session.SetLayerBlendMode(innerGroupId, "Multiply");
    session.SetGroupTransform(innerGroupId, 4, 3, session.Width - 8, session.Height - 6, 8);
    session.EnsureLayerMask(innerGroupId);
    session.ReplaceLayerMask(innerGroupId,
        GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width / 2, session.Height));
    Guid groupId = session.GroupLayers([innerGroupId], "Outer group");
    session.SetLayerOpacity(groupId, 0.76);
    session.SetLayerBlendMode(groupId, "Screen");
    session.SetGroupTransform(groupId, 2, 1, session.Width - 4, session.Height - 2, 5);
    session.EnsureLayerMask(groupId);
    session.ReplaceLayerMask(groupId,
        GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width, session.Height / 2));
    TileRaster expected = ImageProjectWorkflow.RenderFlatNormal(session);
    TileRaster copied = ImageProjectWorkflow.RenderLayerForCopy(session, groupId);
    AssertRaster(expected, copied);
    TileRaster nestedCopied = ImageProjectWorkflow.RenderLayerForCopy(session, innerGroupId);
    AssertRaster(expected, nestedCopied);
    string saved = Path.Combine(output, "GroupedLayerCopySaved.comp");
    ImageProjectWorkflow.Save(session, saved);
    var reopened = ImageProjectWorkflow.OpenEditable(saved);
    AssertRaster(expected, ImageProjectWorkflow.RenderLayerForCopy(reopened, groupId));
    AssertRaster(expected, ImageProjectWorkflow.RenderLayerForCopy(reopened, innerGroupId));
    Console.WriteLine("PASS: root and nested group visible-result copy preserves clipping, group masks, appearance and transforms Alpha");
}

static void CheckGroupedLeafLayerViaCopy(string output, string fixtures)
{
    string source = Path.Combine(output, "GroupedLeafCopySource.comp");
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "alpha-tiles.png"), source);
    var session = ImageProjectWorkflow.OpenEditable(source);
    Guid leafId = session.Layers.Single().Id;
    TileRaster sourceRaster = session.GetLayerRaster(leafId);
    Guid groupId = session.GroupLayer(leafId, "Masked parent");
    session.EnsureLayerMask(groupId);
    session.ReplaceLayerMask(groupId,
        GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width / 2, session.Height));
    session.SetLayerMaskEnabled(groupId, true);
    if (!ImageProjectWorkflow.CanRenderLayerForCopy(session, leafId))
        throw new Exception("A raster layer inside an identity group with an enabled group mask was not enabled for isolated copy.");
    TileRaster copied = ImageProjectWorkflow.RenderLayerForCopy(session, leafId);
    AssertRaster(sourceRaster, copied);
    int insertion = session.Layers.ToList().FindIndex(layer => layer.Id == leafId) + 1;
    Guid copiedId = session.AddRasterLayerToGroup("Layer via Copy", copied, insertion, groupId);
    FlatLayerInfo copiedLayer = session.Layers.Single(layer => layer.Id == copiedId);
    if (copiedLayer.ParentId != groupId || copiedLayer.IsGroup ||
        !session.Layers.Single(layer => layer.Id == groupId).MaskEnabled)
        throw new Exception("Grouped Layer via Copy did not preserve its parent or enabled group mask.");
    TileRaster rendered = ImageProjectWorkflow.RenderFlatNormal(session);
    if (Pixel(rendered, session.Width - 1, session.Height / 2)[3] != 0)
        throw new Exception("The enabled parent group mask did not clip the copied grouped layer.");
    string saved = Path.Combine(output, "GroupedLeafCopySaved.comp");
    ImageProjectWorkflow.Save(session, saved);
    var reopened = ImageProjectWorkflow.OpenEditable(saved);
    FlatLayerInfo reopenedCopy = reopened.Layers.Single(layer => layer.Id == copiedId);
    if (reopenedCopy.ParentId != groupId || !reopened.Layers.Single(layer => layer.Id == groupId).MaskEnabled)
        throw new Exception("Saved grouped Layer via Copy lost its parent or group mask state.");
    AssertRaster(sourceRaster, reopened.GetLayerRaster(copiedId));
    if (Pixel(ImageProjectWorkflow.RenderFlatNormal(reopened), reopened.Width - 1, reopened.Height / 2)[3] != 0)
        throw new Exception("Saved grouped Layer via Copy did not retain the enabled parent mask.");
    Console.WriteLine("PASS: grouped raster Layer via Copy preserves parent insertion, enabled group mask clipping and save/reopen");
}

static void CheckGroupedClippingStackLayerViaCopy(string output, string fixtures)
{
    string source = Path.Combine(output, "GroupedClippingStackCopySource.comp");
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "alpha-tiles.png"), source);
    var session = ImageProjectWorkflow.OpenEditable(source);
    Guid baseId = session.Layers.Single().Id;
    Guid childId = session.AddBlankLayer("Clipped child", 1);
    TileRaster childRaster = session.GetLayerRaster(childId);
    var size = childRaster.TileDimensions(0, 0);
    byte[] tile = childRaster.ReadTileCopy(0, 0);
    int offset = (12 * size.Width + 12) * 4;
    tile[offset] = 100; tile[offset + 1] = 20; tile[offset + 2] = 10; tile[offset + 3] = 170;
    session.ReplaceLayerRaster(childId, childRaster.ReplaceTile(0, 0, tile));
    session.SetLayerOpacity(childId, 0.71);
    session.SetLayerBlendMode(childId, "Screen");
    session.SetLayerMaskSource(childId, baseId);
    Guid groupId = session.GroupLayers([baseId, childId], "Clipping parent");
    session.EnsureLayerMask(groupId);
    session.ReplaceLayerMask(groupId,
        GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width / 2, session.Height));
    session.SetLayerMaskEnabled(groupId, true);
    if (!ImageProjectWorkflow.CanRenderLayerForCopy(session, baseId) ||
        !ImageProjectWorkflow.CanRenderLayerForCopy(session, childId))
        throw new Exception("A complete grouped clipping stack was not enabled for visible-result copy.");
    TileRaster expected = ImageProjectWorkflow.RenderLayerForCopy(session, childId);
    int insertion = ImageProjectWorkflow.GetGroupedLayerCopyInsertionIndex(session, childId);
    if (insertion <= session.Layers.ToList().FindIndex(layer => layer.Id == childId))
        throw new Exception("Grouped clipping stack copy insertion did not advance past the stack.");
    Guid copiedId = session.AddRasterLayerToGroup("Layer via Copy", expected, insertion, groupId);
    FlatLayerInfo copied = session.Layers.Single(layer => layer.Id == copiedId);
    if (copied.ParentId != groupId || session.Layers.ToList().FindIndex(layer => layer.Id == copiedId) != insertion)
        throw new Exception("Grouped clipping stack copy did not insert after the original stack.");
    if (Pixel(ImageProjectWorkflow.RenderFlatNormal(session), session.Width - 1, session.Height / 2)[3] != 0)
        throw new Exception("The enabled parent group mask did not clip the grouped clipping-stack copy.");
    string saved = Path.Combine(output, "GroupedClippingStackCopySaved.comp");
    ImageProjectWorkflow.Save(session, saved);
    var reopened = ImageProjectWorkflow.OpenEditable(saved);
    FlatLayerInfo reopenedCopy = reopened.Layers.Single(layer => layer.Id == copiedId);
    if (reopenedCopy.ParentId != groupId || !reopened.Layers.Single(layer => layer.Id == groupId).MaskEnabled)
        throw new Exception("Saved grouped clipping-stack copy lost its parent or group mask.");
    AssertRaster(expected, reopened.GetLayerRaster(copiedId));
    Console.WriteLine("PASS: grouped clipping-stack visible-result copy preserves stack boundary, parent mask and save/reopen");
}

static void CheckGroupedNestedClippingStackLayerViaCopy(string output, string fixtures)
{
    string source = Path.Combine(output, "GroupedNestedClippingStackCopySource.comp");
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "alpha-tiles.png"), source);
    var session = ImageProjectWorkflow.OpenEditable(source);
    Guid baseId = session.Layers.Single().Id;
    Guid firstChildId = session.AddBlankLayer("First clipped child", 1);
    Guid secondChildId = session.AddBlankLayer("Second clipped child", 2);
    session.SetLayerOpacity(firstChildId, 0.71);
    session.SetLayerBlendMode(firstChildId, "Screen");
    session.SetLayerMaskSource(firstChildId, baseId);
    session.SetLayerOpacity(secondChildId, 0.63);
    session.SetLayerBlendMode(secondChildId, "Multiply");
    session.SetLayerMaskSource(secondChildId, firstChildId);
    Guid groupId = session.GroupLayers([baseId, firstChildId, secondChildId], "Nested clipping parent");
    session.EnsureLayerMask(groupId);
    session.ReplaceLayerMask(groupId,
        GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width / 2, session.Height));
    session.SetLayerMaskEnabled(groupId, true);
    if (!ImageProjectWorkflow.CanRenderLayerForCopy(session, baseId) ||
        !ImageProjectWorkflow.CanRenderLayerForCopy(session, firstChildId) ||
        !ImageProjectWorkflow.CanRenderLayerForCopy(session, secondChildId))
        throw new Exception("A nested grouped clipping stack was not enabled for visible-result copy.");
    TileRaster expected = ImageProjectWorkflow.RenderLayerForCopy(session, secondChildId);
    int insertion = ImageProjectWorkflow.GetGroupedLayerCopyInsertionIndex(session, secondChildId);
    int secondChildIndex = session.Layers.ToList().FindIndex(layer => layer.Id == secondChildId);
    if (insertion <= secondChildIndex)
        throw new Exception("Nested grouped clipping-stack copy insertion did not advance past the complete stack.");
    Guid copiedId = session.AddRasterLayerToGroup("Layer via Copy", expected, insertion, groupId);
    FlatLayerInfo copied = session.Layers.Single(layer => layer.Id == copiedId);
    if (copied.ParentId != groupId || session.Layers.ToList().FindIndex(layer => layer.Id == copiedId) != insertion)
        throw new Exception("Nested grouped clipping-stack copy did not insert after the complete stack.");
    if (Pixel(ImageProjectWorkflow.RenderFlatNormal(session), session.Width - 1, session.Height / 2)[3] != 0)
        throw new Exception("The enabled parent group mask did not clip the nested grouped clipping-stack copy.");
    string saved = Path.Combine(output, "GroupedNestedClippingStackCopySaved.comp");
    ImageProjectWorkflow.Save(session, saved);
    var reopened = ImageProjectWorkflow.OpenEditable(saved);
    FlatLayerInfo reopenedFirst = reopened.Layers.Single(layer => layer.Id == firstChildId);
    FlatLayerInfo reopenedSecond = reopened.Layers.Single(layer => layer.Id == secondChildId);
    FlatLayerInfo reopenedCopy = reopened.Layers.Single(layer => layer.Id == copiedId);
    if (reopenedFirst.MaskSourceId != baseId || reopenedSecond.MaskSourceId != firstChildId ||
        reopenedCopy.ParentId != groupId || !reopened.Layers.Single(layer => layer.Id == groupId).MaskEnabled)
        throw new Exception("Saved nested grouped clipping stack lost its relationships or parent mask.");
    AssertRaster(expected, reopened.GetLayerRaster(copiedId));
    Console.WriteLine("PASS: nested grouped clipping-stack visible-result copy preserves chained relationships and save/reopen");
}

static void CheckGroupedDiscontinuousClippingStackLayerViaCopy(string output, string fixtures)
{
    string expectedSource = Path.Combine(output, "GroupedDiscontinuousClippingExpected.comp");
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "alpha-tiles.png"), expectedSource);
    var expectedSession = ImageProjectWorkflow.OpenEditable(expectedSource);
    Guid expectedBaseId = expectedSession.Layers.Single().Id;
    Guid expectedChildId = expectedSession.AddBlankLayer("Clipped child", 1);
    expectedSession.SetLayerOpacity(expectedChildId, 0.66);
    expectedSession.SetLayerBlendMode(expectedChildId, "Screen");
    expectedSession.SetLayerMaskSource(expectedChildId, expectedBaseId);
    expectedSession.GroupLayers([expectedBaseId, expectedChildId], "Expected parent");
    TileRaster expected = ImageProjectWorkflow.RenderLayerForCopy(expectedSession, expectedChildId);

    string source = Path.Combine(output, "GroupedDiscontinuousClippingCopySource.comp");
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "alpha-tiles.png"), source);
    var session = ImageProjectWorkflow.OpenEditable(source);
    Guid baseId = session.Layers.Single().Id;
    Guid unrelatedId = session.AddBlankLayer("Unrelated sibling", 1);
    Guid childId = session.AddBlankLayer("Clipped child", 2);
    session.SetLayerOpacity(childId, 0.66);
    session.SetLayerBlendMode(childId, "Screen");
    session.SetLayerMaskSource(childId, baseId);
    Guid groupId = session.GroupLayers([baseId, unrelatedId, childId], "Discontinuous parent");
    if (!ImageProjectWorkflow.CanRenderLayerForCopy(session, childId))
        throw new Exception("A same-parent discontinuous clipping stack was not enabled for visible-result copy.");
    TileRaster actual = ImageProjectWorkflow.RenderLayerForCopy(session, childId);
    AssertRaster(expected, actual);
    int childIndex = session.Layers.ToList().FindIndex(layer => layer.Id == childId);
    int insertion = ImageProjectWorkflow.GetGroupedLayerCopyInsertionIndex(session, childId);
    if (insertion != childIndex + 1)
        throw new Exception("Discontinuous clipping-stack copy did not insert after the selected target.");
    Guid copiedId = session.AddRasterLayerToGroup("Layer via Copy", actual, insertion, groupId);
    if (session.Layers.ToList().FindIndex(layer => layer.Id == copiedId) != insertion ||
        session.Layers.Single(layer => layer.Id == copiedId).ParentId != groupId)
        throw new Exception("Discontinuous clipping-stack copy did not preserve the parent insertion point.");
    string saved = Path.Combine(output, "GroupedDiscontinuousClippingCopySaved.comp");
    ImageProjectWorkflow.Save(session, saved);
    var reopened = ImageProjectWorkflow.OpenEditable(saved);
    if (reopened.Layers.Single(layer => layer.Id == childId).MaskSourceId != baseId ||
        reopened.Layers.Single(layer => layer.Id == copiedId).ParentId != groupId)
        throw new Exception("Saved discontinuous clipping stack lost its original relationship or parent.");
    AssertRaster(actual, reopened.GetLayerRaster(copiedId));
    Console.WriteLine("PASS: same-parent discontinuous clipping-stack visible-result copy preserves target semantics and save/reopen");
}

static void CheckCrossParentGroupedLeafLayerViaCopy(string output, string fixtures)
{
    string source = Path.Combine(output, "CrossParentGroupedLeafCopySource.comp");
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "alpha-tiles.png"), source);
    var session = ImageProjectWorkflow.OpenEditable(source);
    Guid sourceId = session.Layers.Single().Id;
    Guid targetId = session.AddBlankLayer("Cross-parent clipped target", session.Layers.Count);
    TileRaster targetRaster = session.GetLayerRaster(targetId);
    var targetSize = targetRaster.TileDimensions(0, 0);
    byte[] targetTile = targetRaster.ReadTileCopy(0, 0);
    int targetOffset = (16 * targetSize.Width + 16) * 4;
    targetTile[targetOffset] = 150; targetTile[targetOffset + 1] = 40;
    targetTile[targetOffset + 2] = 20; targetTile[targetOffset + 3] = 220;
    session.ReplaceLayerRaster(targetId, targetRaster.ReplaceTile(0, 0, targetTile));
    Guid sourceGroupId = session.GroupLayer(sourceId, "External source parent");
    Guid targetGroupId = session.GroupLayer(targetId, "Cross-parent target parent");
    ImageProjectWorkflow.Save(session, source);
    var crossParentManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "manifest.json")))!.AsObject();
    crossParentManifest["layers"]!.AsArray().Single(node =>
        Guid.Parse(node!["id"]!.GetValue<string>()) == targetId)!["maskSourceID"] = sourceId.ToString("D");
    File.WriteAllText(Path.Combine(source, "manifest.json"), crossParentManifest.ToJsonString());
    session = ImageProjectWorkflow.OpenEditable(source);
    if (!ImageProjectWorkflow.CanRenderLayerForCopy(session, targetId) ||
        !ImageProjectWorkflow.GroupedLayerCopyRequiresRootInsertion(session, targetId))
        throw new Exception("A cross-parent grouped clipping target was not enabled for root visible-result copy.");
    TileRaster expected = ImageProjectWorkflow.RenderLayerForCopy(session, targetId);
    int targetGroupIndex = session.Layers.ToList().FindIndex(layer => layer.Id == targetGroupId);
    int insertion = targetGroupIndex + 1;
    while (insertion < session.Layers.Count &&
        session.Layers[insertion].ParentId is { } parentId && parentId == targetGroupId)
        insertion++;
    Guid copiedId = session.AddRootRasterLayer("Layer via Copy", expected, insertion);
    FlatLayerInfo copied = session.Layers.Single(layer => layer.Id == copiedId);
    if (copied.ParentId is not null || session.Layers.ToList().FindIndex(layer => layer.Id == copiedId) != insertion)
        throw new Exception("Cross-parent grouped clipping copy did not insert after the target group subtree.");
    AssertRaster(expected, session.GetLayerRaster(copiedId));
    string saved = Path.Combine(output, "CrossParentGroupedLeafCopySaved.comp");
    ImageProjectWorkflow.Save(session, saved);
    var reopened = ImageProjectWorkflow.OpenEditable(saved);
    if (reopened.Layers.Single(layer => layer.Id == targetId).MaskSourceId != sourceId ||
        reopened.Layers.Single(layer => layer.Id == copiedId).ParentId is not null ||
        reopened.Layers.Single(layer => layer.Id == sourceId).ParentId != sourceGroupId)
        throw new Exception("Saved cross-parent grouped clipping copy lost its relation or root placement.");
    AssertRaster(expected, reopened.GetLayerRaster(copiedId));

    string complexSource = Path.Combine(output, "CrossParentGroupedAppearanceSource.comp");
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "alpha-tiles.png"), complexSource);
    var complexSession = ImageProjectWorkflow.OpenEditable(complexSource);
    Guid complexSourceId = complexSession.Layers.Single().Id;
    Guid complexTargetId = complexSession.AddBlankLayer("Complex cross-parent target", complexSession.Layers.Count);
    Guid complexSourceGroupId = complexSession.GroupLayer(complexSourceId, "Complex external source parent");
    Guid complexTargetGroupId = complexSession.GroupLayer(complexTargetId, "Complex target parent");
    ConfigureExternalSourceGroup(complexSession, complexSourceGroupId);
    ImageProjectWorkflow.Save(complexSession, complexSource);
    string referencePath = Path.Combine(output, "CrossParentGroupedAppearanceReference.comp");
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "alpha-tiles.png"), referencePath);
    var reference = ImageProjectWorkflow.OpenEditable(referencePath);
    Guid referenceSourceId = reference.Layers.Single().Id;
    Guid referenceGroupId = reference.GroupLayer(referenceSourceId, "Reference source parent");
    ConfigureExternalSourceGroup(reference, referenceGroupId);
    TileRaster externalVisible = ImageProjectWorkflow.RenderFlatNormal(reference);
    TileRaster complexTargetRaster = complexSession.GetLayerRaster(complexTargetId);
    var complexManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(complexSource, "manifest.json")))!.AsObject();
    complexManifest["layers"]!.AsArray().Single(node =>
        Guid.Parse(node!["id"]!.GetValue<string>()) == complexTargetId)!["maskSourceID"] =
        complexSourceId.ToString("D");
    File.WriteAllText(Path.Combine(complexSource, "manifest.json"), complexManifest.ToJsonString());
    complexSession = ImageProjectWorkflow.OpenEditable(complexSource);
    TileRaster expectedComplex = RasterCompositor.ApplyAlphaMask(complexTargetRaster, externalVisible, 1);
    if (!ImageProjectWorkflow.CanRenderLayerForCopy(complexSession, complexTargetId) ||
        !ImageProjectWorkflow.GroupedLayerCopyRequiresRootInsertion(complexSession, complexTargetId))
        throw new Exception("A cross-parent clipping target with external group appearance was not enabled.");
    TileRaster actualComplex = ImageProjectWorkflow.RenderLayerForCopy(complexSession, complexTargetId);
    AssertRaster(expectedComplex, actualComplex);
    int complexGroupIndex = complexSession.Layers.ToList().FindIndex(layer => layer.Id == complexTargetGroupId);
    int complexInsertion = complexGroupIndex + 1;
    while (complexInsertion < complexSession.Layers.Count &&
        complexSession.Layers[complexInsertion].ParentId is { } parentId && parentId == complexTargetGroupId)
        complexInsertion++;
    Guid complexCopiedId = complexSession.AddRootRasterLayer("Layer via Copy", actualComplex, complexInsertion);
    ImageProjectWorkflow.Save(complexSession, complexSource);
    var reopenedComplex = ImageProjectWorkflow.OpenEditable(complexSource);
    if (reopenedComplex.Layers.Single(layer => layer.Id == complexTargetId).MaskSourceId != complexSourceId ||
        reopenedComplex.Layers.Single(layer => layer.Id == complexCopiedId).ParentId is not null)
        throw new Exception("Saved complex cross-parent clipping copy lost its relationship or root placement.");
    AssertRaster(expectedComplex, reopenedComplex.GetLayerRaster(complexCopiedId));

    string chainedSource = Path.Combine(output, "CrossParentGroupedClippingChainSource.comp");
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "alpha-tiles.png"), chainedSource);
    var chainedSession = ImageProjectWorkflow.OpenEditable(chainedSource);
    Guid chainedSourceBaseId = chainedSession.Layers.Single().Id;
    var chainedSourceIds = new List<Guid> { chainedSourceBaseId };
    for (int level = 14; level >= 1; level--)
        chainedSourceIds.Add(chainedSession.AddBlankLayer($"Intermediate clipped source {level}", chainedSession.Layers.Count));
    Guid chainedTargetId = chainedSession.AddBlankLayer("Cross-parent chain target", chainedSession.Layers.Count);
    foreach (Guid chainSourceId in chainedSourceIds.Skip(1))
        chainedSession.ReplaceLayerRaster(chainSourceId, chainedSession.GetLayerRaster(chainedSourceBaseId));
    chainedSession.ReplaceLayerRaster(chainedTargetId, chainedSession.GetLayerRaster(chainedSourceBaseId));
    chainedSession.SetLayerOpacity(chainedSourceBaseId, 0.72);
    var chainedSourceGroupIds = new List<Guid>();
    for (int level = 0; level < chainedSourceIds.Count; level++)
        chainedSourceGroupIds.Add(chainedSession.GroupLayer(chainedSourceIds[level],
            $"External source parent {level + 1}"));
    Guid chainedTargetGroupId = chainedSession.GroupLayer(chainedTargetId, "Cross-parent chain target parent");
    ImageProjectWorkflow.Save(chainedSession, chainedSource);
    var chainedManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(chainedSource, "manifest.json")))!.AsObject();
    for (int level = 1; level < chainedSourceIds.Count; level++)
        chainedManifest["layers"]!.AsArray().Single(node =>
            Guid.Parse(node!["id"]!.GetValue<string>()) == chainedSourceIds[level])!["maskSourceID"] =
            chainedSourceIds[level - 1].ToString("D");
    chainedManifest["layers"]!.AsArray().Single(node =>
        Guid.Parse(node!["id"]!.GetValue<string>()) == chainedTargetId)!["maskSourceID"] =
        chainedSourceIds[^1].ToString("D");
    File.WriteAllText(Path.Combine(chainedSource, "manifest.json"), chainedManifest.ToJsonString());
    chainedSession = ImageProjectWorkflow.OpenEditable(chainedSource);
    TileRaster expectedChained = chainedSession.GetLayerRaster(chainedSourceBaseId);
    for (int level = 1; level < chainedSourceIds.Count; level++)
        expectedChained = RasterCompositor.ApplyAlphaMask(
            chainedSession.GetLayerRaster(chainedSourceIds[level]), expectedChained, level == 1 ? 0.72 : 1);
    expectedChained = RasterCompositor.ApplyAlphaMask(
        chainedSession.GetLayerRaster(chainedTargetId), expectedChained, 1);
    if (!ImageProjectWorkflow.CanRenderLayerForCopy(chainedSession, chainedTargetId) ||
        !ImageProjectWorkflow.GroupedLayerCopyRequiresRootInsertion(chainedSession, chainedTargetId))
        throw new Exception("A cross-parent fifteen-level clipping target was not enabled.");
    TileRaster actualChained = ImageProjectWorkflow.RenderLayerForCopy(chainedSession, chainedTargetId);
    AssertRaster(expectedChained, actualChained);
    int chainedGroupIndex = chainedSession.Layers.ToList().FindIndex(layer => layer.Id == chainedTargetGroupId);
    int chainedInsertion = chainedGroupIndex + 1;
    while (chainedInsertion < chainedSession.Layers.Count &&
        chainedSession.Layers[chainedInsertion].ParentId is { } parentId && parentId == chainedTargetGroupId)
        chainedInsertion++;
    Guid chainedCopiedId = chainedSession.AddRootRasterLayer("Layer via Copy", actualChained, chainedInsertion);
    ImageProjectWorkflow.Save(chainedSession, chainedSource);
    var reopenedChained = ImageProjectWorkflow.OpenEditable(chainedSource);
    for (int level = 1; level < chainedSourceIds.Count; level++)
        if (reopenedChained.Layers.Single(layer => layer.Id == chainedSourceIds[level]).MaskSourceId !=
            chainedSourceIds[level - 1])
            throw new Exception("Saved cross-parent fifteen-level clipping copy lost a source relationship.");
    for (int level = 0; level < chainedSourceIds.Count; level++)
        if (reopenedChained.Layers.Single(layer => layer.Id == chainedSourceIds[level]).ParentId !=
            chainedSourceGroupIds[level])
            throw new Exception("Saved cross-parent fifteen-level clipping copy lost a source parent.");
    if (reopenedChained.Layers.Single(layer => layer.Id == chainedTargetId).MaskSourceId != chainedSourceIds[^1] ||
        reopenedChained.Layers.Single(layer => layer.Id == chainedCopiedId).ParentId is not null)
        throw new Exception("Saved cross-parent fifteen-level clipping copy lost its relationships or root placement.");
    AssertRaster(expectedChained, reopenedChained.GetLayerRaster(chainedCopiedId));
    Console.WriteLine("PASS: cross-parent grouped clipping visible-result copy preserves root insertion, relationship and save/reopen");
    Console.WriteLine("PASS: cross-parent grouped clipping copy composes external group transform, mask and appearance");
    Console.WriteLine("PASS: cross-parent grouped clipping copy composes a fifteen-level external clipping chain");

    static void ConfigureExternalSourceGroup(ProjectSession session, Guid groupId)
    {
        session.SetLayerOpacity(groupId, 0.58);
        session.SetLayerBlendMode(groupId, "Multiply");
        session.SetGroupTransform(groupId, 8, 4, session.Width - 16, session.Height - 8, 11);
        session.EnsureLayerMask(groupId);
        session.ReplaceLayerMask(groupId,
            GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width * 3 / 4, session.Height));
        session.SetLayerMaskEnabled(groupId, true);
    }
}

static void CheckTransformedGroupedLeafLayerViaCopy(string output, string fixtures)
{
    string source = Path.Combine(output, "TransformedGroupedLeafCopySource.comp");
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "alpha-tiles.png"), source);
    var session = ImageProjectWorkflow.OpenEditable(source);
    Guid leafId = session.Layers.Single().Id;
    Guid groupId = session.GroupLayer(leafId, "Transformed parent");
    session.SetLayerOpacity(groupId, 0.82);
    session.SetLayerBlendMode(groupId, "Multiply");
    session.SetGroupTransform(groupId, 4, 3, session.Width - 8, session.Height - 6, 8);
    session.EnsureLayerMask(groupId);
    session.ReplaceLayerMask(groupId,
        GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width / 2, session.Height));
    session.SetLayerMaskEnabled(groupId, true);
    if (!ImageProjectWorkflow.CanRenderLayerForCopy(session, leafId) ||
        !ImageProjectWorkflow.GroupedLayerCopyRequiresRootInsertion(session, leafId))
        throw new Exception("A raster layer inside a transformed group was not enabled for root visible-result copy.");
    TileRaster expected = ImageProjectWorkflow.RenderLayerForCopy(session, leafId);
    int groupIndex = session.Layers.ToList().FindIndex(layer => layer.Id == groupId);
    int insertion = groupIndex + 1;
    while (insertion < session.Layers.Count &&
        session.Layers[insertion].ParentId is { } parentId && parentId == groupId)
        insertion++;
    Guid copiedId = session.AddRootRasterLayer("Layer via Copy", expected, insertion);
    FlatLayerInfo copied = session.Layers.Single(layer => layer.Id == copiedId);
    if (copied.ParentId is not null || session.Layers.ToList().FindIndex(layer => layer.Id == copiedId) != insertion)
        throw new Exception("Transformed grouped leaf copy did not insert after the outer group subtree.");
    AssertRaster(expected, session.GetLayerRaster(copiedId));
    string saved = Path.Combine(output, "TransformedGroupedLeafCopySaved.comp");
    ImageProjectWorkflow.Save(session, saved);
    var reopened = ImageProjectWorkflow.OpenEditable(saved);
    FlatLayerInfo reopenedCopy = reopened.Layers.Single(layer => layer.Id == copiedId);
    if (reopenedCopy.ParentId is not null || !reopened.Layers.Single(layer => layer.Id == groupId).MaskEnabled)
        throw new Exception("Saved transformed grouped leaf copy lost its root placement or source group mask.");
    AssertRaster(expected, reopened.GetLayerRaster(copiedId));
    Console.WriteLine("PASS: transformed grouped leaf visible-result copy bakes ancestor transform and preserves root insertion/save-reopen");
}

static void CheckEditableLayerTransform(string output, string fixtures)
{
    string source = Path.Combine(output, "LayerTransformSource.comp");
    ImageProjectWorkflow.Import(Path.Combine(fixtures, "alpha-tiles.png"), source);
    var session = ImageProjectWorkflow.OpenEditable(source);
    Guid layerId = session.Layers.Single().Id;
    TileRaster original = session.GetLayerRaster(layerId);
    TileRaster baseline = ImageProjectWorkflow.RenderFlatNormal(session);

    session.MoveLayerTransform(layerId, 12, -7);
    TileRaster moved = ImageProjectWorkflow.RenderFlatNormal(session);
    if (SameRaster(baseline, moved) || !SameRaster(original, session.GetLayerRaster(layerId)))
        throw new Exception("Flat layer move did not change the preview without resampling the source asset.");
    if (!session.Undo() || session.IsDirty || !SameRaster(baseline, ImageProjectWorkflow.RenderFlatNormal(session)) ||
        !SameRaster(original, session.GetLayerRaster(layerId)))
        throw new Exception("Flat layer transform undo did not restore the clean source state.");
    if (!session.Redo() || !SameRaster(moved, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Flat layer transform redo did not restore the moved preview.");

    string saved = Path.Combine(output, "LayerTransformMoved.comp");
    ImageProjectWorkflow.Save(session, saved);
    var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(saved, "manifest.json")))!;
    var transform = manifest["layers"]![0]!["transform"]!;
    if (transform["origin"]![0]!.GetValue<double>() != 12 || transform["origin"]![1]!.GetValue<double>() != -7)
        throw new Exception("Flat layer transform metadata was not saved.");
    try
    {
        ProjectStore.ExportPng(session, Path.Combine(output, "raw-transformed-export.png"));
        throw new Exception("Raw export ignored a non-destructive layer transform.");
    }
    catch (NotSupportedException) { }
    var reopened = ImageProjectWorkflow.OpenEditable(saved);
    if (!SameRaster(moved, ImageProjectWorkflow.RenderFlatNormal(reopened)) ||
        !SameRaster(original, reopened.GetLayerRaster(layerId)))
        throw new Exception("Saved flat layer transform did not reopen with original pixels and transformed preview.");

    var flipped = ImageProjectWorkflow.OpenEditable(source);
    flipped.FlipLayerTransform(layerId, horizontal: true);
    if (!SameRaster(HorizontalFlip(baseline), ImageProjectWorkflow.RenderFlatNormal(flipped)))
        throw new Exception("Flat layer horizontal transform did not match the expected canvas result.");

    var scaled = ImageProjectWorkflow.OpenEditable(source);
    scaled.ScaleLayerTransform(layerId, 0.5);
    TileRaster scaledPreview = ImageProjectWorkflow.RenderFlatNormal(scaled);
    if (SameRaster(baseline, scaledPreview) || !SameRaster(original, scaled.GetLayerRaster(layerId)))
        throw new Exception("Flat layer scale did not render as a non-destructive transform.");
    var baked = ImageProjectWorkflow.OpenEditable(source);
    baked.ScaleLayerTransform(layerId, 0.5);
    TileRaster bakedPreview = ImageProjectWorkflow.RenderFlatNormal(baked);
    ImageProjectWorkflow.BakeLayerTransform(baked, layerId);
    if (!baked.IsLayerTransformIdentity(layerId) || !SameRaster(bakedPreview, ImageProjectWorkflow.RenderFlatNormal(baked)) ||
        SameRaster(original, baked.GetLayerRaster(layerId)))
        throw new Exception("Baking a flat layer transform did not preserve the preview or materialize the raster.");
    if (!baked.Undo() || baked.IsLayerTransformIdentity(layerId) ||
        !SameRaster(bakedPreview, ImageProjectWorkflow.RenderFlatNormal(baked)) ||
        !SameRaster(original, baked.GetLayerRaster(layerId)) ||
        !baked.Redo() || !baked.IsLayerTransformIdentity(layerId) ||
        !SameRaster(bakedPreview, ImageProjectWorkflow.RenderFlatNormal(baked)))
        throw new Exception("Flat layer bake undo/redo did not restore the transform transaction.");

    var rotated = ImageProjectWorkflow.OpenEditable(source);
    rotated.RotateLayerTransform90(layerId, clockwise: true);
    TileRaster rotatedPreview = ImageProjectWorkflow.RenderFlatNormal(rotated);
    if (SameRaster(baseline, rotatedPreview) || !SameRaster(original, rotated.GetLayerRaster(layerId)))
        throw new Exception("Flat layer rotation did not render as a non-destructive transform.");

    var freelyRotated = ImageProjectWorkflow.OpenEditable(source);
    freelyRotated.RotateLayerTransform(layerId, 15);
    TileRaster freelyRotatedPreview = ImageProjectWorkflow.RenderFlatNormal(freelyRotated);
    if (SameRaster(baseline, freelyRotatedPreview) ||
        !SameRaster(original, freelyRotated.GetLayerRaster(layerId)) || !freelyRotated.Undo() ||
        !SameRaster(baseline, ImageProjectWorkflow.RenderFlatNormal(freelyRotated)) ||
        !freelyRotated.Redo() || !SameRaster(freelyRotatedPreview, ImageProjectWorkflow.RenderFlatNormal(freelyRotated)))
        throw new Exception("Flat layer free rotation did not preserve the source, preview and history.");
    string freeRotationPath = Path.Combine(output, "LayerTransformFreeRotation.comp");
    ImageProjectWorkflow.Save(freelyRotated, freeRotationPath);
    var freeRotationManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(freeRotationPath, "manifest.json")))!;
    double freeRotation = freeRotationManifest["layers"]![0]!["transform"]!["rotation"]!.GetValue<double>();
    if (Math.Abs(freeRotation - 15) > 1e-9)
        throw new Exception("Flat layer free rotation metadata was not saved.");
    var reopenedFreeRotation = ImageProjectWorkflow.OpenEditable(freeRotationPath);
    if (!SameRaster(freelyRotatedPreview, ImageProjectWorkflow.RenderFlatNormal(reopenedFreeRotation)))
        throw new Exception("Flat layer free rotation did not survive save and reopen.");

    var masked = ImageProjectWorkflow.OpenEditable(source);
    masked.EnsureLayerMask(layerId);
    masked.ReplaceLayerMask(layerId,
        GrayTileRaster.Rectangle(masked.Width, masked.Height, 0, 0, masked.Width / 2, masked.Height));
    GrayTileRaster maskedMask = masked.GetLayerMask(layerId)!;
    TileRaster maskedBaseline = ImageProjectWorkflow.RenderFlatNormal(masked);
    masked.FlipLayerTransform(layerId, horizontal: true);
    TileRaster maskedFlipped = ImageProjectWorkflow.RenderFlatNormal(masked);
    if (!SameRaster(HorizontalFlip(maskedBaseline), maskedFlipped) ||
        !SameRaster(original, masked.GetLayerRaster(layerId)) ||
        !SameCoverage(maskedMask, masked.GetLayerMask(layerId)!))
        throw new Exception("Flat layer transform did not carry the editable mask with the source asset.");
    string maskedPath = Path.Combine(output, "LayerTransformMasked.comp");
    ImageProjectWorkflow.Save(masked, maskedPath);
    var maskedReopened = ImageProjectWorkflow.OpenEditable(maskedPath);
    if (!SameRaster(maskedFlipped, ImageProjectWorkflow.RenderFlatNormal(maskedReopened)) ||
        !SameRaster(masked.GetLayerRaster(layerId), maskedReopened.GetLayerRaster(layerId)))
        throw new Exception("Masked flat layer transform did not survive save and reopen.");
    var bakedMasked = ImageProjectWorkflow.OpenEditable(source);
    bakedMasked.EnsureLayerMask(layerId);
    bakedMasked.ReplaceLayerMask(layerId,
        GrayTileRaster.Rectangle(bakedMasked.Width, bakedMasked.Height, 0, 0, bakedMasked.Width / 2, bakedMasked.Height));
    bakedMasked.ScaleLayerTransform(layerId, 0.5);
    TileRaster bakedMaskedPreview = ImageProjectWorkflow.RenderFlatNormal(bakedMasked);
    ImageProjectWorkflow.BakeLayerTransform(bakedMasked, layerId);
    TileRaster bakedMaskedActual = ImageProjectWorkflow.RenderFlatNormal(bakedMasked);
    if (!bakedMasked.IsLayerTransformIdentity(layerId) ||
        !SameRaster(bakedMaskedPreview, bakedMaskedActual) || bakedMasked.GetLayerMask(layerId) is null)
        throw new Exception($"Baking a masked flat layer transform did not preserve the preview or editable mask (max diff {MaxDifference(bakedMaskedPreview, bakedMaskedActual)}).");
    Console.WriteLine("PASS: flat layer non-destructive move/flip/scale/90-degree transform, undo/redo, mask sync and save/reopen");
}

static bool SameRaster(TileRaster first, TileRaster second)
{
    if (first.Width != second.Width || first.Height != second.Height) return false;
    for (int row = 0; row * TileRaster.TileSize < first.Height; row++)
    for (int column = 0; column * TileRaster.TileSize < first.Width; column++)
        if (!first.ReadTileCopy(column, row).SequenceEqual(second.ReadTileCopy(column, row))) return false;
    return true;
}

static bool SameCoverage(GrayTileRaster first, GrayTileRaster second)
{
    if (first.Width != second.Width || first.Height != second.Height) return false;
    for (int row = 0; row * TileRaster.TileSize < first.Height; row++)
    for (int column = 0; column * TileRaster.TileSize < first.Width; column++)
        if (!first.ReadTileCopy(column, row).SequenceEqual(second.ReadTileCopy(column, row))) return false;
    return true;
}

static int MaxDifference(TileRaster first, TileRaster second)
{
    int maximum = 0;
    for (int row = 0; row * TileRaster.TileSize < first.Height; row++)
    for (int column = 0; column * TileRaster.TileSize < first.Width; column++)
    {
        byte[] left = first.ReadTileCopy(column, row), right = second.ReadTileCopy(column, row);
        for (int index = 0; index < left.Length; index++) maximum = Math.Max(maximum, Math.Abs(left[index] - right[index]));
    }
    return maximum;
}

static void ExpectNotSupported(Action action, string description)
{
    try
    {
        action();
        throw new Exception($"{description} was accepted outside the supported slice.");
    }
    catch (NotSupportedException) { }
}

static void CheckCompositing(string output, string fixtures)
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
    var flatSession = ImageProjectWorkflow.OpenEditable(flat);
    if (!flatSession.CanEdit || flatSession.Raster is not null)
        throw new Exception("Flat multi-layer project did not open for metadata editing.");
    Guid topLayerId = Guid.Parse(topId);
    flatSession.RenameLayer(topLayerId, "Top");
    flatSession.SetLayerVisible(topLayerId, true);
    flatSession.MoveLayer(topLayerId, 1);
    if (flatSession.IsDirty) throw new Exception("No-op layer changes created history.");
    AssertRaster(result, ImageProjectWorkflow.RenderFlatNormal(flat));
    var orderSession = ImageProjectWorkflow.OpenEditable(flat);
    orderSession.MoveLayer(Guid.Parse(topId), 0);
    AssertRaster(RasterCompositor.SourceOver(top, bottom), ImageProjectWorkflow.RenderFlatNormal(orderSession));
    try
    {
        topLayer["isVisible"] = false;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        AssertRaster(bottom, ImageProjectWorkflow.RenderFlatNormal(flat));
        topLayer["isVisible"] = true;
        topLayer["blendMode"] = "Unknown mode";
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        if (ProjectStore.Open(flat).CanEdit) throw new Exception("Unsupported blend mode became editable.");
        try
        {
            ImageProjectWorkflow.RenderFlatNormal(flat);
            throw new Exception("Unsupported blend mode was rendered as Normal.");
        }
        catch (NotSupportedException) { }
        topLayer.Remove("blendMode");
        topLayer["opacity"] = -0.1;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        if (ProjectStore.Open(flat).CanEdit) throw new Exception("Unsupported opacity became editable.");
        try
        {
            ImageProjectWorkflow.RenderFlatNormal(flat);
            throw new Exception("Unsupported opacity was rendered as fully opaque.");
        }
        catch (NotSupportedException) { }
        topLayer.Remove("opacity");
        topLayer["transform"]!["origin"]![0] = 1;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        if (!ProjectStore.Open(flat).CanEdit) throw new Exception("Supported transform was rejected as read-only.");
        if (SameRaster(result, ImageProjectWorkflow.RenderFlatNormal(flat)))
            throw new Exception("Supported transform was rendered at the original position.");
    }
    finally { File.WriteAllText(manifestPath, originalManifest); }

    flatSession.RenameLayer(topLayerId, "Overlay");
    flatSession.SetLayerVisible(topLayerId, false);
    flatSession.MoveLayer(topLayerId, 0);
    if (!flatSession.IsDirty || flatSession.Layers[0].Id != topLayerId ||
        flatSession.Layers[0].Name != "Overlay" || flatSession.Layers[0].IsVisible)
        throw new Exception("Flat layer metadata transaction lost identity or ordering.");
    AssertRaster(bottom, ImageProjectWorkflow.RenderFlatNormal(flatSession));
    if (!flatSession.Undo()) throw new Exception("Layer reorder did not undo.");
    AssertRaster(bottom, ImageProjectWorkflow.RenderFlatNormal(flatSession));
    if (!flatSession.Undo()) throw new Exception("Layer visibility did not undo.");
    AssertRaster(result, ImageProjectWorkflow.RenderFlatNormal(flatSession));
    if (!flatSession.Undo() || flatSession.IsDirty || flatSession.Layers[1].Name != "Top")
        throw new Exception("Layer rename undo did not return to the save point.");
    if (!flatSession.Redo() || !flatSession.Redo() || !flatSession.Redo() || flatSession.Redo())
        throw new Exception("Flat layer metadata redo path is wrong.");
    string flatEdited = Path.Combine(output, "FlatEdited.comp");
    ImageProjectWorkflow.Save(flatSession, flatEdited);
    if (flatSession.IsDirty) throw new Exception("Flat multi-layer save did not advance the save point.");
    foreach (string asset in Directory.GetFiles(images))
        if (!File.ReadAllBytes(asset).SequenceEqual(File.ReadAllBytes(Path.Combine(flatEdited, "images", Path.GetFileName(asset)))))
            throw new Exception("Flat multi-layer save changed an untouched layer asset.");
    var flatReopened = ImageProjectWorkflow.OpenEditable(flatEdited);
    if (!flatReopened.CanEdit || flatReopened.Layers[0].Name != "Overlay" ||
        flatReopened.Layers[0].Id != topLayerId || flatReopened.Layers[0].IsVisible)
        throw new Exception("Flat multi-layer save did not reopen with its layer identity.");
    AssertRaster(bottom, ImageProjectWorkflow.RenderFlatNormal(flatReopened));
    string flatPreview = Path.Combine(output, "flat-edited-export.png");
    ImageProjectWorkflow.ExportPng(flatReopened, flatPreview);
    AssertRaster(bottom, ImageCodec.Load(flatPreview));
    try
    {
        ProjectStore.ExportPng(flatReopened, Path.Combine(output, "flat-unsafe-raw.png"));
        throw new Exception("A raw layer asset was exported as the whole flat project.");
    }
    catch (NotSupportedException) { }
    if (!flatSession.Undo() || !flatSession.IsDirty || !flatSession.Redo() || flatSession.IsDirty)
        throw new Exception("Flat multi-layer history lost its saved revision.");
    string flatMutable = Path.Combine(output, "FlatMutable.comp");
    Directory.CreateDirectory(Path.Combine(flatMutable, "images"));
    File.Copy(Path.Combine(flatEdited, "manifest.json"), Path.Combine(flatMutable, "manifest.json"));
    foreach (string asset in Directory.GetFiles(Path.Combine(flatEdited, "images")))
        File.Copy(asset, Path.Combine(flatMutable, "images", Path.GetFileName(asset)));
    var mutableFlat = ImageProjectWorkflow.OpenEditable(flatMutable);
    mutableFlat.SetLayerVisible(topLayerId, true);
    string changedAsset = Path.Combine(flatMutable, "images", topLayer["imageFile"]!.GetValue<string>());
    using (var stream = new FileStream(changedAsset, FileMode.Append)) stream.WriteByte(1);
    string rejectedFlatSave = Path.Combine(output, "FlatChanged.comp");
    try
    {
        ImageProjectWorkflow.Save(mutableFlat, rejectedFlatSave);
        throw new Exception("Changed multi-layer asset was saved.");
    }
    catch (IOException) { }
    if (!mutableFlat.IsDirty || Directory.Exists(rejectedFlatSave) ||
        Directory.GetDirectories(output, "FlatChanged.comp.tmp-*").Length != 0)
        throw new Exception("Rejected multi-layer save changed state or left temporary files.");

    Guid bottomLayerId = Guid.Parse(baseLayer["id"]!.GetValue<string>());
    var pixelSession = ImageProjectWorkflow.OpenEditable(flat);
    TileRaster originalBottom = pixelSession.GetLayerRaster(bottomLayerId);
    TileRaster originalTop = pixelSession.GetLayerRaster(topLayerId);
    byte[] pixelTile = originalTop.ReadTileCopy(0, 0);
    new byte[] { 200, 0, 0, 200 }.CopyTo(pixelTile, 0);
    TileRaster changedTop = originalTop.ReplaceTile(0, 0, pixelTile);
    AssertRaster(RasterCompositor.SourceOver(bottom, changedTop),
        ImageProjectWorkflow.RenderFlatNormal(pixelSession, topLayerId, changedTop));
    AssertRaster(result, ImageProjectWorkflow.RenderFlatNormal(pixelSession));
    if (pixelSession.IsDirty || pixelSession.Undo() ||
        !ReferenceEquals(pixelSession.GetLayerRaster(topLayerId), originalTop))
        throw new Exception("Temporary layer preview changed pixels or history.");
    try
    {
        ImageProjectWorkflow.RenderFlatNormal(pixelSession, topLayerId, new TileRaster(1, 1));
        throw new Exception("Preview accepted mismatched layer dimensions.");
    }
    catch (ArgumentException) { }
    try
    {
        ImageProjectWorkflow.RenderFlatNormal(pixelSession, Guid.NewGuid(), changedTop);
        throw new Exception("Preview accepted a layer outside the document.");
    }
    catch (ArgumentException) { }
    pixelSession.ReplaceLayerRaster(topLayerId, changedTop);
    pixelSession.RenameLayer(bottomLayerId, "Background");
    pixelSession.SetLayerVisible(topLayerId, false);
    if (!ReferenceEquals(pixelSession.GetLayerRaster(bottomLayerId), originalBottom) ||
        !Pixel(originalTop, 0, 0).SequenceEqual(new byte[] { 80, 20, 40, 128 }))
        throw new Exception("Editing one layer changed another layer or an old pixel snapshot.");
    AssertRaster(bottom, ImageProjectWorkflow.RenderFlatNormal(pixelSession));
    AssertRaster(bottom, ImageProjectWorkflow.RenderFlatNormal(pixelSession, topLayerId, changedTop));
    if (!pixelSession.Undo()) throw new Exception("Multi-layer visibility did not undo.");
    TileRaster changedComposite = RasterCompositor.SourceOver(bottom, changedTop);
    AssertRaster(changedComposite, ImageProjectWorkflow.RenderFlatNormal(pixelSession));
    if (!pixelSession.Undo() || !pixelSession.Undo() || pixelSession.IsDirty)
        throw new Exception("Multi-layer metadata and pixel edits did not undo to the original save point.");
    AssertRaster(result, ImageProjectWorkflow.RenderFlatNormal(pixelSession));
    if (!pixelSession.Redo() || !pixelSession.Redo() || !pixelSession.Redo() ||
        !pixelSession.Undo() || !pixelSession.IsDirty)
        throw new Exception("Multi-layer pixel and metadata redo path is wrong.");
    string flatPixels = Path.Combine(output, "FlatPixels.comp");
    ImageProjectWorkflow.Save(pixelSession, flatPixels);
    if (pixelSession.IsDirty || !ReferenceEquals(pixelSession.GetLayerRaster(bottomLayerId), originalBottom))
        throw new Exception("Multi-layer pixel save lost its save point or changed an untouched layer.");
    string bottomAsset = baseLayer["imageFile"]!.GetValue<string>();
    string topAsset = topLayer["imageFile"]!.GetValue<string>();
    if (!File.ReadAllBytes(Path.Combine(images, bottomAsset)).SequenceEqual(
            File.ReadAllBytes(Path.Combine(flatPixels, "images", bottomAsset))) ||
        File.ReadAllBytes(Path.Combine(images, topAsset)).SequenceEqual(
            File.ReadAllBytes(Path.Combine(flatPixels, "images", topAsset))))
        throw new Exception("Multi-layer pixel save did not preserve the untouched asset and rewrite the edited one.");
    var reopenedPixels = ImageProjectWorkflow.OpenEditable(flatPixels);
    AssertRaster(originalBottom, reopenedPixels.GetLayerRaster(bottomLayerId));
    AssertRaster(changedTop, reopenedPixels.GetLayerRaster(topLayerId));
    AssertRaster(changedComposite, ImageProjectWorkflow.RenderFlatNormal(reopenedPixels));
    string flatPixelExport = Path.Combine(output, "flat-pixels-export.png");
    ImageProjectWorkflow.ExportPng(reopenedPixels, flatPixelExport);
    AssertRaster(changedComposite, ImageCodec.Load(flatPixelExport));
    byte[] bottomTile = originalBottom.ReadTileCopy(0, 0);
    new byte[] { 30, 40, 50, 255 }.CopyTo(bottomTile, 0);
    TileRaster changedBottom = originalBottom.ReplaceTile(0, 0, bottomTile);
    reopenedPixels.ReplaceLayerRaster(bottomLayerId, changedBottom);
    string bothPixels = Path.Combine(output, "FlatPixelsBoth.comp");
    ImageProjectWorkflow.Save(reopenedPixels, bothPixels);
    if (!File.ReadAllBytes(Path.Combine(flatPixels, "images", topAsset)).SequenceEqual(
            File.ReadAllBytes(Path.Combine(bothPixels, "images", topAsset))) ||
        File.ReadAllBytes(Path.Combine(flatPixels, "images", bottomAsset)).SequenceEqual(
            File.ReadAllBytes(Path.Combine(bothPixels, "images", bottomAsset))))
        throw new Exception("Editing the second layer rewrote the first or skipped the second.");
    AssertRaster(changedBottom, ImageProjectWorkflow.OpenEditable(bothPixels).GetLayerRaster(bottomLayerId));

    string exported = Path.Combine(output, "composite.png");
    ImageCodec.SavePng(ImageProjectWorkflow.RenderFlatNormal(flat), exported);
    AssertRaster(result, ImageCodec.Load(exported));

    string masked = Path.Combine(output, "Masked.comp");
    string maskedImages = Path.Combine(masked, "images");
    Directory.CreateDirectory(maskedImages);
    foreach (string asset in Directory.GetFiles(images))
        File.Copy(asset, Path.Combine(maskedImages, Path.GetFileName(asset)));
    var maskedManifest = JsonNode.Parse(originalManifest)!.AsObject();
    var maskedTop = maskedManifest["layers"]![1]!.AsObject();
    string maskName = topId.ToUpperInvariant() + ".mask.png";
    maskedTop["maskFile"] = maskName;
    maskedTop["maskEnabled"] = true;
    string maskedManifestPath = Path.Combine(masked, "manifest.json");
    File.WriteAllText(maskedManifestPath, maskedManifest.ToJsonString());
    string maskPath = Path.Combine(maskedImages, maskName);
    SaveGrayMask(maskPath, 300, 300);
    var maskedSession = ImageProjectWorkflow.OpenEditable(masked);
    if (!maskedSession.CanEdit || maskedSession.GetLayerMask(topLayerId) is not { } loadedMask)
        throw new Exception("Full-canvas Gray8 mask project did not open as editable.");
    byte[] editableMaskTile = loadedMask.ReadTileCopy(0, 0);
    editableMaskTile[0] = 255;
    maskedSession.ReplaceLayerMask(topLayerId, loadedMask.ReplaceTile(0, 0, editableMaskTile));
    if (!Pixel(ImageProjectWorkflow.RenderFlatNormal(maskedSession), 0, 0)
            .SequenceEqual(new byte[] { 100, 60, 100, 228 }))
        throw new Exception("Editing a layer mask did not update the composite.");
    maskedSession.SetLayerMaskEnabled(topLayerId, false);
    AssertRaster(result, ImageProjectWorkflow.RenderFlatNormal(maskedSession));
    maskedSession.SetLayerMaskEnabled(topLayerId, true);
    string maskedEdited = Path.Combine(output, "MaskedEdited.comp");
    ImageProjectWorkflow.Save(maskedSession, maskedEdited);
    var reopenedMasked = ImageProjectWorkflow.OpenEditable(maskedEdited);
    if (reopenedMasked.GetLayerMask(topLayerId) is not { } reopenedMask ||
        reopenedMask.ReadTileCopy(0, 0)[0] != 255 ||
        Pixel(ImageProjectWorkflow.RenderFlatNormal(reopenedMasked), 0, 0)[0] != 100)
        throw new Exception("Edited layer mask did not survive save and reopen.");
    if (!maskedSession.Undo() || !maskedSession.Undo() || !maskedSession.Redo() || !maskedSession.Redo())
        throw new Exception("Layer mask enable/edit history did not undo and redo.");
    string singleMasked = Path.Combine(output, "SingleMasked.comp");
    string singleMaskedImages = Path.Combine(singleMasked, "images");
    Directory.CreateDirectory(singleMaskedImages);
    string singleSource = Path.Combine(output, "Image.comp");
    var singleManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(singleSource, "manifest.json")))!.AsObject();
    var singleLayer = singleManifest["layers"]![0]!.AsObject();
    string singleImageName = singleLayer["imageFile"]!.GetValue<string>();
    File.Copy(Path.Combine(singleSource, "images", singleImageName), Path.Combine(singleMaskedImages, singleImageName));
    string singleMaskName = Guid.Parse(singleLayer["id"]!.GetValue<string>()).ToString("D").ToUpperInvariant() + ".mask.png";
    singleLayer["maskFile"] = singleMaskName;
    singleLayer["maskEnabled"] = true;
    File.WriteAllText(Path.Combine(singleMasked, "manifest.json"), singleManifest.ToJsonString());
    string singleMaskPath = Path.Combine(singleMaskedImages, singleMaskName);
    SaveGrayMask(singleMaskPath, singleManifest["width"]!.GetValue<int>(), singleManifest["height"]!.GetValue<int>());
    var singleMaskedSession = ImageProjectWorkflow.OpenEditable(singleMasked);
    TileRaster singleRaw = ImageCodec.Load(Path.Combine(singleMaskedImages, singleImageName));
    AssertRaster(RasterCompositor.ApplyMask(singleRaw, ImageCodec.LoadGrayMask(singleMaskPath)),
        ImageProjectWorkflow.RenderFlatNormal(singleMaskedSession));
    GrayTileRaster gray = ImageCodec.LoadGrayMask(maskPath);
    byte[] changedMaskTile = gray.ReadTileCopy(0, 0);
    changedMaskTile[0] = 255;
    if (gray.ReadTileCopy(0, 0)[0] != 0 ||
        gray.ReplaceTile(0, 0, changedMaskTile).ReadTileCopy(0, 0)[0] != 255)
        throw new Exception("Gray mask snapshot did not own its tile bytes.");
    TileRaster maskedResult = ImageProjectWorkflow.RenderFlatNormal(masked);
    if (!Pixel(maskedResult, 0, 0).SequenceEqual(new byte[] { 40, 80, 120, 200 }) ||
        !Pixel(maskedResult, 2, 0).SequenceEqual(new byte[] { 255, 0, 0, 255 }) ||
        !Pixel(maskedResult, 256, 0).SequenceEqual(new byte[] { 30, 124, 90, 214 }) ||
        !Pixel(top, 0, 0).SequenceEqual(new byte[] { 80, 20, 40, 128 }))
        throw new Exception("Gray8 layer mask coverage produced wrong premultiplied pixels.");
    string maskedExport = Path.Combine(output, "masked-composite.png");
    ImageCodec.SavePng(maskedResult, maskedExport);
    AssertRaster(maskedResult, ImageCodec.Load(maskedExport));
    maskedTop["maskEnabled"] = false;
    File.WriteAllText(maskedManifestPath, maskedManifest.ToJsonString());
    AssertRaster(result, ImageProjectWorkflow.RenderFlatNormal(masked));
    maskedTop["maskEnabled"] = true;
    File.WriteAllText(maskedManifestPath, maskedManifest.ToJsonString());
    SaveGrayMask(maskPath, 299, 300);
    try
    {
        ImageProjectWorkflow.RenderFlatNormal(masked);
        throw new Exception("Incorrect mask dimensions were rendered.");
    }
    catch (NotSupportedException) { }
    File.Copy(Path.Combine(fixtures, "alpha-tiles.png"), maskPath, overwrite: true);
    try
    {
        ImageProjectWorkflow.RenderFlatNormal(masked);
        throw new Exception("RGBA image was accepted as a Gray8 mask.");
    }
    catch (InvalidDataException) { }
    SaveGrayMask(maskPath, 300, 300);

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

static void CheckClippingMask(string output)
{
    string project = Path.Combine(output, "ClippingMask.comp");
    var session = ProjectSession.CreateBlank(2, 1);
    Guid sourceId = session.ActiveLayerId!.Value;
    var source = new TileRaster(2, 1).ReplaceTile(0, 0, [64, 0, 0, 128, 64, 0, 0, 255]);
    session.ReplaceRaster(source);
    Guid targetId = session.AddBlankLayer("Clipped", 1);
    var target = new TileRaster(2, 1).ReplaceTile(0, 0, [0, 0, 255, 255, 0, 0, 255, 255]);
    session.ReplaceLayerRaster(targetId, target);
    session.SetLayerMaskSource(targetId, sourceId);
    session.SetLayerVisible(sourceId, false);
    ImageProjectWorkflow.Save(session, project);
    var reopened = ImageProjectWorkflow.OpenEditable(project);
    if (!reopened.CanEdit || reopened.Layers.Count != 2)
        throw new Exception("A valid flat clipping-mask project was not editable.");
    TileRaster result = ImageProjectWorkflow.RenderFlatNormal(reopened);
    if (!Pixel(result, 0, 0).SequenceEqual(new byte[] { 0, 0, 128, 128 }) ||
        !Pixel(result, 1, 0).SequenceEqual(new byte[] { 0, 0, 255, 255 }))
        throw new Exception("Clipping-mask source alpha was not applied without using source RGB or visibility.");
    string saved = Path.Combine(output, "ClippingMaskSaved.comp");
    ImageProjectWorkflow.Save(reopened, saved);
    var persisted = ImageProjectWorkflow.OpenEditable(saved);
    if (!Pixel(ImageProjectWorkflow.RenderFlatNormal(persisted), 0, 0).SequenceEqual(new byte[] { 0, 0, 128, 128 }))
        throw new Exception("Clipping-mask relationship was not preserved by save and reopen.");
    try
    {
        persisted.DeleteLayer(sourceId);
        throw new Exception("A clipping-mask source was deleted without an explicit release operation.");
    }
    catch (NotSupportedException) { }
    try
    {
        persisted.MoveLayer(targetId, 0);
        throw new Exception("Clipping-mask layers were reordered without a placement policy.");
    }
    catch (NotSupportedException) { }
    persisted.SetLayerMaskSource(targetId, null);
    persisted.DeleteLayer(sourceId);
    if (persisted.Layers.Count != 1 || persisted.Layers[0].MaskSourceId is not null)
        throw new Exception("Releasing a clipping relationship did not restore ordinary layer editing.");
}

static void CheckInvalidClippingRelationships(string output)
{
    string source = Path.Combine(output, "ClippingMaskSaved.comp");
    var sourceManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "manifest.json")))!.AsObject();
    var sourceLayers = sourceManifest["layers"]!.AsArray();
    Guid sourceId = Guid.Parse(sourceLayers[0]!["id"]!.GetValue<string>());
    Guid targetId = Guid.Parse(sourceLayers[1]!["id"]!.GetValue<string>());

    string missing = Path.Combine(output, "InvalidClippingMissing.comp");
    CloneProject(source, missing);
    var missingManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(missing, "manifest.json")))!.AsObject();
    missingManifest["layers"]!.AsArray()[1]!["maskSourceID"] = Guid.NewGuid().ToString("D");
    File.WriteAllText(Path.Combine(missing, "manifest.json"), missingManifest.ToJsonString());
    ExpectInvalidData(() => ProjectStore.Open(missing), "A clipping relationship with a missing source");

    string cycle = Path.Combine(output, "InvalidClippingCycle.comp");
    CloneProject(source, cycle);
    var cycleManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(cycle, "manifest.json")))!.AsObject();
    var cycleLayers = cycleManifest["layers"]!.AsArray();
    cycleLayers[0]!["maskSourceID"] = targetId.ToString("D");
    cycleLayers[1]!["maskSourceID"] = sourceId.ToString("D");
    File.WriteAllText(Path.Combine(cycle, "manifest.json"), cycleManifest.ToJsonString());
    ExpectInvalidData(() => ProjectStore.Open(cycle), "A cyclic clipping relationship");
    Console.WriteLine("PASS: cyclic and missing clipping relationships are rejected before rendering");
}

static void CheckExposureAdjustment(string output)
{
    string project = Path.Combine(output, "ExposureAdjustment.comp");
    var session = ProjectSession.CreateBlank(2, 1);
    Guid rasterId = session.ActiveLayerId!.Value;
    TileRaster source = new TileRaster(2, 1).ReplaceTile(0, 0,
        [32, 16, 8, 128, 100, 40, 20, 255]);
    session.ReplaceRaster(source);
    Guid adjustmentId = session.AddExposureAdjustment("Exposure", new ExposureSettings(1, 0, 1), 1);
    if (!session.Layers.Single(layer => layer.Id == adjustmentId).IsAdjustment ||
        session.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Exposure" ||
        session.GetExposureAdjustment(adjustmentId) != new ExposureSettings(1, 0, 1))
        throw new Exception("Exposure adjustment metadata was not created.");
    TileRaster expected = RasterCompositor.ApplyExposure(source, new ExposureSettings(1, 0, 1));
    AssertRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session));
    if (Pixel(expected, 0, 0)[3] != Pixel(source, 0, 0)[3])
        throw new Exception("Exposure adjustment changed alpha.");

    session.SetExposureAdjustment(adjustmentId, new ExposureSettings(-1, 0, 1));
    TileRaster changed = ImageProjectWorkflow.RenderFlatNormal(session);
    AssertRaster(RasterCompositor.ApplyExposure(source, new ExposureSettings(-1, 0, 1)), changed);
    if (!session.Undo() || !SameRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session)) ||
        !session.Redo() || !SameRaster(changed, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Exposure adjustment did not participate in undo/redo history.");

    ImageProjectWorkflow.Save(session, project);
    var reopened = ImageProjectWorkflow.OpenEditable(project);
    if (!reopened.CanEdit || reopened.Layers.Count != 2 ||
        reopened.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Exposure")
        throw new Exception("Exposure adjustment project did not reopen as editable metadata.");
    AssertRaster(changed, ImageProjectWorkflow.RenderFlatNormal(reopened));
    Console.WriteLine("PASS: Exposure adjustment layer metadata, premultiplied pixels, history and save/reopen");
}

static void CheckLevelsAdjustment(string output)
{
    string project = Path.Combine(output, "LevelsAdjustment.comp");
    var session = ProjectSession.CreateBlank(2, 1);
    TileRaster source = new TileRaster(2, 1).ReplaceTile(0, 0,
        [32, 16, 8, 128, 100, 40, 20, 255]);
    session.ReplaceRaster(source);
    var range = new LevelRange(16, 240, 1.2, 8, 250);
    Guid adjustmentId = session.AddLevelsAdjustment("Levels", new LevelsSettings(range, new(), new(), new()), 1);
    if (!session.Layers.Single(layer => layer.Id == adjustmentId).IsAdjustment ||
        session.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Levels" ||
        session.GetLevelsAdjustment(adjustmentId).Rgb != range)
        throw new Exception("Levels adjustment metadata was not created.");
    TileRaster expected = RasterCompositor.ApplyLevels(source, new LevelsSettings(range, new(), new(), new()));
    AssertRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session));
    if (Pixel(expected, 0, 0)[3] != Pixel(source, 0, 0)[3])
        throw new Exception("Levels adjustment changed alpha.");

    var changedSettings = new LevelsSettings(new LevelRange(0, 255, 1, 0, 255), new(), new(), new());
    session.SetLevelsAdjustment(adjustmentId, changedSettings);
    TileRaster changed = ImageProjectWorkflow.RenderFlatNormal(session);
    AssertRaster(RasterCompositor.ApplyLevels(source, changedSettings), changed);
    if (!session.Undo() || !SameRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session)) ||
        !session.Redo() || !SameRaster(changed, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Levels adjustment did not participate in undo/redo history.");

    ImageProjectWorkflow.Save(session, project);
    var reopened = ImageProjectWorkflow.OpenEditable(project);
    if (!reopened.CanEdit || reopened.Layers.Count != 2 ||
        reopened.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Levels")
        throw new Exception("Levels adjustment project did not reopen as editable metadata.");
    AssertRaster(changed, ImageProjectWorkflow.RenderFlatNormal(reopened));
    Console.WriteLine("PASS: Levels adjustment layer metadata, premultiplied pixels, history and save/reopen");
}

static void CheckHueSaturationAdjustment(string output)
{
    string project = Path.Combine(output, "HueSaturationAdjustment.comp");
    var session = ProjectSession.CreateBlank(2, 1);
    TileRaster source = new TileRaster(2, 1).ReplaceTile(0, 0,
        [32, 16, 8, 128, 100, 40, 20, 255]);
    session.ReplaceRaster(source);
    var settings = new HueSaturationSettings(60, 25, 10);
    Guid adjustmentId = session.AddHueSaturationAdjustment("Hue/Saturation", settings, 1);
    if (!session.Layers.Single(layer => layer.Id == adjustmentId).IsAdjustment ||
        session.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Hue/Saturation" ||
        session.GetHueSaturationAdjustment(adjustmentId) != settings)
        throw new Exception("Hue/Saturation adjustment metadata was not created.");
    TileRaster expected = RasterCompositor.ApplyHueSaturation(source, settings);
    AssertRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session));
    if (Pixel(expected, 0, 0)[3] != Pixel(source, 0, 0)[3])
        throw new Exception("Hue/Saturation adjustment changed alpha.");

    var changedSettings = new HueSaturationSettings(0, 0, 0, true);
    session.SetHueSaturationAdjustment(adjustmentId, changedSettings);
    TileRaster changed = ImageProjectWorkflow.RenderFlatNormal(session);
    AssertRaster(RasterCompositor.ApplyHueSaturation(source, changedSettings), changed);
    if (!session.Undo() || !SameRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session)) ||
        !session.Redo() || !SameRaster(changed, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Hue/Saturation adjustment did not participate in undo/redo history.");

    ImageProjectWorkflow.Save(session, project);
    var reopened = ImageProjectWorkflow.OpenEditable(project);
    if (!reopened.CanEdit || reopened.Layers.Count != 2 ||
        reopened.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Hue/Saturation")
        throw new Exception("Hue/Saturation adjustment project did not reopen as editable metadata.");
    AssertRaster(changed, ImageProjectWorkflow.RenderFlatNormal(reopened));
    Console.WriteLine("PASS: Hue/Saturation adjustment layer metadata, premultiplied pixels, history and save/reopen");
}

static void CheckCurvesAdjustment(string output)
{
    string project = Path.Combine(output, "CurvesAdjustment.comp");
    var session = ProjectSession.CreateBlank(2, 1);
    TileRaster source = new TileRaster(2, 1).ReplaceTile(0, 0,
        [32, 16, 8, 128, 100, 40, 20, 255]);
    session.ReplaceRaster(source);
    var settings = new CurvesSettings(20, 160, 240,
        new CurveChannelSettings(10, 140, 230),
        new CurveChannelSettings(5, 150, 245),
        new CurveChannelSettings(15, 170, 235));
    Guid adjustmentId = session.AddCurvesAdjustment("Curves", settings, 1);
    if (!session.Layers.Single(layer => layer.Id == adjustmentId).IsAdjustment ||
        session.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Curves" ||
        session.GetCurvesAdjustment(adjustmentId) != settings)
        throw new Exception("Curves adjustment metadata was not created.");
    TileRaster expected = RasterCompositor.ApplyCurves(source, settings);
    AssertRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session));
    if (Pixel(expected, 0, 0)[3] != Pixel(source, 0, 0)[3])
        throw new Exception("Curves adjustment changed alpha.");

    var changedSettings = new CurvesSettings(0, 128, 255,
        new CurveChannelSettings(0, 128, 255),
        new CurveChannelSettings(0, 128, 255),
        new CurveChannelSettings(0, 128, 255));
    session.SetCurvesAdjustment(adjustmentId, changedSettings);
    TileRaster changed = ImageProjectWorkflow.RenderFlatNormal(session);
    AssertRaster(RasterCompositor.ApplyCurves(source, changedSettings), changed);
    if (!session.Undo() || !SameRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session)) ||
        !session.Redo() || !SameRaster(changed, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Curves adjustment did not participate in undo/redo history.");

    ImageProjectWorkflow.Save(session, project);
    var reopened = ImageProjectWorkflow.OpenEditable(project);
    if (!reopened.CanEdit || reopened.Layers.Count != 2 ||
        reopened.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Curves" ||
        reopened.GetCurvesAdjustment(adjustmentId) != changedSettings)
        throw new Exception("Curves adjustment project did not reopen as editable metadata.");
    AssertRaster(changed, ImageProjectWorkflow.RenderFlatNormal(reopened));
    Console.WriteLine("PASS: Curves adjustment layer metadata, premultiplied pixels, history and save/reopen");
}

static void CheckGradientMapAdjustment(string output)
{
    string project = Path.Combine(output, "GradientMapAdjustment.comp");
    var session = ProjectSession.CreateBlank(2, 1);
    TileRaster source = new TileRaster(2, 1).ReplaceTile(0, 0,
        [32, 16, 8, 128, 100, 40, 20, 255]);
    session.ReplaceRaster(source);
    var settings = new GradientMapSettings(new GradientMapStop(0, 20, 60), new GradientMapStop(220, 240, 255));
    Guid adjustmentId = session.AddGradientMapAdjustment("Gradient Map", settings, 1);
    if (!session.Layers.Single(layer => layer.Id == adjustmentId).IsAdjustment ||
        session.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Gradient Map" ||
        session.GetGradientMapAdjustment(adjustmentId) != settings)
        throw new Exception("Gradient Map adjustment metadata was not created.");
    TileRaster expected = RasterCompositor.ApplyGradientMap(source, settings);
    AssertRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session));
    if (Pixel(expected, 0, 0)[3] != Pixel(source, 0, 0)[3])
        throw new Exception("Gradient Map adjustment changed alpha.");

    var changedSettings = new GradientMapSettings(new GradientMapStop(30, 0, 40), new GradientMapStop(255, 210, 120));
    session.SetGradientMapAdjustment(adjustmentId, changedSettings);
    TileRaster changed = ImageProjectWorkflow.RenderFlatNormal(session);
    AssertRaster(RasterCompositor.ApplyGradientMap(source, changedSettings), changed);
    if (!session.Undo() || !SameRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session)) ||
        !session.Redo() || !SameRaster(changed, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Gradient Map adjustment did not participate in undo/redo history.");

    ImageProjectWorkflow.Save(session, project);
    var reopened = ImageProjectWorkflow.OpenEditable(project);
    if (!reopened.CanEdit || reopened.Layers.Count != 2 ||
        reopened.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Gradient Map" ||
        reopened.GetGradientMapAdjustment(adjustmentId) != changedSettings)
        throw new Exception("Gradient Map adjustment project did not reopen as editable metadata.");
    AssertRaster(changed, ImageProjectWorkflow.RenderFlatNormal(reopened));
    Console.WriteLine("PASS: Gradient Map adjustment layer metadata, premultiplied pixels, history and save/reopen");
}

static void CheckGaussianBlurAdjustment(string output)
{
    string project = Path.Combine(output, "GaussianBlurAdjustment.comp");
    var session = ProjectSession.CreateBlank(3, 1);
    TileRaster source = new TileRaster(3, 1).ReplaceTile(0, 0,
        [32, 16, 8, 128, 100, 40, 20, 255, 0, 0, 0, 0]);
    session.ReplaceRaster(source);
    var settings = new GaussianBlurSettings(2);
    Guid adjustmentId = session.AddGaussianBlurAdjustment("Gaussian Blur", settings, 1);
    if (!session.Layers.Single(layer => layer.Id == adjustmentId).IsAdjustment ||
        session.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Gaussian Blur" ||
        session.GetGaussianBlurAdjustment(adjustmentId) != settings)
        throw new Exception("Gaussian Blur adjustment metadata was not created.");
    TileRaster expected = RasterCompositor.ApplyGaussianBlur(source, settings);
    AssertRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session));
    if (Pixel(expected, 2, 0)[3] == 0 || Pixel(expected, 2, 0)[0] > Pixel(expected, 2, 0)[3])
        throw new Exception("Gaussian Blur adjustment did not preserve premultiplied alpha at transparent edges.");

    var changedSettings = new GaussianBlurSettings(3);
    session.SetGaussianBlurAdjustment(adjustmentId, changedSettings);
    TileRaster changed = ImageProjectWorkflow.RenderFlatNormal(session);
    AssertRaster(RasterCompositor.ApplyGaussianBlur(source, changedSettings), changed);
    if (!session.Undo() || !SameRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session)) ||
        !session.Redo() || !SameRaster(changed, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Gaussian Blur adjustment did not participate in undo/redo history.");

    ImageProjectWorkflow.Save(session, project);
    var reopened = ImageProjectWorkflow.OpenEditable(project);
    if (!reopened.CanEdit || reopened.Layers.Count != 2 ||
        reopened.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Gaussian Blur" ||
        reopened.GetGaussianBlurAdjustment(adjustmentId) != changedSettings)
        throw new Exception("Gaussian Blur adjustment project did not reopen as editable metadata.");
    AssertRaster(changed, ImageProjectWorkflow.RenderFlatNormal(reopened));
    Console.WriteLine("PASS: Gaussian Blur adjustment layer metadata, premultiplied pixels, history and save/reopen");
}

static void CheckSelectionGaussianBlurFilter(string output)
{
    var sourcePixels = new byte[7 * 5 * 4];
    for (int y = 0; y < 5; y++)
    for (int x = 0; x < 7; x++)
    {
        byte value = (byte)(((x + y) & 1) == 0 ? 255 : 0);
        int pixel = (y * 7 + x) * 4;
        sourcePixels[pixel] = sourcePixels[pixel + 1] = sourcePixels[pixel + 2] = value;
        sourcePixels[pixel + 3] = 255;
    }
    TileRaster source = new TileRaster(7, 5).ReplaceTile(0, 0, sourcePixels);
    GrayTileRaster selection = GrayTileRaster.Rectangle(7, 5, 0, 0, 3, 5);
    TileRaster filtered = RasterCompositor.ApplyGaussianBlur(source, new GaussianBlurSettings(2));
    TileRaster changed = RasterCompositor.BlendThroughMask(source, filtered, selection);
    if (SameRaster(source, changed))
        throw new Exception("Selection Gaussian Blur did not change covered pixels.");
    for (int y = 0; y < 5; y++)
    for (int x = 3; x < 7; x++)
        if (!Pixel(source, x, y).SequenceEqual(Pixel(changed, x, y)))
            throw new Exception("Selection Gaussian Blur changed pixels outside the selection.");
    Console.WriteLine("PASS: selection Gaussian Blur blends the filtered raster through coverage without changing outside pixels");
}

static void CheckSelectionMotionBlurFilter(string output)
{
    var sourcePixels = new byte[7 * 5 * 4];
    for (int y = 0; y < 5; y++)
    for (int x = 0; x < 7; x++)
    {
        byte value = (byte)(((x * 3 + y) & 1) == 0 ? 255 : 0);
        int pixel = (y * 7 + x) * 4;
        sourcePixels[pixel] = sourcePixels[pixel + 1] = sourcePixels[pixel + 2] = value;
        sourcePixels[pixel + 3] = 255;
    }
    TileRaster source = new TileRaster(7, 5).ReplaceTile(0, 0, sourcePixels);
    GrayTileRaster selection = GrayTileRaster.Rectangle(7, 5, 0, 0, 3, 5);
    TileRaster filtered = RasterCompositor.ApplyMotionBlur(source, new MotionBlurSettings(45, 3));
    TileRaster changed = RasterCompositor.BlendThroughMask(source, filtered, selection);
    if (SameRaster(source, changed))
        throw new Exception("Selection Motion Blur did not change covered pixels.");
    for (int y = 0; y < 5; y++)
    for (int x = 3; x < 7; x++)
        if (!Pixel(source, x, y).SequenceEqual(Pixel(changed, x, y)))
            throw new Exception("Selection Motion Blur changed pixels outside the selection.");
    Console.WriteLine("PASS: selection Motion Blur blends the filtered raster through coverage without changing outside pixels");
}

static void CheckMotionBlurAdjustment(string output)
{
    string project = Path.Combine(output, "MotionBlurAdjustment.comp");
    var session = ProjectSession.CreateBlank(5, 1);
    TileRaster source = new TileRaster(5, 1).ReplaceTile(0, 0,
        [0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 255, 255, 0, 0, 0, 0, 0, 0, 0, 0]);
    session.ReplaceRaster(source);
    var settings = new MotionBlurSettings(0, 2);
    Guid adjustmentId = session.AddMotionBlurAdjustment("Motion Blur", settings, 1);
    FlatLayerInfo adjustment = session.Layers.Single(layer => layer.Id == adjustmentId);
    if (!adjustment.IsAdjustment || adjustment.AdjustmentKind != "Motion Blur" ||
        session.GetMotionBlurAdjustment(adjustmentId) != settings)
        throw new Exception("Motion Blur adjustment metadata was not created.");
    TileRaster expected = RasterCompositor.ApplyMotionBlur(source, settings);
    AssertRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session));
    byte[] edge = Pixel(expected, 0, 0);
    if (edge[3] == 0 || edge[0] > edge[3] || edge[1] > edge[3] || edge[2] > edge[3])
        throw new Exception("Motion Blur adjustment did not preserve premultiplied alpha at transparent edges.");

    var changedSettings = new MotionBlurSettings(90, 3);
    session.SetMotionBlurAdjustment(adjustmentId, changedSettings);
    TileRaster changed = ImageProjectWorkflow.RenderFlatNormal(session);
    AssertRaster(RasterCompositor.ApplyMotionBlur(source, changedSettings), changed);
    if (SameRaster(expected, changed) || !session.Undo() || !SameRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session)) ||
        !session.Redo() || !SameRaster(changed, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Motion Blur adjustment did not participate in angle/distance or undo/redo history.");

    ImageProjectWorkflow.Save(session, project);
    var reopened = ImageProjectWorkflow.OpenEditable(project);
    if (!reopened.CanEdit || reopened.Layers.Count != 2 ||
        reopened.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Motion Blur" ||
        reopened.GetMotionBlurAdjustment(adjustmentId) != changedSettings)
        throw new Exception("Motion Blur adjustment project did not reopen as editable metadata.");
    AssertRaster(changed, ImageProjectWorkflow.RenderFlatNormal(reopened));
    Console.WriteLine("PASS: Motion Blur adjustment layer metadata, premultiplied pixels, history and save/reopen");
}

static void CheckNoiseAdjustment(string output)
{
    string project = Path.Combine(output, "NoiseAdjustment.comp");
    var session = ProjectSession.CreateBlank(40, 40);
    byte[] sourcePixels = new byte[40 * 40 * 4];
    for (int pixel = 0; pixel < sourcePixels.Length; pixel += 4)
        new byte[] { 128, 128, 128, 255 }.CopyTo(sourcePixels, pixel);
    new byte[] { 0, 0, 0, 0 }.CopyTo(sourcePixels, sourcePixels.Length - 4);
    TileRaster source = new TileRaster(40, 40).ReplaceTile(0, 0, sourcePixels);
    session.ReplaceRaster(source);
    var settings = new NoiseSettings(80, false, false, 7);
    Guid adjustmentId = session.AddNoiseAdjustment("Add Noise", settings, 1);
    FlatLayerInfo adjustment = session.Layers.Single(layer => layer.Id == adjustmentId);
    if (!adjustment.IsAdjustment || adjustment.AdjustmentKind != "Add Noise" ||
        session.GetNoiseAdjustment(adjustmentId) != settings)
        throw new Exception("Add Noise adjustment metadata was not created.");
    TileRaster expected = RasterCompositor.ApplyNoise(source, settings);
    AssertRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session));
    bool changed = false, channelsDiffer = false;
    for (int y = 0; y < 40; y++)
    for (int x = 0; x < 40; x++)
    {
        byte[] pixel = Pixel(expected, x, y);
        changed |= pixel[0] != 128 || pixel[1] != 128 || pixel[2] != 128;
        channelsDiffer |= pixel[0] != pixel[1] || pixel[1] != pixel[2];
    }
    if (!changed || !channelsDiffer || Pixel(expected, 39, 39)[3] != 0)
        throw new Exception("Add Noise adjustment did not vary color channels while preserving transparency.");

    var changedSettings = new NoiseSettings(80, true, true, 8);
    session.SetNoiseAdjustment(adjustmentId, changedSettings);
    TileRaster changedRaster = ImageProjectWorkflow.RenderFlatNormal(session);
    AssertRaster(RasterCompositor.ApplyNoise(source, changedSettings), changedRaster);
    byte[] monochromatic = Pixel(changedRaster, 0, 0);
    if (SameRaster(expected, changedRaster) || monochromatic[0] != monochromatic[1] || monochromatic[1] != monochromatic[2] ||
        !session.Undo() || !SameRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session)) ||
        !session.Redo() || !SameRaster(changedRaster, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Add Noise adjustment did not participate in distribution, monochromatic mode or undo/redo history.");

    ImageProjectWorkflow.Save(session, project);
    var reopened = ImageProjectWorkflow.OpenEditable(project);
    if (!reopened.CanEdit || reopened.Layers.Count != 2 ||
        reopened.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Add Noise" ||
        reopened.GetNoiseAdjustment(adjustmentId) != changedSettings)
        throw new Exception("Add Noise adjustment project did not reopen as editable metadata.");
    AssertRaster(changedRaster, ImageProjectWorkflow.RenderFlatNormal(reopened));
    Console.WriteLine("PASS: Add Noise adjustment layer metadata, distributions, premultiplied pixels, history and save/reopen");
}

static void CheckLensCorrectionAdjustment(string output)
{
    string project = Path.Combine(output, "LensCorrectionAdjustment.comp");
    var session = ProjectSession.CreateBlank(5, 5);
    byte[] sourcePixels = new byte[5 * 5 * 4];
    for (int y = 0; y < 5; y++)
    for (int x = 0; x < 5; x++)
    {
        int pixel = (y * 5 + x) * 4;
        sourcePixels[pixel] = (byte)(x * 40 + y * 3);
        sourcePixels[pixel + 1] = (byte)(y * 40 + x * 5);
        sourcePixels[pixel + 2] = (byte)(x * 17 + y * 23);
        sourcePixels[pixel + 3] = 255;
    }
    TileRaster source = new TileRaster(5, 5).ReplaceTile(0, 0, sourcePixels);
    session.ReplaceRaster(source);
    var settings = new LensCorrectionSettings(50);
    Guid adjustmentId = session.AddLensCorrectionAdjustment("Lens Correction", settings, 1);
    FlatLayerInfo adjustment = session.Layers.Single(layer => layer.Id == adjustmentId);
    if (!adjustment.IsAdjustment || adjustment.AdjustmentKind != "Lens Correction" ||
        session.GetLensCorrectionAdjustment(adjustmentId) != settings)
        throw new Exception("Lens Correction adjustment metadata was not created.");
    TileRaster expected = RasterCompositor.ApplyLensCorrection(source, settings);
    AssertRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session));
    if (SameRaster(source, expected) || Pixel(expected, 0, 0)[3] == 0)
        throw new Exception("Lens Correction adjustment did not warp the full-canvas raster as expected.");

    var changedSettings = new LensCorrectionSettings(-100);
    session.SetLensCorrectionAdjustment(adjustmentId, changedSettings);
    TileRaster changed = ImageProjectWorkflow.RenderFlatNormal(session);
    AssertRaster(RasterCompositor.ApplyLensCorrection(source, changedSettings), changed);
    if (SameRaster(expected, changed) || Pixel(changed, 0, 0)[3] >= Pixel(source, 0, 0)[3] ||
        !session.Undo() || !SameRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session)) ||
        !session.Redo() || !SameRaster(changed, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Lens Correction adjustment did not participate in distortion, transparent-edge or undo/redo history.");

    ImageProjectWorkflow.Save(session, project);
    var reopened = ImageProjectWorkflow.OpenEditable(project);
    if (!reopened.CanEdit || reopened.Layers.Count != 2 ||
        reopened.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Lens Correction" ||
        reopened.GetLensCorrectionAdjustment(adjustmentId) != changedSettings)
        throw new Exception("Lens Correction adjustment project did not reopen as editable metadata.");
    AssertRaster(changed, ImageProjectWorkflow.RenderFlatNormal(reopened));
    Console.WriteLine("PASS: Lens Correction adjustment layer metadata, radial warp, transparent edges, history and save/reopen");
}

static void CheckGrainAdjustment(string output)
{
    string project = Path.Combine(output, "GrainAdjustment.comp");
    var session = ProjectSession.CreateBlank(40, 40);
    byte[] sourcePixels = new byte[40 * 40 * 4];
    for (int pixel = 0; pixel < sourcePixels.Length; pixel += 4)
        new byte[] { 128, 128, 128, 255 }.CopyTo(sourcePixels, pixel);
    new byte[] { 0, 0, 0, 0 }.CopyTo(sourcePixels, sourcePixels.Length - 4);
    TileRaster source = new TileRaster(40, 40).ReplaceTile(0, 0, sourcePixels);
    session.ReplaceRaster(source);
    var settings = new GrainSettings(60, 2, 40, 7);
    Guid adjustmentId = session.AddGrainAdjustment("Grain", settings, 1);
    if (!session.Layers.Single(layer => layer.Id == adjustmentId).IsAdjustment ||
        session.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Grain" ||
        session.GetGrainAdjustment(adjustmentId) != settings)
        throw new Exception("Grain adjustment metadata was not created.");
    TileRaster expected = RasterCompositor.ApplyGrain(source, settings);
    AssertRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session));
    byte[] firstPixel = Pixel(expected, 0, 0);
    if ((firstPixel[0] == 128 && firstPixel[1] == 128 && firstPixel[2] == 128) ||
        firstPixel[0] != firstPixel[1] || firstPixel[1] != firstPixel[2] || Pixel(expected, 39, 39)[3] != 0)
        throw new Exception("Grain adjustment did not vary brightness while preserving neutral color and transparency.");
    var changedSettings = new GrainSettings(60, 2, 40, 8);
    session.SetGrainAdjustment(adjustmentId, changedSettings);
    TileRaster changed = ImageProjectWorkflow.RenderFlatNormal(session);
    AssertRaster(RasterCompositor.ApplyGrain(source, changedSettings), changed);
    if (SameRaster(expected, changed) || !session.Undo() || !SameRaster(expected, ImageProjectWorkflow.RenderFlatNormal(session)) ||
        !session.Redo() || !SameRaster(changed, ImageProjectWorkflow.RenderFlatNormal(session)))
        throw new Exception("Grain adjustment did not participate in deterministic seed or undo/redo history.");

    ImageProjectWorkflow.Save(session, project);
    var reopened = ImageProjectWorkflow.OpenEditable(project);
    if (!reopened.CanEdit || reopened.Layers.Count != 2 ||
        reopened.Layers.Single(layer => layer.Id == adjustmentId).AdjustmentKind != "Grain" ||
        reopened.GetGrainAdjustment(adjustmentId) != changedSettings)
        throw new Exception("Grain adjustment project did not reopen as editable metadata.");
    AssertRaster(changed, ImageProjectWorkflow.RenderFlatNormal(reopened));
    Console.WriteLine("PASS: Grain adjustment layer metadata, deterministic seed, premultiplied pixels, history and save/reopen");
}

static void CloneProject(string source, string destination)
{
    foreach (string path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string target = Path.Combine(destination, Path.GetRelativePath(source, path));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(path, target);
    }
}

static void ExpectInvalidData(Action action, string description)
{
    try
    {
        action();
        throw new Exception($"{description} was accepted.");
    }
    catch (InvalidDataException) { }
}

static void SaveGrayMask(string path, int width, int height)
{
    using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
    byte[] pixels = Enumerable.Repeat((byte)128, bitmap.RowBytes * height).ToArray();
    pixels[0] = 0;
    pixels[1] = 255;
    pixels[2] = 255;
    Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100)
        ?? throw new IOException("Cannot encode Gray8 test mask.");
    using var stream = File.Create(path);
    data.SaveTo(stream);
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
            "blendMode" => "Unknown mode",
            "opacity" => -0.1,
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

static void CheckLayerStructure(string output, string sourcePng)
{
    string source = Path.Combine(output, "LayerStructureSource.comp");
    var session = ImageProjectWorkflow.Import(sourcePng, source);
    Guid originalId = session.Layers[0].Id;
    TileRaster original = session.GetLayerRaster(originalId);
    byte[] originalPng = File.ReadAllBytes(Path.Combine(source, "images", session.ImageName));
    Guid blankId = session.AddBlankLayer("Blank", 1);
    if (session.ActiveLayerId != blankId || session.Layers.Count != 2 || session.Raster is not null ||
        session.GetLayerRaster(blankId).StoredBytes != 0 || !session.IsDirty)
        throw new Exception("Blank layer insertion lost layer identity, active layer, or empty pixels.");
    AssertRaster(original, ImageProjectWorkflow.RenderFlatNormal(session));
    string rejectedSave = Path.Combine(output, "LayerStructureNoEncoder.comp");
    try { ProjectStore.Save(session, rejectedSave); throw new Exception("New layer was saved without an encoder."); }
    catch (NotSupportedException) { }
    if (Directory.Exists(rejectedSave) || Directory.GetDirectories(output, "LayerStructureNoEncoder.comp.tmp-*").Length != 0 ||
        !session.IsDirty) throw new Exception("Rejected new-layer save left files or changed the save point.");
    if (!session.Undo() || session.IsDirty || session.ActiveLayerId != originalId ||
        !ReferenceEquals(original, session.Raster) || !session.Redo())
        throw new Exception("Single-to-multi layer transition did not undo/redo with its active layer.");
    Guid copyId = session.DuplicateLayer(originalId, "Copy");
    if (copyId == originalId || session.ActiveLayerId != copyId || session.Layers[1].Id != copyId ||
        !ReferenceEquals(original, session.GetLayerRaster(copyId)))
        throw new Exception("Duplicate did not get independent identity with shared immutable pixels.");
    byte[] tile = original.ReadTileCopy(0, 0);
    new byte[] { 70, 80, 90, 255 }.CopyTo(tile, 0);
    TileRaster changed = original.ReplaceTile(0, 0, tile);
    session.ReplaceLayerRaster(copyId, changed);
    if (!ReferenceEquals(original, session.GetLayerRaster(originalId)))
        throw new Exception("Editing a duplicate changed its source layer.");
    session.DeleteLayer(blankId);
    if (session.ActiveLayerId != copyId || session.Layers.Count != 2)
        throw new Exception("Deleting another layer changed the active layer.");
    string added = Path.Combine(output, "LayerStructureAdded.comp");
    ImageProjectWorkflow.Save(session, added);
    var reopened = ImageProjectWorkflow.OpenEditable(added);
    if (session.IsDirty || reopened.Layers.Count != 2 || reopened.ActiveLayerId != copyId ||
        !originalPng.SequenceEqual(File.ReadAllBytes(Path.Combine(added, "images", originalId.ToString("D").ToUpperInvariant() + ".png"))) ||
        Directory.GetFiles(Path.Combine(added, "images")).Length != 2)
        throw new Exception("Added layers did not save with exact current assets and untouched source bytes.");
    AssertRaster(original, reopened.GetLayerRaster(originalId));
    AssertRaster(changed, reopened.GetLayerRaster(copyId));
    TileRaster withCopy = ImageProjectWorkflow.RenderFlatNormal(reopened);
    reopened.DeleteLayer(copyId);
    if (reopened.ActiveLayerId != originalId || !ReferenceEquals(reopened.Raster, reopened.GetLayerRaster(originalId)))
        throw new Exception("Deleting an active layer did not select its neighbor or return to single-layer access.");
    ImageProjectWorkflow.Save(reopened, added);
    if (Directory.GetFiles(Path.Combine(added, "images")).Length != 1 || reopened.IsDirty)
        throw new Exception("Deleting and saving retained an orphan asset.");
    if (!reopened.Undo() || reopened.ActiveLayerId != copyId || !reopened.IsDirty)
        throw new Exception("Saved deletion did not undo to the deleted layer and its active identity.");
    AssertRaster(changed, reopened.GetLayerRaster(copyId));
    AssertRaster(withCopy, ImageProjectWorkflow.RenderFlatNormal(reopened));
    ImageProjectWorkflow.Save(reopened, added);
    AssertRaster(changed, ImageProjectWorkflow.OpenEditable(added).GetLayerRaster(copyId));
    if (!reopened.Redo()) throw new Exception("Saving an undo state lost the deletion redo step.");
    reopened.DeleteLayer(originalId);
    if (reopened.ActiveLayerId is not null || reopened.Layers.Count != 0 || reopened.Raster is not null)
        throw new Exception("Deleting the final layer left a dangling active layer.");
    var transparent = new TileRaster(original.Width, original.Height);
    AssertRaster(transparent, ImageProjectWorkflow.RenderFlatNormal(reopened));
    string empty = Path.Combine(output, "LayerStructureEmpty.comp");
    ImageProjectWorkflow.Save(reopened, empty);
    var reopenedEmpty = ImageProjectWorkflow.OpenEditable(empty);
    if (!reopenedEmpty.CanEdit || reopenedEmpty.Layers.Count != 0 || reopenedEmpty.IsDirty ||
        Directory.GetFiles(Path.Combine(empty, "images")).Length != 0)
        throw new Exception("Empty v8 project did not save and reopen as editable.");
    string emptyExport = Path.Combine(output, "empty-project.png");
    ImageProjectWorkflow.ExportPng(reopenedEmpty, emptyExport);
    AssertRaster(transparent, ImageCodec.Load(emptyExport));
    if (!reopened.Undo() || reopened.ActiveLayerId != originalId)
        throw new Exception("Empty saved document could not restore its deleted final layer.");
    string restored = Path.Combine(output, "LayerStructureRestored.comp");
    ImageProjectWorkflow.Save(reopened, restored);
    AssertRaster(original, ImageProjectWorkflow.OpenEditable(restored).GetLayerRaster(originalId));
    try { reopenedEmpty.RenameLayer("Missing"); throw new Exception("Empty document accepted a layer rename."); }
    catch (InvalidOperationException) { }
    if (reopenedEmpty.IsDirty || reopenedEmpty.Undo()) throw new Exception("Rejected empty rename changed history.");
    Guid emptyBlank = reopenedEmpty.AddBlankLayer("Start again", 0);
    if (reopenedEmpty.ActiveLayerId != emptyBlank || !reopenedEmpty.Undo() || reopenedEmpty.IsDirty ||
        !reopenedEmpty.Redo()) throw new Exception("Adding to an empty document lost its history or save point.");
    ImageProjectWorkflow.Save(reopenedEmpty, Path.Combine(output, "LayerStructureNewBlank.comp"));

    var clean = ImageProjectWorkflow.OpenEditable(source);
    foreach (Action rejected in new Action[]
    {
        () => clean.AddBlankLayer(" ", 1), () => clean.AddBlankLayer("Out of range", 2),
        () => clean.DuplicateLayer(Guid.NewGuid(), "Foreign"), () => clean.DuplicateLayer(originalId, ""),
        () => clean.DeleteLayer(Guid.NewGuid())
    })
    {
        try { rejected(); throw new Exception("Invalid layer structure edit was accepted."); }
        catch (ArgumentException) { }
    }
    if (clean.IsDirty || clean.Undo() || clean.Layers.Count != 1)
        throw new Exception("Rejected layer structure edits changed document or history.");
    var unloaded = ProjectStore.Open(source);
    try { unloaded.AddBlankLayer("Unloaded", 1); throw new Exception("Unloaded layer edit was accepted."); }
    catch (InvalidOperationException) { }
    string legacy = Path.Combine(output, "Legacy.comp");
    var legacySession = ImageProjectWorkflow.OpenEditable(legacy);
    try { legacySession.DeleteLayer(legacySession.Layers[0].Id); throw new Exception("Legacy structure was silently changed."); }
    catch (NotSupportedException) { }
}

static void CheckNewCanvas(string output)
{
    var session = ProjectSession.CreateBlank(300, 257, 300);
    Guid layerId = session.Layers.Single().Id;
    if (!session.CanEdit || !session.IsDirty || session.HasBeenSaved || session.SavedDirectory is not null ||
        session.LayerName != "Layer 1" || session.ActiveLayerId != layerId || session.Undo() || session.Redo() ||
        session.Raster is null || session.Raster.StoredBytes != 0)
        throw new Exception("New canvas is not an unsaved transparent document with a selected initial layer.");
    try { _ = session.SourceDirectory; throw new Exception("New canvas invented a source directory."); }
    catch (InvalidOperationException) { }
    TileRaster blank = new(300, 257);
    AssertRaster(blank, ImageProjectWorkflow.RenderFlatNormal(session));
    string beforeSave = Path.Combine(output, "new-canvas-unsaved.png");
    ImageProjectWorkflow.ExportPng(session, beforeSave);
    AssertRaster(blank, ImageCodec.Load(beforeSave));
    if (!session.IsDirty || session.HasBeenSaved) throw new Exception("Unsaved export marked the document saved.");

    ProjectSession current = session;
    foreach (var invalid in new (int Width, int Height, double Resolution)[]
    {
        (0, 10, 72), (-1, 10, 72), (30001, 1, 72), (int.MaxValue, 1, 72),
        (1, 0, 72), (1, -1, 72), (1, 30001, 72), (10001, 10000, 72),
        (10, 10, 0), (10, 10, 0.5), (10, 10, 9600.1),
        (10, 10, double.NaN), (10, 10, double.PositiveInfinity), (10, 10, double.NegativeInfinity)
    })
    {
        try
        {
            current = ProjectSession.CreateBlank(invalid.Width, invalid.Height, invalid.Resolution);
            throw new Exception("Invalid new canvas was accepted.");
        }
        catch (ArgumentOutOfRangeException) { }
    }
    if (!ReferenceEquals(current, session) || session.Undo() || !session.IsDirty)
        throw new Exception("Invalid new canvas input changed the caller's existing document or history.");
    foreach (var allowed in new[] { ProjectSession.CreateBlank(30000, 1, 1), ProjectSession.CreateBlank(1, 30000, 9600),
        ProjectSession.CreateBlank(10000, 10000) })
        if (allowed.Raster!.StoredBytes != 0 || allowed.HasBeenSaved || !allowed.IsDirty)
            throw new Exception("New canvas limits allocated full pixels or marked a new document saved.");

    foreach (string? missing in new string?[] { null, "" })
    {
        try { ImageProjectWorkflow.Save(session, missing!); throw new Exception("Missing first-save path was accepted."); }
        catch (ArgumentException) { }
    }
    string existing = Path.Combine(output, "Image.comp");
    byte[] existingManifest = File.ReadAllBytes(Path.Combine(existing, "manifest.json"));
    try { ImageProjectWorkflow.Save(session, existing); throw new Exception("New canvas overwrote an existing project."); }
    catch (IOException) { }
    if (!existingManifest.SequenceEqual(File.ReadAllBytes(Path.Combine(existing, "manifest.json"))) ||
        session.HasBeenSaved || session.SavedDirectory is not null || !session.IsDirty)
        throw new Exception("Rejected first save changed source state or an existing project.");
    byte[] existingExport = File.ReadAllBytes(beforeSave);
    try { ImageProjectWorkflow.Save(session, beforeSave); throw new Exception("New canvas overwrote an existing file."); }
    catch (IOException) { }
    if (!existingExport.SequenceEqual(File.ReadAllBytes(beforeSave)))
        throw new Exception("Rejected first save damaged an existing file.");
    string noEncoder = Path.Combine(output, "NewCanvasNoEncoder.comp");
    try { ProjectStore.Save(session, noEncoder); throw new Exception("New transparent pixels were saved without an encoder."); }
    catch (NotSupportedException) { }
    if (Directory.Exists(noEncoder) || Directory.GetDirectories(output, "NewCanvasNoEncoder.comp.tmp-*").Length != 0)
        throw new Exception("Failed first save left a partial project.");
    var failed = ProjectSession.CreateBlank(2, 2);
    byte[] invalidPixels = new byte[16];
    invalidPixels[0] = 1;
    failed.ReplaceRaster(failed.Raster!.ReplaceTile(0, 0, invalidPixels));
    string failedPath = Path.Combine(output, "NewCanvasEncodingFailed.comp");
    try { ImageProjectWorkflow.Save(failed, failedPath); throw new Exception("Invalid first-save pixels were accepted."); }
    catch (InvalidDataException) { }
    if (failed.HasBeenSaved || failed.SavedDirectory is not null || !failed.IsDirty || Directory.Exists(failedPath) ||
        Directory.GetDirectories(output, "NewCanvasEncodingFailed.comp.tmp-*").Length != 0 || !failed.Undo() || !failed.IsDirty)
        throw new Exception("Failed first-save encoding changed saved state, lost undo or left files.");
    ImageProjectWorkflow.Save(failed, Path.Combine(output, "NewCanvasRecovered.comp"));

    session.RenameLayer("Paint layer");
    byte[] tile = blank.ReadTileCopy(0, 0);
    new byte[] { 20, 30, 40, 255 }.CopyTo(tile, 0);
    TileRaster changed = blank.ReplaceTile(0, 0, tile);
    session.ReplaceLayerRaster(layerId, changed);
    if (!session.Undo() || !session.Undo() || !session.IsDirty || session.HasBeenSaved)
        throw new Exception("Undo to the new document's initial state pretended it had been saved.");
    AssertRaster(blank, ImageProjectWorkflow.RenderFlatNormal(session));
    string destination = Path.Combine(output, "NewCanvas.comp");
    ImageProjectWorkflow.Save(session, destination);
    if (session.IsDirty || !session.HasBeenSaved || session.SavedDirectory != destination || session.SourceDirectory != destination)
        throw new Exception("First save did not establish the saved source and save point.");
    var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "manifest.json")))!;
    if (manifest["version"]!.GetValue<int>() != 8 || manifest["resolution"]!.GetValue<double>() != 300 ||
        !Guid.TryParse(manifest["documentID"]!.GetValue<string>(), out _) ||
        Guid.Parse(manifest["layers"]![0]!["id"]!.GetValue<string>()) != layerId ||
        manifest["width"]!.GetValue<int>() != 300 || manifest["height"]!.GetValue<int>() != 257)
        throw new Exception("New document dimensions, resolution or identity changed at first save.");
    var reopened = ImageProjectWorkflow.OpenEditable(destination);
    if (reopened.IsDirty || !reopened.HasBeenSaved || reopened.ActiveLayerId != layerId)
        throw new Exception("First-saved blank document did not reopen with saved identity.");
    AssertRaster(blank, reopened.GetLayerRaster(layerId));
    string initial = Path.Combine(output, "NewCanvasInitial.comp");
    ImageProjectWorkflow.Save(reopened, initial);
    if (!session.Redo() || !session.Redo() || !session.IsDirty)
        throw new Exception("First save lost edits on the redo branch or their dirty state.");
    ImageProjectWorkflow.Save(session, destination);
    AssertRaster(changed, ImageProjectWorkflow.OpenEditable(destination).GetLayerRaster(layerId));
    if (!session.Undo() || !session.Undo() || !session.IsDirty || !session.HasBeenSaved ||
        !session.Redo() || !session.Redo() || session.IsDirty)
        throw new Exception("Saved new canvas no longer obeys normal history and save-point rules.");
    ImageProjectWorkflow.ExportPng(session, Path.Combine(output, "new-canvas-saved.png"));
    AssertRaster(changed, ImageCodec.Load(Path.Combine(output, "new-canvas-saved.png")));
    Guid documentId = Guid.Parse(manifest["documentID"]!.GetValue<string>());
    string independent = Path.Combine(output, "NewCanvasIndependent.comp");
    var other = ProjectSession.CreateBlank(1, 1);
    ImageProjectWorkflow.Save(other, independent);
    var independentManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(independent, "manifest.json")))!;
    if (Guid.Parse(independentManifest["documentID"]!.GetValue<string>()) == documentId || other.Layers[0].Id == layerId ||
        independentManifest["resolution"]!.GetValue<double>() != 72)
        throw new Exception("Independent new documents reused identity or lost the default resolution.");
}

static void CheckLayerSelection(string output)
{
    var session = ProjectSession.CreateBlank(16, 16);
    Guid first = session.Layers[0].Id, second = session.AddBlankLayer("Second", 1);
    var original = session.GetLayerRaster(first);
    byte[] tile = original.ReadTileCopy(0, 0);
    new byte[] { 10, 20, 30, 255 }.CopyTo(tile, 0);
    var painted = original.ReplaceTile(0, 0, tile);
    session.ReplaceLayerRaster(first, painted);
    string project = Path.Combine(output, "LayerSelection.comp");
    ImageProjectWorkflow.Save(session, project);
    session.SelectLayer(first);
    if (session.IsDirty || session.ActiveLayerId != first || !ReferenceEquals(session.GetLayerRaster(first), painted))
        throw new Exception("Selecting a layer changed pixels, dirty state or active identity.");
    if (!session.Undo() || session.ActiveLayerId != second)
        throw new Exception("Selection mutated a prior pixel history snapshot sharing the manifest.");
    AssertRaster(original, session.GetLayerRaster(first));
    if (!session.Redo() || session.ActiveLayerId != first || session.IsDirty)
        throw new Exception("Selection added an undo step or lost its saved-state revision.");
    ImageProjectWorkflow.Save(session, project);
    if (ImageProjectWorkflow.OpenEditable(project).ActiveLayerId != first)
        throw new Exception("A selected layer was not persisted by explicit save.");
    Guid third = session.AddBlankLayer("Third", 2);
    if (!session.Undo() || session.ActiveLayerId != first || !session.Redo() || session.ActiveLayerId != third)
        throw new Exception("Structure undo did not restore the user's selected layer.");
    session.SelectLayer(first);
    session.DeleteLayer(first);
    if (session.ActiveLayerId != second || !session.Undo() || session.ActiveLayerId != first)
        throw new Exception("Delete undo did not restore the user's selected layer and pixels.");
    ImageProjectWorkflow.Save(session, project);
    var reopened = ImageProjectWorkflow.OpenEditable(project);
    if (reopened.ActiveLayerId != first || reopened.Layers.Count != 3)
        throw new Exception("Selection and restored structure did not save and reopen together.");
    AssertRaster(painted, reopened.GetLayerRaster(first));
    try { session.SelectLayer(Guid.NewGuid()); throw new Exception("Selecting a foreign layer was accepted."); }
    catch (ArgumentException) { }
    if (session.IsDirty || session.ActiveLayerId != first || !session.Undo() || session.Layers.Count != 2 ||
        !session.Undo() || session.ActiveLayerId != second || !session.Redo() || !session.Redo())
        throw new Exception("Rejected selection changed document state or prior history.");
}

static void CheckMacFlatProduced(string macProject, string output)
{
    string sourceManifest = Path.Combine(macProject, "manifest.json");
    byte[] originalManifest = File.ReadAllBytes(sourceManifest);
    var session = ImageProjectWorkflow.OpenEditable(macProject);
    if (session.Layers.Count != 2 || session.Layers[0].Name != "Mac Overlay" ||
        !session.Layers[0].IsVisible || !session.Layers[1].IsVisible || session.IsDirty)
        throw new Exception("Mac-produced flat project did not open with its edited metadata.");
    TileRaster macExport = ImageCodec.Load(Path.Combine(Path.GetDirectoryName(macProject)!, "mac-after.png"));
    AssertRaster(macExport, ImageProjectWorkflow.RenderFlatNormal(session));
    Guid editedId = session.Layers[0].Id, untouchedId = session.Layers[1].Id;
    TileRaster originalEdited = session.GetLayerRaster(editedId);
    TileRaster originalUntouched = session.GetLayerRaster(untouchedId);
    byte[] editedTile = originalEdited.ReadTileCopy(0, 0);
    new byte[] { 150, 0, 0, 150 }.CopyTo(editedTile, 0);
    TileRaster changed = originalEdited.ReplaceTile(0, 0, editedTile);
    session.ReplaceLayerRaster(editedId, changed);
    session.RenameLayer(editedId, "Windows continuation");
    session.SetLayerVisible(untouchedId, false);
    session.MoveLayer(editedId, 1);
    for (int i = 0; i < 4; i++)
        if (!session.Undo()) throw new Exception("Mac flat continuation did not undo.");
    if (session.IsDirty) throw new Exception("Mac flat continuation did not return to its save point.");
    AssertRaster(macExport, ImageProjectWorkflow.RenderFlatNormal(session));
    for (int i = 0; i < 4; i++)
        if (!session.Redo()) throw new Exception("Mac flat continuation did not redo.");
    if (!session.IsDirty || session.Layers[0].Id != untouchedId || session.Layers[0].IsVisible ||
        session.Layers[1].Id != editedId || session.Layers[1].Name != "Windows continuation")
        throw new Exception("Mac flat continuation lost layer identity or order.");
    AssertRaster(changed, ImageProjectWorkflow.RenderFlatNormal(session));
    string destination = Path.Combine(output, "MacFlatContinued.comp");
    ImageProjectWorkflow.Save(session, destination);
    var reopened = ImageProjectWorkflow.OpenEditable(destination);
    if (session.IsDirty || reopened.IsDirty || reopened.Layers[0].Id != untouchedId ||
        reopened.Layers[1].Id != editedId || reopened.Layers[1].Name != "Windows continuation")
        throw new Exception("Mac flat continuation did not save and reopen.");
    AssertRaster(originalUntouched, reopened.GetLayerRaster(untouchedId));
    AssertRaster(changed, reopened.GetLayerRaster(editedId));
    AssertRaster(changed, ImageProjectWorkflow.RenderFlatNormal(reopened));
    string exported = Path.Combine(output, "mac-flat-continued.png");
    ImageProjectWorkflow.ExportPng(reopened, exported);
    AssertRaster(changed, ImageCodec.Load(exported));
    var before = JsonNode.Parse(originalManifest)!;
    var after = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "manifest.json")))!;
    if (before["documentID"]!.GetValue<string>() != after["documentID"]!.GetValue<string>() ||
        before["activeLayerID"]!.GetValue<string>() != after["activeLayerID"]!.GetValue<string>() ||
        !File.ReadAllBytes(sourceManifest).SequenceEqual(originalManifest))
        throw new Exception("Mac flat continuation changed document identity or source project.");
    string untouchedName = before["layers"]![1]!["imageFile"]!.GetValue<string>();
    string editedName = before["layers"]![0]!["imageFile"]!.GetValue<string>();
    if (!File.ReadAllBytes(Path.Combine(macProject, "images", untouchedName)).SequenceEqual(
            File.ReadAllBytes(Path.Combine(destination, "images", untouchedName))) ||
        File.ReadAllBytes(Path.Combine(macProject, "images", editedName)).SequenceEqual(
            File.ReadAllBytes(Path.Combine(destination, "images", editedName))))
        throw new Exception("Mac flat continuation did not isolate the edited asset.");

    var bounded = ImageProjectWorkflow.OpenEditable(macProject);
    for (int edit = 1; edit <= 105; edit++)
    {
        TileRaster current = bounded.GetLayerRaster(editedId);
        byte[] tile = current.ReadTileCopy(0, 0);
        new byte[] { (byte)edit, 0, 0, 255 }.CopyTo(tile, 0);
        bounded.ReplaceLayerRaster(editedId, current.ReplaceTile(0, 0, tile));
    }
    for (int i = 0; i < 100; i++)
        if (!bounded.Undo()) throw new Exception("Multi-layer history lost a retained undo step.");
    if (bounded.Undo() || !bounded.IsDirty ||
        !Pixel(bounded.GetLayerRaster(editedId), 0, 0).SequenceEqual(new byte[] { 5, 0, 0, 255 }))
        throw new Exception("Multi-layer history exceeded its step bound or lost the saved marker.");
    for (int i = 0; i < 100; i++)
        if (!bounded.Redo()) throw new Exception("Multi-layer history lost its redo path.");
    string boundedDestination = Path.Combine(output, "MacFlatBoundedPixels.comp");
    ImageProjectWorkflow.Save(bounded, boundedDestination);
    if (!File.ReadAllBytes(Path.Combine(macProject, "images", untouchedName)).SequenceEqual(
            File.ReadAllBytes(Path.Combine(boundedDestination, "images", untouchedName))))
        throw new Exception("History trimming caused an untouched Mac asset to be rewritten.");
}
