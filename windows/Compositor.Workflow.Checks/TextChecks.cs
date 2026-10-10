using System.Text.Json.Nodes;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

namespace Compositor.Workflow.Checks;

internal static class TextChecks
{
    public static void Run(string fixtures, string output)
    {
        string missingProject = Path.Combine(fixtures, "extended", "F12-missing-font.comp");
        var missing = ProjectStore.Open(missingProject);
        if (!missing.HasTextLayers || missing.TextLayers.Count != 1)
            throw new Exception("F12 text metadata was not retained on open.");
        TextLayerMetadata missingMetadata = missing.TextLayers[0];
        TextLayerStatus missingStatus = TextLayerWorkflow.Inspect(missing)[0];
        if (missingStatus.FontAvailable || !missingStatus.Message.Contains("cached pixels", StringComparison.Ordinal) ||
            !missingStatus.Message.Contains("choose a font", StringComparison.Ordinal))
            throw new Exception("Missing-font text did not report the cache-preserving choice.");
        TileRaster missingCache = ImageCodec.Load(Path.Combine(missingProject, "images", missingMetadata.ImageFile));
        TextLayerRenderResult missingRender = TextLayerWorkflow.Render(missing, missingMetadata.Id);
        if (!missingRender.UsedCache || !SameRaster(missingCache, missingRender.Raster))
            throw new Exception("Missing-font text did not preserve its cached raster.");
        string missingManifest = Path.Combine(missingProject, "manifest.json");
        byte[] manifestBytes = File.ReadAllBytes(missingManifest);
        _ = ImageProjectWorkflow.RenderFlatNormal(missing);
        if (!File.ReadAllBytes(missingManifest).SequenceEqual(manifestBytes))
            throw new Exception("Rendering missing-font text changed the source manifest.");

        string availableFamily = TextLayerWorkflow.AvailableFonts.FirstOrDefault()
            ?? throw new Exception("No installed font was available for the text check.");
        string availableProject = Path.Combine(output, "TextAvailable.comp");
        Guid documentId = Guid.Parse("00000000-0000-4000-9000-000000000020");
        Guid layerId = Guid.Parse("00000000-0000-4000-9000-000000000021");
        Directory.CreateDirectory(Path.Combine(availableProject, "images"));
        string imageName = layerId.ToString("D").ToUpperInvariant() + ".png";
        ImageCodec.SavePng(new TileRaster(96, 48), Path.Combine(availableProject, "images", imageName));
        var manifest = new JsonObject
        {
            ["format"] = "com.compositor.project", ["version"] = 8,
            ["documentID"] = documentId.ToString("D"), ["colorSpace"] = "sRGB", ["resolution"] = 72,
            ["width"] = 96, ["height"] = 48, ["activeLayerID"] = layerId.ToString("D"),
            ["layers"] = new JsonArray(new JsonObject
            {
                ["id"] = layerId.ToString("D"), ["imageFile"] = imageName, ["isVisible"] = true,
                ["name"] = "Available text", ["text"] = new JsonObject
                {
                    ["alignment"] = "left", ["alpha"] = 1, ["blue"] = 0, ["content"] = "Windows text",
                    ["fontPostScriptName"] = availableFamily, ["fontSizePoints"] = 18, ["green"] = 0,
                    ["layout"] = new JsonObject { ["point"] = new JsonObject() }, ["lineSpacingPoints"] = 0,
                    ["red"] = 0, ["trackingPoints"] = 0
                },
                ["transform"] = new JsonObject
                {
                    ["flipX"] = false, ["flipY"] = false, ["origin"] = new JsonArray(0, 0),
                    ["rotation"] = 0, ["sampling"] = "High quality", ["size"] = new JsonArray(96, 48)
                }
            })
        };
        File.WriteAllText(Path.Combine(availableProject, "manifest.json"), manifest.ToJsonString());
        var available = ImageProjectWorkflow.OpenEditable(availableProject);
        TextLayerRenderResult availableRender = TextLayerWorkflow.Render(available, layerId);
        if (!availableRender.Status.FontAvailable || availableRender.UsedCache ||
            !HasInk(availableRender.Raster) || !SameRaster(availableRender.Raster, ImageProjectWorkflow.RenderFlatNormal(available)))
            throw new Exception("Available-font text did not take the redraw path.");
        TextLayerMetadata originalMetadata = available.TextLayers.Single();
        TextHitTestResult textStart = TextLayerWorkflow.HitTest(originalMetadata, 72, 0, 0);
        TextHitTestResult textEnd = TextLayerWorkflow.HitTest(originalMetadata, 72, 1000, 0);
        if (textStart.CharacterIndex != 0 || textStart.LineIndex != 0 || textEnd.CharacterIndex != originalMetadata.Content.Length)
            throw new Exception("Text hit testing did not expose stable UTF-16 offsets for the point layout.");
        LayerTransformInfo mirroredTransform = new(8, 12, available.Width, available.Height, 0, true, false);
        TextHitTestResult mirroredHit = TextLayerWorkflow.HitTest(originalMetadata, mirroredTransform,
            available.Width, available.Height, 72, 104, 12);
        if (mirroredHit.CharacterIndex != 0 || mirroredHit.LineIndex != 0)
            throw new Exception("Mirrored text hit testing did not map the transformed document point.");
        LayerTransformInfo rotatedTransform = new(8, 12, available.Width, available.Height, 90, false, false);
        (double rotatedX, double rotatedY) = ForwardTransform(rotatedTransform, 0, 0);
        TextHitTestResult rotatedHit = TextLayerWorkflow.HitTest(originalMetadata, rotatedTransform,
            available.Width, available.Height, 72, (float)rotatedX, (float)rotatedY);
        if (rotatedHit.CharacterIndex != 0 || rotatedHit.LineIndex != 0)
            throw new Exception("Rotated text hit testing did not map the transformed document point.");
        TextLayerMetadata editedMetadata = originalMetadata with
        {
            Content = "Edited Windows text",
            FontSizePoints = 22,
            Red = 0.1,
            Green = 0.3,
            Blue = 0.9,
            LineSpacingPoints = 4,
            TrackingPoints = 1.5
        };
        TextLayerWorkflow.Update(available, editedMetadata);
        if (!available.IsDirty || available.TextLayers.Single() != editedMetadata ||
            !HasInk(available.GetLayerRaster(layerId)) ||
            !SameRaster(available.GetLayerRaster(layerId), ImageProjectWorkflow.RenderFlatNormal(available)))
            throw new Exception("Text metadata edit did not redraw, update history, and refresh preview.");
        if (!available.Undo() || available.TextLayers.Single() != originalMetadata ||
            !available.Redo() || available.TextLayers.Single() != editedMetadata)
            throw new Exception("Text metadata edit did not participate in undo and redo.");
        string saved = Path.Combine(output, "TextAvailableSaved.comp");
        ImageProjectWorkflow.Save(available, saved);
        var reopened = ProjectStore.Open(saved);
        if (reopened.TextLayers.Count != 1 || reopened.TextLayers[0].Content != "Edited Windows text" ||
            reopened.TextLayers[0].FontPostScriptName != availableFamily)
            throw new Exception("Text metadata was not preserved through save and reopen.");

        var created = ProjectSession.CreateBlank(96, 48);
        var createdMetadata = new TextLayerMetadata(Guid.Empty, "", "Created Windows text", availableFamily, 18,
            0, 0, 0, 1, "left", 0, 0, "point", null);
        TileRaster createdRaster = TextLayerWorkflow.RenderText(createdMetadata, created.Width, created.Height, created.Resolution);
        Guid createdId = created.AddTextLayer("Created text", createdMetadata, createdRaster, created.Layers.Count);
        if (!created.Layers.Single(layer => layer.Id == createdId).IsText ||
            created.TextLayers.Single().Content != "Created Windows text" ||
            !HasInk(created.GetLayerRaster(createdId)))
            throw new Exception("Creating a text layer did not persist metadata and rendered pixels.");
        TextLayerMetadata createdBoxMetadata = createdMetadata with
        {
            Content = "中文 English 🙂 é second line",
            Layout = "box",
            BoxWidth = 72
        };
        TileRaster createdBoxRaster = TextLayerWorkflow.RenderText(createdBoxMetadata, created.Width, created.Height, created.Resolution);
        Guid createdBoxId = created.AddTextLayer("Created box text", createdBoxMetadata, createdBoxRaster, created.Layers.Count);
        TextHitTestResult boxHit = TextLayerWorkflow.HitTest(created.TextLayers.Single(item => item.Id == createdBoxId), 72, 1, 1);
        if (boxHit.LineIndex != 0 || boxHit.CharacterIndex != 0 || !boxHit.IsInside)
            throw new Exception("Box text hit testing did not map the first line to the start of the content.");
        TextLayoutSnapshot boxLayout = TextLayerWorkflow.Layout(createdBoxMetadata, 72, created.Width);
        TextCaretPosition boxStart = boxLayout.Caret(0);
        TextCaretPosition boxEnd = boxLayout.Caret(createdBoxMetadata.Content.Length);
        IReadOnlyList<TextSelectionRectangle> boxSelection = boxLayout.Selection(0, createdBoxMetadata.Content.Length);
        if (boxLayout.Lines.Count < 2 || boxStart.CharacterIndex != 0 || boxStart.LineIndex != 0 ||
            boxEnd.CharacterIndex != createdBoxMetadata.Content.Length || boxSelection.Count < 2 ||
            boxSelection.Any(rectangle => rectangle.Width <= 0 || rectangle.Height <= 0))
            throw new Exception("Text layout did not expose stable caret and multi-line selection geometry.");
        string crlfContent = "A\r\n中文";
        TextLayoutSnapshot crlfLayout = TextLayerWorkflow.Layout(createdBoxMetadata with { Content = crlfContent }, 72, created.Width);
        if (crlfLayout.Caret(3).LineIndex != 1 || crlfLayout.Caret(crlfContent.Length).CharacterIndex != crlfContent.Length ||
            crlfLayout.Selection(0, crlfContent.Length).Count != 2)
            throw new Exception("Text layout did not preserve UTF-16 caret offsets across CRLF and selection lines.");
        int combiningIndex = createdBoxMetadata.Content.IndexOf("é", StringComparison.Ordinal);
        if (combiningIndex < 0 || boxLayout.Caret(combiningIndex + 1).CharacterIndex == combiningIndex + 1)
            throw new Exception("Text layout split a combining grapheme into an editable caret position.");
        if (!created.Undo() || created.Layers.Count != 2 || !created.Redo() || created.Layers.Count != 3)
            throw new Exception("Creating text layers did not participate in undo and redo.");
        string createdPath = Path.Combine(output, "TextCreated.comp");
        ImageProjectWorkflow.Save(created, createdPath);
        var reopenedCreated = ProjectStore.Open(createdPath);
        if (reopenedCreated.TextLayers.Count != 2 ||
            reopenedCreated.TextLayers[0].Content != "Created Windows text" ||
            reopenedCreated.TextLayers[1].Content != "中文 English 🙂 é second line" ||
            reopenedCreated.TextLayers[1].Layout != "box" ||
            !HasInk(ImageCodec.Load(Path.Combine(createdPath, "images", reopenedCreated.TextLayers[0].ImageFile))) ||
            !HasInk(ImageCodec.Load(Path.Combine(createdPath, "images", reopenedCreated.TextLayers[1].ImageFile))))
            throw new Exception("Created text layer was not preserved through save and reopen.");
        Console.WriteLine("PASS: v8 text metadata is exposed; text layers can be created, edited, saved, and reopened; missing fonts preserve cache and request an explicit font choice");
    }

    private static bool HasInk(TileRaster raster)
    {
        for (int row = 0; row * TileRaster.TileSize < raster.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < raster.Width; column++)
            if (raster.ReadTileCopy(column, row).Where((value, index) => index % 4 == 3).Any(value => value != 0)) return true;
        return false;
    }

    private static bool SameRaster(TileRaster expected, TileRaster actual)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height) return false;
        for (int row = 0; row * TileRaster.TileSize < expected.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < expected.Width; column++)
            if (!expected.ReadTileCopy(column, row).SequenceEqual(actual.ReadTileCopy(column, row))) return false;
        return true;
    }

    private static (double X, double Y) ForwardTransform(LayerTransformInfo transform, double x, double y)
    {
        double localX = x - transform.Width / 2;
        double localY = y - transform.Height / 2;
        if (transform.FlipX) localX = -localX;
        if (transform.FlipY) localY = -localY;
        double radians = transform.Rotation * Math.PI / 180;
        double rotatedX = localX * Math.Cos(radians) - localY * Math.Sin(radians);
        double rotatedY = localX * Math.Sin(radians) + localY * Math.Cos(radians);
        return (transform.X + transform.Width / 2 + rotatedX, transform.Y + transform.Height / 2 + rotatedY);
    }
}
