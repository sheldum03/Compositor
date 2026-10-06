using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.App;
using Compositor.Core;
using Compositor.Imaging;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length is not (2 or 3)) throw new ArgumentException("Usage: <image fixtures> <new output directory> [native library]");
        string fixture = Path.GetFullPath(Path.Combine(args[0], "alpha-tiles.png"));
        string output = Path.GetFullPath(args[1]);
        if (Path.Exists(output)) throw new IOException("Output must not exist.");
        Directory.CreateDirectory(output);
        if (args.Length == 3)
        {
            nint library = NativeLibrary.Load(Path.GetFullPath(args[2]));
            NativeLibrary.SetDllImportResolver(typeof(NativeSelections).Assembly,
                (name, _, _) => name == "compositor_native" ? library : 0);
        }
        AppBuilder.Configure<CompositorApplication>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

        var workspace = new EditorWorkspace();
        string source = Path.Combine(output, "Source.comp");
        workspace.Import(fixture, source);
        var original = workspace.Session!;
        Reject(() => workspace.Open(Path.Combine(output, "Missing.comp")));
        Require(ReferenceEquals(workspace.Session, original), "Failed open replaced the current document.");
        // Make a second layer using the real v8 schema; file picker input is excluded from headless checks.
        string manifestPath = Path.Combine(source, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        var layer = manifest["layers"]![0]!.DeepClone();
        string id = Guid.NewGuid().ToString("D"), image = id.ToUpperInvariant() + ".png";
        File.Copy(Path.Combine(source, "images", layer["imageFile"]!.GetValue<string>()), Path.Combine(source, "images", image));
        layer["id"] = id; layer["imageFile"] = image; layer["name"] = "Top";
        manifest["layers"]!.AsArray().Add(layer);
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        workspace.Open(source);
        CheckPreview(workspace.Preview!);
        var window = new MainWindow(workspace);
        window.Show(); Dispatcher.UIThread.RunJobs();
        Control<ListBox>(window, "Layers").SelectedItem = workspace.Session!.Layers.Single(layer => layer.Id == Guid.Parse(id));
        Control<TextBox>(window, "LayerName").Text = "中文 Overlay";
        Click(window, "Rename");
        Require(workspace.Session!.Layers[^1].Name == "中文 Overlay" && workspace.IsDirty, "Rename button did not commit.");
        Require(window.Title!.StartsWith("●"), "Window title did not reflect unsaved changes.");
        Click(window, "Undo");
        Require(workspace.Session.Layers[^1].Name == "Top" && !workspace.IsDirty, "Undo did not return to the saved state.");
        Click(window, "Redo");
        Click(window, "Visibility");
        Require(!workspace.Session.Layers[^1].IsVisible, "Visibility button did not hide selected layer.");
        Click(window, "MoveDown");
        Require(workspace.Session.Layers[0].Id == Guid.Parse(id), "Layer panel did not move selected layer toward the bottom.");
        Click(window, "MoveUp");
        Require(workspace.Session.Layers[^1].Id == Guid.Parse(id), "Layer panel did not move selected layer toward the top.");
        Guid clippingSourceId = workspace.Session.Layers[0].Id;
        Click(window, "SetClippingMask");
        Require(workspace.Session.Layers[^1].MaskSourceId == clippingSourceId,
            "Clipping relationship button did not use the lower layer as the source.");
        Click(window, "ReleaseClippingMask");
        Require(workspace.Session.Layers[^1].MaskSourceId is null,
            "Release clipping button did not clear the relationship.");
        string png = Path.Combine(output, "export.png"), jpeg = Path.Combine(output, "export.jpg");
        workspace.Export(png, false); workspace.Export(jpeg, true);
        Require(workspace.IsDirty, "Export incorrectly cleared unsaved changes.");
        Reject(() => workspace.SaveAs(source));
        Require(workspace.IsDirty, "Rejected Save As changed the save point.");
        CheckEqual(workspace.Preview!, ImageCodec.Load(png));
        using (var screenshot = new RenderTargetBitmap(new PixelSize(1120, 760), new Vector(96, 96)))
        {
            screenshot.Render(window);
            screenshot.Save(Path.Combine(output, "window.png"));
        }

        window.Close(); Dispatcher.UIThread.RunJobs();
        var cancelDialog = window.OwnedWindows.Single();
        DialogClick(cancelDialog, "取消"); Pump(window);
        Require(window.IsVisible && workspace.IsDirty, "Cancel close lost the dirty document.");
        string assetPath = Path.Combine(source, "images", image);
        byte[] assetBytes = File.ReadAllBytes(assetPath);
        File.WriteAllBytes(assetPath, [.. assetBytes, 0]);
        window.Close(); Dispatcher.UIThread.RunJobs();
        DialogClick(window.OwnedWindows.Single(), "保存"); Pump(window);
        Require(window.IsVisible && workspace.IsDirty, "Failed save on close discarded the current document.");
        File.WriteAllBytes(assetPath, assetBytes);
        window.Close(); Dispatcher.UIThread.RunJobs();
        DialogClick(window.OwnedWindows.Single(), "保存"); Pump(window);
        Require(!window.IsVisible && !workspace.IsDirty, "Save on close did not save and close.");
        var reopened = ImageProjectWorkflow.OpenEditable(source);
        Require(reopened.Layers[^1].Name == "中文 Overlay" && !reopened.Layers[^1].IsVisible, "Close-save lost layer state.");
        CheckEqual(workspace.Preview!, ImageProjectWorkflow.RenderFlatNormal(reopened));

        string textProject = Path.Combine(output, "TextCreationWindow.comp");
        var textWorkspace = new EditorWorkspace();
        textWorkspace.Import(fixture, textProject);
        int textLayerCount = textWorkspace.Session!.Layers.Count;
        var textWindow = new MainWindow(textWorkspace);
        textWindow.Show(); Dispatcher.UIThread.RunJobs();
        Click(textWindow, "AddTextLayer");
        FlatLayerInfo createdText = textWorkspace.Session.Layers.Single(layer => layer.IsText);
        Require(textWorkspace.Session.Layers.Count == textLayerCount + 1 && textWorkspace.Session.ActiveLayerId == createdText.Id &&
            textWorkspace.Session.TextLayers.Single().Content == "文字" &&
            textWorkspace.Session.GetLayerRaster(createdText.Id).StoredBytes > 0 &&
            Control<Button>(textWindow, "ApplyText").IsEffectivelyEnabled,
            "Add text button did not create a rendered, editable text layer.");
        Control<TextBox>(textWindow, "TextContent").Text = "Headless text";
        Control<ComboBox>(textWindow, "TextColor").SelectedIndex = 2;
        Control<NumericUpDown>(textWindow, "TextLineSpacing").Value = 4;
        Control<NumericUpDown>(textWindow, "TextTracking").Value = 1.5m;
        Click(textWindow, "ApplyText");
        TextLayerMetadata editedText = textWorkspace.Session.TextLayers.Single();
        Require(editedText.Content == "Headless text" && editedText.Red == 0.1 && editedText.Green == 0.3 &&
            editedText.Blue == 0.9 && editedText.LineSpacingPoints == 4 && editedText.TrackingPoints == 1.5 &&
            textWorkspace.IsDirty,
            "Created text layer did not accept the formal text style editor update.");
        Click(textWindow, "AddBoxTextLayer");
        TextLayerMetadata createdBoxText = textWorkspace.Session.TextLayers.Single(item => item.Id == textWorkspace.Session.ActiveLayerId);
        Require(createdBoxText.Layout == "box" && createdBoxText.BoxWidth == Math.Min(360, textWorkspace.Session.Width) &&
            textWorkspace.Session.TextLayers.Count == 2,
            "Add box-text button did not create a bounded box layout.");
        Control<NumericUpDown>(textWindow, "TextBoxWidth").Value = 120;
        Click(textWindow, "ApplyText");
        Require(textWorkspace.Session.TextLayers.Single(item => item.Id == createdBoxText.Id).BoxWidth == 120,
            "Box text editor did not commit the bounded layout width.");
        textWorkspace.Save(); textWindow.Close(); Dispatcher.UIThread.RunJobs();
        var reopenedText = ImageProjectWorkflow.OpenEditable(textProject);
        Require(reopenedText.TextLayers.Count == 2 && reopenedText.TextLayers.Any(item => item.Content == "Headless text") &&
            reopenedText.TextLayers.Any(item => item.Layout == "box"),
            "Created text layer did not survive save and reopen.");

        string mergeProject = Path.Combine(output, "MergeLayer.comp");
        var mergeWorkspace = new EditorWorkspace();
        mergeWorkspace.Import(fixture, mergeProject);
        Guid mergeTopId = mergeWorkspace.Session!.AddBlankLayer("Top", 1);
        Guid mergeLowerId = mergeWorkspace.Session.Layers[0].Id;
        TileRaster blankTop = mergeWorkspace.Session.GetLayerRaster(mergeTopId);
        var topSize = blankTop.TileDimensions(0, 0);
        byte[] topTile = blankTop.ReadTileCopy(0, 0);
        int topOffset = (10 * topSize.Width + 10) * 4;
        topTile[topOffset] = 128; topTile[topOffset + 3] = 128;
        mergeWorkspace.Edit(session =>
        {
            session.SetLayerOpacity(mergeLowerId, 0.6);
            session.SetLayerOpacity(mergeTopId, 0.7);
            session.ReplaceLayerRaster(mergeTopId, blankTop.ReplaceTile(0, 0, topTile));
        });
        mergeWorkspace.Save();
        FlatLayerInfo mergeLower = mergeWorkspace.Session.Layers.Single(layer => layer.Id == mergeLowerId);
        FlatLayerInfo mergeTop = mergeWorkspace.Session.Layers.Single(layer => layer.Id == mergeTopId);
        TileRaster expectedMerge = LayerCompositor.Composite(new TileRaster(mergeWorkspace.Session.Width, mergeWorkspace.Session.Height),
            mergeWorkspace.Session.GetLayerRaster(mergeLowerId), mergeLower.Opacity, "Normal");
        expectedMerge = LayerCompositor.Composite(expectedMerge, mergeWorkspace.Session.GetLayerRaster(mergeTopId), mergeTop.Opacity, "Normal");
        TileRaster mergeBefore = ImageProjectWorkflow.RenderFlatNormal(mergeWorkspace.Session);
        var mergeWindow = new MainWindow(mergeWorkspace);
        mergeWindow.Show(); Dispatcher.UIThread.RunJobs();
        Control<ListBox>(mergeWindow, "Layers").SelectedItem = mergeWorkspace.Session.Layers.Single(layer => layer.Id == mergeTopId);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(mergeWindow, "MergeLayerDown").IsEffectivelyEnabled,
            "Normal flat layers did not enable the merge-down command.");
        Click(mergeWindow, "MergeLayerDown");
        Require(mergeWorkspace.Session.Layers.Count == 1 && mergeWorkspace.Session.ActiveLayerId == mergeLowerId &&
            mergeWorkspace.IsDirty, "Merge-down did not produce one active merged layer.");
        FlatLayerInfo mergedLayer = mergeWorkspace.Session.Layers.Single();
        Require(mergedLayer.IsVisible && mergedLayer.Opacity == 1 && mergedLayer.BlendMode == "Normal",
            "Merge-down did not normalize the merged layer appearance metadata.");
        CheckEqual(mergeWorkspace.Preview!, expectedMerge);
        Click(mergeWindow, "Undo");
        Require(mergeWorkspace.Session.Layers.Count == 2 && !mergeWorkspace.IsDirty,
            "Undo did not restore both source layers and the saved state after merge-down.");
        CheckEqual(mergeWorkspace.Preview!, mergeBefore);
        Click(mergeWindow, "Redo");
        Require(mergeWorkspace.Session.Layers.Count == 1 && mergeWorkspace.IsDirty,
            "Redo did not restore the merged layer.");
        CheckEqual(mergeWorkspace.Preview!, expectedMerge);
        mergeWorkspace.Save();
        var reopenedMerge = ImageProjectWorkflow.OpenEditable(mergeProject);
        Require(reopenedMerge.Layers.Count == 1, "Saved merge-down did not reopen as one layer.");
        CheckEqual(ImageProjectWorkflow.RenderFlatNormal(reopenedMerge), expectedMerge);
        mergeWindow.Close(); Dispatcher.UIThread.RunJobs();

        string appearanceMergeProject = Path.Combine(output, "AppearanceMerge.comp");
        var appearanceMergeWorkspace = new EditorWorkspace();
        appearanceMergeWorkspace.Import(fixture, appearanceMergeProject);
        Guid appearanceLowerId = appearanceMergeWorkspace.Session!.Layers[0].Id;
        Guid appearanceTopId = appearanceMergeWorkspace.Session.AddBlankLayer("Appearance top", 1);
        TileRaster appearanceTopRaster = appearanceMergeWorkspace.Session.GetLayerRaster(appearanceTopId);
        var appearanceSize = appearanceTopRaster.TileDimensions(0, 0);
        byte[] appearanceTile = appearanceTopRaster.ReadTileCopy(0, 0);
        int appearanceOffset = (14 * appearanceSize.Width + 14) * 4;
        appearanceTile[appearanceOffset] = 150; appearanceTile[appearanceOffset + 1] = 70;
        appearanceTile[appearanceOffset + 2] = 40; appearanceTile[appearanceOffset + 3] = 192;
        appearanceMergeWorkspace.Edit(session =>
        {
            session.SetLayerOpacity(appearanceLowerId, 0.65);
            session.SetLayerBlendMode(appearanceLowerId, "Multiply");
            session.SetLayerOpacity(appearanceTopId, 0.55);
            session.SetLayerBlendMode(appearanceTopId, "Screen");
            session.ReplaceLayerRaster(appearanceTopId, appearanceTopRaster.ReplaceTile(0, 0, appearanceTile));
        });
        appearanceMergeWorkspace.Save();
        TileRaster appearanceBefore = ImageProjectWorkflow.RenderFlatNormal(appearanceMergeWorkspace.Session);
        var appearanceWindow = new MainWindow(appearanceMergeWorkspace);
        appearanceWindow.Show(); Dispatcher.UIThread.RunJobs();
        Control<ListBox>(appearanceWindow, "Layers").SelectedItem =
            appearanceMergeWorkspace.Session.Layers.Single(layer => layer.Id == appearanceTopId);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(appearanceWindow, "MergeLayerDown").IsEffectivelyEnabled,
            "Non-Normal flat layers did not enable the restricted appearance merge command.");
        Click(appearanceWindow, "MergeLayerDown");
        Require(appearanceMergeWorkspace.Session.Layers.Count == 1 && appearanceMergeWorkspace.IsDirty,
            "Non-Normal appearance merge did not produce one layer.");
        FlatLayerInfo appearanceMerged = appearanceMergeWorkspace.Session.Layers.Single();
        Require(appearanceMerged.Opacity == 1 && appearanceMerged.BlendMode == "Normal",
            "Non-Normal appearance merge did not normalize output metadata.");
        CheckEqual(appearanceMergeWorkspace.Preview!, appearanceBefore);
        Click(appearanceWindow, "Undo");
        Require(appearanceMergeWorkspace.Session.Layers.Count == 2 && !appearanceMergeWorkspace.IsDirty,
            "Undo did not restore the non-Normal appearance layers.");
        CheckEqual(appearanceMergeWorkspace.Preview!, appearanceBefore);
        Click(appearanceWindow, "Redo");
        appearanceMergeWorkspace.Save();
        var reopenedAppearanceMerge = ImageProjectWorkflow.OpenEditable(appearanceMergeProject);
        Require(reopenedAppearanceMerge.Layers.Count == 1 &&
            reopenedAppearanceMerge.Layers[0].Opacity == 1 && reopenedAppearanceMerge.Layers[0].BlendMode == "Normal",
            "Saved non-Normal appearance merge did not reopen as a normalized layer.");
        CheckEqual(ImageProjectWorkflow.RenderFlatNormal(reopenedAppearanceMerge), appearanceBefore);
        appearanceWindow.Close(); Dispatcher.UIThread.RunJobs();

        string appearanceClippingProject = Path.Combine(output, "AppearanceClippingMerge.comp");
        var appearanceClippingWorkspace = new EditorWorkspace();
        appearanceClippingWorkspace.Import(fixture, appearanceClippingProject);
        Guid appearanceClippingSourceId = appearanceClippingWorkspace.Session!.Layers[0].Id;
        Guid appearanceClippingTargetId = appearanceClippingWorkspace.Session.AddBlankLayer("Appearance clipped", 1);
        TileRaster appearanceClippingRaster = appearanceClippingWorkspace.Session.GetLayerRaster(appearanceClippingTargetId);
        var appearanceClippingSize = appearanceClippingRaster.TileDimensions(0, 0);
        byte[] appearanceClippingTile = appearanceClippingRaster.ReadTileCopy(0, 0);
        int appearanceClippingOffset = (16 * appearanceClippingSize.Width + 16) * 4;
        appearanceClippingTile[appearanceClippingOffset] = 130;
        appearanceClippingTile[appearanceClippingOffset + 1] = 80;
        appearanceClippingTile[appearanceClippingOffset + 2] = 40;
        appearanceClippingTile[appearanceClippingOffset + 3] = 180;
        appearanceClippingWorkspace.Edit(session =>
        {
            session.SetLayerOpacity(appearanceClippingSourceId, 0.72);
            session.SetLayerBlendMode(appearanceClippingSourceId, "Multiply");
            session.SetLayerOpacity(appearanceClippingTargetId, 0.58);
            session.SetLayerBlendMode(appearanceClippingTargetId, "Screen");
            session.ReplaceLayerRaster(appearanceClippingTargetId,
                appearanceClippingRaster.ReplaceTile(0, 0, appearanceClippingTile));
            session.SetLayerMaskSource(appearanceClippingTargetId, appearanceClippingSourceId);
        });
        appearanceClippingWorkspace.Save();
        TileRaster appearanceClippingBefore = ImageProjectWorkflow.RenderFlatNormal(appearanceClippingWorkspace.Session);
        var appearanceClippingWindow = new MainWindow(appearanceClippingWorkspace);
        appearanceClippingWindow.Show(); Dispatcher.UIThread.RunJobs();
        var appearanceClippingList = Control<ListBox>(appearanceClippingWindow, "Layers");
        appearanceClippingList.SelectedItems!.Clear();
        foreach (FlatLayerInfo layerInfo in appearanceClippingList.ItemsView!.Cast<FlatLayerInfo>())
            appearanceClippingList.SelectedItems.Add(layerInfo);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(appearanceClippingWindow, "MergeLayerDown").IsEffectivelyEnabled,
            "Non-Normal clipping stack did not enable the restricted appearance merge command.");
        Click(appearanceClippingWindow, "MergeLayerDown");
        Require(appearanceClippingWorkspace.Session.Layers.Count == 1 && appearanceClippingWorkspace.IsDirty,
            "Non-Normal clipping stack merge did not produce one layer.");
        CheckEqual(appearanceClippingWorkspace.Preview!, appearanceClippingBefore);
        appearanceClippingWorkspace.Save();
        var reopenedAppearanceClipping = ImageProjectWorkflow.OpenEditable(appearanceClippingProject);
        Require(reopenedAppearanceClipping.Layers.Count == 1 &&
            reopenedAppearanceClipping.Layers[0].Opacity == 1 && reopenedAppearanceClipping.Layers[0].BlendMode == "Normal" &&
            reopenedAppearanceClipping.Layers[0].MaskSourceId is null,
            "Saved non-Normal clipping stack merge did not normalize metadata.");
        CheckEqual(ImageProjectWorkflow.RenderFlatNormal(reopenedAppearanceClipping), appearanceClippingBefore);
        appearanceClippingWindow.Close(); Dispatcher.UIThread.RunJobs();

        string transformedMergeProject = Path.Combine(output, "TransformedMerge.comp");
        var transformedMergeWorkspace = new EditorWorkspace();
        transformedMergeWorkspace.Import(fixture, transformedMergeProject);
        Guid transformedLowerId = transformedMergeWorkspace.Session!.Layers[0].Id;
        Guid transformedUpperId = transformedMergeWorkspace.Session.AddBlankLayer("Transformed upper", 1);
        transformedMergeWorkspace.Edit(session =>
        {
            session.SetLayerTransform(transformedLowerId, 12, 9, session.Width - 24, session.Height - 18, 11);
            session.SetLayerTransform(transformedUpperId, 28, 17, session.Width - 56, session.Height - 34, -7);
            session.SetLayerOpacity(transformedLowerId, 0.68);
            session.SetLayerBlendMode(transformedUpperId, "Screen");
            session.SetLayerOpacity(transformedUpperId, 0.57);
        });
        transformedMergeWorkspace.Save();
        TileRaster transformedMergeBefore = ImageProjectWorkflow.RenderFlatNormal(transformedMergeWorkspace.Session);
        var transformedMergeWindow = new MainWindow(transformedMergeWorkspace);
        transformedMergeWindow.Show(); Dispatcher.UIThread.RunJobs();
        var transformedMergeList = Control<ListBox>(transformedMergeWindow, "Layers");
        transformedMergeList.SelectedItems!.Clear();
        foreach (FlatLayerInfo layerInfo in transformedMergeList.ItemsView!.Cast<FlatLayerInfo>())
            transformedMergeList.SelectedItems.Add(layerInfo);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(transformedMergeWindow, "MergeLayerDown").IsEffectivelyEnabled,
            "Transformed flat layers did not enable the merge command.");
        Click(transformedMergeWindow, "MergeLayerDown");
        Require(transformedMergeWorkspace.Session.Layers.Count == 1 && transformedMergeWorkspace.IsDirty,
            "Transformed flat merge did not produce one layer.");
        LayerTransformInfo transformedMergedTransform = transformedMergeWorkspace.Session.GetLayerTransform(transformedLowerId);
        Require(transformedMergedTransform.X == 0 && transformedMergedTransform.Y == 0 &&
            transformedMergedTransform.Width == transformedMergeWorkspace.Session.Width &&
            transformedMergedTransform.Height == transformedMergeWorkspace.Session.Height &&
            transformedMergedTransform.Rotation == 0 && !transformedMergedTransform.FlipX && !transformedMergedTransform.FlipY,
            "Transformed flat merge did not normalize the merged layer transform.");
        CheckEqual(transformedMergeWorkspace.Preview!, transformedMergeBefore);
        Click(transformedMergeWindow, "Undo");
        Require(transformedMergeWorkspace.Session.Layers.Count == 2 && !transformedMergeWorkspace.IsDirty,
            "Undo did not restore transformed flat layers and saved state.");
        CheckEqual(transformedMergeWorkspace.Preview!, transformedMergeBefore);
        Click(transformedMergeWindow, "Redo");
        transformedMergeWorkspace.Save();
        var reopenedTransformedMerge = ImageProjectWorkflow.OpenEditable(transformedMergeProject);
        Require(reopenedTransformedMerge.Layers.Count == 1 &&
            reopenedTransformedMerge.GetLayerTransform(reopenedTransformedMerge.Layers[0].Id).Rotation == 0,
            "Saved transformed flat merge did not reopen with normalized transform metadata.");
        CheckEqual(ImageProjectWorkflow.RenderFlatNormal(reopenedTransformedMerge), transformedMergeBefore);
        transformedMergeWindow.Close(); Dispatcher.UIThread.RunJobs();

        string multiMergeProject = Path.Combine(output, "MultiMerge.comp");
        var multiMergeWorkspace = new EditorWorkspace();
        multiMergeWorkspace.Import(fixture, multiMergeProject);
        Guid multiLowerId = multiMergeWorkspace.Session!.Layers[0].Id;
        Guid multiMiddleId = multiMergeWorkspace.Session.AddBlankLayer("Middle", 1);
        Guid multiTopId = multiMergeWorkspace.Session.AddBlankLayer("Top", 2);
        Guid multiTopmostId = multiMergeWorkspace.Session.AddBlankLayer("Topmost", 3);
        TileRaster middleRaster = multiMergeWorkspace.Session.GetLayerRaster(multiMiddleId);
        TileRaster topRaster = multiMergeWorkspace.Session.GetLayerRaster(multiTopId);
        var middleSize = middleRaster.TileDimensions(0, 0);
        byte[] middleTile = middleRaster.ReadTileCopy(0, 0);
        byte[] topTileMulti = topRaster.ReadTileCopy(0, 0);
        int middleOffset = (12 * middleSize.Width + 12) * 4;
        int topOffsetMulti = (12 * middleSize.Width + 12) * 4;
        middleTile[middleOffset] = 40; middleTile[middleOffset + 1] = 160;
        middleTile[middleOffset + 2] = 80; middleTile[middleOffset + 3] = 192;
        topTileMulti[topOffsetMulti] = 140; topTileMulti[topOffsetMulti + 1] = 60;
        topTileMulti[topOffsetMulti + 2] = 40; topTileMulti[topOffsetMulti + 3] = 160;
        multiMergeWorkspace.Edit(session =>
        {
            session.SetLayerOpacity(multiLowerId, 0.5);
            session.SetLayerOpacity(multiMiddleId, 0.75);
            session.SetLayerOpacity(multiTopId, 0.6);
            session.SetLayerOpacity(multiTopmostId, 0.4);
            session.ReplaceLayerRaster(multiMiddleId, middleRaster.ReplaceTile(0, 0, middleTile));
            session.ReplaceLayerRaster(multiTopId, topRaster.ReplaceTile(0, 0, topTileMulti));
        });
        multiMergeWorkspace.Save();
        FlatLayerInfo[] multiLayers = multiMergeWorkspace.Session.Layers.ToArray();
        TileRaster expectedMultiMerge = new TileRaster(multiMergeWorkspace.Session.Width, multiMergeWorkspace.Session.Height);
        foreach (FlatLayerInfo layerInfo in multiLayers)
            expectedMultiMerge = LayerCompositor.Composite(expectedMultiMerge,
                multiMergeWorkspace.Session.GetLayerRaster(layerInfo.Id), layerInfo.Opacity, "Normal");
        TileRaster multiMergeBefore = ImageProjectWorkflow.RenderFlatNormal(multiMergeWorkspace.Session);
        var multiMergeWindow = new MainWindow(multiMergeWorkspace);
        multiMergeWindow.Show(); Dispatcher.UIThread.RunJobs();
        var multiLayerList = Control<ListBox>(multiMergeWindow, "Layers");
        multiLayerList.SelectedItems!.Clear();
        foreach (FlatLayerInfo layerInfo in multiLayerList.ItemsView!.Cast<FlatLayerInfo>()
            .Where(layerInfo => new[] { multiLowerId, multiMiddleId, multiTopmostId }.Contains(layerInfo.Id)))
            multiLayerList.SelectedItems.Add(layerInfo);
        Dispatcher.UIThread.RunJobs();
        Require(!Control<Button>(multiMergeWindow, "MergeLayerDown").IsEffectivelyEnabled,
            "Non-contiguous multi-selection incorrectly enabled the merge command.");
        multiLayerList.SelectedItems.Clear();
        foreach (FlatLayerInfo layerInfo in multiLayerList.ItemsView!.Cast<FlatLayerInfo>())
            multiLayerList.SelectedItems.Add(layerInfo);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(multiMergeWindow, "MergeLayerDown").IsEffectivelyEnabled,
            "Contiguous normal flat multi-selection did not enable the merge command.");
        Click(multiMergeWindow, "MergeLayerDown");
        Require(multiMergeWorkspace.Session.Layers.Count == 1 &&
            multiMergeWorkspace.Session.ActiveLayerId == multiLowerId && multiMergeWorkspace.IsDirty,
            "Multi-layer merge did not produce one active merged layer.");
        FlatLayerInfo multiMergedLayer = multiMergeWorkspace.Session.Layers.Single();
        Require(multiMergedLayer.IsVisible && multiMergedLayer.Opacity == 1 && multiMergedLayer.BlendMode == "Normal",
            "Multi-layer merge did not normalize the merged layer appearance metadata.");
        CheckEqual(multiMergeWorkspace.Preview!, expectedMultiMerge);
        Click(multiMergeWindow, "Undo");
        Require(multiMergeWorkspace.Session.Layers.Count == 4 && !multiMergeWorkspace.IsDirty,
            "Undo did not restore all source layers and the saved state after multi-layer merge.");
        CheckEqual(multiMergeWorkspace.Preview!, multiMergeBefore);
        Click(multiMergeWindow, "Redo");
        Require(multiMergeWorkspace.Session.Layers.Count == 1 && multiMergeWorkspace.IsDirty,
            "Redo did not restore the multi-layer merge.");
        CheckEqual(multiMergeWorkspace.Preview!, expectedMultiMerge);
        multiMergeWorkspace.Save();
        var reopenedMultiMerge = ImageProjectWorkflow.OpenEditable(multiMergeProject);
        Require(reopenedMultiMerge.Layers.Count == 1, "Saved multi-layer merge did not reopen as one layer.");
        CheckEqual(ImageProjectWorkflow.RenderFlatNormal(reopenedMultiMerge), expectedMultiMerge);
        multiMergeWindow.Close(); Dispatcher.UIThread.RunJobs();

        string clippingMergeProject = Path.Combine(output, "ClippingMerge.comp");
        var clippingMergeWorkspace = new EditorWorkspace();
        clippingMergeWorkspace.Import(fixture, clippingMergeProject);
        Guid clipMergeSourceId = clippingMergeWorkspace.Session!.Layers[0].Id;
        Guid clipMergeTargetId = clippingMergeWorkspace.Session.AddBlankLayer("Clipped", 1);
        TileRaster clippingSourceRaster = clippingMergeWorkspace.Session.GetLayerRaster(clipMergeSourceId);
        TileRaster clippingTargetRaster = clippingMergeWorkspace.Session.GetLayerRaster(clipMergeTargetId);
        var clippingSize = clippingSourceRaster.TileDimensions(0, 0);
        byte[] clippingSourceTile = clippingSourceRaster.ReadTileCopy(0, 0);
        byte[] clippingTargetTile = clippingTargetRaster.ReadTileCopy(0, 0);
        int clippingOffset = (18 * clippingSize.Width + 18) * 4;
        clippingSourceTile[clippingOffset] = 90; clippingSourceTile[clippingOffset + 1] = 90;
        clippingSourceTile[clippingOffset + 2] = 90; clippingSourceTile[clippingOffset + 3] = 128;
        clippingTargetTile[clippingOffset] = 140; clippingTargetTile[clippingOffset + 1] = 60;
        clippingTargetTile[clippingOffset + 2] = 40; clippingTargetTile[clippingOffset + 3] = 200;
        clippingMergeWorkspace.Edit(session =>
        {
            session.SetLayerOpacity(clipMergeSourceId, 0.75);
            session.SetLayerOpacity(clipMergeTargetId, 0.8);
            session.ReplaceLayerRaster(clipMergeSourceId, clippingSourceRaster.ReplaceTile(0, 0, clippingSourceTile));
            session.ReplaceLayerRaster(clipMergeTargetId, clippingTargetRaster.ReplaceTile(0, 0, clippingTargetTile));
        });
        clippingMergeWorkspace.Edit(session => session.SetLayerMaskSource(clipMergeTargetId, clipMergeSourceId));
        clippingMergeWorkspace.Save();
        TileRaster expectedClippingMerge = ImageProjectWorkflow.RenderFlatNormal(clippingMergeWorkspace.Session);
        var clippingMergeWindow = new MainWindow(clippingMergeWorkspace);
        clippingMergeWindow.Show(); Dispatcher.UIThread.RunJobs();
        var clippingLayerList = Control<ListBox>(clippingMergeWindow, "Layers");
        clippingLayerList.SelectedItems!.Clear();
        foreach (FlatLayerInfo layerInfo in clippingLayerList.ItemsView!.Cast<FlatLayerInfo>()
            .Where(layerInfo => layerInfo.Id == clipMergeSourceId || layerInfo.Id == clipMergeTargetId))
            clippingLayerList.SelectedItems.Add(layerInfo);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(clippingMergeWindow, "MergeLayerDown").IsEffectivelyEnabled,
            "A contiguous clipping stack did not enable the restricted merge command.");
        Click(clippingMergeWindow, "MergeLayerDown");
        Require(clippingMergeWorkspace.Session.Layers.Count == 1 && clippingMergeWorkspace.IsDirty,
            "Clipping stack merge did not produce one merged layer.");
        FlatLayerInfo mergedClippingLayer = clippingMergeWorkspace.Session.Layers.Single();
        Require(mergedClippingLayer.MaskSourceId is null && mergedClippingLayer.Opacity == 1 &&
            mergedClippingLayer.BlendMode == "Normal", "Clipping stack merge left invalid relationship metadata.");
        CheckEqual(clippingMergeWorkspace.Preview!, expectedClippingMerge);
        Click(clippingMergeWindow, "Undo");
        Require(clippingMergeWorkspace.Session.Layers.Count == 2 && !clippingMergeWorkspace.IsDirty,
            "Undo did not restore the clipping stack and saved state.");
        CheckEqual(clippingMergeWorkspace.Preview!, expectedClippingMerge);
        Click(clippingMergeWindow, "Redo");
        Require(clippingMergeWorkspace.Session.Layers.Count == 1 && clippingMergeWorkspace.IsDirty,
            "Redo did not restore the clipping stack merge.");
        clippingMergeWorkspace.Save();
        var reopenedClippingMerge = ImageProjectWorkflow.OpenEditable(clippingMergeProject);
        Require(reopenedClippingMerge.Layers.Count == 1 && reopenedClippingMerge.Layers[0].MaskSourceId is null,
            "Saved clipping stack merge did not reopen as a flat layer.");
        CheckEqual(ImageProjectWorkflow.RenderFlatNormal(reopenedClippingMerge), expectedClippingMerge);
        clippingMergeWindow.Close(); Dispatcher.UIThread.RunJobs();

        string multiClippingMergeProject = Path.Combine(output, "MultiClippingMerge.comp");
        var multiClippingMergeWorkspace = new EditorWorkspace();
        multiClippingMergeWorkspace.Import(fixture, multiClippingMergeProject);
        Guid multiClippingSourceId = multiClippingMergeWorkspace.Session!.Layers[0].Id;
        Guid multiClippingFirstId = multiClippingMergeWorkspace.Session.AddBlankLayer("Clipped first", 1);
        Guid multiClippingSecondId = multiClippingMergeWorkspace.Session.AddBlankLayer("Clipped second", 2);
        multiClippingMergeWorkspace.Edit(session =>
        {
            session.SetLayerOpacity(multiClippingSourceId, 0.74);
            session.SetLayerBlendMode(multiClippingSourceId, "Multiply");
            session.SetLayerOpacity(multiClippingFirstId, 0.63);
            session.SetLayerBlendMode(multiClippingFirstId, "Screen");
            session.SetLayerOpacity(multiClippingSecondId, 0.51);
            session.SetLayerBlendMode(multiClippingSecondId, "Overlay");
            session.SetLayerMaskSource(multiClippingFirstId, multiClippingSourceId);
            session.SetLayerMaskSource(multiClippingSecondId, multiClippingSourceId);
        });
        multiClippingMergeWorkspace.Save();
        TileRaster expectedMultiClippingMerge = ImageProjectWorkflow.RenderFlatNormal(multiClippingMergeWorkspace.Session);
        var multiClippingMergeWindow = new MainWindow(multiClippingMergeWorkspace);
        multiClippingMergeWindow.Show(); Dispatcher.UIThread.RunJobs();
        var multiClippingMergeList = Control<ListBox>(multiClippingMergeWindow, "Layers");
        multiClippingMergeList.SelectedItems!.Clear();
        foreach (FlatLayerInfo layerInfo in multiClippingMergeList.ItemsView!.Cast<FlatLayerInfo>())
            multiClippingMergeList.SelectedItems.Add(layerInfo);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(multiClippingMergeWindow, "MergeLayerDown").IsEffectivelyEnabled,
            "A multi-child clipping stack did not enable the merge command.");
        Click(multiClippingMergeWindow, "MergeLayerDown");
        Require(multiClippingMergeWorkspace.Session.Layers.Count == 1 && multiClippingMergeWorkspace.IsDirty,
            "Multi-child clipping stack merge did not produce one layer.");
        FlatLayerInfo multiClippingMergedLayer = multiClippingMergeWorkspace.Session.Layers.Single();
        Require(multiClippingMergedLayer.MaskSourceId is null && multiClippingMergedLayer.Opacity == 1 &&
            multiClippingMergedLayer.BlendMode == "Normal",
            "Multi-child clipping stack merge left invalid relationship or appearance metadata.");
        CheckEqual(multiClippingMergeWorkspace.Preview!, expectedMultiClippingMerge);
        Click(multiClippingMergeWindow, "Undo");
        Require(multiClippingMergeWorkspace.Session.Layers.Count == 3 && !multiClippingMergeWorkspace.IsDirty,
            "Undo did not restore the multi-child clipping stack and saved state.");
        Click(multiClippingMergeWindow, "Redo");
        multiClippingMergeWorkspace.Save();
        var reopenedMultiClippingMerge = ImageProjectWorkflow.OpenEditable(multiClippingMergeProject);
        Require(reopenedMultiClippingMerge.Layers.Count == 1 &&
            reopenedMultiClippingMerge.Layers[0].MaskSourceId is null,
            "Saved multi-child clipping stack merge did not reopen as a flat layer.");
        CheckEqual(ImageProjectWorkflow.RenderFlatNormal(reopenedMultiClippingMerge), expectedMultiClippingMerge);
        multiClippingMergeWindow.Close(); Dispatcher.UIThread.RunJobs();

        string clippingGuardProject = Path.Combine(output, "ClippingMergeGuard.comp");
        var clippingGuardWorkspace = new EditorWorkspace();
        clippingGuardWorkspace.Import(fixture, clippingGuardProject);
        Guid guardSourceId = clippingGuardWorkspace.Session!.Layers[0].Id;
        Guid guardTargetId = clippingGuardWorkspace.Session.AddBlankLayer("Clipped", 1);
        Guid guardExternalId = clippingGuardWorkspace.Session.AddBlankLayer("External", 2);
        clippingGuardWorkspace.Edit(session =>
        {
            session.SetLayerMaskSource(guardTargetId, guardSourceId);
            session.SetLayerMaskSource(guardExternalId, guardSourceId);
        });
        var clippingGuardWindow = new MainWindow(clippingGuardWorkspace);
        clippingGuardWindow.Show(); Dispatcher.UIThread.RunJobs();
        var clippingGuardLayerList = Control<ListBox>(clippingGuardWindow, "Layers");
        clippingGuardLayerList.SelectedItems!.Clear();
        foreach (FlatLayerInfo layerInfo in clippingGuardLayerList.ItemsView!.Cast<FlatLayerInfo>()
            .Where(layerInfo => layerInfo.Id == guardSourceId || layerInfo.Id == guardTargetId))
            clippingGuardLayerList.SelectedItems.Add(layerInfo);
        Dispatcher.UIThread.RunJobs();
        Require(!Control<Button>(clippingGuardWindow, "MergeLayerDown").IsEffectivelyEnabled,
            "The merge command allowed a clipping source with an external target.");
        clippingGuardLayerList.SelectedItems.Clear();
        foreach (FlatLayerInfo layerInfo in clippingGuardLayerList.ItemsView!.Cast<FlatLayerInfo>()
            .Where(layerInfo => layerInfo.Id == guardTargetId || layerInfo.Id == guardExternalId))
            clippingGuardLayerList.SelectedItems.Add(layerInfo);
        Dispatcher.UIThread.RunJobs();
        Require(!Control<Button>(clippingGuardWindow, "MergeLayerDown").IsEffectivelyEnabled,
            "The merge command allowed a clipping target without its source.");
        clippingGuardWindow.Close(); Dispatcher.UIThread.RunJobs();

        string clippingMoveProject = Path.Combine(output, "ClippingMove.comp");
        var clippingMoveWorkspace = new EditorWorkspace();
        clippingMoveWorkspace.Import(fixture, clippingMoveProject);
        Guid moveSourceId = clippingMoveWorkspace.Session!.Layers[0].Id;
        Guid moveTargetId = clippingMoveWorkspace.Session.AddBlankLayer("Clipped", 1);
        Guid moveOutsideId = clippingMoveWorkspace.Session.AddBlankLayer("Outside", 2);
        clippingMoveWorkspace.Edit(session => session.SetLayerMaskSource(moveTargetId, moveSourceId));
        clippingMoveWorkspace.Save();
        TileRaster clippingMoveBefore = ImageProjectWorkflow.RenderFlatNormal(clippingMoveWorkspace.Session);
        var clippingMoveWindow = new MainWindow(clippingMoveWorkspace);
        clippingMoveWindow.Show(); Dispatcher.UIThread.RunJobs();
        var clippingMoveLayerList = Control<ListBox>(clippingMoveWindow, "Layers");
        FlatLayerInfo moveTarget = clippingMoveLayerList.ItemsView!.Cast<FlatLayerInfo>().Single(layer => layer.Id == moveTargetId);
        clippingMoveLayerList.SelectedItems!.Clear();
        clippingMoveLayerList.SelectedItems.Add(moveTarget);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(clippingMoveWindow, "MoveUp").IsEffectivelyEnabled &&
            !Control<Button>(clippingMoveWindow, "MoveDown").IsEffectivelyEnabled,
            "Clipping stack movement buttons did not reflect the valid direction.");
        Click(clippingMoveWindow, "MoveUp");
        Require(clippingMoveWorkspace.Session.Layers.Select(layer => layer.Id).SequenceEqual([moveOutsideId, moveSourceId, moveTargetId]),
            "Moving a clipping target did not move its source and target as one stack.");
        Require(clippingMoveWorkspace.Session.Layers.Single(layer => layer.Id == moveTargetId).MaskSourceId == moveSourceId,
            "Moving a clipping stack changed its source relationship.");
        CheckEqual(clippingMoveWorkspace.Preview!, ImageProjectWorkflow.RenderFlatNormal(clippingMoveWorkspace.Session));
        Click(clippingMoveWindow, "Undo");
        Require(!clippingMoveWorkspace.IsDirty && clippingMoveWorkspace.Session.Layers.Select(layer => layer.Id)
            .SequenceEqual([moveSourceId, moveTargetId, moveOutsideId]),
            "Undo did not restore the saved clipping stack order.");
        CheckEqual(clippingMoveWorkspace.Preview!, clippingMoveBefore);
        Click(clippingMoveWindow, "Redo");
        Require(clippingMoveWorkspace.IsDirty && clippingMoveWorkspace.Session.Layers.Select(layer => layer.Id)
            .SequenceEqual([moveOutsideId, moveSourceId, moveTargetId]),
            "Redo did not restore the clipping stack movement.");
        clippingMoveWorkspace.Save();
        var reopenedClippingMove = ImageProjectWorkflow.OpenEditable(clippingMoveProject);
        Require(reopenedClippingMove.Layers.Select(layer => layer.Id).SequenceEqual([moveOutsideId, moveSourceId, moveTargetId]) &&
            reopenedClippingMove.Layers.Single(layer => layer.Id == moveTargetId).MaskSourceId == moveSourceId,
            "Saved clipping stack movement did not preserve order and relationship.");
        clippingMoveWindow.Close(); Dispatcher.UIThread.RunJobs();

        var secondWorkspace = new EditorWorkspace();
        secondWorkspace.Open(source);
        var tabsWindow = new MainWindow(workspace);
        tabsWindow.Show(); Dispatcher.UIThread.RunJobs();
        tabsWindow.AddProjectTab(secondWorkspace);
        Require(tabsWindow.ProjectCount == 2 && tabsWindow.ActiveProjectIndex == 1 &&
            ReferenceEquals(tabsWindow.Workspace, secondWorkspace),
            "Project tab creation did not activate the new workspace.");
        string shortcutProjectPath = Path.Combine(output, "ShortcutCut.comp");
        var shortcutWorkspace = new EditorWorkspace();
        shortcutWorkspace.Import(fixture, shortcutProjectPath);
        var shortcutWindow = new MainWindow(shortcutWorkspace);
        shortcutWindow.Show(); Dispatcher.UIThread.RunJobs();
        shortcutWorkspace.SelectRectangle(new Rect(0, 0, shortcutWorkspace.Session!.Width / 2, shortcutWorkspace.Session.Height));
        TileRaster shortcutBeforeCut = shortcutWorkspace.Session.GetLayerRaster(shortcutWorkspace.Session.ActiveLayerId!.Value);
        RaiseKey(shortcutWindow, Key.X, KeyModifiers.Control);
        TileRaster shortcutAfterCut = shortcutWorkspace.Session.GetLayerRaster(shortcutWorkspace.Session.ActiveLayerId!.Value);
        Require(shortcutWorkspace.IsDirty && !CheckEqualNoThrow(shortcutBeforeCut, shortcutAfterCut),
            "Ctrl+X did not cut the selected pixels through the production window shortcut.");
        RaiseKey(shortcutWindow, Key.Z, KeyModifiers.Control);
        Require(!shortcutWorkspace.IsDirty && CheckEqualNoThrow(shortcutBeforeCut,
            shortcutWorkspace.Session.GetLayerRaster(shortcutWorkspace.Session.ActiveLayerId!.Value)),
            "Undo after Ctrl+X did not restore the saved pixels.");
        RaiseKey(shortcutWindow, Key.Z, KeyModifiers.Control | KeyModifiers.Shift);
        Require(shortcutWorkspace.IsDirty && !CheckEqualNoThrow(shortcutBeforeCut,
            shortcutWorkspace.Session.GetLayerRaster(shortcutWorkspace.Session.ActiveLayerId!.Value)),
            "Ctrl+Shift+Z did not redo the cut transaction through the production window shortcut.");
        RaiseKey(shortcutWindow, Key.Z, KeyModifiers.Control);
        Require(!shortcutWorkspace.IsDirty && CheckEqualNoThrow(shortcutBeforeCut,
            shortcutWorkspace.Session.GetLayerRaster(shortcutWorkspace.Session.ActiveLayerId!.Value)),
            "Ctrl+Z after Ctrl+Shift+Z did not restore the saved pixels.");
        shortcutWindow.Close(); Dispatcher.UIThread.RunJobs();
        tabsWindow.ActivateProjectTab(0);
        Require(tabsWindow.ActiveProjectIndex == 0 && ReferenceEquals(tabsWindow.Workspace, workspace) &&
            tabsWindow.Workspace.Session!.Layers[^1].Name == "中文 Overlay",
            "Switching project tabs did not restore the first workspace state.");
        workspace.SelectRectangle(new Rect(30, 30, 120, 90));
        tabsWindow.ActivateProjectTab(1);
        tabsWindow.ActivateProjectTab(0);
        Click(tabsWindow, "CopySelection");
        TileRaster secondTabBeforePaste = secondWorkspace.Session!.GetLayerRaster(secondWorkspace.Session.ActiveLayerId!.Value);
        tabsWindow.ActivateProjectTab(1);
        Require(Control<Button>(tabsWindow, "PasteSelection").IsEffectivelyEnabled,
            "Copying in one project did not enable paste in a compatible project tab.");
        Click(tabsWindow, "PasteSelection");
        Require(secondWorkspace.HasFloatingSelection &&
            CheckEqualNoThrow(secondWorkspace.Session.GetLayerRaster(secondWorkspace.Session.ActiveLayerId!.Value), secondTabBeforePaste),
            "Cross-project paste changed target pixels before committing the floating selection.");
        Click(tabsWindow, "CancelFloatingSelection");
        Require(!secondWorkspace.HasFloatingSelection,
            "Cross-project paste did not cancel without changing the target document.");
        tabsWindow.ActivateProjectTab(0);
        tabsWindow.ActivateProjectTab(1);
        string secondTabName = secondWorkspace.Session!.Layers[^1].Name;
        secondWorkspace.Edit(session => session.RenameLayer(session.Layers[^1].Id, "Second tab edit"));
        Click(tabsWindow, "Undo");
        Require(secondWorkspace.Session.Layers[^1].Name == secondTabName && !secondWorkspace.IsDirty,
            "Undo after switching tabs did not use the active project's history.");
        tabsWindow.ActivateProjectTab(0);
        Require(workspace.Session!.Layers[^1].Name == "中文 Overlay" && !workspace.IsDirty,
            "Undo history from the second project leaked into the first project.");
        tabsWindow.ActivateProjectTab(1);
        Click(tabsWindow, "CloseProject");
        Require(tabsWindow.ProjectCount == 1 && ReferenceEquals(tabsWindow.Workspace, workspace),
            "Closing a project tab did not preserve the remaining workspace.");
        tabsWindow.Close(); Dispatcher.UIThread.RunJobs();

        string crossLayerSourcePath = Path.Combine(output, "CrossLayerSource.comp");
        string crossLayerTargetPath = Path.Combine(output, "CrossLayerTarget.comp");
        var crossLayerSource = new EditorWorkspace();
        crossLayerSource.Import(fixture, crossLayerSourcePath);
        Guid crossLayerSourceId = crossLayerSource.Session!.ActiveLayerId!.Value;
        crossLayerSource.Edit(session =>
        {
            session.EnsureLayerMask(crossLayerSourceId);
            session.SetLayerMaskEnabled(crossLayerSourceId, false);
            session.SetLayerOpacity(crossLayerSourceId, 0.63);
            session.SetLayerBlendMode(crossLayerSourceId, "Multiply");
            session.MoveLayerTransform(crossLayerSourceId, 5, 7);
            session.ScaleLayerTransform(crossLayerSourceId, 0.75);
            session.RotateLayerTransform(crossLayerSourceId, 17);
            session.FlipLayerTransform(crossLayerSourceId, true);
            session.FlipLayerTransform(crossLayerSourceId, false);
        });
        crossLayerSource.Save();
        TileRaster crossLayerRaster = crossLayerSource.Session.GetLayerRaster(crossLayerSourceId);
        GrayTileRaster crossLayerMask = crossLayerSource.Session.GetLayerMask(crossLayerSourceId)!;
        LayerTransformInfo crossLayerTransform = crossLayerSource.Session.GetLayerTransform(crossLayerSourceId);
        var crossLayerTarget = new EditorWorkspace();
        crossLayerTarget.Import(fixture, crossLayerTargetPath);
        int crossLayerTargetCount = crossLayerTarget.Session!.Layers.Count;
        Require(crossLayerSource.CanCopyLayerTo(crossLayerTarget, crossLayerSourceId),
            "Matching flat projects did not enable cross-project layer copy.");
        crossLayerSource.CopyLayerTo(crossLayerTarget, crossLayerSourceId);
        FlatLayerInfo copiedCrossLayer = crossLayerTarget.Session.Layers[^1];
        Require(crossLayerTarget.Session.Layers.Count == crossLayerTargetCount + 1 &&
            copiedCrossLayer.Name == crossLayerSource.Session.Layers.Single(layer => layer.Id == crossLayerSourceId).Name &&
            copiedCrossLayer.Opacity == 0.63 && copiedCrossLayer.BlendMode == "Multiply" &&
            copiedCrossLayer.HasMask && !copiedCrossLayer.MaskEnabled &&
            crossLayerTarget.Session.GetLayerTransform(copiedCrossLayer.Id) == crossLayerTransform &&
            !crossLayerSource.IsDirty && crossLayerTarget.IsDirty,
            "Cross-project layer copy did not preserve metadata or source history state.");
        Require(CheckEqualNoThrow(crossLayerRaster, crossLayerTarget.Session.GetLayerRaster(copiedCrossLayer.Id)) &&
            CheckEqualNoThrow(crossLayerMask, crossLayerTarget.Session.GetLayerMask(copiedCrossLayer.Id)!),
            "Cross-project layer copy did not preserve raster or mask pixels.");
        Require(crossLayerTarget.Undo() && crossLayerTarget.Session.Layers.Count == crossLayerTargetCount &&
            !crossLayerTarget.IsDirty && crossLayerTarget.Redo() && crossLayerTarget.Session.Layers.Count == crossLayerTargetCount + 1,
            "Undo/redo did not isolate the cross-project layer copy in the target history.");
        crossLayerTarget.Save();
        var reopenedCrossLayer = ImageProjectWorkflow.OpenEditable(crossLayerTargetPath);
        FlatLayerInfo reopenedCopiedCrossLayer = reopenedCrossLayer.Layers[^1];
        Require(reopenedCopiedCrossLayer.Opacity == 0.63 && reopenedCopiedCrossLayer.BlendMode == "Multiply" &&
            reopenedCopiedCrossLayer.HasMask && !reopenedCopiedCrossLayer.MaskEnabled &&
            reopenedCrossLayer.GetLayerTransform(reopenedCopiedCrossLayer.Id) == crossLayerTransform &&
            CheckEqualNoThrow(crossLayerRaster, reopenedCrossLayer.GetLayerRaster(reopenedCopiedCrossLayer.Id)) &&
            CheckEqualNoThrow(crossLayerMask, reopenedCrossLayer.GetLayerMask(reopenedCopiedCrossLayer.Id)!),
            "Saved cross-project layer copy did not survive reopen.");
        var mismatchedCrossLayerTarget = new EditorWorkspace();
        mismatchedCrossLayerTarget.New(64, 64, 72);
        Require(!crossLayerSource.CanCopyLayerTo(mismatchedCrossLayerTarget, crossLayerSourceId),
            "Cross-project layer copy incorrectly allowed mismatched canvas dimensions.");
        var crossGroupSource = new EditorWorkspace();
        crossGroupSource.Import(fixture, Path.Combine(output, "CrossGroupSource.comp"));
        Guid crossGroupChildId = crossGroupSource.Session!.Layers[0].Id;
        Guid crossGroupSecondChildId = crossGroupSource.Session.AddBlankLayer("Grouped second", 1);
        crossGroupSource.Edit(session =>
        {
            session.EnsureLayerMask(crossGroupChildId);
            session.SetLayerMaskSource(crossGroupSecondChildId, crossGroupChildId);
        });
        Guid crossGroupId = crossGroupSource.Session.GroupLayers([crossGroupChildId, crossGroupSecondChildId], "Copied group");
        crossGroupSource.Edit(session =>
        {
            session.SetGroupTransform(crossGroupId, 8, 11, session.Width - 16, session.Height - 22, 13);
            session.EnsureLayerMask(crossGroupId);
            session.SetLayerMaskEnabled(crossGroupId, false);
        });
        crossGroupSource.Save();
        var crossGroupTarget = new EditorWorkspace();
        string crossGroupTargetPath = Path.Combine(output, "CrossGroupTarget.comp");
        crossGroupTarget.Import(fixture, crossGroupTargetPath);
        int crossGroupTargetCount = crossGroupTarget.Session!.Layers.Count;
        Require(crossGroupSource.CanCopyLayerTo(crossGroupTarget, crossGroupId),
            "Grouped source did not enable complete group copy to a flat target.");
        crossGroupSource.CopyLayerTo(crossGroupTarget, crossGroupId);
        Require(crossGroupTarget.Session.Layers.Count == crossGroupTargetCount + 3,
            "Cross-project group copy did not add the group subtree.");
        FlatLayerInfo copiedGroup = crossGroupTarget.Session.Layers.Single(layer => layer.IsGroup);
        FlatLayerInfo[] copiedGroupChildren = crossGroupTarget.Session.Layers.Where(layer => layer.ParentId == copiedGroup.Id).ToArray();
        Require(copiedGroupChildren.Length == 2 &&
            crossGroupTarget.Session.GetLayerTransform(copiedGroup.Id) == crossGroupSource.Session.GetLayerTransform(crossGroupId) &&
            copiedGroup.HasMask && !copiedGroup.MaskEnabled &&
            copiedGroupChildren[1].MaskSourceId == copiedGroupChildren[0].Id &&
            CheckEqualNoThrow(crossGroupSource.Session.GetLayerMask(crossGroupId)!, crossGroupTarget.Session.GetLayerMask(copiedGroup.Id)!),
            "Cross-project group copy did not preserve group transform, clipping or mask metadata.");
        foreach (FlatLayerInfo sourceChild in crossGroupSource.Session.Layers.Where(layer => layer.ParentId == crossGroupId))
        {
            FlatLayerInfo targetChild = copiedGroupChildren.Single(layer => layer.Name == sourceChild.Name);
            Require(CheckEqualNoThrow(crossGroupSource.Session.GetLayerRaster(sourceChild.Id),
                    crossGroupTarget.Session.GetLayerRaster(targetChild.Id)),
                "Cross-project group copy did not preserve child pixels.");
        }
        Require(crossGroupTarget.Session.ActiveLayerId == copiedGroup.Id,
            "Cross-project group copy did not activate the copied group.");
        crossGroupTarget.Save();
        var reopenedCrossGroup = ImageProjectWorkflow.OpenEditable(crossGroupTargetPath);
        FlatLayerInfo reopenedGroup = reopenedCrossGroup.Layers.Single(layer => layer.IsGroup);
        Require(reopenedCrossGroup.Layers.Count == crossGroupTargetCount + 3 &&
            reopenedCrossGroup.Layers.Count(layer => layer.ParentId == reopenedGroup.Id) == 2 &&
            reopenedGroup.HasMask && !reopenedGroup.MaskEnabled,
            "Saved cross-project group copy did not reopen with its subtree and mask.");
        var nestedGroupTarget = new EditorWorkspace();
        string nestedGroupTargetPath = Path.Combine(output, "NestedGroupTarget.comp");
        nestedGroupTarget.Import(fixture, nestedGroupTargetPath);
        Guid nestedTargetLeaf = nestedGroupTarget.Session!.ActiveLayerId!.Value;
        Guid nestedTargetGroup = Guid.Empty;
        nestedGroupTarget.Edit(session => nestedTargetGroup = session.GroupLayer(nestedTargetLeaf, "Nested target"));
        nestedGroupTarget.Session.SelectLayer(nestedTargetLeaf);
        int nestedGroupTargetCount = nestedGroupTarget.Session.Layers.Count;
        Require(crossGroupSource.CanCopyLayerTo(nestedGroupTarget, crossGroupId),
            "A root group was not enabled for copy into a nested target group.");
        crossGroupSource.CopyLayerTo(nestedGroupTarget, crossGroupId);
        FlatLayerInfo nestedCopiedGroup = nestedGroupTarget.Session.Layers.Single(layer =>
            layer.IsGroup && layer.Id != nestedTargetGroup);
        Require(nestedCopiedGroup.ParentId == nestedTargetGroup &&
            nestedGroupTarget.Session.Layers.Count == nestedGroupTargetCount + 3 &&
            nestedGroupTarget.Session.Layers.Count(layer => layer.ParentId == nestedCopiedGroup.Id) == 2,
            "Copying a group into a nested target group did not preserve the target parent or subtree.");
        nestedGroupTarget.Save();
        var reopenedNestedGroupTarget = ImageProjectWorkflow.OpenEditable(nestedGroupTargetPath);
        FlatLayerInfo reopenedNestedCopiedGroup = reopenedNestedGroupTarget.Layers.Single(layer =>
            layer.IsGroup && layer.Id != nestedTargetGroup);
        Require(reopenedNestedCopiedGroup.ParentId == nestedTargetGroup &&
            reopenedNestedGroupTarget.Layers.Count == nestedGroupTargetCount + 3 &&
            reopenedNestedGroupTarget.Layers.Count(layer => layer.ParentId == reopenedNestedCopiedGroup.Id) == 2,
            "Saved nested target group copy did not reopen with its parent and subtree.");
        crossGroupSource.Edit(session => session.SetLayerMaskEnabled(crossGroupId, true));
        TileRaster enabledGroupMaskPreview = ImageProjectWorkflow.RenderFlatNormal(crossGroupSource.Session);
        var enabledMaskTarget = new EditorWorkspace();
        string enabledMaskTargetPath = Path.Combine(output, "EnabledGroupMaskTarget.comp");
        enabledMaskTarget.New(crossGroupSource.Session.Width, crossGroupSource.Session.Height, 72);
        Guid enabledMaskLeaf = enabledMaskTarget.Session!.ActiveLayerId!.Value;
        Guid enabledMaskTargetGroup = Guid.Empty;
        enabledMaskTarget.Edit(session => enabledMaskTargetGroup = session.GroupLayer(enabledMaskLeaf, "Enabled mask target"));
        enabledMaskTarget.SaveAs(enabledMaskTargetPath);
        enabledMaskTarget.Session.SelectLayer(enabledMaskLeaf);
        Require(!crossGroupSource.CanCopyLayerTo(enabledMaskTarget, crossGroupChildId),
            "A layer inside an enabled group mask was incorrectly enabled for isolated copy.");
        Require(crossGroupSource.CanCopyLayerTo(enabledMaskTarget, crossGroupId),
            "A group with an enabled group mask was not enabled for complete subtree copy.");
        crossGroupSource.CopyLayerTo(enabledMaskTarget, crossGroupId);
        Require(enabledMaskTarget.Session.Layers.Single(layer => layer.IsGroup && layer.Id != enabledMaskTargetGroup).HasMask &&
            enabledMaskTarget.Session.Layers.Single(layer => layer.IsGroup && layer.Id != enabledMaskTargetGroup).MaskEnabled &&
            CheckEqualNoThrow(enabledGroupMaskPreview, ImageProjectWorkflow.RenderFlatNormal(enabledMaskTarget.Session)),
            "Copying an enabled group mask did not preserve the rendered subtree.");
        enabledMaskTarget.Save();
        var reopenedEnabledMaskTarget = ImageProjectWorkflow.OpenEditable(enabledMaskTargetPath);
        Require(CheckEqualNoThrow(enabledGroupMaskPreview, ImageProjectWorkflow.RenderFlatNormal(reopenedEnabledMaskTarget)),
            "Saved enabled group mask copy did not preserve its rendered subtree.");
        crossGroupSource.Edit(session => session.SetLayerMaskEnabled(crossGroupId, false));
        crossGroupTarget.Session.SelectLayer(copiedGroupChildren[0].Id);
        int groupedTargetBeforeFlatCopy = crossGroupTarget.Session.Layers.Count;
        Require(crossLayerSource.CanCopyLayerTo(crossGroupTarget, crossLayerSourceId),
            "A grouped target did not enable a sibling layer copy.");
        crossLayerSource.CopyLayerTo(crossGroupTarget, crossLayerSourceId);
        FlatLayerInfo copiedIntoGroup = crossGroupTarget.Session.Layers.Single(layer =>
            layer.ParentId == copiedGroup.Id && !copiedGroupChildren.Any(child => child.Id == layer.Id));
        Require(crossGroupTarget.Session.Layers.Count == groupedTargetBeforeFlatCopy + 1 &&
            copiedIntoGroup.Name == crossLayerSource.Session.Layers.Single(layer => layer.Id == crossLayerSourceId).Name,
            "A grouped target did not insert a copied layer as a group sibling.");
        crossGroupTarget.Session.SelectLayer(copiedGroupChildren[0].Id);
        Require(!crossGroupSource.CanCopyLayerTo(crossGroupTarget, crossGroupChildId),
            "A raster layer inside a transformed group was incorrectly enabled for isolated copy.");
        crossGroupSource.Edit(session => session.SetGroupTransform(
            crossGroupId, 0, 0, session.Width, session.Height, 0));
        TileRaster groupedLeafRaster = crossGroupSource.Session.GetLayerRaster(crossGroupChildId);
        GrayTileRaster groupedLeafMask = crossGroupSource.Session.GetLayerMask(crossGroupChildId)!;
        crossGroupTarget.Session.SelectLayer(copiedGroupChildren[0].Id);
        int groupedStackTargetCount = crossGroupTarget.Session.Layers.Count;
        Guid[] groupedStackExistingIds = crossGroupTarget.Session.Layers.Select(layer => layer.Id).ToArray();
        Require(crossGroupSource.CanCopyLayerTo(crossGroupTarget, crossGroupSecondChildId),
            "A complete clipping stack inside an identity group did not enable cross-project copy.");
        crossGroupSource.CopyLayerTo(crossGroupTarget, crossGroupSecondChildId);
        FlatLayerInfo copiedGroupedStackSource = crossGroupTarget.Session.Layers.Single(layer =>
            layer.ParentId == copiedGroup.Id && layer.Name == copiedGroupChildren[0].Name &&
            !groupedStackExistingIds.Contains(layer.Id));
        FlatLayerInfo copiedGroupedStackTarget = crossGroupTarget.Session.Layers.Single(layer =>
            layer.ParentId == copiedGroup.Id && layer.Name == copiedGroupChildren[1].Name &&
            !groupedStackExistingIds.Contains(layer.Id));
        Require(copiedGroupedStackTarget.MaskSourceId == copiedGroupedStackSource.Id &&
            CheckEqualNoThrow(crossGroupSource.Session.GetLayerRaster(crossGroupChildId),
                crossGroupTarget.Session.GetLayerRaster(copiedGroupedStackSource.Id)) &&
            CheckEqualNoThrow(crossGroupSource.Session.GetLayerRaster(crossGroupSecondChildId),
                crossGroupTarget.Session.GetLayerRaster(copiedGroupedStackTarget.Id)) &&
            CheckEqualNoThrow(groupedLeafMask,
                crossGroupTarget.Session.GetLayerMask(copiedGroupedStackSource.Id)!) &&
            crossGroupTarget.Session.Layers.Count == groupedStackTargetCount + 2,
            "Cross-project grouped clipping-stack copy did not remap the relationship or preserve pixels and masks.");
        crossGroupTarget.Save();
        var reopenedGroupedTarget = ImageProjectWorkflow.OpenEditable(crossGroupTargetPath);
        Require(reopenedGroupedTarget.Layers.Count == groupedTargetBeforeFlatCopy + 3 &&
            reopenedGroupedTarget.Layers.Any(layer => layer.ParentId == reopenedGroup.Id && layer.Name == copiedIntoGroup.Name),
            "Saved grouped-target layer copy did not reopen inside the target group.");
        FlatLayerInfo reopenedGroupedStackTarget = reopenedGroupedTarget.Layers.Single(layer =>
            layer.ParentId == reopenedGroup.Id && layer.Name == copiedGroupChildren[1].Name &&
            layer.MaskSourceId is not null && layer.Id != copiedGroupChildren[1].Id);
        FlatLayerInfo reopenedGroupedStackSource = reopenedGroupedTarget.Layers.Single(layer =>
            layer.Id == reopenedGroupedStackTarget.MaskSourceId);
        Require(CheckEqualNoThrow(groupedLeafRaster, reopenedGroupedTarget.GetLayerRaster(reopenedGroupedStackSource.Id)) &&
            CheckEqualNoThrow(groupedLeafMask, reopenedGroupedTarget.GetLayerMask(reopenedGroupedStackSource.Id)!),
            "Saved grouped clipping-stack copy did not reopen with its source pixels and mask.");
        var clippedCrossLayerSource = new EditorWorkspace();
        clippedCrossLayerSource.Import(fixture, Path.Combine(output, "ClippedCrossLayerSource.comp"));
        Guid clippedSourceId = clippedCrossLayerSource.Session!.Layers[0].Id;
        Guid clippedTargetId = clippedCrossLayerSource.Session.AddBlankLayer("Clipped target", 1);
        clippedCrossLayerSource.Edit(session => session.SetLayerMaskSource(clippedTargetId, clippedSourceId));
        TileRaster clippedSourceRaster = clippedCrossLayerSource.Session.GetLayerRaster(clippedSourceId);
        TileRaster clippedTargetRaster = clippedCrossLayerSource.Session.GetLayerRaster(clippedTargetId);
        Require(clippedCrossLayerSource.CanCopyLayerTo(crossLayerTarget, clippedSourceId) &&
            clippedCrossLayerSource.CanCopyLayerTo(crossLayerTarget, clippedTargetId),
            "Complete clipping stack did not enable cross-project copy.");
        int crossLayerStackTargetCount = crossLayerTarget.Session.Layers.Count;
        clippedCrossLayerSource.CopyLayerTo(crossLayerTarget, clippedTargetId);
        Require(crossLayerTarget.Session.Layers.Count == crossLayerStackTargetCount + 2,
            "Cross-project clipping stack copy did not add both layers.");
        FlatLayerInfo copiedStackSource = crossLayerTarget.Session.Layers[^2];
        FlatLayerInfo copiedStackTarget = crossLayerTarget.Session.Layers[^1];
        Require(copiedStackTarget.MaskSourceId == copiedStackSource.Id &&
            CheckEqualNoThrow(clippedSourceRaster, crossLayerTarget.Session.GetLayerRaster(copiedStackSource.Id)) &&
            CheckEqualNoThrow(clippedTargetRaster, crossLayerTarget.Session.GetLayerRaster(copiedStackTarget.Id)),
            "Cross-project clipping stack copy did not remap relationship or pixels.");
        crossLayerTarget.Save();
        var reopenedCrossLayerStack = ImageProjectWorkflow.OpenEditable(crossLayerTargetPath);
        Require(reopenedCrossLayerStack.Layers[^1].MaskSourceId == reopenedCrossLayerStack.Layers[^2].Id,
            "Saved cross-project clipping stack did not reopen with remapped relationship.");

        var discontinuousCrossSource = new EditorWorkspace();
        discontinuousCrossSource.Import(fixture, Path.Combine(output, "DiscontinuousCrossLayerSource.comp"));
        Guid discontinuousCrossBaseId = discontinuousCrossSource.Session!.Layers[0].Id;
        discontinuousCrossSource.Session.AddBlankLayer("Unrelated sibling", 1);
        Guid discontinuousCrossTargetId = discontinuousCrossSource.Session.AddBlankLayer("Discontinuous target", 2);
        discontinuousCrossSource.Edit(session => session.SetLayerMaskSource(
            discontinuousCrossTargetId, discontinuousCrossBaseId));
        var discontinuousCrossTarget = new EditorWorkspace();
        string discontinuousCrossTargetPath = Path.Combine(output, "DiscontinuousCrossLayerTarget.comp");
        discontinuousCrossTarget.Import(fixture, discontinuousCrossTargetPath);
        int discontinuousCrossTargetCount = discontinuousCrossTarget.Session!.Layers.Count;
        Require(discontinuousCrossSource.CanCopyLayerTo(discontinuousCrossTarget, discontinuousCrossTargetId),
            "A flat discontinuous clipping stack did not enable cross-project copy.");
        discontinuousCrossSource.CopyLayerTo(discontinuousCrossTarget, discontinuousCrossTargetId);
        FlatLayerInfo copiedDiscontinuousBase = discontinuousCrossTarget.Session.Layers[^2];
        FlatLayerInfo copiedDiscontinuousTarget = discontinuousCrossTarget.Session.Layers[^1];
        Require(discontinuousCrossTarget.Session.Layers.Count == discontinuousCrossTargetCount + 2 &&
            copiedDiscontinuousTarget.MaskSourceId == copiedDiscontinuousBase.Id,
            "Cross-project discontinuous clipping copy did not remap the relationship or layer count.");
        discontinuousCrossTarget.Save();
        var reopenedDiscontinuousCross = ImageProjectWorkflow.OpenEditable(discontinuousCrossTargetPath);
        Require(reopenedDiscontinuousCross.Layers[^1].MaskSourceId == reopenedDiscontinuousCross.Layers[^2].Id,
            "Saved cross-project discontinuous clipping copy did not reopen with its relationship.");

        var groupedDiscontinuousCrossSource = new EditorWorkspace();
        groupedDiscontinuousCrossSource.Import(fixture,
            Path.Combine(output, "GroupedDiscontinuousCrossLayerSource.comp"));
        Guid groupedDiscontinuousBaseId = groupedDiscontinuousCrossSource.Session!.Layers[0].Id;
        groupedDiscontinuousCrossSource.Session.AddBlankLayer("Unrelated sibling", 1);
        Guid groupedDiscontinuousTargetId = groupedDiscontinuousCrossSource.Session
            .AddBlankLayer("Discontinuous grouped target", 2);
        groupedDiscontinuousCrossSource.Edit(session => session.SetLayerMaskSource(
            groupedDiscontinuousTargetId, groupedDiscontinuousBaseId));
        _ = groupedDiscontinuousCrossSource.Session.GroupLayers(
            [groupedDiscontinuousBaseId, groupedDiscontinuousCrossSource.Session.Layers[1].Id,
                groupedDiscontinuousTargetId], "Discontinuous source group");
        var groupedDiscontinuousCrossTarget = new EditorWorkspace();
        string groupedDiscontinuousCrossTargetPath = Path.Combine(output,
            "GroupedDiscontinuousCrossLayerTarget.comp");
        groupedDiscontinuousCrossTarget.Import(fixture, groupedDiscontinuousCrossTargetPath);
        Require(groupedDiscontinuousCrossSource.CanCopyLayerTo(groupedDiscontinuousCrossTarget,
                groupedDiscontinuousTargetId),
            "A grouped discontinuous clipping stack did not enable cross-project copy.");
        groupedDiscontinuousCrossSource.CopyLayerTo(groupedDiscontinuousCrossTarget,
            groupedDiscontinuousTargetId);
        FlatLayerInfo groupedCopiedTarget = groupedDiscontinuousCrossTarget.Session!.Layers[^1];
        FlatLayerInfo groupedCopiedBase = groupedDiscontinuousCrossTarget.Session.Layers[^2];
        Require(groupedCopiedTarget.MaskSourceId == groupedCopiedBase.Id &&
            groupedCopiedTarget.ParentId is null && groupedCopiedBase.ParentId is null,
            "Cross-project grouped discontinuous clipping copy did not flatten the source parent or remap the relationship.");
        groupedDiscontinuousCrossTarget.Save();
        var reopenedGroupedDiscontinuousCross = ImageProjectWorkflow.OpenEditable(
            groupedDiscontinuousCrossTargetPath);
        Require(reopenedGroupedDiscontinuousCross.Layers[^1].MaskSourceId ==
                reopenedGroupedDiscontinuousCross.Layers[^2].Id,
            "Saved grouped discontinuous clipping copy did not reopen with its relationship.");

        var crossLayerUiSource = new EditorWorkspace();
        crossLayerUiSource.Import(fixture, Path.Combine(output, "CrossLayerUiSource.comp"));
        Guid crossLayerUiSourceId = crossLayerUiSource.Session!.Layers[0].Id;
        crossLayerUiSource.Edit(session =>
        {
            session.ScaleLayerTransform(crossLayerUiSourceId, 0.8);
            session.RotateLayerTransform(crossLayerUiSourceId, -13);
            session.FlipLayerTransform(crossLayerUiSourceId, true);
            session.FlipLayerTransform(crossLayerUiSourceId, false);
        });
        LayerTransformInfo crossLayerUiTransform = crossLayerUiSource.Session.GetLayerTransform(crossLayerUiSourceId);
        crossLayerUiSource.Edit(session => session.AddBlankLayer("Second source layer", session.Layers.Count));
        var crossLayerUiTarget = new EditorWorkspace();
        crossLayerUiTarget.Import(fixture, Path.Combine(output, "CrossLayerUiTarget.comp"));
        var crossLayerWindow = new MainWindow(crossLayerUiSource);
        crossLayerWindow.Show(); Dispatcher.UIThread.RunJobs();
        crossLayerWindow.AddProjectTab(crossLayerUiTarget);
        crossLayerWindow.ActivateProjectTab(0);
        var crossLayerList = Control<ListBox>(crossLayerWindow, "Layers");
        crossLayerList.ScrollIntoView(0);
        Dispatcher.UIThread.RunJobs();
        var crossLayerItems = crossLayerList.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Require(crossLayerItems.Length >= 1,
            $"Cross-project drag source did not materialize its layer: visuals={crossLayerItems.Length}, layers={crossLayerUiSource.Session!.Layers.Count}, active={crossLayerWindow.ActiveProjectIndex}.");
        ListBoxItem crossLayerItem = crossLayerItems.Single(item =>
            (item.DataContext as FlatLayerInfo)?.Id == crossLayerUiSourceId);
        Point crossLayerStart = crossLayerItem.TranslatePoint(new Point(20, crossLayerItem.Bounds.Height / 2), crossLayerWindow)!.Value;
        Button crossLayerTab = Control<Button>(crossLayerWindow, "ProjectTab1");
        Point crossLayerEnd = crossLayerTab.TranslatePoint(new Point(crossLayerTab.Bounds.Width / 2, crossLayerTab.Bounds.Height / 2), crossLayerWindow)!.Value;
        crossLayerWindow.MouseDown(crossLayerStart, MouseButton.Left);
        crossLayerWindow.MouseMove(crossLayerEnd);
        crossLayerWindow.MouseUp(crossLayerEnd, MouseButton.Left);
        Pump(crossLayerWindow);
        Require(crossLayerWindow.ActiveProjectIndex == 1 && crossLayerUiTarget.Session!.Layers.Count == 2,
            "Dragging a layer onto another project tab did not copy it into the target project.");
        FlatLayerInfo copiedCrossLayerUi = crossLayerUiTarget.Session!.Layers[^1];
        Require(crossLayerUiTarget.Session.GetLayerTransform(copiedCrossLayerUi.Id) == crossLayerUiTransform,
            "Dragging a transformed flat layer did not preserve its non-destructive transform.");
        Click(crossLayerWindow, "Undo");
        Require(crossLayerUiTarget.Session!.Layers.Count == 1, "Cross-project drag copy did not undo in the target project.");
        Click(crossLayerWindow, "Redo");
        crossLayerUiTarget.Save();
        var reopenedCrossLayerUiTarget = ImageProjectWorkflow.OpenEditable(Path.Combine(output, "CrossLayerUiTarget.comp"));
        Require(reopenedCrossLayerUiTarget.GetLayerTransform(reopenedCrossLayerUiTarget.Layers[^1].Id) == crossLayerUiTransform,
            "Saved transformed layer drag copy did not reopen with its transform.");
        crossLayerWindow.Close(); Dispatcher.UIThread.RunJobs();

        var crossGroupUiSource = new EditorWorkspace();
        crossGroupUiSource.Import(fixture, Path.Combine(output, "CrossGroupUiSource.comp"));
        Guid crossGroupUiChildId = crossGroupUiSource.Session!.Layers[0].Id;
        Guid crossGroupUiSecondChildId = crossGroupUiSource.Session.AddBlankLayer("UI grouped second", 1);
        Guid crossGroupUiId = crossGroupUiSource.Session.GroupLayers([crossGroupUiChildId, crossGroupUiSecondChildId], "UI copied group");
        var crossGroupUiTarget = new EditorWorkspace();
        crossGroupUiTarget.Import(fixture, Path.Combine(output, "CrossGroupUiTarget.comp"));
        var crossGroupWindow = new MainWindow(crossGroupUiSource);
        crossGroupWindow.Show(); Dispatcher.UIThread.RunJobs();
        crossGroupWindow.AddProjectTab(crossGroupUiTarget);
        crossGroupWindow.ActivateProjectTab(0);
        var crossGroupList = Control<ListBox>(crossGroupWindow, "Layers");
        crossGroupList.ScrollIntoView(0);
        Dispatcher.UIThread.RunJobs();
        var crossGroupItem = crossGroupList.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(item => (item.DataContext as FlatLayerInfo)?.Id == crossGroupUiId);
        Point crossGroupStart = crossGroupItem.TranslatePoint(new Point(20, crossGroupItem.Bounds.Height / 2), crossGroupWindow)!.Value;
        Button crossGroupTab = Control<Button>(crossGroupWindow, "ProjectTab1");
        Point crossGroupEnd = crossGroupTab.TranslatePoint(new Point(crossGroupTab.Bounds.Width / 2, crossGroupTab.Bounds.Height / 2), crossGroupWindow)!.Value;
        crossGroupWindow.MouseDown(crossGroupStart, MouseButton.Left);
        crossGroupWindow.MouseMove(crossGroupEnd);
        crossGroupWindow.MouseUp(crossGroupEnd, MouseButton.Left);
        Pump(crossGroupWindow);
        Require(crossGroupWindow.ActiveProjectIndex == 1 &&
            crossGroupUiTarget.Session!.Layers.Count == 4 &&
            crossGroupUiTarget.Session.Layers.Count(layer => layer.IsGroup) == 1,
            "Dragging a group onto another project tab did not copy its subtree.");
        crossGroupWindow.Close(); Dispatcher.UIThread.RunJobs();

        var groupedTargetUi = new EditorWorkspace();
        groupedTargetUi.Import(fixture, Path.Combine(output, "GroupedTargetUi.comp"));
        Guid groupedTargetUiChildId = groupedTargetUi.Session!.Layers[0].Id;
        Guid groupedTargetUiSecondChildId = groupedTargetUi.Session.AddBlankLayer("Target group second", 1);
        Guid groupedTargetUiGroupId = groupedTargetUi.Session.GroupLayers(
            [groupedTargetUiChildId, groupedTargetUiSecondChildId], "Target group");
        groupedTargetUi.Session.SelectLayer(groupedTargetUiChildId);
        var flatTargetUiSource = new EditorWorkspace();
        flatTargetUiSource.Import(fixture, Path.Combine(output, "FlatTargetUiSource.comp"));
        Guid flatTargetUiSourceId = flatTargetUiSource.Session!.Layers[0].Id;
        var groupedTargetWindow = new MainWindow(flatTargetUiSource);
        groupedTargetWindow.Show(); Dispatcher.UIThread.RunJobs();
        groupedTargetWindow.AddProjectTab(groupedTargetUi);
        groupedTargetWindow.ActivateProjectTab(0);
        var flatTargetUiList = Control<ListBox>(groupedTargetWindow, "Layers");
        flatTargetUiList.ScrollIntoView(0);
        Dispatcher.UIThread.RunJobs();
        var flatTargetUiItem = flatTargetUiList.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(item => (item.DataContext as FlatLayerInfo)?.Id == flatTargetUiSourceId);
        Point flatTargetUiStart = flatTargetUiItem.TranslatePoint(new Point(20, flatTargetUiItem.Bounds.Height / 2), groupedTargetWindow)!.Value;
        Button groupedTargetTab = Control<Button>(groupedTargetWindow, "ProjectTab1");
        Point groupedTargetEnd = groupedTargetTab.TranslatePoint(new Point(groupedTargetTab.Bounds.Width / 2, groupedTargetTab.Bounds.Height / 2), groupedTargetWindow)!.Value;
        groupedTargetWindow.MouseDown(flatTargetUiStart, MouseButton.Left);
        groupedTargetWindow.MouseMove(groupedTargetEnd);
        groupedTargetWindow.MouseUp(groupedTargetEnd, MouseButton.Left);
        Pump(groupedTargetWindow);
        Require(groupedTargetWindow.ActiveProjectIndex == 1 &&
            groupedTargetUi.Session!.Layers.Count == 4 &&
            groupedTargetUi.Session.Layers.Count(layer => layer.ParentId == groupedTargetUiGroupId) == 3,
            "Dragging a flat layer into an existing project group did not preserve the target parent.");
        groupedTargetWindow.Close(); Dispatcher.UIThread.RunJobs();

        string clipboardProjectPath = Path.Combine(output, "ClipboardImage.comp");
        var clipboardWorkspace = new EditorWorkspace();
        clipboardWorkspace.Import(fixture, clipboardProjectPath);
        TileRaster clipboardSource = new TileRaster(3, 2).ReplaceTile(0, 0,
            [255, 0, 0, 255, 0, 128, 0, 128, 0, 0, 0, 0,
             0, 0, 255, 255, 255, 255, 255, 255, 0, 0, 0, 64]);
        using (var systemBitmap = RasterBitmap.Create(clipboardSource))
            Require(CheckEqualNoThrow(clipboardSource, RasterBitmap.ToRaster(systemBitmap)),
                "Avalonia bitmap conversion did not preserve premultiplied RGBA pixels.");
        int clipboardLayerCount = clipboardWorkspace.Session!.Layers.Count;
        clipboardWorkspace.PasteBitmapAsLayer(clipboardSource);
        Require(clipboardWorkspace.Session.Layers.Count == clipboardLayerCount + 1 &&
            clipboardWorkspace.Session.Layers[^1].Name == "Clipboard Image",
            "Pasting a system bitmap did not create a new layer.");
        TileRaster clipboardLayer = clipboardWorkspace.Session.GetLayerRaster(clipboardWorkspace.Session.Layers[^1].Id);
        int clipboardX = (clipboardLayer.Width - clipboardSource.Width) / 2;
        int clipboardY = (clipboardLayer.Height - clipboardSource.Height) / 2;
        int clipboardColumn = clipboardX / TileRaster.TileSize, clipboardRow = clipboardY / TileRaster.TileSize;
        var clipboardTileSize = clipboardLayer.TileDimensions(clipboardColumn, clipboardRow);
        byte[] clipboardTile = clipboardLayer.ReadTileCopy(clipboardColumn, clipboardRow);
        int clipboardOffset = ((clipboardY % TileRaster.TileSize) * clipboardTileSize.Width +
            clipboardX % TileRaster.TileSize) * 4;
        Require(clipboardTile[clipboardOffset] == 255 && clipboardTile[clipboardOffset + 3] == 255,
            "Pasted bitmap was not centered on the document.");
        string positionedClipboardPath = Path.Combine(output, "PositionedClipboard.comp");
        var positionedClipboard = new EditorWorkspace();
        positionedClipboard.Import(fixture, positionedClipboardPath);
        positionedClipboard.PasteBitmapAsLayer(clipboardSource, new Point(10, 12));
        TileRaster positionedLayer = positionedClipboard.Session!.GetLayerRaster(positionedClipboard.Session.ActiveLayerId!.Value);
        Require(PixelAt(positionedLayer, 9, 11).SequenceEqual(new byte[] { 255, 0, 0, 255 }) &&
            PixelAt(positionedLayer, 8, 10).SequenceEqual(new byte[4]),
            "Pasting a system bitmap at a document pointer did not preserve the requested position.");
        Require(clipboardWorkspace.Undo() && clipboardWorkspace.Session.Layers.Count == clipboardLayerCount,
            "Undo did not remove the system clipboard layer.");
        Require(clipboardWorkspace.Redo() && clipboardWorkspace.Session.Layers.Count == clipboardLayerCount + 1,
            "Redo did not restore the system clipboard layer.");
        clipboardWorkspace.Save();
        var reopenedClipboard = ImageProjectWorkflow.OpenEditable(clipboardProjectPath);
        Require(reopenedClipboard.Layers.Count == clipboardLayerCount + 1 &&
            CheckEqualNoThrow(clipboardLayer, reopenedClipboard.GetLayerRaster(reopenedClipboard.Layers[^1].Id)),
            "Saved system clipboard layer did not survive reopen.");

        string dragProjectPath = Path.Combine(output, "LayerDrag.comp");
        var dragWorkspace = new EditorWorkspace();
        dragWorkspace.Import(fixture, dragProjectPath);
        dragWorkspace.Edit(session =>
        {
            session.AddBlankLayer("Drag A", session.Layers.Count);
            session.AddBlankLayer("Drag B", session.Layers.Count);
            session.AddBlankLayer("Drag C", session.Layers.Count);
        });
        var dragWindow = new MainWindow(dragWorkspace);
        dragWindow.Show(); Dispatcher.UIThread.RunJobs();
        var dragItems = Control<ListBox>(dragWindow, "Layers").GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Require(dragItems.Length == dragWorkspace.Session!.Layers.Count,
            "Layer list did not materialize all draggable items.");
        FlatLayerInfo dragSource = (FlatLayerInfo)dragItems[0].DataContext!;
        FlatLayerInfo dragTarget = (FlatLayerInfo)dragItems[^1].DataContext!;
        Point dragStart = dragItems[0].TranslatePoint(new Point(20, dragItems[0].Bounds.Height / 2), dragWindow)!.Value;
        Point dragEnd = dragItems[^1].TranslatePoint(new Point(20, dragItems[^1].Bounds.Height / 2), dragWindow)!.Value;
        dragWindow.MouseDown(dragStart, MouseButton.Left);
        dragWindow.MouseMove(new Point(dragStart.X, dragEnd.Y - 4));
        dragWindow.MouseUp(dragEnd, MouseButton.Left);
        Pump(dragWindow);
        Require(dragWorkspace.Session.Layers[0].Id == dragSource.Id &&
            dragWorkspace.Session.Layers[1].Id == dragTarget.Id,
            $"Dragging a layer to the bottom did not reorder the layer list: source={dragSource.Name}, target={dragTarget.Name}, order={string.Join(",", dragWorkspace.Session.Layers.Select(layer => layer.Name))}, dirty={dragWorkspace.IsDirty}.");
        Click(dragWindow, "Undo");
        Require(dragWorkspace.Session.Layers[^1].Id == dragSource.Id,
            "Undo did not restore the layer order after drag and drop.");
        Click(dragWindow, "Redo");
        dragWorkspace.Save();
        dragWindow.Close(); Dispatcher.UIThread.RunJobs();
        var reopenedDrag = ImageProjectWorkflow.OpenEditable(dragProjectPath);
        Require(reopenedDrag.Layers[0].Id == dragSource.Id && reopenedDrag.Layers[1].Id == dragTarget.Id,
            "Saved layer drag order did not survive reopen.");

        var discard = new MainWindow(workspace);
        discard.Show(); Dispatcher.UIThread.RunJobs();
        Control<TextBox>(discard, "LayerName").Text = "Discard me";
        Click(discard, "Rename");
        discard.Close(); Dispatcher.UIThread.RunJobs();
        DialogClick(discard.OwnedWindows.Single(), "不保存"); Pump(discard);
        Require(!discard.IsVisible, "Discard close kept the window open.");
        Require(ImageProjectWorkflow.OpenEditable(source).Layers[^1].Name == "中文 Overlay", "Discard wrote unsaved changes to disk.");
        string groupCreation = Path.Combine(output, "GroupCreation.comp");
        var groupCreationWorkspace = new EditorWorkspace();
        groupCreationWorkspace.Import(fixture, groupCreation);
        groupCreationWorkspace.Edit(session => session.AddBlankLayer("Second", 1));
        var groupCreationWindow = new MainWindow(groupCreationWorkspace);
        groupCreationWindow.Show(); Dispatcher.UIThread.RunJobs();
        var layerList = Control<ListBox>(groupCreationWindow, "Layers");
        var rootItems = layerList.ItemsView!.Cast<FlatLayerInfo>().Where(layer => !layer.IsGroup).ToArray();
        layerList.SelectedItems!.Clear();
        foreach (var item in rootItems) layerList.SelectedItems!.Add(item);
        Dispatcher.UIThread.RunJobs();
        Click(groupCreationWindow, "GroupLayer");
        Require(groupCreationWorkspace.Session!.HasGroups, "Group button did not create a pass-through group.");
        var createdGroup = Control<ListBox>(groupCreationWindow, "Layers").ItemsView!.Cast<FlatLayerInfo>().Single(layer => layer.IsGroup);
        Require(groupCreationWorkspace.Session.Layers.Count(layer => !layer.IsGroup) == 2 &&
            groupCreationWorkspace.Session.Layers.Where(layer => !layer.IsGroup).All(layer => layer.ParentId == createdGroup.Id),
            "Multi-select group button did not group both selected sibling layers.");
        Control<ListBox>(groupCreationWindow, "Layers").SelectedItem = createdGroup;
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(groupCreationWindow, "FlipLayerHorizontal").IsEffectivelyEnabled &&
            Control<Button>(groupCreationWindow, "FlipLayerVertical").IsEffectivelyEnabled,
            "Unmasked editable group did not enable transform buttons.");
        Click(groupCreationWindow, "FlipLayerHorizontal");
        Click(groupCreationWindow, "Undo");
        Click(groupCreationWindow, "UngroupLayer");
        Require(!groupCreationWorkspace.Session.HasGroups, "Ungroup button did not remove a pass-through group.");
        groupCreationWorkspace.Save();
        groupCreationWindow.Close(); Dispatcher.UIThread.RunJobs();

        string groupMergeProject = Path.Combine(output, "GroupMerge.comp");
        var groupMergeWorkspace = new EditorWorkspace();
        groupMergeWorkspace.Import(fixture, groupMergeProject);
        Guid groupMergeLowerId = groupMergeWorkspace.Session!.Layers[0].Id;
        Guid groupMergeChildId = groupMergeWorkspace.Session.AddBlankLayer("Group base", 1);
        Guid groupMergeOverlayId = groupMergeWorkspace.Session.AddBlankLayer("Group overlay", 2);
        TileRaster groupMergeBaseRaster = groupMergeWorkspace.Session.GetLayerRaster(groupMergeLowerId);
        TileRaster groupMergeOverlayRaster = groupMergeWorkspace.Session.GetLayerRaster(groupMergeOverlayId);
        var groupMergeOverlaySize = groupMergeOverlayRaster.TileDimensions(0, 0);
        byte[] groupMergeOverlayTile = groupMergeOverlayRaster.ReadTileCopy(0, 0);
        int groupMergeOverlayOffset = (12 * groupMergeOverlaySize.Width + 12) * 4;
        groupMergeOverlayTile[groupMergeOverlayOffset] = 130;
        groupMergeOverlayTile[groupMergeOverlayOffset + 1] = 50;
        groupMergeOverlayTile[groupMergeOverlayOffset + 2] = 25;
        groupMergeOverlayTile[groupMergeOverlayOffset + 3] = 170;
        groupMergeWorkspace.Edit(session =>
        {
            session.ReplaceLayerRaster(groupMergeChildId, groupMergeBaseRaster);
            session.ReplaceLayerRaster(groupMergeOverlayId, groupMergeOverlayRaster.ReplaceTile(0, 0, groupMergeOverlayTile));
            session.SetLayerOpacity(groupMergeChildId, 0.74);
            session.SetLayerBlendMode(groupMergeOverlayId, "Screen");
            session.SetLayerOpacity(groupMergeOverlayId, 0.63);
            session.SetLayerMaskSource(groupMergeOverlayId, groupMergeChildId);
        });
        Guid groupMergeGroupId = groupMergeWorkspace.Session.GroupLayers(
            [groupMergeChildId, groupMergeOverlayId], "Merge group");
        groupMergeWorkspace.Edit(session =>
        {
            session.SetLayerOpacity(groupMergeGroupId, 0.82);
            session.SetLayerBlendMode(groupMergeGroupId, "Multiply");
            session.SetGroupTransform(groupMergeGroupId, 4, 3, session.Width - 8, session.Height - 6, 8);
        });
        groupMergeWorkspace.Edit(session => session.EnsureLayerMask(groupMergeGroupId));
        groupMergeWorkspace.Edit(session => session.ReplaceLayerMask(groupMergeGroupId,
            GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width / 2, session.Height)));
        groupMergeWorkspace.Save();
        TileRaster groupMergeBefore = ImageProjectWorkflow.RenderFlatNormal(groupMergeWorkspace.Session);
        var groupCopyWorkspace = new EditorWorkspace();
        groupCopyWorkspace.Open(groupMergeProject);
        groupCopyWorkspace.SelectRectangle(new Rect(0, 0, groupCopyWorkspace.Session!.Width / 2, groupCopyWorkspace.Session.Height));
        TileRaster expectedGroupCopy = ImageProjectWorkflow.RenderLayerForCopy(groupCopyWorkspace.Session, groupMergeGroupId);
        var groupCopyWindow = new MainWindow(groupCopyWorkspace);
        groupCopyWindow.Show(); Dispatcher.UIThread.RunJobs();
        Control<ListBox>(groupCopyWindow, "Layers").SelectedItem =
            groupCopyWorkspace.Session.Layers.Single(layer => layer.Id == groupMergeGroupId);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(groupCopyWindow, "LayerViaCopy").IsEffectivelyEnabled,
            "A root group with a mask and clipping stack did not enable Layer via Copy.");
        int groupCopyLayerCount = groupCopyWorkspace.Session.Layers.Count;
        Click(groupCopyWindow, "LayerViaCopy");
        Require(groupCopyWorkspace.Session.Layers.Any(layer => layer.Name == "Layer via Copy"),
            "Root group Layer via Copy command failed: " + Control<TextBlock>(groupCopyWindow, "Status").Text);
        FlatLayerInfo groupCopyLayer = groupCopyWorkspace.Session.Layers.Single(layer => layer.Name == "Layer via Copy");
        Require(groupCopyWorkspace.Session.Layers.Count == groupCopyLayerCount + 1 && groupCopyLayer.ParentId is null &&
            !groupCopyLayer.IsGroup && groupCopyWorkspace.Session.Layers.Any(layer => layer.Id == groupMergeGroupId),
            "Root group Layer via Copy did not create a flat sibling layer.");
        TileRaster actualGroupCopy = groupCopyWorkspace.Session.GetLayerRaster(groupCopyLayer.Id);
        for (int y = 0; y < actualGroupCopy.Height; y++)
        for (int x = 0; x < actualGroupCopy.Width; x++)
        {
            byte[] expected = PixelAt(expectedGroupCopy, x, y), actual = PixelAt(actualGroupCopy, x, y);
            if (x < actualGroupCopy.Width / 2) Require(expected.SequenceEqual(actual),
                "Root group Layer via Copy changed selected pixels.");
            else Require(actual.All(channel => channel == 0),
                "Root group Layer via Copy retained pixels outside the selection.");
        }
        Guid nestedOuterGroupId = Guid.Empty;
        groupCopyWorkspace.Edit(session =>
        {
            nestedOuterGroupId = session.GroupLayers([groupMergeGroupId], "Nested outer group");
            session.SetLayerOpacity(nestedOuterGroupId, 0.76);
            session.SetLayerBlendMode(nestedOuterGroupId, "Screen");
            session.SetGroupTransform(nestedOuterGroupId, 2, 1, session.Width - 4, session.Height - 2, 5);
            session.EnsureLayerMask(nestedOuterGroupId);
        });
        groupCopyWorkspace.Edit(session => session.ReplaceLayerMask(nestedOuterGroupId,
            GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width, session.Height / 2)));
        groupCopyWorkspace.SelectRectangle(new Rect(0, 0, groupCopyWorkspace.Session!.Width / 2,
            groupCopyWorkspace.Session.Height));
        TileRaster expectedNestedGroupCopy = ImageProjectWorkflow.RenderLayerForCopy(
            groupCopyWorkspace.Session, nestedOuterGroupId);
        Control<ListBox>(groupCopyWindow, "Layers").SelectedItem =
            groupCopyWorkspace.Session.Layers.Single(layer => layer.Id == nestedOuterGroupId);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(groupCopyWindow, "LayerViaCopy").IsEffectivelyEnabled,
            "A nested group with ancestor masks did not enable Layer via Copy.");
        int nestedGroupCopyLayerCount = groupCopyWorkspace.Session.Layers.Count;
        Click(groupCopyWindow, "LayerViaCopy");
        Guid nestedCopiedLayerId = groupCopyWorkspace.Session.ActiveLayerId!.Value;
        FlatLayerInfo nestedCopiedLayer = groupCopyWorkspace.Session.Layers.Single(layer => layer.Id == nestedCopiedLayerId);
        Require(groupCopyWorkspace.Session.Layers.Count == nestedGroupCopyLayerCount + 1 &&
            !nestedCopiedLayer.IsGroup && nestedCopiedLayer.ParentId is null &&
            groupCopyWorkspace.Session.Layers.Any(layer => layer.Id == nestedOuterGroupId),
            "Nested group Layer via Copy did not create a root-level flat result.");
        TileRaster actualNestedGroupCopy = groupCopyWorkspace.Session.GetLayerRaster(nestedCopiedLayerId);
        for (int y = 0; y < actualNestedGroupCopy.Height; y++)
        for (int x = 0; x < actualNestedGroupCopy.Width; x++)
        {
            byte[] expected = PixelAt(expectedNestedGroupCopy, x, y), actual = PixelAt(actualNestedGroupCopy, x, y);
            if (x < actualNestedGroupCopy.Width / 2) Require(expected.SequenceEqual(actual),
                "Nested group Layer via Copy changed selected pixels.");
            else Require(actual.All(channel => channel == 0),
                "Nested group Layer via Copy retained pixels outside the selection.");
        }
        groupCopyWindow.Close(); Dispatcher.UIThread.RunJobs();
        var groupedLeafCopyWorkspace = new EditorWorkspace();
        string groupedLeafCopyPath = Path.Combine(output, "GroupedLeafLayerViaCopy.comp");
        groupedLeafCopyWorkspace.Import(fixture, groupedLeafCopyPath);
        Guid groupedLeafId = groupedLeafCopyWorkspace.Session!.ActiveLayerId!.Value;
        Guid groupedLeafGroupId = Guid.Empty;
        groupedLeafCopyWorkspace.Edit(session =>
        {
            groupedLeafGroupId = session.GroupLayer(groupedLeafId, "Masked parent");
            session.EnsureLayerMask(groupedLeafGroupId);
            session.ReplaceLayerMask(groupedLeafGroupId,
                GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width / 2, session.Height));
            session.SetLayerMaskEnabled(groupedLeafGroupId, true);
        });
        groupedLeafCopyWorkspace.Save();
        groupedLeafCopyWorkspace.Session.SelectLayer(groupedLeafId);
        groupedLeafCopyWorkspace.SelectRectangle(new Rect(0, 0,
            groupedLeafCopyWorkspace.Session.Width / 2, groupedLeafCopyWorkspace.Session.Height));
        var groupedLeafCopyWindow = new MainWindow(groupedLeafCopyWorkspace);
        groupedLeafCopyWindow.Show(); Dispatcher.UIThread.RunJobs();
        Control<ListBox>(groupedLeafCopyWindow, "Layers").SelectedItem =
            groupedLeafCopyWorkspace.Session.Layers.Single(layer => layer.Id == groupedLeafId);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(groupedLeafCopyWindow, "LayerViaCopy").IsEffectivelyEnabled,
            "A grouped raster with an enabled parent mask did not enable Layer via Copy.");
        int groupedLeafCopyCount = groupedLeafCopyWorkspace.Session.Layers.Count;
        TileRaster groupedLeafSourceRaster = groupedLeafCopyWorkspace.Session.GetLayerRaster(groupedLeafId);
        Click(groupedLeafCopyWindow, "LayerViaCopy");
        Guid groupedLeafCopiedId = groupedLeafCopyWorkspace.Session.ActiveLayerId!.Value;
        FlatLayerInfo groupedLeafCopied = groupedLeafCopyWorkspace.Session.Layers.Single(layer => layer.Id == groupedLeafCopiedId);
        Require(groupedLeafCopyWorkspace.Session.Layers.Count == groupedLeafCopyCount + 1 &&
            groupedLeafCopied.ParentId == groupedLeafGroupId && !groupedLeafCopied.IsGroup &&
            groupedLeafCopyWorkspace.Session.Layers.Single(layer => layer.Id == groupedLeafGroupId).MaskEnabled,
            "Grouped Layer via Copy did not insert a raster sibling under the masked parent.");
        TileRaster groupedLeafCopiedRaster = groupedLeafCopyWorkspace.Session.GetLayerRaster(groupedLeafCopiedId);
        for (int y = 0; y < groupedLeafCopiedRaster.Height; y++)
        for (int x = 0; x < groupedLeafCopiedRaster.Width; x++)
        {
            byte[] actual = PixelAt(groupedLeafCopiedRaster, x, y);
            if (x < groupedLeafCopiedRaster.Width / 2)
                Require(actual.SequenceEqual(PixelAt(groupedLeafSourceRaster, x, y)),
                    "Grouped Layer via Copy changed pixels inside the selection.");
            else Require(actual.All(channel => channel == 0),
                "Grouped Layer via Copy retained pixels outside the selection.");
        }
        groupedLeafCopyWorkspace.Save();
        var reopenedGroupedLeafCopy = ImageProjectWorkflow.OpenEditable(groupedLeafCopyPath);
        Require(reopenedGroupedLeafCopy.Layers.Single(layer => layer.Id == groupedLeafCopiedId).ParentId == groupedLeafGroupId &&
            reopenedGroupedLeafCopy.Layers.Single(layer => layer.Id == groupedLeafGroupId).MaskEnabled &&
            CheckEqualNoThrow(groupedLeafCopiedRaster, reopenedGroupedLeafCopy.GetLayerRaster(groupedLeafCopiedId)) &&
            PixelAt(ImageProjectWorkflow.RenderFlatNormal(reopenedGroupedLeafCopy), reopenedGroupedLeafCopy.Width - 1,
                reopenedGroupedLeafCopy.Height / 2)[3] == 0,
            "Saved grouped Layer via Copy did not preserve the parent mask or raster.");
        groupedLeafCopyWindow.Close(); Dispatcher.UIThread.RunJobs();
        var groupedStackCopyWorkspace = new EditorWorkspace();
        string groupedStackCopyPath = Path.Combine(output, "GroupedClippingStackLayerViaCopy.comp");
        groupedStackCopyWorkspace.Import(fixture, groupedStackCopyPath);
        Guid groupedStackBaseId = groupedStackCopyWorkspace.Session!.ActiveLayerId!.Value;
        Guid groupedStackChildId = groupedStackCopyWorkspace.Session.AddBlankLayer("Clipped child", 1);
        TileRaster groupedStackChildRaster = groupedStackCopyWorkspace.Session.GetLayerRaster(groupedStackChildId);
        var groupedStackChildSize = groupedStackChildRaster.TileDimensions(0, 0);
        byte[] groupedStackChildTile = groupedStackChildRaster.ReadTileCopy(0, 0);
        int groupedStackChildOffset = (12 * groupedStackChildSize.Width + 12) * 4;
        groupedStackChildTile[groupedStackChildOffset] = 100;
        groupedStackChildTile[groupedStackChildOffset + 1] = 20;
        groupedStackChildTile[groupedStackChildOffset + 2] = 10;
        groupedStackChildTile[groupedStackChildOffset + 3] = 170;
        groupedStackCopyWorkspace.Edit(session =>
        {
            session.ReplaceLayerRaster(groupedStackChildId,
                groupedStackChildRaster.ReplaceTile(0, 0, groupedStackChildTile));
            session.SetLayerOpacity(groupedStackChildId, 0.71);
            session.SetLayerBlendMode(groupedStackChildId, "Screen");
            session.SetLayerMaskSource(groupedStackChildId, groupedStackBaseId);
        });
        Guid groupedStackGroupId = groupedStackCopyWorkspace.Session.GroupLayers(
            [groupedStackBaseId, groupedStackChildId], "Clipping parent");
        groupedStackCopyWorkspace.Edit(session =>
        {
            session.EnsureLayerMask(groupedStackGroupId);
            session.ReplaceLayerMask(groupedStackGroupId,
                GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width / 2, session.Height));
            session.SetLayerMaskEnabled(groupedStackGroupId, true);
        });
        groupedStackCopyWorkspace.Save();
        groupedStackCopyWorkspace.Session.SelectLayer(groupedStackChildId);
        groupedStackCopyWorkspace.SelectRectangle(new Rect(0, 0,
            groupedStackCopyWorkspace.Session.Width / 2, groupedStackCopyWorkspace.Session.Height));
        TileRaster groupedStackExpected = ImageProjectWorkflow.RenderLayerForCopy(
            groupedStackCopyWorkspace.Session, groupedStackChildId);
        int groupedStackCopyCount = groupedStackCopyWorkspace.Session.Layers.Count;
        int groupedStackChildIndex = groupedStackCopyWorkspace.Session.Layers.ToList()
            .FindIndex(layer => layer.Id == groupedStackChildId);
        var groupedStackCopyWindow = new MainWindow(groupedStackCopyWorkspace);
        groupedStackCopyWindow.Show(); Dispatcher.UIThread.RunJobs();
        Control<ListBox>(groupedStackCopyWindow, "Layers").SelectedItem =
            groupedStackCopyWorkspace.Session.Layers.Single(layer => layer.Id == groupedStackChildId);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(groupedStackCopyWindow, "LayerViaCopy").IsEffectivelyEnabled,
            "A grouped clipping stack with an enabled parent mask did not enable Layer via Copy.");
        Click(groupedStackCopyWindow, "LayerViaCopy");
        Guid groupedStackCopiedId = groupedStackCopyWorkspace.Session.ActiveLayerId!.Value;
        FlatLayerInfo groupedStackCopied = groupedStackCopyWorkspace.Session.Layers.Single(layer => layer.Id == groupedStackCopiedId);
        int groupedStackCopiedIndex = groupedStackCopyWorkspace.Session.Layers.ToList()
            .FindIndex(layer => layer.Id == groupedStackCopiedId);
        Require(groupedStackCopyWorkspace.Session.Layers.Count == groupedStackCopyCount + 1 &&
            groupedStackCopied.ParentId == groupedStackGroupId && !groupedStackCopied.IsGroup &&
            groupedStackCopiedIndex == groupedStackChildIndex + 1 &&
            groupedStackCopyWorkspace.Session.Layers.Single(layer => layer.Id == groupedStackGroupId).MaskEnabled,
            "Grouped clipping-stack Layer via Copy did not insert after the stack under the masked parent.");
        TileRaster groupedStackCopiedRaster = groupedStackCopyWorkspace.Session.GetLayerRaster(groupedStackCopiedId);
        for (int y = 0; y < groupedStackCopiedRaster.Height; y++)
        for (int x = 0; x < groupedStackCopiedRaster.Width; x++)
        {
            byte[] actual = PixelAt(groupedStackCopiedRaster, x, y);
            if (x < groupedStackCopiedRaster.Width / 2)
                Require(actual.SequenceEqual(PixelAt(groupedStackExpected, x, y)),
                    "Grouped clipping-stack Layer via Copy changed pixels inside the selection.");
            else Require(actual.All(channel => channel == 0),
                "Grouped clipping-stack Layer via Copy retained pixels outside the selection.");
        }
        groupedStackCopyWorkspace.Save();
        var reopenedGroupedStackCopy = ImageProjectWorkflow.OpenEditable(groupedStackCopyPath);
        Require(reopenedGroupedStackCopy.Layers.Single(layer => layer.Id == groupedStackCopiedId).ParentId == groupedStackGroupId &&
            reopenedGroupedStackCopy.Layers.Single(layer => layer.Id == groupedStackGroupId).MaskEnabled &&
            CheckEqualNoThrow(groupedStackCopiedRaster, reopenedGroupedStackCopy.GetLayerRaster(groupedStackCopiedId)) &&
            PixelAt(ImageProjectWorkflow.RenderFlatNormal(reopenedGroupedStackCopy), reopenedGroupedStackCopy.Width - 1,
                reopenedGroupedStackCopy.Height / 2)[3] == 0,
            "Saved grouped clipping-stack Layer via Copy did not preserve the stack boundary, parent mask or raster.");
        groupedStackCopyWindow.Close(); Dispatcher.UIThread.RunJobs();
        var nestedGroupedStackCopyWorkspace = new EditorWorkspace();
        string nestedGroupedStackCopyPath = Path.Combine(output, "GroupedNestedClippingStackLayerViaCopy.comp");
        nestedGroupedStackCopyWorkspace.Import(fixture, nestedGroupedStackCopyPath);
        Guid nestedGroupedStackBaseId = nestedGroupedStackCopyWorkspace.Session!.ActiveLayerId!.Value;
        Guid nestedGroupedStackFirstChildId = nestedGroupedStackCopyWorkspace.Session.AddBlankLayer("First clipped child", 1);
        Guid nestedGroupedStackSecondChildId = nestedGroupedStackCopyWorkspace.Session.AddBlankLayer("Second clipped child", 2);
        nestedGroupedStackCopyWorkspace.Edit(session =>
        {
            session.SetLayerOpacity(nestedGroupedStackFirstChildId, 0.71);
            session.SetLayerBlendMode(nestedGroupedStackFirstChildId, "Screen");
            session.SetLayerMaskSource(nestedGroupedStackFirstChildId, nestedGroupedStackBaseId);
            session.SetLayerOpacity(nestedGroupedStackSecondChildId, 0.63);
            session.SetLayerBlendMode(nestedGroupedStackSecondChildId, "Multiply");
            session.SetLayerMaskSource(nestedGroupedStackSecondChildId, nestedGroupedStackFirstChildId);
        });
        Guid nestedGroupedStackGroupId = nestedGroupedStackCopyWorkspace.Session.GroupLayers(
            [nestedGroupedStackBaseId, nestedGroupedStackFirstChildId, nestedGroupedStackSecondChildId],
            "Nested clipping parent");
        nestedGroupedStackCopyWorkspace.Edit(session =>
        {
            session.EnsureLayerMask(nestedGroupedStackGroupId);
            session.ReplaceLayerMask(nestedGroupedStackGroupId,
                GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width / 2, session.Height));
            session.SetLayerMaskEnabled(nestedGroupedStackGroupId, true);
        });
        nestedGroupedStackCopyWorkspace.Save();
        nestedGroupedStackCopyWorkspace.Session.SelectLayer(nestedGroupedStackSecondChildId);
        nestedGroupedStackCopyWorkspace.SelectRectangle(new Rect(0, 0,
            nestedGroupedStackCopyWorkspace.Session.Width / 2, nestedGroupedStackCopyWorkspace.Session.Height));
        TileRaster nestedGroupedStackExpected = ImageProjectWorkflow.RenderLayerForCopy(
            nestedGroupedStackCopyWorkspace.Session, nestedGroupedStackSecondChildId);
        int nestedGroupedStackCopyCount = nestedGroupedStackCopyWorkspace.Session.Layers.Count;
        int nestedGroupedStackSecondChildIndex = nestedGroupedStackCopyWorkspace.Session.Layers.ToList()
            .FindIndex(layer => layer.Id == nestedGroupedStackSecondChildId);
        var nestedGroupedStackCopyWindow = new MainWindow(nestedGroupedStackCopyWorkspace);
        nestedGroupedStackCopyWindow.Show(); Dispatcher.UIThread.RunJobs();
        Control<ListBox>(nestedGroupedStackCopyWindow, "Layers").SelectedItem =
            nestedGroupedStackCopyWorkspace.Session.Layers.Single(layer => layer.Id == nestedGroupedStackSecondChildId);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(nestedGroupedStackCopyWindow, "LayerViaCopy").IsEffectivelyEnabled,
            "A nested grouped clipping stack did not enable Layer via Copy.");
        Click(nestedGroupedStackCopyWindow, "LayerViaCopy");
        Guid nestedGroupedStackCopiedId = nestedGroupedStackCopyWorkspace.Session.ActiveLayerId!.Value;
        FlatLayerInfo nestedGroupedStackCopied = nestedGroupedStackCopyWorkspace.Session.Layers
            .Single(layer => layer.Id == nestedGroupedStackCopiedId);
        int nestedGroupedStackCopiedIndex = nestedGroupedStackCopyWorkspace.Session.Layers.ToList()
            .FindIndex(layer => layer.Id == nestedGroupedStackCopiedId);
        Require(nestedGroupedStackCopyWorkspace.Session.Layers.Count == nestedGroupedStackCopyCount + 1 &&
            nestedGroupedStackCopied.ParentId == nestedGroupedStackGroupId &&
            nestedGroupedStackCopiedIndex > nestedGroupedStackSecondChildIndex,
            "Nested grouped clipping-stack Layer via Copy did not insert after the complete stack.");
        TileRaster nestedGroupedStackCopiedRaster = nestedGroupedStackCopyWorkspace.Session
            .GetLayerRaster(nestedGroupedStackCopiedId);
        for (int y = 0; y < nestedGroupedStackCopiedRaster.Height; y++)
        for (int x = 0; x < nestedGroupedStackCopiedRaster.Width; x++)
        {
            byte[] actual = PixelAt(nestedGroupedStackCopiedRaster, x, y);
            if (x < nestedGroupedStackCopiedRaster.Width / 2)
                Require(actual.SequenceEqual(PixelAt(nestedGroupedStackExpected, x, y)),
                    "Nested grouped clipping-stack Layer via Copy changed selected pixels.");
            else Require(actual.All(channel => channel == 0),
                "Nested grouped clipping-stack Layer via Copy retained pixels outside the selection.");
        }
        nestedGroupedStackCopyWorkspace.Save();
        var reopenedNestedGroupedStackCopy = ImageProjectWorkflow.OpenEditable(nestedGroupedStackCopyPath);
        Require(reopenedNestedGroupedStackCopy.Layers.Single(layer => layer.Id == nestedGroupedStackFirstChildId).MaskSourceId == nestedGroupedStackBaseId &&
            reopenedNestedGroupedStackCopy.Layers.Single(layer => layer.Id == nestedGroupedStackSecondChildId).MaskSourceId == nestedGroupedStackFirstChildId &&
            CheckEqualNoThrow(nestedGroupedStackCopiedRaster,
                reopenedNestedGroupedStackCopy.GetLayerRaster(nestedGroupedStackCopiedId)),
            "Saved nested grouped clipping-stack copy did not preserve the chain or raster.");
        nestedGroupedStackCopyWindow.Close(); Dispatcher.UIThread.RunJobs();
        var discontinuousGroupedStackWorkspace = new EditorWorkspace();
        string discontinuousGroupedStackPath = Path.Combine(output, "GroupedDiscontinuousClippingStackLayerViaCopy.comp");
        discontinuousGroupedStackWorkspace.Import(fixture, discontinuousGroupedStackPath);
        Guid discontinuousBaseId = discontinuousGroupedStackWorkspace.Session!.ActiveLayerId!.Value;
        Guid discontinuousUnrelatedId = discontinuousGroupedStackWorkspace.Session.AddBlankLayer("Unrelated sibling", 1);
        Guid discontinuousChildId = discontinuousGroupedStackWorkspace.Session.AddBlankLayer("Clipped child", 2);
        discontinuousGroupedStackWorkspace.Edit(session =>
        {
            session.SetLayerOpacity(discontinuousChildId, 0.66);
            session.SetLayerBlendMode(discontinuousChildId, "Screen");
            session.SetLayerMaskSource(discontinuousChildId, discontinuousBaseId);
        });
        Guid discontinuousGroupId = discontinuousGroupedStackWorkspace.Session.GroupLayers(
            [discontinuousBaseId, discontinuousUnrelatedId, discontinuousChildId], "Discontinuous clipping parent");
        discontinuousGroupedStackWorkspace.Save();
        discontinuousGroupedStackWorkspace.Session.SelectLayer(discontinuousChildId);
        discontinuousGroupedStackWorkspace.SelectRectangle(new Rect(0, 0,
            discontinuousGroupedStackWorkspace.Session.Width / 2,
            discontinuousGroupedStackWorkspace.Session.Height));
        TileRaster discontinuousExpected = ImageProjectWorkflow.RenderLayerForCopy(
            discontinuousGroupedStackWorkspace.Session, discontinuousChildId);
        int discontinuousChildIndex = discontinuousGroupedStackWorkspace.Session.Layers.ToList()
            .FindIndex(layer => layer.Id == discontinuousChildId);
        var discontinuousGroupedStackWindow = new MainWindow(discontinuousGroupedStackWorkspace);
        discontinuousGroupedStackWindow.Show(); Dispatcher.UIThread.RunJobs();
        Control<ListBox>(discontinuousGroupedStackWindow, "Layers").SelectedItem =
            discontinuousGroupedStackWorkspace.Session.Layers.Single(layer => layer.Id == discontinuousChildId);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(discontinuousGroupedStackWindow, "LayerViaCopy").IsEffectivelyEnabled,
            "A same-parent discontinuous clipping stack did not enable Layer via Copy.");
        Click(discontinuousGroupedStackWindow, "LayerViaCopy");
        Guid discontinuousCopiedId = discontinuousGroupedStackWorkspace.Session.ActiveLayerId!.Value;
        FlatLayerInfo discontinuousCopied = discontinuousGroupedStackWorkspace.Session.Layers
            .Single(layer => layer.Id == discontinuousCopiedId);
        int discontinuousCopiedIndex = discontinuousGroupedStackWorkspace.Session.Layers.ToList()
            .FindIndex(layer => layer.Id == discontinuousCopiedId);
        Require(discontinuousCopied.ParentId == discontinuousGroupId &&
            discontinuousCopiedIndex == discontinuousChildIndex + 1,
            "Discontinuous clipping-stack Layer via Copy did not insert after the selected target.");
        TileRaster discontinuousCopiedRaster = discontinuousGroupedStackWorkspace.Session
            .GetLayerRaster(discontinuousCopiedId);
        for (int y = 0; y < discontinuousCopiedRaster.Height; y++)
        for (int x = 0; x < discontinuousCopiedRaster.Width; x++)
        {
            byte[] actual = PixelAt(discontinuousCopiedRaster, x, y);
            if (x < discontinuousCopiedRaster.Width / 2)
                Require(actual.SequenceEqual(PixelAt(discontinuousExpected, x, y)),
                    "Discontinuous clipping-stack Layer via Copy changed selected pixels.");
            else Require(actual.All(channel => channel == 0),
                "Discontinuous clipping-stack Layer via Copy retained pixels outside the selection.");
        }
        discontinuousGroupedStackWorkspace.Save();
        var reopenedDiscontinuous = ImageProjectWorkflow.OpenEditable(discontinuousGroupedStackPath);
        Require(reopenedDiscontinuous.Layers.Single(layer => layer.Id == discontinuousChildId).MaskSourceId == discontinuousBaseId &&
            reopenedDiscontinuous.Layers.Single(layer => layer.Id == discontinuousCopiedId).ParentId == discontinuousGroupId &&
            CheckEqualNoThrow(discontinuousCopiedRaster,
                reopenedDiscontinuous.GetLayerRaster(discontinuousCopiedId)),
            "Saved discontinuous clipping-stack copy did not preserve the relationship, parent or raster.");
        discontinuousGroupedStackWindow.Close(); Dispatcher.UIThread.RunJobs();
        var crossParentWorkspace = new EditorWorkspace();
        string crossParentPath = Path.Combine(output, "CrossParentGroupedLeafLayerViaCopy.comp");
        crossParentWorkspace.Import(fixture, crossParentPath);
        Guid crossParentSourceId = crossParentWorkspace.Session!.ActiveLayerId!.Value;
        Guid crossParentTargetId = Guid.Empty;
        Guid crossParentSourceGroupId = Guid.Empty;
        Guid crossParentTargetGroupId = Guid.Empty;
        crossParentWorkspace.Edit(session =>
        {
            crossParentTargetId = session.AddBlankLayer("Cross-parent clipped target", session.Layers.Count);
            crossParentSourceGroupId = session.GroupLayer(crossParentSourceId, "External source parent");
            crossParentTargetGroupId = session.GroupLayer(crossParentTargetId, "Cross-parent target parent");
            session.SetLayerOpacity(crossParentSourceGroupId, 0.58);
            session.SetLayerBlendMode(crossParentSourceGroupId, "Multiply");
            session.SetGroupTransform(crossParentSourceGroupId, 8, 4,
                session.Width - 16, session.Height - 8, 11);
            session.EnsureLayerMask(crossParentSourceGroupId);
            session.ReplaceLayerMask(crossParentSourceGroupId,
                GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0,
                    session.Width * 3 / 4, session.Height));
            session.SetLayerMaskEnabled(crossParentSourceGroupId, true);
        });
        crossParentWorkspace.Save();
        var crossParentManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(crossParentPath, "manifest.json")))!.AsObject();
        crossParentManifest["layers"]!.AsArray().Single(node =>
            Guid.Parse(node!["id"]!.GetValue<string>()) == crossParentTargetId)!["maskSourceID"] =
            crossParentSourceId.ToString("D");
        File.WriteAllText(Path.Combine(crossParentPath, "manifest.json"), crossParentManifest.ToJsonString());
        crossParentWorkspace.Open(crossParentPath);
        crossParentWorkspace.Session!.SelectLayer(crossParentTargetId);
        crossParentWorkspace.SelectRectangle(new Rect(0, 0,
            crossParentWorkspace.Session.Width / 2, crossParentWorkspace.Session.Height));
        TileRaster crossParentExpected = ImageProjectWorkflow.RenderLayerForCopy(
            crossParentWorkspace.Session, crossParentTargetId);
        int crossParentTargetGroupIndex = crossParentWorkspace.Session.Layers.ToList()
            .FindIndex(layer => layer.Id == crossParentTargetGroupId);
        var crossParentWindow = new MainWindow(crossParentWorkspace);
        crossParentWindow.Show(); Dispatcher.UIThread.RunJobs();
        Control<ListBox>(crossParentWindow, "Layers").SelectedItem =
            crossParentWorkspace.Session.Layers.Single(layer => layer.Id == crossParentTargetId);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(crossParentWindow, "LayerViaCopy").IsEffectivelyEnabled,
            "A cross-parent grouped clipping target did not enable Layer via Copy.");
        Click(crossParentWindow, "LayerViaCopy");
        Guid crossParentCopiedId = crossParentWorkspace.Session.ActiveLayerId!.Value;
        FlatLayerInfo crossParentCopied = crossParentWorkspace.Session.Layers
            .Single(layer => layer.Id == crossParentCopiedId);
        int crossParentCopiedIndex = crossParentWorkspace.Session.Layers.ToList()
            .FindIndex(layer => layer.Id == crossParentCopiedId);
        Require(crossParentCopied.ParentId is null && crossParentCopiedIndex == crossParentTargetGroupIndex + 2,
            "Cross-parent grouped clipping copy did not insert after the target group subtree.");
        TileRaster crossParentCopiedRaster = crossParentWorkspace.Session.GetLayerRaster(crossParentCopiedId);
        for (int y = 0; y < crossParentCopiedRaster.Height; y++)
        for (int x = 0; x < crossParentCopiedRaster.Width; x++)
        {
            byte[] actual = PixelAt(crossParentCopiedRaster, x, y);
            if (x < crossParentCopiedRaster.Width / 2)
                Require(actual.SequenceEqual(PixelAt(crossParentExpected, x, y)),
                    "Cross-parent grouped clipping Layer via Copy changed selected pixels.");
            else Require(actual.All(channel => channel == 0),
                "Cross-parent grouped clipping Layer via Copy retained pixels outside the selection.");
        }
        crossParentWorkspace.Save();
        var reopenedCrossParent = ImageProjectWorkflow.OpenEditable(crossParentPath);
        Require(reopenedCrossParent.Layers.Single(layer => layer.Id == crossParentTargetId).MaskSourceId == crossParentSourceId &&
            reopenedCrossParent.Layers.Single(layer => layer.Id == crossParentSourceId).ParentId == crossParentSourceGroupId &&
            reopenedCrossParent.Layers.Single(layer => layer.Id == crossParentCopiedId).ParentId is null &&
            CheckEqualNoThrow(crossParentCopiedRaster, reopenedCrossParent.GetLayerRaster(crossParentCopiedId)),
            "Saved cross-parent grouped clipping copy did not preserve relationship, root placement or raster.");
        crossParentWindow.Close(); Dispatcher.UIThread.RunJobs();
        var crossParentChainWorkspace = new EditorWorkspace();
        string crossParentChainPath = Path.Combine(output, "CrossParentGroupedClippingChainLayerViaCopy.comp");
        crossParentChainWorkspace.Import(fixture, crossParentChainPath);
        Guid crossParentChainSourceBaseId = crossParentChainWorkspace.Session!.ActiveLayerId!.Value;
        var crossParentChainSourceIds = new List<Guid> { crossParentChainSourceBaseId };
        var crossParentChainSourceGroupIds = new List<Guid>();
        Guid crossParentChainTargetId = Guid.Empty;
        Guid crossParentChainTargetGroupId = Guid.Empty;
        crossParentChainWorkspace.Edit(session =>
        {
            for (int level = 11; level >= 1; level--)
                crossParentChainSourceIds.Add(session.AddBlankLayer(
                    $"Intermediate clipped source {level}", session.Layers.Count));
            crossParentChainTargetId = session.AddBlankLayer("Cross-parent chain target", session.Layers.Count);
            foreach (Guid chainSourceId in crossParentChainSourceIds.Skip(1))
                session.ReplaceLayerRaster(chainSourceId, session.GetLayerRaster(crossParentChainSourceBaseId));
            session.ReplaceLayerRaster(crossParentChainTargetId,
                session.GetLayerRaster(crossParentChainSourceBaseId));
            session.SetLayerOpacity(crossParentChainSourceBaseId, 0.72);
            for (int level = 0; level < crossParentChainSourceIds.Count; level++)
                crossParentChainSourceGroupIds.Add(session.GroupLayer(crossParentChainSourceIds[level],
                    $"External source parent {level + 1}"));
            crossParentChainTargetGroupId = session.GroupLayer(crossParentChainTargetId,
                "Cross-parent chain target parent");
        });
        crossParentChainWorkspace.Save();
        var crossParentChainManifest = JsonNode.Parse(
            File.ReadAllText(Path.Combine(crossParentChainPath, "manifest.json")))!.AsObject();
        for (int level = 1; level < crossParentChainSourceIds.Count; level++)
        {
            crossParentChainManifest["layers"]!.AsArray().Single(node =>
                Guid.Parse(node!["id"]!.GetValue<string>()) == crossParentChainSourceIds[level])!["maskSourceID"] =
                crossParentChainSourceIds[level - 1].ToString("D");
        }
        crossParentChainManifest["layers"]!.AsArray().Single(node =>
            Guid.Parse(node!["id"]!.GetValue<string>()) == crossParentChainTargetId)!["maskSourceID"] =
            crossParentChainSourceIds[^1].ToString("D");
        File.WriteAllText(Path.Combine(crossParentChainPath, "manifest.json"), crossParentChainManifest.ToJsonString());
        crossParentChainWorkspace.Open(crossParentChainPath);
        crossParentChainWorkspace.Session!.SelectLayer(crossParentChainTargetId);
        crossParentChainWorkspace.SelectRectangle(new Rect(0, 0,
            crossParentChainWorkspace.Session.Width / 2, crossParentChainWorkspace.Session.Height));
        TileRaster crossParentChainExpected = crossParentChainWorkspace.Session
            .GetLayerRaster(crossParentChainSourceBaseId);
        for (int level = 1; level < crossParentChainSourceIds.Count; level++)
            crossParentChainExpected = RasterCompositor.ApplyAlphaMask(
                crossParentChainWorkspace.Session.GetLayerRaster(crossParentChainSourceIds[level]),
                crossParentChainExpected, level == 1 ? 0.72 : 1);
        crossParentChainExpected = RasterCompositor.ApplyAlphaMask(
            crossParentChainWorkspace.Session.GetLayerRaster(crossParentChainTargetId),
            crossParentChainExpected, 1);
        int crossParentChainTargetGroupIndex = crossParentChainWorkspace.Session.Layers.ToList()
            .FindIndex(layer => layer.Id == crossParentChainTargetGroupId);
        var crossParentChainWindow = new MainWindow(crossParentChainWorkspace);
        crossParentChainWindow.Show(); Dispatcher.UIThread.RunJobs();
        Control<ListBox>(crossParentChainWindow, "Layers").SelectedItem =
            crossParentChainWorkspace.Session.Layers.Single(layer => layer.Id == crossParentChainTargetId);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(crossParentChainWindow, "LayerViaCopy").IsEffectivelyEnabled,
            "A cross-parent twelve-level clipping target did not enable Layer via Copy.");
        Click(crossParentChainWindow, "LayerViaCopy");
        Guid crossParentChainCopiedId = crossParentChainWorkspace.Session.ActiveLayerId!.Value;
        FlatLayerInfo crossParentChainCopied = crossParentChainWorkspace.Session.Layers
            .Single(layer => layer.Id == crossParentChainCopiedId);
        int crossParentChainCopiedIndex = crossParentChainWorkspace.Session.Layers.ToList()
            .FindIndex(layer => layer.Id == crossParentChainCopiedId);
        Require(crossParentChainCopied.ParentId is null &&
            crossParentChainCopiedIndex == crossParentChainTargetGroupIndex + 2,
            "Cross-parent twelve-level clipping copy did not insert after the target group subtree.");
        TileRaster crossParentChainCopiedRaster = crossParentChainWorkspace.Session
            .GetLayerRaster(crossParentChainCopiedId);
        for (int y = 0; y < crossParentChainCopiedRaster.Height; y++)
        for (int x = 0; x < crossParentChainCopiedRaster.Width; x++)
        {
            byte[] actual = PixelAt(crossParentChainCopiedRaster, x, y);
            if (x < crossParentChainCopiedRaster.Width / 2)
                Require(actual.SequenceEqual(PixelAt(crossParentChainExpected, x, y)),
                    "Cross-parent twelve-level clipping Layer via Copy changed selected pixels.");
            else Require(actual.All(channel => channel == 0),
                "Cross-parent twelve-level clipping Layer via Copy retained pixels outside the selection.");
        }
        crossParentChainWorkspace.Save();
        var reopenedCrossParentChain = ImageProjectWorkflow.OpenEditable(crossParentChainPath);
        for (int level = 1; level < crossParentChainSourceIds.Count; level++)
            Require(reopenedCrossParentChain.Layers.Single(layer => layer.Id == crossParentChainSourceIds[level]).MaskSourceId ==
                crossParentChainSourceIds[level - 1],
                "Saved cross-parent twelve-level clipping copy lost a source relationship.");
        for (int level = 0; level < crossParentChainSourceIds.Count; level++)
            Require(reopenedCrossParentChain.Layers.Single(layer => layer.Id == crossParentChainSourceIds[level]).ParentId ==
                crossParentChainSourceGroupIds[level],
                "Saved cross-parent twelve-level clipping copy lost a source parent.");
        Require(reopenedCrossParentChain.Layers.Single(layer => layer.Id == crossParentChainTargetId).MaskSourceId ==
            crossParentChainSourceIds[^1] &&
            reopenedCrossParentChain.Layers.Single(layer => layer.Id == crossParentChainCopiedId).ParentId is null &&
            CheckEqualNoThrow(crossParentChainCopiedRaster,
                reopenedCrossParentChain.GetLayerRaster(crossParentChainCopiedId)),
            "Saved cross-parent twelve-level clipping copy did not preserve relationships, root placement or raster.");
        crossParentChainWindow.Close(); Dispatcher.UIThread.RunJobs();
        var transformedGroupedLeafWorkspace = new EditorWorkspace();
        string transformedGroupedLeafPath = Path.Combine(output, "TransformedGroupedLeafLayerViaCopy.comp");
        transformedGroupedLeafWorkspace.Import(fixture, transformedGroupedLeafPath);
        Guid transformedGroupedLeafId = transformedGroupedLeafWorkspace.Session!.ActiveLayerId!.Value;
        Guid transformedGroupedGroupId = Guid.Empty;
        transformedGroupedLeafWorkspace.Edit(session =>
        {
            transformedGroupedGroupId = session.GroupLayer(transformedGroupedLeafId, "Transformed parent");
            session.SetLayerOpacity(transformedGroupedGroupId, 0.82);
            session.SetLayerBlendMode(transformedGroupedGroupId, "Multiply");
            session.SetGroupTransform(transformedGroupedGroupId, 4, 3,
                session.Width - 8, session.Height - 6, 8);
            session.EnsureLayerMask(transformedGroupedGroupId);
            session.ReplaceLayerMask(transformedGroupedGroupId,
                GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0,
                    session.Width / 2, session.Height));
            session.SetLayerMaskEnabled(transformedGroupedGroupId, true);
        });
        transformedGroupedLeafWorkspace.Save();
        transformedGroupedLeafWorkspace.Session.SelectLayer(transformedGroupedLeafId);
        transformedGroupedLeafWorkspace.SelectRectangle(new Rect(0, 0,
            transformedGroupedLeafWorkspace.Session.Width / 2,
            transformedGroupedLeafWorkspace.Session.Height));
        TileRaster transformedGroupedExpected = ImageProjectWorkflow.RenderLayerForCopy(
            transformedGroupedLeafWorkspace.Session, transformedGroupedLeafId);
        int transformedGroupedCopyCount = transformedGroupedLeafWorkspace.Session.Layers.Count;
        int transformedGroupedIndex = transformedGroupedLeafWorkspace.Session.Layers.ToList()
            .FindIndex(layer => layer.Id == transformedGroupedGroupId);
        var transformedGroupedLeafWindow = new MainWindow(transformedGroupedLeafWorkspace);
        transformedGroupedLeafWindow.Show(); Dispatcher.UIThread.RunJobs();
        Control<ListBox>(transformedGroupedLeafWindow, "Layers").SelectedItem =
            transformedGroupedLeafWorkspace.Session.Layers.Single(layer => layer.Id == transformedGroupedLeafId);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(transformedGroupedLeafWindow, "LayerViaCopy").IsEffectivelyEnabled,
            "A grouped raster under a transformed parent did not enable Layer via Copy.");
        Click(transformedGroupedLeafWindow, "LayerViaCopy");
        Guid transformedGroupedCopiedId = transformedGroupedLeafWorkspace.Session.ActiveLayerId!.Value;
        FlatLayerInfo transformedGroupedCopied = transformedGroupedLeafWorkspace.Session.Layers
            .Single(layer => layer.Id == transformedGroupedCopiedId);
        int transformedGroupedCopiedIndex = transformedGroupedLeafWorkspace.Session.Layers.ToList()
            .FindIndex(layer => layer.Id == transformedGroupedCopiedId);
        Require(transformedGroupedLeafWorkspace.Session.Layers.Count == transformedGroupedCopyCount + 1 &&
            transformedGroupedCopied.ParentId is null && transformedGroupedCopiedIndex == transformedGroupedIndex + 2,
            "Transformed grouped Layer via Copy did not insert a root sibling after the group subtree.");
        TileRaster transformedGroupedCopiedRaster = transformedGroupedLeafWorkspace.Session
            .GetLayerRaster(transformedGroupedCopiedId);
        for (int y = 0; y < transformedGroupedCopiedRaster.Height; y++)
        for (int x = 0; x < transformedGroupedCopiedRaster.Width; x++)
        {
            byte[] actual = PixelAt(transformedGroupedCopiedRaster, x, y);
            if (x < transformedGroupedCopiedRaster.Width / 2)
                Require(actual.SequenceEqual(PixelAt(transformedGroupedExpected, x, y)),
                    "Transformed grouped Layer via Copy changed pixels inside the selection.");
            else Require(actual.All(channel => channel == 0),
                "Transformed grouped Layer via Copy retained pixels outside the selection.");
        }
        transformedGroupedLeafWorkspace.Save();
        var reopenedTransformedGrouped = ImageProjectWorkflow.OpenEditable(transformedGroupedLeafPath);
        Require(reopenedTransformedGrouped.Layers.Single(layer => layer.Id == transformedGroupedCopiedId).ParentId is null &&
            reopenedTransformedGrouped.Layers.Single(layer => layer.Id == transformedGroupedGroupId).MaskEnabled &&
            CheckEqualNoThrow(transformedGroupedCopiedRaster,
                reopenedTransformedGrouped.GetLayerRaster(transformedGroupedCopiedId)),
            "Saved transformed grouped Layer via Copy did not preserve root placement, source mask or raster.");
        transformedGroupedLeafWindow.Close(); Dispatcher.UIThread.RunJobs();
        var groupMergeWindow = new MainWindow(groupMergeWorkspace);
        groupMergeWindow.Show(); Dispatcher.UIThread.RunJobs();
        Control<ListBox>(groupMergeWindow, "Layers").SelectedItem =
            groupMergeWorkspace.Session.Layers.Single(layer => layer.Id == groupMergeGroupId);
        Dispatcher.UIThread.RunJobs();
        Require(Control<Button>(groupMergeWindow, "MergeLayerDown").IsEffectivelyEnabled,
            "A grouped root with a lower sibling did not enable the group merge command.");
        Click(groupMergeWindow, "MergeLayerDown");
        Require(groupMergeWorkspace.Session.Layers.Count == 1 &&
            groupMergeWorkspace.Session.ActiveLayerId == groupMergeLowerId && groupMergeWorkspace.IsDirty &&
            !groupMergeWorkspace.Session.HasGroups,
            "Group merge did not flatten the selected root subtree into the lower sibling.");
        FlatLayerInfo groupMergedLayer = groupMergeWorkspace.Session.Layers.Single(layer => layer.Id == groupMergeLowerId);
        Require(groupMergedLayer.Opacity == 1 && groupMergedLayer.BlendMode == "Normal" && !groupMergedLayer.HasMask,
            "Masked group merge did not normalize the flattened layer metadata.");
        CheckEqual(groupMergeWorkspace.Preview!, groupMergeBefore);
        Click(groupMergeWindow, "Undo");
        Require(groupMergeWorkspace.Session.Layers.Count == 4 && groupMergeWorkspace.Session.HasGroups &&
            !groupMergeWorkspace.IsDirty,
            "Undo did not restore the grouped merge source tree and saved state.");
        CheckEqual(groupMergeWorkspace.Preview!, groupMergeBefore);
        Click(groupMergeWindow, "Redo");
        Require(groupMergeWorkspace.Session.Layers.Count == 1 && !groupMergeWorkspace.Session.HasGroups,
            "Redo did not restore the flattened group merge.");
        groupMergeWorkspace.Save();
        var reopenedGroupMerge = ImageProjectWorkflow.OpenEditable(groupMergeProject);
        Require(reopenedGroupMerge.Layers.Count == 1 && !reopenedGroupMerge.HasGroups,
            "Saved group merge did not reopen as one flat layer.");
        CheckEqual(ImageProjectWorkflow.RenderFlatNormal(reopenedGroupMerge), groupMergeBefore);
        groupMergeWindow.Close(); Dispatcher.UIThread.RunJobs();

        string grouped = CreateEditableGroupFixture(output, args[0]);
        var groupedWorkspace = new EditorWorkspace();
        groupedWorkspace.Open(grouped);
        var groupedWindow = new MainWindow(groupedWorkspace);
        groupedWindow.Show(); Dispatcher.UIThread.RunJobs();
        var groupedItem = Control<ListBox>(groupedWindow, "Layers").ItemsView!.Cast<FlatLayerInfo>().Single(layer => layer.IsGroup);
        Control<ListBox>(groupedWindow, "Layers").SelectedItem = groupedItem;
        Dispatcher.UIThread.RunJobs();
        foreach (string name in new[] { "AddLayer", "AddTextLayer", "AddBoxTextLayer", "CanvasSize", "ImageSize", "RotateClockwise", "RotateCounterClockwise",
            "DuplicateLayer", "DeleteLayer", "SetClippingMask", "ReleaseClippingMask", "MoveUp", "MoveDown",
            "CopySelection", "CutSelection", "PasteSelection", "LoadAlphaSelection", "BakeLayerTransform" })
            Require(!Control<Button>(groupedWindow, name).IsEffectivelyEnabled, "Grouped project enabled unsupported button: " + name);
        foreach (string name in new[] { "FlipLayerHorizontal", "FlipLayerVertical", "ScaleGroupDown", "ScaleGroupUp",
            "RotateGroupCounterClockwise", "RotateGroupClockwise", "RotateLayerCounterClockwise", "RotateLayerClockwise", "RotateLayerCustom" })
            Require(Control<Button>(groupedWindow, name).IsEffectivelyEnabled,
                "Enabled group mask did not expose group transform button: " + name);
        Require(Control<Button>(groupedWindow, "MoveLayer").IsEffectivelyEnabled,
            "Enabled group mask did not expose group move button.");
        Control<NumericUpDown>(groupedWindow, "LayerMoveX").Value = 12;
        Control<NumericUpDown>(groupedWindow, "LayerMoveY").Value = -7;
        Click(groupedWindow, "MoveLayer");
        Click(groupedWindow, "Undo");
        Click(groupedWindow, "ScaleGroupUp");
        Require(groupedWorkspace.Undo(), "Group scale button did not create an undo step.");
        Click(groupedWindow, "RotateGroupClockwise");
        Require(groupedWorkspace.Undo(), "Group rotation button did not create an undo step.");
        Click(groupedWindow, "RotateLayerClockwise");
        Require(groupedWorkspace.Undo(), "Group free rotation button did not create an undo step.");
        Control<NumericUpDown>(groupedWindow, "LayerRotation").Value = 12.5m;
        Click(groupedWindow, "RotateLayerCustom");
        Require(groupedWorkspace.Undo(), "Group custom rotation button did not create an undo step.");
        Click(groupedWindow, "FlipLayerHorizontal");
        Require(groupedWorkspace.Undo(), "Masked group flip button did not create an undo step.");
        Require(Control<Button>(groupedWindow, "ToggleMask").IsEffectivelyEnabled,
            "Grouped project disabled supported group-mask editing.");
        Click(groupedWindow, "ScaleGroupUp");
        Require(Control<Button>(groupedWindow, "BakeUngroupLayer").IsEffectivelyEnabled &&
            !Control<Button>(groupedWindow, "UngroupLayer").IsEffectivelyEnabled,
            "Transformed group did not expose bake-ungroup protection.");
        Click(groupedWindow, "BakeUngroupLayer");
        Require(!groupedWorkspace.Session!.HasGroups,
            "Bake-ungroup button did not flatten the transformed group.");
        groupedWorkspace.Save();
        groupedWindow.Close(); Dispatcher.UIThread.RunJobs();
        CanvasChecks.Run(source, fixture, output, args.Length == 3);
        NewDocumentChecks.Run(source, output);
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        {
            passed = true, platform = RuntimeInformation.OSDescription, headless = true,
            checks = new[] { "failed open preserves session", "tile preview byte parity", "actual rename/visibility/reorder/undo/redo buttons",
                "set/release clipping relationship buttons", "formal point-text layer creation, editing, save and reopen",
                "dirty title", "PNG/JPEG export", "save-as existing protection", "cancel/save/discard close dialogs", "failed close-save preserves document", "saved layer and pixel roundtrip", "restricted normal-layer merge-down with undo/redo/save/reopen", "restricted non-Normal appearance merge with undo/redo/save/reopen", "restricted non-Normal clipping-stack merge with undo/redo/save/reopen", "transformed flat-layer merge with transform normalization and undo/redo/save/reopen", "restricted contiguous multi-layer merge with undo/redo/save/reopen", "restricted clipping-stack merge with undo/redo/save/reopen", "multi-child clipping-stack merge with undo/redo/save/reopen", "external clipping relationship merge guard", "clipping stack movement with undo/redo/save/reopen", "project-tab undo history isolation",
                "cross-project copy/paste with non-destructive floating selection", "Ctrl+X cut and Ctrl+Shift+Z redo shortcuts with pixel history restore", "cross-project transformed selection paste normalized to document coordinates and save/reopen", "cross-project layer drag copy with mask/appearance/transform/clipping stack/group/target-group and undo/redo/save/reopen", "cross-project flat and grouped discontinuous clipping-stack copy with relationship remapping and save/reopen", "system clipboard bitmap conversion and centered layer paste with undo/redo/save/reopen", "masked clipping visible-result Layer via Copy", "root group visible-result Layer via Copy with selection clipping", "grouped nested clipping-stack visible-result Layer via Copy with chain preservation", "same-parent discontinuous clipping-stack visible-result Layer via Copy with target insertion and save/reopen", "cross-parent grouped clipping visible-result Layer via Copy with external group transform/mask/appearance, root insertion and save/reopen", "cross-parent twelve-level clipping visible-result Layer via Copy with chain preservation, root insertion and save/reopen", "transformed and non-Normal layer-via-copy visible pixels", "layer-list drag reorder with undo/redo/save/reopen", "group merge with root subtree flattening and undo/redo/save/reopen", "grouped-project structure button protection and group-mask availability", "root group/ungroup buttons", "transformed group bake-ungroup" },
            limits = "Headless Avalonia window integration only; native file dialogs, native IME/DPI, Windows packaging and performance not tested."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: Avalonia production window, layer commands, preview pixels and cancel/save/discard protection");
    }

    private static T Control<T>(Window window, string name) where T : Avalonia.Controls.Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static void Click(MainWindow window, string name)
    {
        var button = Control<Button>(window, name);
        Require(button.IsEffectivelyEnabled, "Button is unexpectedly disabled: " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(window);
    }
    private static void RaiseKey(MainWindow window, Key key, KeyModifiers modifiers)
    {
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = window
        });
        Pump(window);
    }
    private static void DialogClick(Window dialog, string label) => dialog.GetVisualDescendants().OfType<Button>()
        .Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Pump(MainWindow window)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Window command did not complete.");
            Thread.Sleep(5);
        } while (window.IsBusy);
        Dispatcher.UIThread.RunJobs();
    }
    private static void CheckPreview(TileRaster raster)
    {
        using var bitmap = RasterBitmap.Create(raster);
        using var frame = bitmap.Lock();
        byte[] rowBytes = new byte[raster.Width * 4];
        for (int row = 0; row * TileRaster.TileSize < raster.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < raster.Width; column++)
        {
            var size = raster.TileDimensions(column, row);
            byte[] expected = raster.ReadTileCopy(column, row);
            for (int y = 0; y < size.Height; y++)
            {
                Marshal.Copy(frame.Address + (row * TileRaster.TileSize + y) * frame.RowBytes + column * TileRaster.TileSize * 4,
                    rowBytes, 0, size.Width * 4);
                Require(rowBytes.AsSpan(0, size.Width * 4).SequenceEqual(expected.AsSpan(y * size.Width * 4, size.Width * 4)),
                    "Preview changed pixel channels, alpha, orientation or tile boundaries.");
            }
        }
    }
    private static void CheckEqual(TileRaster a, TileRaster b)
    {
        Require(a.Width == b.Width && a.Height == b.Height, "Raster dimensions changed.");
        for (int y = 0; y * TileRaster.TileSize < a.Height; y++)
        for (int x = 0; x * TileRaster.TileSize < a.Width; x++)
            Require(a.ReadTileCopy(x, y).SequenceEqual(b.ReadTileCopy(x, y)), "Raster pixels changed.");
    }
    private static bool CheckEqualNoThrow(TileRaster a, TileRaster b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        for (int y = 0; y * TileRaster.TileSize < a.Height; y++)
        for (int x = 0; x * TileRaster.TileSize < a.Width; x++)
            if (!a.ReadTileCopy(x, y).SequenceEqual(b.ReadTileCopy(x, y))) return false;
        return true;
    }
    private static bool CheckEqualNoThrow(GrayTileRaster a, GrayTileRaster b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        for (int y = 0; y * TileRaster.TileSize < a.Height; y++)
        for (int x = 0; x * TileRaster.TileSize < a.Width; x++)
            if (!a.ReadTileCopy(x, y).SequenceEqual(b.ReadTileCopy(x, y))) return false;
        return true;
    }
    private static byte[] PixelAt(TileRaster raster, int x, int y)
    {
        int column = x / TileRaster.TileSize, row = y / TileRaster.TileSize;
        var size = raster.TileDimensions(column, row);
        byte[] tile = raster.ReadTileCopy(column, row);
        return tile.AsSpan(((y % TileRaster.TileSize) * size.Width + x % TileRaster.TileSize) * 4, 4).ToArray();
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static string CreateEditableGroupFixture(string output, string fixtures)
    {
        string source = Path.GetFullPath(Path.Combine(fixtures, "..", "..", "..", "docs", "windows", "fixtures", "F06.comp"));
        string destination = Path.Combine(output, "Grouped.comp");
        Directory.CreateDirectory(Path.Combine(destination, "images"));
        foreach (string asset in Directory.GetFiles(Path.Combine(source, "images")))
            File.Copy(asset, Path.Combine(destination, "images", Path.GetFileName(asset)));
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "manifest.json")))!.AsObject();
        manifest["version"] = 8;
        File.WriteAllText(Path.Combine(destination, "manifest.json"), manifest.ToJsonString());
        return destination;
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (IOException) { return; }
        throw new Exception("Expected file operation rejection.");
    }
}
