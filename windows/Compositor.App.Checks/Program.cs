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
            CheckEqualNoThrow(crossGroupSource.Session.GetLayerMask(crossGroupId)!, crossGroupTarget.Session.GetLayerMask(copiedGroup.Id)!),
            "Cross-project group copy did not preserve group transform or mask metadata.");
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

        var crossLayerUiSource = new EditorWorkspace();
        crossLayerUiSource.Import(fixture, Path.Combine(output, "CrossLayerUiSource.comp"));
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
        Point crossLayerStart = crossLayerItems[0].TranslatePoint(new Point(20, crossLayerItems[0].Bounds.Height / 2), crossLayerWindow)!.Value;
        Button crossLayerTab = Control<Button>(crossLayerWindow, "ProjectTab1");
        Point crossLayerEnd = crossLayerTab.TranslatePoint(new Point(crossLayerTab.Bounds.Width / 2, crossLayerTab.Bounds.Height / 2), crossLayerWindow)!.Value;
        crossLayerWindow.MouseDown(crossLayerStart, MouseButton.Left);
        crossLayerWindow.MouseMove(crossLayerEnd);
        crossLayerWindow.MouseUp(crossLayerEnd, MouseButton.Left);
        Pump(crossLayerWindow);
        Require(crossLayerWindow.ActiveProjectIndex == 1 && crossLayerUiTarget.Session!.Layers.Count == 2,
            "Dragging a layer onto another project tab did not copy it into the target project.");
        Click(crossLayerWindow, "Undo");
        Require(crossLayerUiTarget.Session!.Layers.Count == 1, "Cross-project drag copy did not undo in the target project.");
        Click(crossLayerWindow, "Redo");
        crossLayerUiTarget.Save();
        crossLayerWindow.Close(); Dispatcher.UIThread.RunJobs();

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
        string grouped = CreateEditableGroupFixture(output, args[0]);
        var groupedWorkspace = new EditorWorkspace();
        groupedWorkspace.Open(grouped);
        var groupedWindow = new MainWindow(groupedWorkspace);
        groupedWindow.Show(); Dispatcher.UIThread.RunJobs();
        var groupedItem = Control<ListBox>(groupedWindow, "Layers").ItemsView!.Cast<FlatLayerInfo>().Single(layer => layer.IsGroup);
        Control<ListBox>(groupedWindow, "Layers").SelectedItem = groupedItem;
        Dispatcher.UIThread.RunJobs();
        foreach (string name in new[] { "AddLayer", "CanvasSize", "ImageSize", "RotateClockwise", "RotateCounterClockwise",
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
                "set/release clipping relationship buttons",
                "dirty title", "PNG/JPEG export", "save-as existing protection", "cancel/save/discard close dialogs", "failed close-save preserves document", "saved layer and pixel roundtrip", "restricted normal-layer merge-down with undo/redo/save/reopen", "restricted non-Normal appearance merge with undo/redo/save/reopen", "restricted non-Normal clipping-stack merge with undo/redo/save/reopen", "transformed flat-layer merge with transform normalization and undo/redo/save/reopen", "restricted contiguous multi-layer merge with undo/redo/save/reopen", "restricted clipping-stack merge with undo/redo/save/reopen", "multi-child clipping-stack merge with undo/redo/save/reopen", "external clipping relationship merge guard", "clipping stack movement with undo/redo/save/reopen", "project-tab undo history isolation",
                "cross-project copy/paste with non-destructive floating selection", "cross-project layer drag copy with mask/appearance/transform/clipping stack/group and undo/redo/save/reopen", "system clipboard bitmap conversion and centered layer paste with undo/redo/save/reopen", "masked clipping visible-result Layer via Copy", "transformed and non-Normal layer-via-copy visible pixels", "layer-list drag reorder with undo/redo/save/reopen", "grouped-project structure button protection and group-mask availability", "root group/ungroup buttons", "transformed group bake-ungroup" },
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
