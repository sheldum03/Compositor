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
        workspace.SelectRectangle(new Rect(30, 30, 120, 90));
        canvas.SetSelectionRect(workspace.SelectionBounds);
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
            !SamePixels(previewBeforeScale, workspace.Preview!) && !Find<CheckBox>(window, "Paint").IsEffectivelyEnabled &&
            !canvas.SelectionEnabled && scalePreviewDiff == 0, $"Flat layer scale did not use a non-destructive transform or protect pixel tools (previewDiff={scalePreviewDiff}).");
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
            !SamePixels(previewBeforeRotate, workspace.Preview!) && !Find<CheckBox>(window, "Paint").IsEffectivelyEnabled,
            "Flat layer rotation did not use a non-destructive transform or protect pixel tools.");
        Click("Undo");
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
        maskTransformWorkspace.ResizeImage(128, 128);
        Require(maskTransformWorkspace.Session!.Width == 128 && maskTransformWorkspace.Session.Height == 128 &&
            maskTransformWorkspace.Session.GetLayerMask(maskTransformWorkspace.Session.ActiveLayerId!.Value) is { Width: 128, Height: 128 },
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
        var maskWindow = new MainWindow(maskWorkspace);
        maskWindow.Show(); Dispatcher.UIThread.RunJobs();
        Require(Find<Button>(maskWindow, "Save").IsEffectivelyEnabled &&
            Find<Button>(maskWindow, "ToggleMask").IsEffectivelyEnabled &&
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
            ["content"] = "Missing font cache",
            ["fontPostScriptName"] = "Compositor-Missing-Font",
            ["fontSizePoints"] = 18
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

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
