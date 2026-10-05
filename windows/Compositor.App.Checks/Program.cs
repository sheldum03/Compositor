using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
            "RotateGroupCounterClockwise", "RotateGroupClockwise" })
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
                "dirty title", "PNG/JPEG export", "save-as existing protection", "cancel/save/discard close dialogs", "failed close-save preserves document", "saved layer and pixel roundtrip",
                "grouped-project structure button protection and group-mask availability", "root group/ungroup buttons", "transformed group bake-ungroup" },
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
