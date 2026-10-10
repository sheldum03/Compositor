using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
using SkiaSharp;

internal static class CanvasChecks
{
    private sealed record BrushCase(string Name, int Diameter, double Opacity, double[] Color, double[][] Points);

    public static void Run(string project, string fixture, string output, bool nativeAvailable)
    {
        string references = Path.Combine(AppContext.BaseDirectory, "fixtures", "soft-brush-reference.zip");
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(references, ".json")));
        Require(Hash(fixture) == metadata.RootElement.GetProperty("inputSha256").GetString(), "Brush reference input identity changed.");
        Require(Hash(references) == metadata.RootElement.GetProperty("zipSha256").GetString(), "Brush reference archive identity changed.");
        using var zip = ZipFile.OpenRead(references);
        using var casesStream = zip.GetEntry("cases.json")!.Open();
        var cases = JsonSerializer.Deserialize<BrushCase[]>(casesStream)!;
        var original = ImageCodec.Load(fixture);
        Require(original.Width == metadata.RootElement.GetProperty("width").GetInt32() &&
            original.Height == metadata.RootElement.GetProperty("height").GetInt32(), "Brush reference dimensions changed.");
        var persistedLayerViaCopy = new EditorWorkspace();
        string persistedLayerViaCopyPath = Path.Combine(output, "LayerViaCopy.comp");
        persistedLayerViaCopy.Open(project);
        Guid persistedSourceId = persistedLayerViaCopy.Session!.ActiveLayerId!.Value;
        int persistedBaselineCount = persistedLayerViaCopy.Session.Layers.Count;
        TileRaster persistedSource = persistedLayerViaCopy.Session.GetLayerRaster(persistedSourceId);
        persistedLayerViaCopy.SelectRectangle(new Rect(30, 30, 120, 90));
        persistedLayerViaCopy.FeatherSelection(3);
        GrayTileRaster persistedSelection = persistedLayerViaCopy.Selection!;
        persistedLayerViaCopy.LayerViaCopy();
        Guid persistedCopyId = persistedLayerViaCopy.Session.ActiveLayerId!.Value;
        persistedLayerViaCopy.SaveAs(persistedLayerViaCopyPath);
        var persistedReopened = ImageProjectWorkflow.OpenEditable(persistedLayerViaCopyPath);
        bool sawFeatheredPixel = false;
        for (int y = 0; y < persistedSelection.Height && !sawFeatheredPixel; y++)
        for (int x = 0; x < persistedSelection.Width && !sawFeatheredPixel; x++)
        {
            byte coverage = MaskPixel(persistedSelection, x, y);
            if (coverage is > 0 and < 255 && Pixel(persistedSource, x, y)[3] > 0)
            {
                byte expectedAlpha = (byte)((Pixel(persistedSource, x, y)[3] * coverage + 127) / 255);
                sawFeatheredPixel = Pixel(persistedReopened.GetLayerRaster(persistedCopyId), x, y)[3] == expectedAlpha &&
                    expectedAlpha < Pixel(persistedSource, x, y)[3];
            }
        }
        Require(persistedReopened.Layers.Count == persistedBaselineCount + 1 &&
            persistedReopened.ActiveLayerId == persistedCopyId &&
            Pixel(persistedReopened.GetLayerRaster(persistedCopyId), 50, 50).SequenceEqual(Pixel(persistedSource, 50, 50)) &&
            Pixel(persistedReopened.GetLayerRaster(persistedCopyId), 10, 10).SequenceEqual(new byte[4]) &&
            sawFeatheredPixel,
            "Layer via Copy did not survive save and reopen.");
        var visibleResultCopy = new EditorWorkspace();
        visibleResultCopy.Open(project);
        Guid visibleSourceId = visibleResultCopy.Session!.ActiveLayerId!.Value;
        TileRaster visibleSourceRaster = visibleResultCopy.Session.GetLayerRaster(visibleSourceId);
        Guid visibleTargetId = visibleResultCopy.Session.AddBlankLayer("Masked target", 1);
        int visibleBaselineCount = visibleResultCopy.Session.Layers.Count;
        visibleResultCopy.Edit(session => session.ReplaceLayerRaster(visibleTargetId, visibleSourceRaster));
        visibleResultCopy.AddActiveLayerMask();
        visibleResultCopy.SelectRectangle(new Rect(0, 0, visibleResultCopy.Session.Width / 2, visibleResultCopy.Session.Height));
        visibleResultCopy.ApplySelectionToActiveLayerMask(reveal: false);
        visibleResultCopy.SetActiveLayerClipping(enabled: true);
        GrayTileRaster visibleMask = visibleResultCopy.Session.GetLayerMask(visibleTargetId)!;
        TileRaster expectedVisibleResult = RasterCompositor.ApplyAlphaMask(
            RasterCompositor.ApplyMask(visibleSourceRaster, visibleMask), visibleSourceRaster);
        visibleResultCopy.SelectAll();
        Require(visibleResultCopy.CanLayerViaCopy, "Layer via Copy did not allow a supported masked clipping layer.");
        visibleResultCopy.LayerViaCopy();
        TileRaster visibleCopyRaster = visibleResultCopy.Session.GetLayerRaster(visibleResultCopy.Session.ActiveLayerId!.Value);
        Require(Bytes(visibleCopyRaster).SequenceEqual(Bytes(expectedVisibleResult)),
            "Layer via Copy did not preserve the masked clipping visible result.");
        bool visibleCopyUndone = visibleResultCopy.Undo();
        Require(visibleCopyUndone && visibleResultCopy.Session.Layers.Count == visibleBaselineCount,
            $"Undo did not remove the masked clipping Layer via Copy result: undone={visibleCopyUndone}, layers={visibleResultCopy.Session.Layers.Count}.");
        var transformedCopy = new EditorWorkspace();
        transformedCopy.Open(project);
        Guid transformedSourceId = transformedCopy.Session!.ActiveLayerId!.Value;
        int transformedBaselineCount = transformedCopy.Session.Layers.Count;
        transformedCopy.MoveActiveLayer(18, 14);
        transformedCopy.SelectAll();
        Require(transformedCopy.CanLayerViaCopy,
            "Layer via Copy did not allow a Normal layer with a non-destructive transform.");
        TileRaster expectedTransformedCopy = ImageProjectWorkflow.RenderLayerForCopy(transformedCopy.Session, transformedSourceId);
        transformedCopy.LayerViaCopy();
        TileRaster actualTransformedCopy = transformedCopy.Session.GetLayerRaster(transformedCopy.Session.ActiveLayerId!.Value);
        Require(Bytes(actualTransformedCopy).SequenceEqual(Bytes(expectedTransformedCopy)) &&
            transformedCopy.Session.Layers.Count == transformedBaselineCount + 1 && !transformedCopy.HasSelection,
            "Layer via Copy did not bake the transformed visible result into the new layer.");
        var nonNormalCopy = new EditorWorkspace();
        nonNormalCopy.Open(project);
        Guid nonNormalSourceId = nonNormalCopy.Session!.ActiveLayerId!.Value;
        int nonNormalBaselineCount = nonNormalCopy.Session.Layers.Count;
        TileRaster nonNormalSource = nonNormalCopy.Session.GetLayerRaster(nonNormalSourceId);
        nonNormalCopy.Edit(session =>
        {
            session.SetLayerOpacity(nonNormalSourceId, 0.37);
            session.SetLayerBlendMode(nonNormalSourceId, "Multiply");
        });
        nonNormalCopy.SelectAll();
        Require(nonNormalCopy.CanLayerViaCopy,
            "Layer via Copy incorrectly disabled a supported non-Normal raster layer.");
        nonNormalCopy.LayerViaCopy();
        TileRaster actualNonNormalCopy = nonNormalCopy.Session.GetLayerRaster(nonNormalCopy.Session.ActiveLayerId!.Value);
        Require(Bytes(actualNonNormalCopy).SequenceEqual(Bytes(nonNormalSource)) &&
            nonNormalCopy.Session.Layers.Count == nonNormalBaselineCount + 1 && !nonNormalCopy.HasSelection,
            "Layer via Copy baked source opacity or blend mode into the new raster layer.");
        var hardCanvas = new TileRaster(21, 21);
        var hardStroke = new SoftBrushStroke(hardCanvas, new SoftBrushSettings(9, 1, [1, 0, 0], 1));
        hardStroke.Append(new BrushPoint(10.5, 10.5));
        TileRaster hardResult = hardStroke.Commit();
        byte[] hardTile = hardResult.ReadTileCopy(0, 0);
        Require(hardTile[(10 * 21 + 10) * 4 + 3] == 255 && hardTile[3] == 0,
            "Hard brush did not produce a hard circular edge.");
        var lowPressure = new SoftBrushStroke(new TileRaster(21, 21), new SoftBrushSettings(9, 1, [1, 0, 0], 1));
        lowPressure.Append(new BrushPoint(10.5, 10.5, 0.25));
        byte lowAlpha = lowPressure.Commit().ReadTileCopy(0, 0)[(10 * 21 + 10) * 4 + 3];
        var highPressure = new SoftBrushStroke(new TileRaster(21, 21), new SoftBrushSettings(9, 1, [1, 0, 0], 1));
        highPressure.Append(new BrushPoint(10.5, 10.5, 0.75));
        byte highAlpha = highPressure.Commit().ReadTileCopy(0, 0)[(10 * 21 + 10) * 4 + 3];
        Require(lowAlpha > 0 && highAlpha > lowAlpha && highAlpha < 255,
            "Brush pressure did not scale single-dab coverage.");
        var selectionHistoryWorkspace = new EditorWorkspace();
        selectionHistoryWorkspace.Import(fixture, Path.Combine(output, "SelectionHistory.comp"));
        selectionHistoryWorkspace.SelectRectangle(new Rect(30, 30, 120, 90));
        selectionHistoryWorkspace.SelectRectangle(new Rect(10, 10, 20, 20));
        Require(selectionHistoryWorkspace.Undo() && selectionHistoryWorkspace.SelectionBounds is { X: 30, Y: 30 } && !selectionHistoryWorkspace.IsDirty,
            "Undo did not restore the previous session selection without a pixel transaction.");
        Require(selectionHistoryWorkspace.Redo() && selectionHistoryWorkspace.SelectionBounds is { X: 10, Y: 10 } && !selectionHistoryWorkspace.IsDirty,
            "Redo did not restore the next session selection without a pixel transaction.");
        selectionHistoryWorkspace.SelectRectangle(new Rect(30, 30, 120, 90));
        GrayTileRaster selectionHistoryBeforeFeather = selectionHistoryWorkspace.Selection!;
        selectionHistoryWorkspace.FeatherSelection(3);
        GrayTileRaster selectionHistoryFeathered = selectionHistoryWorkspace.Selection!;
        Require(!SameCoverage(selectionHistoryBeforeFeather, selectionHistoryFeathered) &&
            HasPartialCoverage(selectionHistoryFeathered) && !selectionHistoryWorkspace.IsDirty,
            "Selection feather did not create fractional coverage in the session-only path.");
        Require(selectionHistoryWorkspace.Undo() && SameCoverage(selectionHistoryBeforeFeather, selectionHistoryWorkspace.Selection!) &&
            !selectionHistoryWorkspace.IsDirty && selectionHistoryWorkspace.Redo() &&
            SameCoverage(selectionHistoryFeathered, selectionHistoryWorkspace.Selection!),
            "Selection feather did not participate in session-only Undo/Redo history.");
        foreach (var test in cases)
        {
            var stroke = new SoftBrushStroke(original, new SoftBrushSettings(test.Diameter, test.Opacity, test.Color));
            foreach (var point in test.Points) stroke.Append(new BrushPoint(point[0], point[1]));
            CheckGolden(stroke.Snapshot(), zip, test.Name + "-preview.rgba");
            CheckGolden(stroke.Commit(), zip, test.Name + "-final.rgba");
        }

        var workspace = new EditorWorkspace();
        workspace.Open(project);
        workspace.SaveAs(Path.Combine(output, "Brush.comp"));
        Guid layerId = workspace.Session!.Layers[^1].Id;
        workspace.Edit(s => s.SetLayerVisible(layerId, true)); workspace.Save();
        TileRaster baseline = workspace.Session.GetLayerRaster(layerId);
        byte[] baselineBytes = Bytes(baseline);
        Guid otherId = workspace.Session.Layers[0].Id;
        byte[] otherBytes = Bytes(workspace.Session.GetLayerRaster(otherId));
        var window = new MainWindow(workspace);
        window.Show(); Dispatcher.UIThread.RunJobs();
        var canvas = Find<CanvasView>(window, "Canvas");
        IPointer? pointer = null;
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Tunnel);
        Find<NumericUpDown>(window, "BrushDiameter").Value = 47;
        Find<NumericUpDown>(window, "BrushOpacity").Value = 40;
        Find<ComboBox>(window, "BrushType").SelectedIndex = 1;
        Require(Find<ComboBox>(window, "BrushType").IsEffectivelyEnabled, "Brush type control is unavailable.");
        Find<ComboBox>(window, "BrushType").SelectedIndex = 0;
        Find<ComboBox>(window, "BrushColor").SelectedIndex = 3;
        Find<CheckBox>(window, "Eyedropper").IsChecked = true;
        Require(canvas.EyedropperEnabled && !canvas.PaintEnabled,
            "Eyedropper mode did not disable painting or enable canvas sampling.");
        window.MouseDown(DocumentPoint(new Point(100, 100)), MouseButton.Left);
        window.MouseUp(DocumentPoint(new Point(100, 100)), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Require(Find<ComboBox>(window, "BrushColor").SelectedIndex == 4 && !workspace.IsDirty,
            "Eyedropper did not select a sampled palette color without changing the document.");
        Find<CheckBox>(window, "Eyedropper").IsChecked = false;
        Find<CheckBox>(window, "Paint").IsChecked = true;
        Point anchor = new(100.25, 110.25);
        Point viewAnchor = canvas.Viewport.ToView(anchor);
        window.MouseWheel(WindowPoint(viewAnchor), new Vector(0, 2));
        Dispatcher.UIThread.RunJobs();
        Require(Near(canvas.Viewport.ToDocument(viewAnchor), anchor), "Wheel zoom moved the document anchor.");
        Vector offset = canvas.Viewport.Offset;
        Point panStart = new(120, 130), panEnd = panStart + new Vector(17, -11);
        window.MouseDown(WindowPoint(panStart), MouseButton.Middle);
        window.MouseMove(WindowPoint(panEnd)); window.MouseUp(WindowPoint(panEnd), MouseButton.Middle);
        Dispatcher.UIThread.RunJobs();
        Require((canvas.Viewport.Offset - offset - new Vector(17, -11)).Length < 1e-8 && !workspace.IsDirty,
            "Pan changed document state or failed to move the viewport.");
        offset = canvas.Viewport.Offset;
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.MouseDown(WindowPoint(panStart), MouseButton.Left);
        window.MouseMove(WindowPoint(panEnd)); window.MouseUp(WindowPoint(panEnd), MouseButton.Left);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Require((canvas.Viewport.Offset - offset - new Vector(17, -11)).Length < 1e-8 && !workspace.HasActiveStroke && !workspace.IsDirty,
            "Space plus left-button pan painted pixels or failed to move the viewport.");
        Click("ActualSize");
        Require(Math.Abs(canvas.Viewport.Scale - 1) < 1e-8 && !workspace.IsDirty,
            "100% view changed the document or failed to set exact scale.");
        canvas.Viewport.Zoom(new Point(560, 380), 6);
        canvas.InvalidateVisual(); Dispatcher.UIThread.RunJobs();
        var gridOff = RenderWindow(window, Path.Combine(output, "pixel-grid-off.png"));
        Find<CheckBox>(window, "PixelGrid").IsChecked = true; Dispatcher.UIThread.RunJobs();
        var gridOn = RenderWindow(window, Path.Combine(output, "pixel-grid-on.png"));
        Require(!SamePixels(gridOff, gridOn) && !workspace.IsDirty,
            "Pixel grid did not change the zoomed canvas view or changed document state.");
        Find<CheckBox>(window, "PixelGrid").IsChecked = false; Dispatcher.UIThread.RunJobs();
        canvas.Fit();
        byte[] selectionBaseline = Bytes(workspace.Session.GetLayerRaster(layerId));
        Find<CheckBox>(window, "RectSelect").IsChecked = true;
        Require(canvas.SelectionEnabled && !canvas.PaintEnabled, "Rectangle selection mode did not activate.");
        int selectionFinished = 0, selectionCanceled = 0; Rect? selectedRect = null;
        canvas.SelectionFinished += rect => { selectionFinished++; selectedRect = rect; };
        canvas.SelectionCanceled += () => selectionCanceled++;
        window.MouseDown(DocumentPoint(new Point(30, 30)), MouseButton.Left);
        Require(canvas.IsSelecting, $"Rectangle selection did not capture pointer: selection={canvas.SelectionEnabled}, paint={canvas.PaintEnabled}, view={DocumentPoint(new Point(30, 30))}, bounds={canvas.Bounds}.");
        window.MouseMove(DocumentPoint(new Point(150, 120)));
        window.MouseUp(DocumentPoint(new Point(150, 120)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(!canvas.IsSelecting && canvas.SelectionRect is { Width: > 0, Height: > 0 },
            $"Rectangle selection did not release a rectangle: selecting={canvas.IsSelecting}, rect={canvas.SelectionRect}, finished={selectionFinished}, canceled={selectionCanceled}, eventRect={selectedRect}.");
        Require(workspace.HasSelection && workspace.SelectedPixels == 120 * 90 && !workspace.IsDirty,
            $"Rectangle selection did not create the expected in-memory mask without history: has={workspace.HasSelection}, pixels={workspace.SelectedPixels}, bounds={workspace.SelectionBounds}, dirty={workspace.IsDirty}.");
        Find<NumericUpDown>(window, "SelectionFeatherRadius").Value = 3;
        Require(Find<Button>(window, "FeatherSelection").IsEffectivelyEnabled,
            "Selection feather controls were not enabled for a non-empty selection.");
        Click("FeatherSelection");
        GrayTileRaster featheredSelection = workspace.Selection!;
        Require(HasPartialCoverage(featheredSelection) && !workspace.IsDirty,
            "Selection feather did not create fractional edge coverage without changing document history.");
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        window.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Require(workspace.SelectionBounds is { X: 0, Y: 0 } bounds && bounds.Width == workspace.Session.Width && bounds.Height == workspace.Session.Height &&
            workspace.SelectedPixels == (long)workspace.Session.Width * workspace.Session.Height && !workspace.IsDirty,
            "Ctrl+A did not cover the full canvas without changing document history.");
        window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control | RawInputModifiers.Shift);
        window.KeyReleaseQwerty(PhysicalKey.I, RawInputModifiers.Control | RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Require(!workspace.HasSelection && !workspace.IsDirty,
            "Ctrl+Shift+I did not invert a full selection to empty without changing document history.");
        Find<CheckBox>(window, "RectSelect").IsChecked = true;
        window.MouseDown(DocumentPoint(new Point(30, 30)), MouseButton.Left);
        window.MouseMove(DocumentPoint(new Point(150, 120)));
        window.MouseUp(DocumentPoint(new Point(150, 120)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Guid layerViaCopySourceId = workspace.Session!.ActiveLayerId!.Value;
        int layerViaCopyBaselineCount = workspace.Session.Layers.Count;
        TileRaster layerViaCopySource = workspace.Session.GetLayerRaster(layerViaCopySourceId);
        Require(Find<Button>(window, "LayerViaCopy").IsEffectivelyEnabled,
            "Layer via Copy was not enabled for a flat, untransformed layer selection.");
        Click("LayerViaCopy");
        Guid layerViaCopyId = workspace.Session.ActiveLayerId!.Value;
        TileRaster layerViaCopyRaster = workspace.Session.GetLayerRaster(layerViaCopyId);
        Require(layerViaCopyId != layerViaCopySourceId, "Layer via Copy did not activate a new layer.");
        Require(workspace.Session.Layers.Count == layerViaCopyBaselineCount + 1,
            $"Layer via Copy did not add one layer: active={workspace.Session.ActiveLayerId}, source={layerViaCopySourceId}, count={workspace.Session.Layers.Count}, baseline={layerViaCopyBaselineCount}, can={workspace.CanLayerViaCopy}.");
        Require(!workspace.HasSelection && workspace.IsDirty, "Layer via Copy did not clear the session selection or create history.");
        Require(Pixel(layerViaCopyRaster, 50, 50).SequenceEqual(Pixel(layerViaCopySource, 50, 50)),
            "Layer via Copy did not retain a selected pixel.");
        Require(Pixel(layerViaCopyRaster, 10, 10).SequenceEqual(new byte[4]),
            "Layer via Copy retained a pixel outside the selection.");
        Click("Undo");
        Require(workspace.Session.Layers.Count == layerViaCopyBaselineCount && !workspace.IsDirty,
            "Undo did not remove the Layer via Copy transaction or restore the save point.");
        Click("Redo");
        Require(workspace.Session.Layers.Count == layerViaCopyBaselineCount + 1 && workspace.IsDirty,
            "Redo did not restore the Layer via Copy transaction.");
        Click("Undo");
        window.MouseDown(DocumentPoint(new Point(30, 30)), MouseButton.Left);
        window.MouseMove(DocumentPoint(new Point(150, 120)));
        window.MouseUp(DocumentPoint(new Point(150, 120)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Click("CopySelection");
        Require(workspace.HasClipboard && !workspace.IsDirty, "Copy selection changed document history or did not retain a clipboard snapshot.");
        Click("CopyMergedSelection");
        Require(workspace.HasClipboard && !workspace.IsDirty, "Copy merged selection changed document history or did not retain a clipboard snapshot.");
        Click("CutSelection");
        Require(workspace.IsDirty && !Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(selectionBaseline),
            "Cut selection did not create a history step or clear selected pixels.");
        Click("Undo");
        Require(!workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(selectionBaseline),
            "Undo did not restore the cut selection baseline.");
        Click("PasteSelection");
        Require(!workspace.IsDirty && workspace.HasFloatingSelection &&
            Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(selectionBaseline),
            "Paste selection did not create a non-destructive floating selection.");
        Click("CancelFloatingSelection");
        Require(!workspace.IsDirty && !workspace.HasFloatingSelection &&
            Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(selectionBaseline),
            "Canceling a floating selection changed the document.");
        Click("PasteSelection");
        workspace.MoveSelection(20, 10);
        Require(!workspace.IsDirty && workspace.HasFloatingSelection && workspace.SelectionBounds is { X: 50, Y: 40 },
            "Moving a floating selection changed the document before commit.");
        Click("CommitFloatingSelection");
        Require(workspace.IsDirty && !Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(selectionBaseline),
            "Committing a moved floating selection did not create one pixel history step.");
        Click("Undo");
        Require(!workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(selectionBaseline),
            "Undo did not remove the committed floating selection history.");
        workspace.SelectRectangle(new Rect(30, 30, 120, 90));
        canvas.SetSelectionRect(workspace.SelectionBounds);
        canvas.SetSelectionOutline(workspace.SelectionOutline);
        Find<CheckBox>(window, "MoveSelection").IsChecked = true;
        window.MouseDown(DocumentPoint(new Point(80, 80)), MouseButton.Left);
        window.MouseMove(DocumentPoint(new Point(100, 90)));
        window.MouseUp(DocumentPoint(new Point(100, 90)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(workspace.IsDirty && workspace.SelectionBounds is { X: 50, Y: 40 },
            $"Selection move did not shift the mask and pixels: dirty={workspace.IsDirty}, bounds={workspace.SelectionBounds}.");
        Click("Undo");
        Require(!workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(selectionBaseline),
            "Undo did not restore pixels after selection move.");
        Require(workspace.SelectionBounds is { X: 30, Y: 30 },
            $"Undo did not restore the pre-move selection bounds: {workspace.SelectionBounds}.");
        Click("Redo");
        Require(workspace.IsDirty && workspace.SelectionBounds is { X: 50, Y: 40 },
            "Redo did not restore the moved selection bounds.");
        Click("Undo");
        Require(!workspace.IsDirty && workspace.SelectionBounds is { X: 30, Y: 30 },
            "Second undo did not restore the original selection state.");
        workspace.SelectRectangle(new Rect(30, 30, 120, 90));
        canvas.SetSelectionRect(workspace.SelectionBounds);
        window.MouseDown(DocumentPoint(new Point(100, 90)), MouseButton.Left);
        window.MouseMove(DocumentPoint(new Point(80, 80)));
        window.MouseUp(DocumentPoint(new Point(80, 80)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(workspace.IsDirty && workspace.SelectionBounds is { X: 10, Y: 20 },
            $"Reverse selection move did not preserve the drag origin: dirty={workspace.IsDirty}, bounds={workspace.SelectionBounds}.");
        Click("Undo");
        Require(!workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(selectionBaseline),
            "Undo did not restore pixels after reverse selection move.");
        Click("LoadAlphaSelection");
        Require(workspace.HasSelection && workspace.SelectedPixels > 0 && !workspace.IsDirty,
            "Loading a layer alpha did not create a non-dirty selection.");
        workspace.SelectRectangle(new Rect(30, 30, 120, 90));
        canvas.SetSelectionRect(workspace.SelectionBounds);
        Find<CheckBox>(window, "MoveSelection").IsChecked = false;
        Find<CheckBox>(window, "RectSelect").IsChecked = false;
        Find<CheckBox>(window, "Paint").IsChecked = true;
        window.MouseDown(DocumentPoint(new Point(220, 220)), MouseButton.Left);
        window.MouseUp(DocumentPoint(new Point(220, 220)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(!workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(selectionBaseline),
            "Painting outside the rectangle changed pixels or document state.");
        window.MouseDown(DocumentPoint(new Point(80, 80)), MouseButton.Left);
        window.MouseUp(DocumentPoint(new Point(80, 80)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(workspace.IsDirty && !Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(selectionBaseline),
            "Painting inside the rectangle did not change selected pixels.");
        Click("Undo");
        Require(!workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(selectionBaseline),
            "Undo did not restore the selected painting baseline.");
        Find<ComboBox>(window, "SelectionOperation").SelectedItem = "加选";
        Find<CheckBox>(window, "RectSelect").IsChecked = true;
        window.MouseDown(DocumentPoint(new Point(120, 80)), MouseButton.Left);
        window.MouseMove(DocumentPoint(new Point(200, 160)));
        window.MouseUp(DocumentPoint(new Point(200, 160)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(workspace.SelectedPixels == 16000, $"Add selection produced {workspace.SelectedPixels} pixels instead of 16000.");
        Find<ComboBox>(window, "SelectionOperation").SelectedItem = "减选";
        window.MouseDown(DocumentPoint(new Point(50, 50)), MouseButton.Left);
        window.MouseMove(DocumentPoint(new Point(100, 90)));
        window.MouseUp(DocumentPoint(new Point(100, 90)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(workspace.SelectedPixels == 14000, $"Subtract selection produced {workspace.SelectedPixels} pixels instead of 14000.");
        Find<ComboBox>(window, "SelectionShape").SelectedItem = "椭圆";
        Find<ComboBox>(window, "SelectionOperation").SelectedItem = "替换";
        window.MouseDown(DocumentPoint(new Point(60, 40)), MouseButton.Left);
        window.MouseMove(DocumentPoint(new Point(180, 140)));
        window.MouseUp(DocumentPoint(new Point(180, 140)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(workspace.SelectedPixels > 0 && workspace.SelectedPixels < 120 * 100,
            $"Ellipse selection produced an invalid coverage count: {workspace.SelectedPixels}.");
        Find<ComboBox>(window, "SelectionShape").SelectedItem = "套索";
        window.MouseDown(DocumentPoint(new Point(60, 60)), MouseButton.Left);
        window.MouseMove(DocumentPoint(new Point(180, 70)));
        window.MouseMove(DocumentPoint(new Point(140, 160)));
        window.MouseMove(DocumentPoint(new Point(60, 60)));
        window.MouseUp(DocumentPoint(new Point(60, 60)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(workspace.SelectedPixels > 0 && workspace.SelectedPixels < 120 * 100,
            $"Lasso selection produced an invalid coverage count: {workspace.SelectedPixels}.");
        if (nativeAvailable)
        {
            Find<ComboBox>(window, "SelectionShape").SelectedItem = "魔棒";
            Find<ComboBox>(window, "SelectionOperation").SelectedItem = "替换";
            Find<ComboBox>(window, "WandRadius").SelectedIndex = 1;
            Find<NumericUpDown>(window, "WandTolerance").Value = 255;
            Require(Find<ComboBox>(window, "WandRadius").IsEffectivelyEnabled,
                "Magic wand radius control did not activate with the magic wand tool.");
            window.MouseDown(DocumentPoint(new Point(128, 128)), MouseButton.Left);
            window.MouseUp(DocumentPoint(new Point(128, 128)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Require(workspace.HasSelection && workspace.SelectedPixels > 0 && !workspace.IsDirty,
                $"Native magic wand did not create a non-dirty selection: has={workspace.HasSelection}, pixels={workspace.SelectedPixels}.");
            Require(workspace.SelectionOutline is { LoopLengths.Length: > 0 },
                "Native magic wand did not expose a traced selection outline.");
        }
        Click("ClearSelection");
        Require(!workspace.HasSelection && !workspace.IsDirty, "Clear selection changed history or retained a mask.");
        Find<CheckBox>(window, "RectSelect").IsChecked = false;
        Find<CheckBox>(window, "Paint").IsChecked = true;
        var points = cases.Single(test => test.Name == "CrossTile").Points.Select(p => new Point(p[0], p[1])).ToArray();
        window.MouseDown(DocumentPoint(points[0]), MouseButton.Left);
        Require(workspace.HasActiveStroke && !workspace.IsDirty, "Pointer down committed a history step.");
        bool saveRejected = false;
        try { workspace.Save(); } catch (InvalidOperationException) { saveRejected = true; }
        Require(saveRejected, "Save accepted a provisional stroke.");
        window.MouseMove(DocumentPoint(points[1]));
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.MouseUp(DocumentPoint(points[1]), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(!workspace.HasActiveStroke && !workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(baselineBytes),
            "Escape did not cancel the stroke without history or pixel changes.");

        window.MouseDown(DocumentPoint(points[0]), MouseButton.Left);
        window.MouseMove(DocumentPoint(points[1]));
        pointer!.Capture(null); Dispatcher.UIThread.RunJobs();
        window.MouseUp(DocumentPoint(points[1]), MouseButton.Left);
        Require(!workspace.HasActiveStroke && !workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(baselineBytes),
            "Lost pointer capture committed or retained a provisional stroke.");

        window.MouseDown(DocumentPoint(points[0]), MouseButton.Left);
        foreach (var point in points.Skip(1)) window.MouseMove(DocumentPoint(point));
        window.MouseUp(DocumentPoint(points[^1]), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(!workspace.HasActiveStroke && workspace.IsDirty, "Pointer release did not commit the stroke.");
        CheckGolden(workspace.Session.GetLayerRaster(layerId), zip, "CrossTile-final.rgba");
        Require(Bytes(workspace.Session.GetLayerRaster(otherId)).SequenceEqual(otherBytes), "Painting changed the other layer.");
        Require(Bytes(baseline).SequenceEqual(baselineBytes), "Painting mutated an older pixel snapshot.");
        Click("Undo");
        Require(!workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(baselineBytes), "One undo did not remove the whole stroke.");
        Click("Redo"); CheckGolden(workspace.Session.GetLayerRaster(layerId), zip, "CrossTile-final.rgba");
        Find<NumericUpDown>(window, "BrushDiameter").Value = 9;
        Find<NumericUpDown>(window, "BrushOpacity").Value = 100;
        Find<ComboBox>(window, "BrushType").SelectedIndex = 1;
        Find<ComboBox>(window, "BrushColor").SelectedIndex = 0;
        TileRaster lineBefore = workspace.Session.GetLayerRaster(layerId);
        window.KeyPressQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Shift);
        window.MouseDown(DocumentPoint(new Point(50, 50)), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseMove(DocumentPoint(new Point(150, 90)), RawInputModifiers.Shift);
        window.MouseUp(DocumentPoint(new Point(150, 90)), MouseButton.Left, RawInputModifiers.Shift);
        window.KeyReleaseQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Shift); Dispatcher.UIThread.RunJobs();
        Require(workspace.IsDirty && IsHorizontalStroke(lineBefore, workspace.Session.GetLayerRaster(layerId), 50, 8),
            "Shift-drag did not commit a constrained straight brush line.");
        Click("Undo");
        byte[] flipBaseline = Bytes(workspace.Session.GetLayerRaster(layerId));
        TileRaster previewBeforeFlip = workspace.Preview!;
        Click("FlipLayerHorizontal");
        Require(workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(flipBaseline) &&
            !SamePixels(previewBeforeFlip, workspace.Preview!),
            "Horizontal layer flip did not create a non-destructive transformed preview.");
        Click("Undo");
        Require(workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(flipBaseline) &&
            SamePixels(previewBeforeFlip, workspace.Preview!), "Undo did not restore the pre-flip preview.");
        Click("Redo");
        Click("Undo");
        TileRaster previewBeforeVertical = workspace.Preview!;
        Click("FlipLayerVertical");
        Require(workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(flipBaseline) &&
            !SamePixels(previewBeforeVertical, workspace.Preview!),
            "Vertical layer flip did not create a non-destructive transformed preview.");
        Click("Undo");
        Require(workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(flipBaseline) &&
            SamePixels(previewBeforeVertical, workspace.Preview!), "Undo did not restore the pre-vertical-flip preview.");
        Find<NumericUpDown>(window, "LayerMoveX").Value = 7;
        Find<NumericUpDown>(window, "LayerMoveY").Value = -3;
        TileRaster previewBeforeMove = workspace.Preview!;
        Click("MoveLayer");
        Require(workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(flipBaseline) &&
            !SamePixels(previewBeforeMove, workspace.Preview!),
            "Moving the active layer did not create a non-destructive transformed preview.");
        Click("Undo");
        Require(workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(flipBaseline) &&
            SamePixels(previewBeforeMove, workspace.Preview!), "Undo did not restore the pre-move preview.");
        TileRaster previewBeforeScale = workspace.Preview!;
        Click("ScaleGroupDown");
        TileRaster expectedAfterScale = ImageProjectWorkflow.RenderFlatNormal(workspace.Session!);
        int scalePreviewDiff = MaxDifference(expectedAfterScale, workspace.Preview!);
        Require(workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(flipBaseline) &&
            !SamePixels(previewBeforeScale, workspace.Preview!) && Find<CheckBox>(window, "Paint").IsEffectivelyEnabled &&
            Find<CheckBox>(window, "RectSelect").IsEffectivelyEnabled &&
            Find<Button>(window, "CopySelection").IsEffectivelyEnabled &&
            !Find<Button>(window, "PasteSelection").IsEffectivelyEnabled && scalePreviewDiff == 0,
            $"Flat layer scale did not use a non-destructive transform or expose mapped selection tools (previewDiff={scalePreviewDiff}).");
        Require(Find<Button>(window, "BakeLayerTransform").IsEffectivelyEnabled,
            "Transformed flat layer did not expose the explicit bake command.");
        Click("BakeLayerTransform");
        bool bakedPaintEnabled = Find<CheckBox>(window, "Paint").IsEffectivelyEnabled;
        bool bakedPreviewMatches = SamePixels(expectedAfterScale, workspace.Preview!);
        int bakedPreviewDiff = MaxDifference(expectedAfterScale, workspace.Preview!);
        Require(workspace.IsDirty && Find<CheckBox>(window, "Paint").IsEffectivelyEnabled &&
            SamePixels(expectedAfterScale, workspace.Preview!),
            $"Baking a flat layer transform did not preserve the preview or restore pixel editing (dirty={workspace.IsDirty}, paint={bakedPaintEnabled}, preview={bakedPreviewMatches}, maxDiff={bakedPreviewDiff}).");
        Click("Undo");
        Click("Undo");
        TileRaster previewBeforeRotate = workspace.Preview!;
        Click("RotateGroupClockwise");
        Require(workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(flipBaseline) &&
            !SamePixels(previewBeforeRotate, workspace.Preview!) && Find<CheckBox>(window, "Paint").IsEffectivelyEnabled,
            "Flat layer rotation did not use a non-destructive transform or enable mapped pixel tools.");
        Click("Undo");
        TileRaster previewBeforeFreeRotate = workspace.Preview!;
        Click("RotateLayerClockwise");
        Require(workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(flipBaseline) &&
            !SamePixels(previewBeforeFreeRotate, workspace.Preview!) && Find<CheckBox>(window, "Paint").IsEffectivelyEnabled,
            "Flat layer free rotation did not use a non-destructive transform or enable mapped pixel tools.");
        Click("Undo");
        Find<NumericUpDown>(window, "LayerRotation").Value = 12.5m;
        TileRaster previewBeforeCustomRotate = workspace.Preview!;
        Click("RotateLayerCustom");
        Require(workspace.IsDirty && Bytes(workspace.Session.GetLayerRaster(layerId)).SequenceEqual(flipBaseline) &&
            !SamePixels(previewBeforeCustomRotate, workspace.Preview!) && Find<CheckBox>(window, "Paint").IsEffectivelyEnabled,
            "Flat layer custom rotation did not use a non-destructive transform or enable mapped pixel tools.");
        Click("Undo");

        var mappedWorkspace = new EditorWorkspace();
        mappedWorkspace.Open(project);
        Guid mappedLayerId = mappedWorkspace.Session!.ActiveLayerId!.Value;
        TileRaster mappedSourceBefore = mappedWorkspace.Session.GetLayerRaster(mappedLayerId);
        mappedWorkspace.MoveActiveLayer(12, 8);
        mappedWorkspace.SelectRectangle(new Rect(112, 108, 20, 20));
        mappedWorkspace.BeginStroke(mappedLayerId, new SoftBrushSettings(9, 1, [1, 0, 0], 1), new BrushPoint(121, 117));
        mappedWorkspace.CommitStroke(new BrushPoint(121, 117));
        Require(mappedWorkspace.IsDirty && !SamePixels(mappedSourceBefore, mappedWorkspace.Session.GetLayerRaster(mappedLayerId)),
            "A translated layer did not map the document selection and brush point back to source pixels.");
        var rotatedMappedWorkspace = new EditorWorkspace();
        rotatedMappedWorkspace.Open(project);
        Guid rotatedMappedLayerId = rotatedMappedWorkspace.Session!.ActiveLayerId!.Value;
        TileRaster rotatedSourceBefore = rotatedMappedWorkspace.Session.GetLayerRaster(rotatedMappedLayerId);
        rotatedMappedWorkspace.RotateActiveLayer(90);
        rotatedMappedWorkspace.SelectRectangle(new Rect(190, 30, 20, 20));
        rotatedMappedWorkspace.BeginStroke(rotatedMappedLayerId, new SoftBrushSettings(9, 1, [1, 0, 0], 1), new BrushPoint(200, 40));
        rotatedMappedWorkspace.CommitStroke(new BrushPoint(200, 40));
        Require(rotatedMappedWorkspace.IsDirty && !SamePixels(rotatedSourceBefore, rotatedMappedWorkspace.Session.GetLayerRaster(rotatedMappedLayerId)),
            "A rotated layer did not map the document selection and brush point back to source pixels.");
        var mappedMaskWorkspace = new EditorWorkspace();
        mappedMaskWorkspace.Open(project);
        Guid mappedMaskLayerId = mappedMaskWorkspace.Session!.ActiveLayerId!.Value;
        mappedMaskWorkspace.AddActiveLayerMask();
        mappedMaskWorkspace.MoveActiveLayer(12, 8);
        mappedMaskWorkspace.SelectRectangle(new Rect(112, 108, 20, 20));
        mappedMaskWorkspace.ApplySelectionToActiveLayerMask(reveal: false);
        GrayTileRaster mappedMask = mappedMaskWorkspace.Session.GetLayerMask(mappedMaskLayerId)!;
        Require(MaskPixel(mappedMask, 110, 110) == 0 && MaskPixel(mappedMask, 90, 90) == 255,
            "A translated layer did not map the document selection into its editable mask.");
        var scaledMaskWorkspace = new EditorWorkspace();
        scaledMaskWorkspace.Open(project);
        Guid scaledMaskLayerId = scaledMaskWorkspace.Session!.ActiveLayerId!.Value;
        scaledMaskWorkspace.ScaleActiveLayer(enlarge: false);
        scaledMaskWorkspace.AddActiveLayerMask();
        scaledMaskWorkspace.SelectRectangle(new Rect(100, 100, 31, 31));
        scaledMaskWorkspace.ApplySelectionToActiveLayerMask(reveal: false);
        GrayTileRaster scaledMask = scaledMaskWorkspace.Session.GetLayerMask(scaledMaskLayerId)!;
        Require(HasPartialCoverage(scaledMask),
            "A scaled layer did not preserve fractional selection coverage when mapping into its editable mask.");
        var transformedFloatingWorkspace = new EditorWorkspace();
        transformedFloatingWorkspace.Open(project);
        Guid transformedFloatingLayerId = transformedFloatingWorkspace.Session!.ActiveLayerId!.Value;
        transformedFloatingWorkspace.MoveActiveLayer(12, 8);
        transformedFloatingWorkspace.SelectRectangle(new Rect(112, 108, 20, 20));
        TileRaster transformedFloatingBefore = transformedFloatingWorkspace.Session.GetLayerRaster(transformedFloatingLayerId);
        bool transformedFloatingDirtyBeforePaste = transformedFloatingWorkspace.IsDirty;
        transformedFloatingWorkspace.CopySelection();
        Require(transformedFloatingWorkspace.CanPasteSelection,
            "A transformed layer did not accept a paste from its own transform snapshot.");
        transformedFloatingWorkspace.PasteSelection();
        Require(transformedFloatingWorkspace.HasFloatingSelection &&
            transformedFloatingWorkspace.IsDirty == transformedFloatingDirtyBeforePaste &&
            SamePixels(transformedFloatingBefore, transformedFloatingWorkspace.Session.GetLayerRaster(transformedFloatingLayerId)),
            "Pasting a transformed layer selection changed source pixels before commit.");
        transformedFloatingWorkspace.MoveSelection(3, 2);
        Require(transformedFloatingWorkspace.HasFloatingSelection &&
            transformedFloatingWorkspace.IsDirty == transformedFloatingDirtyBeforePaste &&
            transformedFloatingWorkspace.SelectionBounds is { X: 115, Y: 110 },
            "Moving a transformed floating selection did not remain in document coordinates.");
        transformedFloatingWorkspace.CommitFloatingSelection();
        Require(transformedFloatingWorkspace.IsDirty &&
            !SamePixels(transformedFloatingBefore, transformedFloatingWorkspace.Session.GetLayerRaster(transformedFloatingLayerId)) &&
            transformedFloatingWorkspace.Undo() &&
            SamePixels(transformedFloatingBefore, transformedFloatingWorkspace.Session.GetLayerRaster(transformedFloatingLayerId)),
            "Committing a transformed floating selection did not create an undoable source edit.");
        var transformedCrossSource = new EditorWorkspace();
        transformedCrossSource.Open(project);
        transformedCrossSource.MoveActiveLayer(12, 8);
        transformedCrossSource.SelectRectangle(new Rect(112, 108, 20, 20));
        transformedCrossSource.CopySelection();
        var transformedCrossTarget = new EditorWorkspace();
        transformedCrossTarget.Open(project);
        Guid transformedCrossTargetId = transformedCrossTarget.Session!.ActiveLayerId!.Value;
        TileRaster transformedCrossTargetBefore = transformedCrossTarget.Session.GetLayerRaster(transformedCrossTargetId);
        Require(transformedCrossTarget.CanPasteSelectionFrom(transformedCrossSource),
            "A transformed source did not enable cross-project selection paste.");
        transformedCrossTarget.PasteSelectionFrom(transformedCrossSource);
        Require(transformedCrossTarget.HasFloatingSelection &&
            SamePixels(transformedCrossTargetBefore, transformedCrossTarget.Session.GetLayerRaster(transformedCrossTargetId)) &&
            transformedCrossTarget.CanPasteSelection,
            "Cross-project transformed selection paste was not normalized to the target document coordinates.");
        transformedCrossTarget.CommitFloatingSelection();
        Require(transformedCrossTarget.IsDirty && !transformedCrossTarget.HasFloatingSelection,
            "Cross-project transformed selection paste did not commit as one target history step.");
        string transformedCrossTargetPath = Path.Combine(output, "TransformedCrossProjectPaste.comp");
        transformedCrossTarget.SaveAs(transformedCrossTargetPath);
        var reopenedTransformedCrossTarget = ImageProjectWorkflow.OpenEditable(transformedCrossTargetPath);
        Require(SamePixels(transformedCrossTarget.Preview!, ImageProjectWorkflow.RenderFlatNormal(reopenedTransformedCrossTarget)),
            "Saved cross-project transformed selection paste did not reopen with the same preview.");
        var areaWorkspace = new EditorWorkspace();
        areaWorkspace.Open(project);
        Guid areaLayerId = areaWorkspace.Session!.ActiveLayerId!.Value;
        TileRaster areaSource = areaWorkspace.Session.GetLayerRaster(areaLayerId);
        areaWorkspace.ResizeImage(128, 128);
        Require(Pixel(areaWorkspace.Session.GetLayerRaster(areaLayerId), 64, 64)
                .SequenceEqual(AreaPixel(areaSource, 64, 64, 128, 128)),
            "Image downscale did not use the expected area coverage for premultiplied pixels.");
        workspace.Save();
        workspace.Export(Path.Combine(output, "brush-export.png"), false);
        var reopened = ImageProjectWorkflow.OpenEditable(workspace.ProjectDirectory!);
        CheckGolden(reopened.GetLayerRaster(layerId), zip, "CrossTile-final.rgba");
        Require(Bytes(ImageProjectWorkflow.RenderFlatNormal(reopened)).SequenceEqual(Bytes(ImageCodec.Load(Path.Combine(output, "brush-export.png")))),
            "Saved brush preview and exported pixels differ.");
        using (var screenshot = new RenderTargetBitmap(new PixelSize(1120, 760), new Vector(96, 96)))
        {
            screenshot.Render(window); screenshot.Save(Path.Combine(output, "brush-window.png"));
        }
        window.MouseDown(DocumentPoint(points[0]), MouseButton.Left);
        window.MouseMove(DocumentPoint(points[1]));
        window.Close(); Dispatcher.UIThread.RunJobs();
        Require(!window.IsVisible && !workspace.HasActiveStroke && !workspace.IsDirty,
            "Closing the saved document did not cancel the active stroke.");
        CheckGolden(workspace.Session.GetLayerRaster(layerId), zip, "CrossTile-final.rgba");
        string maskedProject = Path.Combine(output, "EditableMask.comp");
        CopyDirectory(project, maskedProject);
        var maskedManifestPath = Path.Combine(maskedProject, "manifest.json");
        var maskedManifest = JsonNode.Parse(File.ReadAllText(maskedManifestPath))!.AsObject();
        var maskedLayers = maskedManifest["layers"]!.AsArray();
        var maskedLayer = maskedLayers[maskedLayers.Count - 1]!.AsObject();
        foreach (JsonNode? node in maskedLayers) node!["isVisible"] = false;
        maskedLayer["isVisible"] = true;
        string maskName = maskedLayer["id"]!.GetValue<string>().ToUpperInvariant() + ".mask.png";
        maskedLayer["maskFile"] = maskName;
        maskedLayer["maskEnabled"] = true;
        File.WriteAllText(maskedManifestPath, maskedManifest.ToJsonString());
        SaveGrayMask(Path.Combine(maskedProject, "images", maskName), workspace.Session.Width, workspace.Session.Height);
        var maskWorkspace = new EditorWorkspace();
        maskWorkspace.Open(maskedProject);
        var maskSession = maskWorkspace.Session!;
        Require(maskWorkspace.CanEdit &&
            maskSession.Layers.Count == workspace.Session.Layers.Count && maskSession.GetLayerMask(maskSession.ActiveLayerId!.Value) is not null &&
            maskWorkspace.Preview is not null,
            "Full-canvas masked project did not open as an editable preview.");
        int beforeMaskAlpha = Pixel(maskWorkspace.Preview!, 128, 128)[3];
        maskWorkspace.SelectRectangle(new Rect(120, 120, 16, 16));
        maskWorkspace.ApplySelectionToActiveLayerMask(reveal: false);
        Require(maskWorkspace.IsDirty && beforeMaskAlpha == 254 && Pixel(maskWorkspace.Preview!, 128, 128)[3] == 0,
            "Mask selection edit did not hide the selected pixels.");
        maskWorkspace.ToggleActiveLayerMask();
        Require(Pixel(maskWorkspace.Preview!, 128, 128)[3] == 254,
            "Mask disable did not restore the underlying layer pixels.");
        maskWorkspace.ToggleActiveLayerMask();
        maskWorkspace.Save();
        var reopenedMaskWorkspace = new EditorWorkspace();
        reopenedMaskWorkspace.Open(maskedProject);
        Require(reopenedMaskWorkspace.CanEdit && reopenedMaskWorkspace.Session!.GetLayerMask(reopenedMaskWorkspace.Session.ActiveLayerId!.Value)!.ReadTileCopy(0, 0)[128 * 256 + 128] == 0,
            "Mask edit did not survive save and reopen.");
        var maskTransformWorkspace = new EditorWorkspace();
        maskTransformWorkspace.Open(maskedProject);
        var transformSession = maskTransformWorkspace.Session!;
        var transformMask = transformSession.GetLayerMask(transformSession.ActiveLayerId!.Value)!;
        Require(MaskPixel(transformMask, 128, 128) == 0, "Saved mask edit was not available for transform checks.");
        TileRaster maskPreviewBeforeMove = maskTransformWorkspace.Preview!;
        maskTransformWorkspace.MoveActiveLayer(1, 0);
        Require(MaskPixel(transformSession.GetLayerMask(transformSession.ActiveLayerId!.Value)!, 120, 128) == 0 &&
            MaskPixel(transformSession.GetLayerMask(transformSession.ActiveLayerId!.Value)!, 136, 128) == 255 &&
            !SamePixels(maskPreviewBeforeMove, maskTransformWorkspace.Preview!),
            "Layer move did not preserve the source mask while changing the transformed preview.");
        Require(maskTransformWorkspace.Undo() && SamePixels(maskPreviewBeforeMove, maskTransformWorkspace.Preview!) &&
            MaskPixel(transformSession.GetLayerMask(transformSession.ActiveLayerId!.Value)!, 120, 128) == 0 &&
            maskTransformWorkspace.Redo() && !SamePixels(maskPreviewBeforeMove, maskTransformWorkspace.Preview!),
            "Undo/redo did not restore the non-destructive layer transform.");
        TileRaster maskPreviewBeforeFlip = maskTransformWorkspace.Preview!;
        maskTransformWorkspace.FlipActiveLayer(horizontal: true);
        Require(MaskPixel(transformSession.GetLayerMask(transformSession.ActiveLayerId!.Value)!, 134, 128) == 0 &&
            !SamePixels(maskPreviewBeforeFlip, maskTransformWorkspace.Preview!),
            "Layer flip did not preserve the source mask while changing the transformed preview.");
        GrayTileRaster maskBeforeResize = transformSession.GetLayerMask(transformSession.ActiveLayerId!.Value)!;
        byte expectedAreaMask = AreaCoveragePixel(maskBeforeResize, 64, 64, 128, 128);
        maskTransformWorkspace.ResizeImage(128, 128);
        Require(maskTransformWorkspace.Session!.Width == 128 && maskTransformWorkspace.Session.Height == 128 &&
            maskTransformWorkspace.Session.GetLayerMask(maskTransformWorkspace.Session.ActiveLayerId!.Value) is { Width: 128, Height: 128 } resizedMask &&
            MaskPixel(resizedMask, 64, 64) == expectedAreaMask,
            "Image resize did not keep the full-canvas layer mask aligned.");
        maskTransformWorkspace.RotateDocument90(clockwise: true);
        Require(maskTransformWorkspace.Session.Width == 128 && maskTransformWorkspace.Session.Height == 128 &&
            maskTransformWorkspace.Session.GetLayerMask(maskTransformWorkspace.Session.ActiveLayerId!.Value) is { Width: 128, Height: 128 },
            "Document rotation did not keep the full-canvas layer mask aligned.");
        var maskBrushWorkspace = new EditorWorkspace();
        maskBrushWorkspace.Open(maskedProject);
        var maskBrushSession = maskBrushWorkspace.Session!;
        Guid maskBrushLayer = maskBrushSession.ActiveLayerId!.Value;
        Require(MaskPixel(maskBrushSession.GetLayerMask(maskBrushLayer)!, 10, 10) == 255,
            "Mask brush fixture did not start with visible coverage.");
        maskBrushWorkspace.BeginMaskStroke(maskBrushLayer,
            new SoftBrushSettings(9, 1, [0, 0, 0], 1), new BrushPoint(10.5, 10.5), reveal: false);
        Require(maskBrushWorkspace.HasActiveStroke && !maskBrushWorkspace.IsDirty,
            "Mask brush committed before pointer release.");
        maskBrushWorkspace.CommitMaskStroke(new BrushPoint(10.5, 10.5));
        Require(maskBrushWorkspace.IsDirty && MaskPixel(maskBrushSession.GetLayerMask(maskBrushLayer)!, 10, 10) == 0,
            "Mask brush did not hide the painted coverage.");
        Require(maskBrushWorkspace.Undo() && MaskPixel(maskBrushSession.GetLayerMask(maskBrushLayer)!, 10, 10) == 255 &&
            maskBrushWorkspace.Redo() && MaskPixel(maskBrushSession.GetLayerMask(maskBrushLayer)!, 10, 10) == 0,
            "Mask brush Undo/Redo did not restore coverage.");
        maskBrushWorkspace.SelectRectangle(new Rect(128, 128, 1, 1));
        maskBrushWorkspace.BeginMaskStroke(maskBrushLayer,
            new SoftBrushSettings(9, 1, [0, 0, 0], 1), new BrushPoint(128.5, 128.5), reveal: true);
        maskBrushWorkspace.CommitMaskStroke(new BrushPoint(128.5, 128.5));
        Require(MaskPixel(maskBrushSession.GetLayerMask(maskBrushLayer)!, 128, 128) == 255 &&
            MaskPixel(maskBrushSession.GetLayerMask(maskBrushLayer)!, 127, 128) == 0,
            "Mask brush reveal did not honor the active selection.");
        GrayTileRaster maskBeforeInvert = maskWorkspace.Session!.GetLayerMask(maskWorkspace.Session.ActiveLayerId!.Value)!;
        maskWorkspace.InvertActiveLayerMask();
        Require(maskWorkspace.IsDirty &&
            MaskPixel(maskWorkspace.Session.GetLayerMask(maskWorkspace.Session.ActiveLayerId!.Value)!, 128, 128) == 255 &&
            MaskPixel(maskWorkspace.Session.GetLayerMask(maskWorkspace.Session.ActiveLayerId!.Value)!, 10, 10) == 0,
            "Mask inversion did not reverse Gray8 coverage.");
        Require(maskWorkspace.Undo() && SameCoverage(maskWorkspace.Session.GetLayerMask(maskWorkspace.Session.ActiveLayerId!.Value)!, maskBeforeInvert) &&
            maskWorkspace.Redo() && MaskPixel(maskWorkspace.Session.GetLayerMask(maskWorkspace.Session.ActiveLayerId!.Value)!, 128, 128) == 255,
            "Mask inversion did not participate in Undo/Redo history.");
        Require(maskWorkspace.Undo() && !maskWorkspace.IsDirty, "Undo did not restore the saved mask before fill checks.");
        maskWorkspace.FillActiveLayerMask(reveal: false);
        Require(maskWorkspace.IsDirty && MaskPixel(maskWorkspace.Session.GetLayerMask(maskWorkspace.Session.ActiveLayerId!.Value)!, 10, 10) == 0,
            "Black mask fill did not clear the complete mask.");
        Require(maskWorkspace.Undo() && !maskWorkspace.IsDirty, "Undo did not restore the mask after black fill.");
        maskWorkspace.FillActiveLayerMask(reveal: true);
        Require(maskWorkspace.IsDirty && MaskPixel(maskWorkspace.Session.GetLayerMask(maskWorkspace.Session.ActiveLayerId!.Value)!, 10, 10) == 255,
            "White mask fill did not reveal the complete mask.");
        Require(maskWorkspace.Undo() && !maskWorkspace.IsDirty, "Undo did not restore the mask after white fill.");
        maskWorkspace.BlurActiveLayerMask(3);
        Require(maskWorkspace.IsDirty && HasPartialCoverage(maskWorkspace.Session.GetLayerMask(maskWorkspace.Session.ActiveLayerId!.Value)!),
            "Mask blur did not create fractional edge coverage.");
        Require(maskWorkspace.Undo() && !maskWorkspace.IsDirty, "Undo did not restore the mask after blur.");
        var maskWindow = new MainWindow(maskWorkspace);
        maskWindow.Show(); Dispatcher.UIThread.RunJobs();
        Require(Find<Button>(maskWindow, "Save").IsEffectivelyEnabled &&
            Find<Button>(maskWindow, "ToggleMask").IsEffectivelyEnabled &&
            Find<Button>(maskWindow, "InvertMask").IsEffectivelyEnabled &&
            Find<Button>(maskWindow, "FillMaskWhite").IsEffectivelyEnabled &&
            Find<Button>(maskWindow, "FillMaskBlack").IsEffectivelyEnabled &&
            Find<Button>(maskWindow, "BlurMask").IsEffectivelyEnabled &&
            Find<NumericUpDown>(maskWindow, "MaskRadius").IsEffectivelyEnabled &&
            Find<Button>(maskWindow, "AddMask").IsEffectivelyEnabled &&
            Find<CheckBox>(maskWindow, "MaskPaint").IsEffectivelyEnabled &&
            Find<Button>(maskWindow, "ExportPng").IsEffectivelyEnabled &&
            Find<CheckBox>(maskWindow, "RectSelect").IsEffectivelyEnabled,
            "Editable masked window did not expose mask and editing controls.");
        maskWindow.Close(); Dispatcher.UIThread.RunJobs();
        string cachedTextProject = Path.Combine(output, "CachedText.comp");
        CopyDirectory(project, cachedTextProject);
        var cachedTextManifestPath = Path.Combine(cachedTextProject, "manifest.json");
        var cachedTextManifest = JsonNode.Parse(File.ReadAllText(cachedTextManifestPath))!.AsObject();
        var cachedTextLayers = cachedTextManifest["layers"]!.AsArray();
        var cachedTextLayer = cachedTextLayers[cachedTextLayers.Count - 1]!.AsObject();
        cachedTextLayer["text"] = new JsonObject
        {
            ["alignment"] = "left",
            ["alpha"] = 1d,
            ["blue"] = 0d,
            ["content"] = "Missing font cache",
            ["fontPostScriptName"] = "Compositor-Missing-Font",
            ["fontSizePoints"] = 18d,
            ["green"] = 0d,
            ["layout"] = new JsonObject { ["point"] = new JsonObject() },
            ["lineSpacingPoints"] = 0d,
            ["red"] = 0d,
            ["trackingPoints"] = 0d
        };
        cachedTextLayer["transform"] = new JsonObject
        {
            ["origin"] = new JsonArray(20d, 16d), ["size"] = new JsonArray(120d, 80d),
            ["rotation"] = 15d, ["flipX"] = true, ["flipY"] = false, ["sampling"] = "High quality"
        };
        File.WriteAllText(cachedTextManifestPath, cachedTextManifest.ToJsonString());
        var cachedTextWorkspace = new EditorWorkspace();
        cachedTextWorkspace.Open(cachedTextProject);
        Require(!cachedTextWorkspace.CanEdit && cachedTextWorkspace.Preview is { Width: > 0, Height: > 0 },
            "Text cache project with a missing font did not open as a read-only transformed preview.");
        string cachedTextExport = Path.Combine(output, "cached-text-export.png");
        cachedTextWorkspace.Export(cachedTextExport, jpeg: false);
        Require(ImageCodec.Load(cachedTextExport).Width == workspace.Session.Width,
            "Missing-font cached preview did not preserve document export dimensions.");
        string resolverFont = TextLayerWorkflow.AvailableFonts.FirstOrDefault()
            ?? throw new Exception("No installed font was available for the missing-font resolver check.");
        var cachedTextWindow = new MainWindow(cachedTextWorkspace);
        cachedTextWindow.Show(); Dispatcher.UIThread.RunJobs();
        var resolverCombo = Find<ComboBox>(cachedTextWindow, "TextFont");
        var resolverButton = Find<Button>(cachedTextWindow, "ResolveTextFont");
        Require(!cachedTextWorkspace.CanEdit && resolverCombo.IsEffectivelyEnabled && resolverButton.IsEffectivelyEnabled &&
            !Find<TextBox>(cachedTextWindow, "TextContent").IsEffectivelyEnabled,
            "Missing-font text did not expose only the explicit font resolver.");
        resolverCombo.SelectedItem = resolverFont;
        resolverButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var resolverTimer = Stopwatch.StartNew();
        while (cachedTextWindow.IsBusy)
        {
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(5);
            if (resolverTimer.Elapsed.TotalSeconds > 30) throw new TimeoutException("Missing-font resolution did not finish.");
        }
        Require(cachedTextWorkspace.CanEdit && cachedTextWorkspace.Session!.TextLayers.Single().FontPostScriptName == resolverFont &&
            Find<TextBox>(cachedTextWindow, "TextContent").IsEffectivelyEnabled,
            "Explicit missing-font resolution did not unlock the text layer.");
        Require(cachedTextWorkspace.Undo() && !cachedTextWorkspace.CanEdit &&
            cachedTextWorkspace.Session!.TextLayers.Single().FontPostScriptName == "Compositor-Missing-Font",
            "Undoing explicit font replacement did not restore the missing-font read-only state.");
        Require(cachedTextWorkspace.Redo() && cachedTextWorkspace.CanEdit &&
            cachedTextWorkspace.Session!.TextLayers.Single().FontPostScriptName == resolverFont,
            "Redoing explicit font replacement did not restore the editable state.");
        cachedTextWindow.Close(); Dispatcher.UIThread.RunJobs();
        string availableTextProject = Path.Combine(output, "AvailableText.comp");
        CopyDirectory(cachedTextProject, availableTextProject);
        string availableTextManifestPath = Path.Combine(availableTextProject, "manifest.json");
        var availableTextManifest = JsonNode.Parse(File.ReadAllText(availableTextManifestPath))!.AsObject();
        var availableTextLayer = availableTextManifest["layers"]!.AsArray()[^1]!.AsObject();
        string availableTextFont = resolverFont;
        availableTextLayer["text"]!["fontPostScriptName"] = availableTextFont;
        File.WriteAllText(availableTextManifestPath, availableTextManifest.ToJsonString());
        var availableTextWorkspace = new EditorWorkspace();
        availableTextWorkspace.Open(availableTextProject);
        var availableTextWindow = new MainWindow(availableTextWorkspace);
        availableTextWindow.Show(); Dispatcher.UIThread.RunJobs();
        var availableTextContent = Find<TextBox>(availableTextWindow, "TextContent");
        Require(availableTextWorkspace.CanEdit && availableTextContent.IsEffectivelyEnabled &&
            Find<Button>(availableTextWindow, "ApplyText").IsEffectivelyEnabled,
            "Available-font text did not expose the editable text controls.");
        CanvasView availableTextCanvas = Find<CanvasView>(availableTextWindow, "Canvas");
        TextLayerMetadata availableTextMetadata = availableTextWorkspace.Session!.TextLayers.Single();
        TextLayoutLine availableTextLine = TextLayerWorkflow.Layout(availableTextMetadata,
            availableTextWorkspace.Session.Resolution, availableTextWorkspace.Session.GetLayerRaster(availableTextMetadata.Id).Width).Lines[0];
        TileRaster availableTextRaster = availableTextWorkspace.Session.GetLayerRaster(availableTextMetadata.Id);
        LayerTransformInfo availableTextTransform = availableTextWorkspace.Session.GetLayerTransform(availableTextMetadata.Id);
        Point textSourceEnd = new(availableTextLine.X + availableTextLine.Width, availableTextLine.Y + availableTextLine.Height / 2);
        double localX = textSourceEnd.X * availableTextTransform.Width / availableTextRaster.Width - availableTextTransform.Width / 2;
        double localY = textSourceEnd.Y * availableTextTransform.Height / availableTextRaster.Height - availableTextTransform.Height / 2;
        if (availableTextTransform.FlipX) localX = -localX;
        if (availableTextTransform.FlipY) localY = -localY;
        double rotation = availableTextTransform.Rotation * Math.PI / 180;
        Point textDocumentEnd = new(
            availableTextTransform.X + availableTextTransform.Width / 2 + localX * Math.Cos(rotation) - localY * Math.Sin(rotation),
            availableTextTransform.Y + availableTextTransform.Height / 2 + localX * Math.Sin(rotation) + localY * Math.Cos(rotation));
        Point textViewEnd = availableTextCanvas.Viewport.ToView(textDocumentEnd);
        Point textWindowEnd = availableTextCanvas.TranslatePoint(textViewEnd, availableTextWindow)
            ?? throw new Exception("Available-font text canvas is detached.");
        availableTextWindow.MouseDown(textWindowEnd, MouseButton.Left);
        availableTextWindow.MouseUp(textWindowEnd, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Require(availableTextContent.CaretIndex == availableTextMetadata.Content.Length &&
            availableTextContent.SelectionStart == availableTextMetadata.Content.Length &&
            availableTextContent.SelectionEnd == availableTextMetadata.Content.Length,
            $"Canvas text hit did not place the sidebar caret at the end of the text: caret={availableTextContent.CaretIndex}, start={availableTextContent.SelectionStart}, end={availableTextContent.SelectionEnd}, length={availableTextMetadata.Content.Length}, document={textDocumentEnd}, view={textViewEnd}.");
        Require(availableTextCanvas.TextCaretOverlayVisible && availableTextCanvas.TextSelectionOverlayCount == 0,
            "Canvas text hit did not expose the shared caret overlay.");
        availableTextContent.SelectionStart = 0;
        availableTextContent.SelectionEnd = availableTextMetadata.Content.Length;
        Dispatcher.UIThread.RunJobs();
        Require(availableTextCanvas.TextCaretOverlayVisible && availableTextCanvas.TextSelectionOverlayCount == 1,
            "Sidebar text selection did not expose the shared canvas selection overlay.");
        availableTextContent.Text = "Edited from the Windows text panel";
        Find<Button>(availableTextWindow, "ApplyText").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var textTimer = Stopwatch.StartNew();
        while (availableTextWindow.IsBusy)
        {
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(5);
            if (textTimer.Elapsed.TotalSeconds > 30) throw new TimeoutException("Text edit did not finish.");
        }
        ProjectSession availableTextSession = availableTextWorkspace.Session
            ?? throw new Exception("Text edit closed its project session.");
        Require(availableTextWorkspace.IsDirty &&
            availableTextSession.TextLayers.Single().Content == "Edited from the Windows text panel",
            "Text panel edit did not update the text metadata or dirty state.");
        Require(availableTextWorkspace.Undo() &&
            availableTextSession.TextLayers.Single().Content == "Missing font cache" &&
            availableTextWorkspace.Redo() &&
            availableTextSession.TextLayers.Single().Content == "Edited from the Windows text panel",
            "Text panel edit did not participate in undo and redo.");
        availableTextWindow.Close(); Dispatcher.UIThread.RunJobs();
        var faceChoices = TextLayerWorkflow.AvailableFonts
            .Where(choice => choice.Contains(" / ", StringComparison.Ordinal))
            .GroupBy(choice => choice[..choice.IndexOf(" / ", StringComparison.Ordinal)], StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() >= 2);
        Require(faceChoices is not null, "No same-family multi-face font choices were exposed.");
        string selectedFace = faceChoices!.Skip(1).First();
        string faceProject = Path.Combine(output, "TextFaceSelection.comp");
        var faceWorkspace = new EditorWorkspace();
        faceWorkspace.Import(fixture, faceProject);
        faceWorkspace.AddTextLayer("Face selection");
        var faceWindow = new MainWindow(faceWorkspace);
        faceWindow.Show(); Dispatcher.UIThread.RunJobs();
        Find<ListBox>(faceWindow, "Layers").SelectedItem =
            faceWorkspace.Session!.Layers.Single(layer => layer.IsText);
        Dispatcher.UIThread.RunJobs();
        var faceCombo = Find<ComboBox>(faceWindow, "TextFont");
        faceCombo.SelectedItem = selectedFace;
        Find<Button>(faceWindow, "ApplyText").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var faceTimer = Stopwatch.StartNew();
        while (faceWindow.IsBusy)
        {
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(5);
            if (faceTimer.Elapsed.TotalSeconds > 30) throw new TimeoutException("Same-family face selection did not finish.");
        }
        Require(faceWorkspace.Session!.TextLayers.Single().FontPostScriptName == selectedFace,
            "The text editor did not commit the selected same-family face identity.");
        faceWorkspace.Save(); faceWindow.Close(); Dispatcher.UIThread.RunJobs();
        var reopenedFace = ProjectStore.Open(faceProject);
        Require(reopenedFace.TextLayers.Single().FontPostScriptName == selectedFace,
            "The selected same-family face identity did not survive save and reopen.");
        string? ttcSource = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fixture)!, "..", "..", "..", "docs", "windows", "fixtures", "fonts", "two-faces.ttc"));
        if (!File.Exists(ttcSource) || SKTypeface.FromFile(ttcSource, 1) is null)
        {
            string[] systemTtcs = Directory.Exists("/System/Library/Fonts")
                ? Directory.GetFiles("/System/Library/Fonts", "*.ttc") : [];
            ttcSource = systemTtcs.FirstOrDefault(path => SKTypeface.FromFile(path, 1) is not null);
        }
        if (ttcSource is not null)
        {
            var appFontLibrary = new FontLibrary(Path.Combine(output, "AppFontLibrary"));
        var importWindow = new MainWindow(new EditorWorkspace(), appFontLibrary);
        importWindow.Show(); Dispatcher.UIThread.RunJobs();
        Task<ImportedFont?> cancelledImport = importWindow.ImportFontFileAsync(ttcSource);
        Window importDialog = WaitForDialog(importWindow);
        Require(Find<ComboBox>(importDialog, "FontFace").SelectedIndex == 0 &&
            Find<Button>(importDialog, "ImportFontFace").IsEffectivelyEnabled &&
            Find<Button>(importDialog, "CancelFontFace").IsEffectivelyEnabled,
            "TTC import did not expose an explicit face-index choice with cancellation.");
        Find<Button>(importDialog, "CancelFontFace").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(WaitForTask(cancelledImport) is null && appFontLibrary.Entries.Count == 0,
            "Cancelling TTC face selection changed the font library.");
        Task<ImportedFont?> selectedImport = importWindow.ImportFontFileAsync(ttcSource);
        importDialog = WaitForDialog(importWindow);
        var importFace = Find<ComboBox>(importDialog, "FontFace");
        importFace.SelectedIndex = 1;
        Find<Button>(importDialog, "ImportFontFace").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ImportedFont importedFace = WaitForTask(selectedImport)
            ?? throw new Exception("TTC face import returned no selected face.");
        IReadOnlyList<FontFace> importedChoices = FontLibrary.EnumerateFaces(ttcSource);
        Require(importedFace.FaceIndex == importedChoices[1].FaceIndex &&
            importedFace.SelectionName == importedChoices[1].SelectionName &&
            Equals(Find<ComboBox>(importWindow, "TextFont").SelectedItem, importedFace.SelectionName) &&
            appFontLibrary.Entries.Single().FaceIndex == importedChoices[1].FaceIndex,
            "TTC import did not persist the selected face-index token.");
            importWindow.Close(); Dispatcher.UIThread.RunJobs();
        }
        else Console.WriteLine("SKIA TTC face-index support is unavailable on this host; Windows CI covers the dialog path.");
        string recoveryRoot = Path.Combine(output, "FontRecoveryStatus");
        Directory.CreateDirectory(recoveryRoot);
        File.WriteAllText(Path.Combine(recoveryRoot, "fonts.json"), JsonSerializer.Serialize(new[]
        {
            new ImportedFont("missing-user-font.ttf", "Missing user font", 0, new string('0', 64))
        }));
        var recoveryLibrary = new FontLibrary(recoveryRoot);
        var recoveryWindow = new MainWindow(new EditorWorkspace(), recoveryLibrary);
        recoveryWindow.Show(); Dispatcher.UIThread.RunJobs();
        string recoveryStatus = Find<TextBlock>(recoveryWindow, "Status").Text ?? "";
        Require(recoveryLibrary.RecoveryReport.Issues.Count == 1 &&
            recoveryLibrary.RecoveryReport.Issues[0].FileName == "missing-user-font.ttf" &&
            recoveryLibrary.RecoveryReport.Issues[0].Reason == "文件缺失" &&
            recoveryStatus.Contains("1 个失效条目", StringComparison.Ordinal) &&
            recoveryStatus.Contains("missing-user-font.ttf", StringComparison.Ordinal) &&
            recoveryStatus.Contains("文件缺失", StringComparison.Ordinal) &&
            JsonSerializer.Deserialize<List<ImportedFont>>(File.ReadAllText(Path.Combine(recoveryRoot, "fonts.json")))?.Count == 0,
            "Font recovery did not expose the missing filename and reason in the formal startup status.");
        recoveryWindow.Close(); Dispatcher.UIThread.RunJobs();
        var resizeWorkspace = new EditorWorkspace();
        resizeWorkspace.Open(project);
        int originalWidth = resizeWorkspace.Session!.Width, originalHeight = resizeWorkspace.Session.Height;
        byte[] originalTopLeft = resizeWorkspace.Session.GetLayerRaster(resizeWorkspace.Session.Layers[^1].Id).ReadTileCopy(0, 0);
        resizeWorkspace.ResizeCanvas(originalWidth - 1, originalHeight - 2);
        Require(resizeWorkspace.Session.Width == originalWidth - 1 && resizeWorkspace.Session.Height == originalHeight - 2 &&
            resizeWorkspace.Session.GetLayerRaster(resizeWorkspace.Session.Layers[^1].Id).ReadTileCopy(0, 0).AsSpan(0, 4).SequenceEqual(originalTopLeft.AsSpan(0, 4)) && resizeWorkspace.IsDirty,
            "Canvas resize did not crop to the requested size or preserve the top-left pixel.");
        Require(resizeWorkspace.Undo() && resizeWorkspace.Session.Width == originalWidth && resizeWorkspace.Session.Height == originalHeight && !resizeWorkspace.IsDirty,
            "Undo did not restore the original canvas dimensions.");
        Require(resizeWorkspace.Redo() && resizeWorkspace.Session.Width == originalWidth - 1 && resizeWorkspace.Session.Height == originalHeight - 2,
            "Redo did not restore the resized canvas dimensions.");
        resizeWorkspace.ResizeImage(130, 129);
        Require(resizeWorkspace.Session.Width == 130 && resizeWorkspace.Session.Height == 129 && resizeWorkspace.IsDirty,
            "Image resize did not scale to the requested dimensions.");
        var qualityWorkspace = new EditorWorkspace();
        qualityWorkspace.New(2, 2, 72);
        var corners = new TileRaster(2, 2).ReplaceTile(0, 0,
            [0, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255]);
        qualityWorkspace.Edit(session => session.ReplaceLayerRaster(session.ActiveLayerId!.Value, corners));
        qualityWorkspace.ResizeImage(3, 3);
        byte[] center = qualityWorkspace.Session!.GetLayerRaster(qualityWorkspace.Session.ActiveLayerId!.Value).ReadTileCopy(0, 0);
        Require(center[16] is >= 63 and <= 65 && center[17] is >= 63 and <= 65 &&
            center[18] is >= 63 and <= 65 && center[19] == 255,
            "Image resize did not bilinearly resample the center pixel.");
        var lanczosWorkspace = new EditorWorkspace();
        lanczosWorkspace.New(4, 4, 72);
        Guid lanczosLayerId = lanczosWorkspace.Session!.ActiveLayerId!.Value;
        lanczosWorkspace.SaveAs(Path.Combine(output, "Lanczos.comp"));
        var lanczosInput = new TileRaster(4, 4).ReplaceTile(0, 0,
        [
            0, 0, 0, 0, 96, 0, 0, 96, 0, 200, 0, 255, 0, 0, 48, 64,
            16, 24, 32, 64, 40, 50, 60, 128, 70, 80, 90, 192, 100, 110, 120, 255,
            130, 0, 0, 255, 0, 70, 0, 128, 0, 0, 150, 192, 40, 45, 50, 64,
            200, 210, 220, 255, 20, 30, 40, 64, 60, 70, 80, 128, 90, 100, 110, 192
        ]);
        var lanczosMask = GrayTileRaster.FromCoverage(4, 4,
        [0, 64, 128, 255, 32, 96, 160, 224, 16, 80, 144, 208, 48, 112, 176, 240]);
        lanczosWorkspace.Edit(session =>
        {
            session.ReplaceLayerRaster(lanczosLayerId, lanczosInput);
            session.EnsureLayerMask(lanczosLayerId);
            session.ReplaceLayerMask(lanczosLayerId, lanczosMask);
        });
        lanczosWorkspace.ResizeImage(2, 2, ResizeFilter.Lanczos3);
        byte[][] expectedLanczosPixels =
        [
            [22, 18, 30, 47], [53, 121, 65, 213],
            [91, 80, 57, 176], [44, 45, 109, 144]
        ];
        byte[] expectedLanczosMask = [46, 200, 55, 196];
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
            Require(Pixel(lanczosWorkspace.Session.GetLayerRaster(lanczosLayerId), x, y)
                .SequenceEqual(expectedLanczosPixels[y * 2 + x]),
                "Lanczos RGBA resize changed the fixed premultiplied edge sample.");
        GrayTileRaster resizedLanczosMask = lanczosWorkspace.Session.GetLayerMask(lanczosLayerId)!;
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
            Require(MaskPixel(resizedLanczosMask, x, y) == expectedLanczosMask[y * 2 + x],
                "Lanczos Gray8 resize changed the fixed coverage edge sample.");
        var rotateWorkspace = new EditorWorkspace();
        rotateWorkspace.Open(project);
        int rotateWidth = rotateWorkspace.Session!.Width, rotateHeight = rotateWorkspace.Session.Height;
        rotateWorkspace.RotateDocument90(clockwise: true);
        Require(rotateWorkspace.Session.Width == rotateHeight && rotateWorkspace.Session.Height == rotateWidth && rotateWorkspace.IsDirty,
            "Clockwise document rotation did not exchange the canvas dimensions.");
        Require(rotateWorkspace.Undo() && rotateWorkspace.Session.Width == rotateWidth && rotateWorkspace.Session.Height == rotateHeight && !rotateWorkspace.IsDirty,
            "Undo did not restore the pre-rotation canvas dimensions.");
        Require(rotateWorkspace.Redo() && rotateWorkspace.Session.Width == rotateHeight && rotateWorkspace.Session.Height == rotateWidth,
            "Redo did not restore the rotated canvas dimensions.");
        File.WriteAllText(Path.Combine(output, "canvas-results.json"), JsonSerializer.Serialize(new
        {
            passed = true, referenceCases = cases.Length, imageWidth = original.Width, imageHeight = original.Height, provisionalAndFinalExact = true,
            pointerStroke = "single commit, Escape/capture-loss/close cancellation, source snapshot and other layer preserved",
            viewport = "zoom anchor, middle-button and Space plus left-button pan in logical coordinates, exact 100% and pixel grid",
            selection = nativeAvailable ? "rectangle/ellipse/lasso/combined/native-wand masks, wand radius, traced outline, non-dirty selection state, brush clipping, undo and clear" : "rectangle/ellipse/lasso/combined mask, non-dirty selection state, brush clipping, undo and clear",
            layerViaCopy = "selected pixels become a new in-place flat layer; button state, pixel bounds, undo/redo verified",
            limits = "Headless input; no native Windows/DPI/pressure or S02 performance claim."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: production soft brush matches fixed M1 pixels, real pointer commit/cancel, viewport and saved export");

        Point WindowPoint(Point point) => canvas.TranslatePoint(point, window) ?? throw new Exception("Canvas is detached.");
        Point DocumentPoint(Point point) => WindowPoint(canvas.Viewport.ToView(point));
        void Click(string name)
        {
            Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var timer = Stopwatch.StartNew();
            while (window.IsBusy)
            {
                Dispatcher.UIThread.RunJobs(); Thread.Sleep(5);
                if (timer.Elapsed.TotalSeconds > 30) throw new TimeoutException("Canvas history command did not finish.");
            }
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static void DialogClick(Window dialog, string label) => dialog.GetVisualDescendants().OfType<Button>()
        .Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Window WaitForDialog(Window owner)
    {
        var timer = Stopwatch.StartNew();
        while (owner.OwnedWindows.Count == 0)
        {
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(5);
            if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Font dialog did not open.");
        }
        return owner.OwnedWindows.Single();
    }
    private static T WaitForTask<T>(Task<T> task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(5);
            if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Font import did not finish.");
        }
        return task.GetAwaiter().GetResult();
    }
    private static bool Near(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) < 1e-8;
    private static TileRaster RenderWindow(Window window, string path)
    {
        using var screenshot = new RenderTargetBitmap(new PixelSize(1120, 760), new Vector(96, 96));
        screenshot.Render(window); screenshot.Save(path);
        return ImageCodec.Load(path);
    }
    private static bool SamePixels(TileRaster a, TileRaster b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        for (int row = 0; row * TileRaster.TileSize < a.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < a.Width; column++)
            if (!a.ReadTileCopy(column, row).SequenceEqual(b.ReadTileCopy(column, row))) return false;
        return true;
    }

    private static bool SameCoverage(GrayTileRaster a, GrayTileRaster b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        for (int row = 0; row * TileRaster.TileSize < a.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < a.Width; column++)
            if (!a.ReadTileCopy(column, row).AsSpan().SequenceEqual(b.ReadTileCopy(column, row))) return false;
        return true;
    }

    private static bool IsHorizontalStroke(TileRaster before, TileRaster after, int centerY, int margin)
    {
        byte[] first = Bytes(before), second = Bytes(after);
        int left = after.Width, top = after.Height, right = -1, bottom = -1;
        for (int y = 0; y < after.Height; y++)
        for (int x = 0; x < after.Width; x++)
        {
            int offset = (y * after.Width + x) * 4;
            if (first.AsSpan(offset, 4).SequenceEqual(second.AsSpan(offset, 4))) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        return right - left >= 50 && top >= centerY - margin && bottom <= centerY + margin;
    }
    private static int MaxDifference(TileRaster a, TileRaster b)
    {
        int maximum = 0;
        for (int row = 0; row * TileRaster.TileSize < a.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < a.Width; column++)
        {
            byte[] left = a.ReadTileCopy(column, row), right = b.ReadTileCopy(column, row);
            for (int index = 0; index < left.Length; index++) maximum = Math.Max(maximum, Math.Abs(left[index] - right[index]));
        }
        return maximum;
    }
    private static byte[] Pixel(TileRaster raster, int x, int y)
    {
        int column = x / TileRaster.TileSize, row = y / TileRaster.TileSize;
        var size = raster.TileDimensions(column, row);
        byte[] tile = raster.ReadTileCopy(column, row);
        return tile.AsSpan(((y % TileRaster.TileSize) * size.Width + x % TileRaster.TileSize) * 4, 4).ToArray();
    }
    private static byte MaskPixel(GrayTileRaster raster, int x, int y)
    {
        int column = x / TileRaster.TileSize, row = y / TileRaster.TileSize;
        var size = raster.TileDimensions(column, row);
        return raster.ReadTileCopy(column, row)[(y % TileRaster.TileSize) * size.Width + x % TileRaster.TileSize];
    }
    private static bool HasPartialCoverage(GrayTileRaster raster)
    {
        for (int row = 0; row * TileRaster.TileSize < raster.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < raster.Width; column++)
        {
            byte[] tile = raster.ReadTileCopy(column, row);
            if (tile.Any(value => value is > 0 and < 255)) return true;
        }
        return false;
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
    private static void SaveGrayMask(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        byte[] pixels = Enumerable.Repeat((byte)255, bitmap.RowBytes * height).ToArray();
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new IOException("Cannot encode Gray8 mask.");
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
    private static void CheckGolden(TileRaster raster, ZipArchive zip, string entry)
    {
        using var stream = zip.GetEntry(entry)!.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory);
        byte[] actual = Bytes(raster), expected = memory.ToArray();
        Require(actual.SequenceEqual(expected), "M1 brush pixel mismatch: " + entry);
    }
    private static byte[] Bytes(TileRaster raster)
    {
        byte[] bytes = new byte[raster.Width * raster.Height * 4];
        for (int row = 0; row * 256 < raster.Height; row++)
        for (int column = 0; column * 256 < raster.Width; column++)
        {
            var size = raster.TileDimensions(column, row); byte[] tile = raster.ReadTileCopy(column, row);
            for (int y = 0; y < size.Height; y++)
                tile.AsSpan(y * size.Width * 4, size.Width * 4).CopyTo(bytes.AsSpan(((row * 256 + y) * raster.Width + column * 256) * 4));
        }
        return bytes;
    }

    private static byte[] AreaPixel(TileRaster source, int x, int y, int width, int height)
    {
        byte[] input = Bytes(source), result = new byte[4];
        double startX = x * (double)source.Width / width, endX = (x + 1) * (double)source.Width / width;
        double startY = y * (double)source.Height / height, endY = (y + 1) * (double)source.Height / height;
        int firstY = (int)Math.Floor(startY), lastY = (int)Math.Ceiling(endY);
        double[] vertical = new double[4];
        double yWeightSum = 0;
        for (int sourceY = firstY; sourceY < lastY; sourceY++)
        {
            double yWeight = Math.Min(endY, sourceY + 1) - Math.Max(startY, sourceY);
            if (yWeight <= 0) continue;
            int firstX = (int)Math.Floor(startX), lastX = (int)Math.Ceiling(endX);
            double[] horizontal = new double[4];
            double xWeightSum = 0;
            for (int sourceX = firstX; sourceX < lastX; sourceX++)
            {
                double xWeight = Math.Min(endX, sourceX + 1) - Math.Max(startX, sourceX);
                if (xWeight <= 0) continue;
                xWeightSum += xWeight;
                int offset = (sourceY * source.Width + sourceX) * 4;
                for (int channel = 0; channel < 4; channel++) horizontal[channel] += input[offset + channel] * xWeight;
            }
            for (int channel = 0; channel < 4; channel++)
                vertical[channel] += Math.Round(horizontal[channel] / xWeightSum, MidpointRounding.AwayFromZero) * yWeight;
            yWeightSum += yWeight;
        }
        for (int channel = 0; channel < 4; channel++)
            result[channel] = (byte)Math.Clamp(Math.Round(vertical[channel] / yWeightSum, MidpointRounding.AwayFromZero), 0, 255);
        return result;
    }

    private static byte AreaCoveragePixel(GrayTileRaster source, int x, int y, int width, int height)
    {
        double startX = x * (double)source.Width / width, endX = (x + 1) * (double)source.Width / width;
        double startY = y * (double)source.Height / height, endY = (y + 1) * (double)source.Height / height;
        int firstY = (int)Math.Floor(startY), lastY = (int)Math.Ceiling(endY);
        double sum = 0, yWeightSum = 0;
        for (int sourceY = firstY; sourceY < lastY; sourceY++)
        {
            double yWeight = Math.Min(endY, sourceY + 1) - Math.Max(startY, sourceY);
            if (yWeight <= 0) continue;
            double horizontal = 0, xWeightSum = 0;
            for (int sourceX = (int)Math.Floor(startX); sourceX < (int)Math.Ceiling(endX); sourceX++)
            {
                double xWeight = Math.Min(endX, sourceX + 1) - Math.Max(startX, sourceX);
                if (xWeight <= 0) continue;
                horizontal += MaskPixel(source, sourceX, sourceY) * xWeight;
                xWeightSum += xWeight;
            }
            sum += Math.Round(horizontal / xWeightSum, MidpointRounding.AwayFromZero) * yWeight;
            yWeightSum += yWeight;
        }
        return (byte)Math.Clamp(Math.Round(sum / yWeightSum, MidpointRounding.AwayFromZero), 0, 255);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
