using System.Diagnostics;
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

internal static class NewDocumentChecks
{
    public static void Run(string project, string output)
    {
        byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(project, "manifest.json")));
        var workspace = new EditorWorkspace(); workspace.Open(project);
        var previous = workspace.Session!;
        try { workspace.New(30000, 30000, 72); throw new Exception("Invalid new canvas accepted."); }
        catch (ArgumentOutOfRangeException) { }
        Require(ReferenceEquals(workspace.Session, previous), "Invalid new canvas replaced the current document.");
        var window = new MainWindow(workspace); window.Show(); Dispatcher.UIThread.RunJobs();
        Begin("New");
        var dialog = window.OwnedWindows.Single();
        DialogClick(dialog, "取消"); Pump(window);
        Require(ReferenceEquals(workspace.Session, previous) && !workspace.IsDirty, "Cancel new canvas replaced or modified the document.");
        Begin("New"); dialog = window.OwnedWindows.Single();
        Find<NumericUpDown>(dialog, "NewWidth").Value = 30000;
        Find<NumericUpDown>(dialog, "NewHeight").Value = 30000;
        DialogClick(dialog, "创建"); Dispatcher.UIThread.RunJobs();
        Require(dialog.IsVisible && ReferenceEquals(workspace.Session, previous), "Oversized new canvas closed the dialog or replaced state.");
        Find<NumericUpDown>(dialog, "NewWidth").Value = 259.5m;
        Find<NumericUpDown>(dialog, "NewHeight").Value = 257;
        DialogClick(dialog, "创建"); Dispatcher.UIThread.RunJobs();
        Require(dialog.IsVisible && ReferenceEquals(workspace.Session, previous), "Fractional pixel dimensions were accepted.");
        Find<NumericUpDown>(dialog, "NewWidth").Value = 259;
        Find<NumericUpDown>(dialog, "NewResolution").Value = 300;
        DialogClick(dialog, "创建"); Pump(window);
        Require(!ReferenceEquals(workspace.Session, previous) && workspace.IsDirty && workspace.ProjectDirectory is null &&
            !workspace.Session!.HasBeenSaved && !workspace.Session.Undo() && window.Title!.Contains("未命名"),
            "New canvas did not preserve its unsaved state and identity.");
        Guid firstId = workspace.Session!.ActiveLayerId!.Value;
        Require(firstId != previous.ActiveLayerId && workspace.Session.Layers.Count == 1 &&
            workspace.Preview!.Width == 259 && workspace.Preview.Height == 257 && workspace.Session.GetLayerRaster(firstId).StoredBytes == 0,
            "New canvas is not a transparent initial layer.");
        window.Close(); Dispatcher.UIThread.RunJobs(); DialogClick(window.OwnedWindows.Single(), "取消"); Pump(window);
        Require(window.IsVisible && workspace.IsDirty && workspace.ProjectDirectory is null, "Cancel close discarded an unsaved new canvas.");
        var current = workspace.Session;
        Begin("New"); DialogClick(window.OwnedWindows.Single(), "取消"); Pump(window);
        Require(ReferenceEquals(workspace.Session, current) && workspace.IsDirty, "Canceled document replacement lost unsaved content.");
        Stroke(new Point(40, 40));
        byte[] firstPixels = Pixels(workspace.Session.GetLayerRaster(firstId));
        Require(firstPixels.Any(value => value != 0), "The initial new layer did not receive brush pixels.");
        Click("AddLayer");
        Guid blank = workspace.Session.ActiveLayerId!.Value;
        Require(workspace.Session.Layers.Count == 2 && Selected() == blank && workspace.Session.GetLayerRaster(blank).StoredBytes == 0,
            "Add layer button did not add and select an empty layer.");
        Find<ListBox>(window, "Layers").SelectedItem = workspace.Session.Layers.Single(layer => layer.Id == firstId);
        Require(workspace.Session.ActiveLayerId == firstId, "Layer selection did not reach the formal session.");
        Click("DuplicateLayer"); Guid copy = workspace.Session.ActiveLayerId!.Value;
        Require(copy != firstId && workspace.Session.Layers.Count == 3 && Selected() == copy &&
            Pixels(workspace.Session.GetLayerRaster(copy)).SequenceEqual(firstPixels), "Duplicate button lost identity, pixels or selection.");
        Stroke(new Point(160, 180));
        Require(!Pixels(workspace.Session.GetLayerRaster(copy)).SequenceEqual(firstPixels) &&
            Pixels(workspace.Session.GetLayerRaster(firstId)).SequenceEqual(firstPixels), "Painting a duplicate did not modify its own pixels independently.");
        string saved = Path.Combine(output, "NewWindow.comp");
        workspace.SaveAs(saved); Click("Save");
        Require(!workspace.IsDirty && workspace.ProjectDirectory == saved && window.Title!.Contains("NewWindow.comp"),
            "First save did not establish the window's save point and path.");
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(saved, "manifest.json"))))
            Require(manifest.RootElement.GetProperty("resolution").GetDouble() == 300, "New canvas resolution was not saved.");
        using (var screenshot = new RenderTargetBitmap(new PixelSize(1120, 760), new Vector(96, 96)))
        {
            screenshot.Render(window); screenshot.Save(Path.Combine(output, "new-window.png"));
        }
        var selected = workspace.Session;
        Find<ListBox>(window, "Layers").SelectedItem = selected.Layers.Single(layer => layer.Id == firstId);
        Require(!workspace.IsDirty, "Selecting a layer added a dirty history step.");
        Click("Save");
        Require(ImageProjectWorkflow.OpenEditable(saved).ActiveLayerId == firstId, "Explicit save did not persist selection.");
        Find<NumericUpDown>(window, "LayerOpacity").Value = 25;
        Find<ComboBox>(window, "LayerBlendMode").SelectedItem = "Multiply";
        Click("ApplyAppearance");
        Require(workspace.IsDirty && workspace.Session.Layers.Single(layer => layer.Id == firstId).Opacity == 0.25 &&
            workspace.Session.Layers.Single(layer => layer.Id == firstId).BlendMode == "Multiply",
            "Appearance controls did not commit opacity and blend mode.");
        Click("Undo"); Click("Undo");
        Require(!workspace.IsDirty && workspace.Session.Layers.Single(layer => layer.Id == firstId).Opacity == 1 &&
            workspace.Session.Layers.Single(layer => layer.Id == firstId).BlendMode == "Normal",
            "Appearance undo did not restore the saved state.");
        Click("Redo"); Click("Redo");
        Require(workspace.Session.Layers.Single(layer => layer.Id == firstId).Opacity == 0.25 &&
            workspace.Session.Layers.Single(layer => layer.Id == firstId).BlendMode == "Multiply",
            "Appearance redo did not restore both properties.");
        Click("Save");
        var appearanceReopened = ImageProjectWorkflow.OpenEditable(saved);
        Require(appearanceReopened.Layers.Single(layer => layer.Id == firstId).Opacity == 0.25 &&
            appearanceReopened.Layers.Single(layer => layer.Id == firstId).BlendMode == "Multiply",
            "Saved appearance did not reopen.");
        Guid lastId = Guid.Empty; byte[]? lastPixels = null;
        while (workspace.Session.Layers.Count > 0)
        {
            lastId = workspace.Session.ActiveLayerId!.Value; lastPixels = Pixels(workspace.Session.GetLayerRaster(lastId));
            Click("DeleteLayer");
        }
        var canvas = Find<CanvasView>(window, "Canvas");
        Require(Selected() is null && !canvas.PaintEnabled && !Find<Button>(window, "DeleteLayer").IsEffectivelyEnabled &&
            !Find<Button>(window, "DuplicateLayer").IsEffectivelyEnabled && Find<Button>(window, "AddLayer").IsEffectivelyEnabled,
            "Empty document retained layer tools or disabled add layer.");
        Click("Save");
        Require(ImageProjectWorkflow.OpenEditable(saved).Layers.Count == 0 && Directory.GetFiles(Path.Combine(saved, "images")).Length == 0,
            "Saving an empty document retained deleted assets.");
        Click("Undo");
        Require(workspace.IsDirty && workspace.Session.Layers.Count == 1 && Selected() == lastId &&
            Pixels(workspace.Session.GetLayerRaster(lastId)).SequenceEqual(lastPixels!), "Undo after saved deletion lost pixels or active layer.");
        Click("Save");
        var reopened = ImageProjectWorkflow.OpenEditable(saved);
        Require(reopened.ActiveLayerId == lastId && Pixels(reopened.GetLayerRaster(lastId)).SequenceEqual(lastPixels!),
            "Resaving the restored layer did not recreate the asset.");
        Begin("CanvasSize");
        var resizeDialog = window.OwnedWindows.Single();
        Find<NumericUpDown>(resizeDialog, "ResizeWidth").Value = 240;
        Find<NumericUpDown>(resizeDialog, "ResizeHeight").Value = 250;
        DialogClick(resizeDialog, "应用"); Pump(window);
        Require(workspace.Session.Width == 240 && workspace.Session.Height == 250 && workspace.IsDirty,
            "Canvas size dialog did not commit the requested dimensions.");
        Click("Undo");
        Require(workspace.Session.Width == 259 && workspace.Session.Height == 257 && !workspace.IsDirty,
            "Undo did not restore the saved canvas dimensions.");
        Begin("ImageSize");
        resizeDialog = window.OwnedWindows.Single();
        Find<NumericUpDown>(resizeDialog, "ResizeWidth").Value = 130;
        Find<NumericUpDown>(resizeDialog, "ResizeHeight").Value = 129;
        DialogClick(resizeDialog, "应用"); Pump(window);
        Require(workspace.Session.Width == 130 && workspace.Session.Height == 129 && workspace.IsDirty,
            "Image size dialog did not commit the requested dimensions.");
        Click("Undo");
        Require(workspace.Session.Width == 259 && workspace.Session.Height == 257 && !workspace.IsDirty,
            "Undo did not restore the saved image dimensions.");
        Begin("RotateClockwise"); Pump(window);
        Require(workspace.Session.Width == 257 && workspace.Session.Height == 259 && workspace.IsDirty,
            "Clockwise rotation button did not exchange the document dimensions.");
        Click("Undo");
        Require(workspace.Session.Width == 259 && workspace.Session.Height == 257 && !workspace.IsDirty,
            "Undo did not restore the saved dimensions after rotation.");
        Begin("RotateCounterClockwise"); Pump(window);
        Require(workspace.Session.Width == 257 && workspace.Session.Height == 259 && workspace.IsDirty,
            "Counter-clockwise rotation button did not exchange the document dimensions.");
        Click("Undo");
        Require(workspace.Session.Width == 259 && workspace.Session.Height == 257 && !workspace.IsDirty,
            "Undo did not restore the saved dimensions after counter-clockwise rotation.");
        Begin("RotateClockwise"); Pump(window);
        Click("Save");
        var rotatedReopened = ImageProjectWorkflow.OpenEditable(saved);
        Require(rotatedReopened.Width == 257 && rotatedReopened.Height == 259,
            "Saved rotated document did not reopen with exchanged dimensions.");
        Click("Undo");
        Require(workspace.Session.Width == 259 && workspace.Session.Height == 257 && workspace.IsDirty,
            "Undo after saving rotation did not restore the previous dimensions.");
        Click("Redo");
        Require(workspace.Session.Width == 257 && workspace.Session.Height == 259 && !workspace.IsDirty,
            "Redo after saving rotation did not restore the saved rotated dimensions.");
        Click("Save");
        Require(!workspace.IsDirty, "Saving the reopened rotation state did not restore the save point.");
        window.Close(); Dispatcher.UIThread.RunJobs(); Require(!window.IsVisible, "Saved new document did not close.");
        Require(SHA256.HashData(File.ReadAllBytes(Path.Combine(project, "manifest.json"))).SequenceEqual(sourceHash),
            "New document workflow changed the source project.");
        File.WriteAllText(Path.Combine(output, "new-results.json"), JsonSerializer.Serialize(new
        {
            passed = true, width = 259, height = 257, resolution = 300,
            dialogs = "new cancellation, invalid size/integer validation, creation and unsaved close/replacement cancellation",
            layers = "actual add/duplicate/delete/selection/opacity/blend buttons, brush isolation, empty saved document and undo/resave restoration",
            limits = "Headless only; first chosen save path exercised through workspace SaveAs. Native first-save folder picker/IME/DPI and Windows not executed."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: actual new/cancel/invalid dialogs, layer structure/selection buttons, unsaved close and empty save/undo restoration");

        Guid? Selected() => (Find<ListBox>(window, "Layers").SelectedItem as FlatLayerInfo)?.Id;
        void Begin(string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        void Click(string name) { Require(Find<Button>(window, name).IsEffectivelyEnabled, "Disabled button: " + name); Begin(name); Pump(window); }
        void Stroke(Point point)
        {
            var canvas = Find<CanvasView>(window, "Canvas");
            Point position = canvas.TranslatePoint(canvas.Viewport.ToView(point), window)!.Value;
            window.MouseDown(position, MouseButton.Left); window.MouseUp(position, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        }
    }

    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static void DialogClick(Window dialog, string label) => dialog.GetVisualDescendants().OfType<Button>()
        .Single(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Pump(MainWindow window)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(5);
            if (timer.Elapsed.TotalSeconds > 30) throw new TimeoutException("New document command did not finish.");
        } while (window.IsBusy);
    }
    private static byte[] Pixels(TileRaster raster)
    {
        using var stream = new MemoryStream();
        for (int row = 0; row * 256 < raster.Height; row++)
        for (int column = 0; column * 256 < raster.Width; column++) stream.Write(raster.ReadTileCopy(column, row));
        return stream.ToArray();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
