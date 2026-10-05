using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
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
        if (nativeAvailable)
        {
            Find<ComboBox>(window, "SelectionShape").SelectedItem = "魔棒";
            Find<ComboBox>(window, "SelectionOperation").SelectedItem = "替换";
            window.MouseDown(DocumentPoint(new Point(128, 128)), MouseButton.Left);
            window.MouseUp(DocumentPoint(new Point(128, 128)), MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Require(workspace.HasSelection && workspace.SelectedPixels > 0 && !workspace.IsDirty,
                $"Native magic wand did not create a non-dirty selection: has={workspace.HasSelection}, pixels={workspace.SelectedPixels}.");
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
        File.WriteAllText(Path.Combine(output, "canvas-results.json"), JsonSerializer.Serialize(new
        {
            passed = true, referenceCases = cases.Length, imageWidth = original.Width, imageHeight = original.Height, provisionalAndFinalExact = true,
            pointerStroke = "single commit, Escape/capture-loss/close cancellation, source snapshot and other layer preserved",
            viewport = "zoom anchor, middle-button and Space plus left-button pan in logical coordinates, exact 100% and pixel grid",
            selection = nativeAvailable ? "rectangle/ellipse/combined/native-wand masks, non-dirty selection state, brush clipping, undo and clear" : "rectangle/ellipse/combined mask, non-dirty selection state, brush clipping, undo and clear",
            limits = "Headless input; no native Windows/DPI/pressure or S02 performance claim."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: production soft brush matches fixed M1 pixels, real pointer commit/cancel, viewport and saved export");

        Point WindowPoint(Point point) => canvas.TranslatePoint(point, window) ?? throw new Exception("Canvas is detached.");
        Point DocumentPoint(Point point) => WindowPoint(canvas.Viewport.ToView(point));
        void Click(string name)
        {
            Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
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
