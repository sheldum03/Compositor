using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;

public sealed class MainWindow : Window
{
    private readonly DockPanel layout = new() { Margin = new Thickness(12) };
    private readonly CanvasView canvas = new();
    private readonly StackPanel toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
    private readonly DockPanel sidebar = new() { Width = 260, Margin = new Thickness(12, 0, 0, 0) };
    private readonly CheckBox paint = new() { Content = "软笔", Name = "Paint", IsChecked = true };
    private readonly NumericUpDown diameter = new() { Name = "BrushDiameter", Minimum = 1, Maximum = 2000, Value = 40, Width = 90 };
    private readonly NumericUpDown opacity = new() { Name = "BrushOpacity", Minimum = 1, Maximum = 100, Value = 100, Width = 90 };
    private readonly ComboBox color = new() { Name = "BrushColor", ItemsSource = new[] { "黑色", "白色", "蓝色", "橙色" }, SelectedIndex = 0, Width = 90 };
    private readonly ComboBox brushType = new() { Name = "BrushType", ItemsSource = new[] { "软笔", "硬笔" }, SelectedIndex = 0, Width = 75 };
    private readonly StackPanel brushOptions = new() { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 12) };
    private readonly ListBox layers = new() { Name = "Layers" };
    private readonly TextBox layerName = new() { Name = "LayerName", Watermark = "图层名称" };
    private readonly NumericUpDown layerOpacity = new() { Name = "LayerOpacity", Minimum = 0, Maximum = 100, Value = 100, Width = 90 };
    private readonly NumericUpDown layerMoveX = new() { Name = "LayerMoveX", Minimum = -30000, Maximum = 30000, Value = 0, Width = 70 };
    private readonly NumericUpDown layerMoveY = new() { Name = "LayerMoveY", Minimum = -30000, Maximum = 30000, Value = 0, Width = 70 };
    private readonly ComboBox layerBlendMode = new() { Name = "LayerBlendMode", Width = 150 };
    private readonly CheckBox pixelGrid = new() { Name = "PixelGrid", Content = "像素网格" };
    private readonly CheckBox rectangleSelect = new() { Name = "RectSelect", Content = "矩形选区" };
    private readonly CheckBox moveSelection = new() { Name = "MoveSelection", Content = "移动选区" };
    private readonly ComboBox selectionShape = new() { Name = "SelectionShape", Width = 90, ItemsSource = new[] { "矩形", "椭圆", "魔棒", "套索" }, SelectedIndex = 0 };
    private readonly ComboBox wandRadius = new() { Name = "WandRadius", Width = 70, ItemsSource = new[] { "点", "3×3", "5×5" }, SelectedIndex = 0 };
    private readonly NumericUpDown wandTolerance = new() { Name = "WandTolerance", Minimum = 0, Maximum = 255, Value = 0, Width = 65 };
    private readonly CheckBox wandContiguous = new() { Name = "WandContiguous", Content = "连续" , IsChecked = true };
    private readonly ComboBox selectionOperation = new() { Name = "SelectionOperation", Width = 90, ItemsSource = new[] { "替换", "加选", "减选" }, SelectedIndex = 0 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<Button> documentButtons = [];
    private readonly List<Button> layerButtons = [];
    private WriteableBitmap? preview;
    private ProjectSession? displayedSession;
    private Guid? selectedId;
    private bool refreshing, allowClose;
    public EditorWorkspace Workspace { get; }
    public bool IsBusy { get; private set; }

    public MainWindow() : this(new EditorWorkspace()) { }

    public MainWindow(EditorWorkspace workspace)
    {
        Workspace = workspace;
        Width = 1120; Height = 760; MinWidth = 850; MinHeight = 540;
        FontFamily = new FontFamily("avares://Compositor.App/Fonts/SourceHanSansSC-Regular.otf#Source Han Sans SC");
        FontSize = 13;
        RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        toolbar.Children.Add(Command("New", "新建", NewAsync));
        toolbar.Children.Add(Command("Open", "打开工程", OpenAsync));
        toolbar.Children.Add(Command("Import", "导入图片", ImportAsync));
        toolbar.Children.Add(Command("Save", "保存", SaveAsync, document: true));
        toolbar.Children.Add(Command("SaveAs", "另存为", SaveAsAsync, document: true));
        toolbar.Children.Add(Command("Undo", "撤销", () => Task.Run(() => Workspace.Undo()), document: true));
        toolbar.Children.Add(Command("Redo", "重做", () => Task.Run(() => Workspace.Redo()), document: true));
        toolbar.Children.Add(Command("ExportPng", "导出 PNG", () => ExportAsync(false), document: true));
        toolbar.Children.Add(Command("ExportJpeg", "导出 JPEG", () => ExportAsync(true), document: true));
        toolbar.Children.Add(Command("Fit", "适合窗口", () => { canvas.Fit(); return Task.CompletedTask; }, document: true));
        toolbar.Children.Add(Command("ActualSize", "100%", () => { canvas.ActualSize(); return Task.CompletedTask; }, document: true));
        toolbar.Children.Add(Command("CanvasSize", "画布尺寸", () => ResizeAsync(scale: false), document: true));
        toolbar.Children.Add(Command("ImageSize", "图像尺寸", () => ResizeAsync(scale: true), document: true));
        toolbar.Children.Add(pixelGrid);
        toolbar.Children.Add(rectangleSelect);
        toolbar.Children.Add(moveSelection);
        toolbar.Children.Add(selectionShape);
        toolbar.Children.Add(selectionOperation);
        toolbar.Children.Add(new TextBlock { Text = "容差", VerticalAlignment = VerticalAlignment.Center });
        toolbar.Children.Add(wandRadius);
        toolbar.Children.Add(wandTolerance);
        toolbar.Children.Add(wandContiguous);
        toolbar.Children.Add(Command("SelectAll", "全选", SelectAllAsync, document: true));
        toolbar.Children.Add(Command("InvertSelection", "反选", InvertSelectionAsync, document: true));
        toolbar.Children.Add(Command("CopySelection", "复制选区", CopySelectionAsync, document: true));
        toolbar.Children.Add(Command("CopyMergedSelection", "合并复制", CopyMergedSelectionAsync, document: true));
        toolbar.Children.Add(Command("CutSelection", "剪切选区", CutSelectionAsync, document: true));
        toolbar.Children.Add(Command("PasteSelection", "粘贴选区", PasteSelectionAsync, document: true));
        toolbar.Children.Add(Command("LoadAlphaSelection", "从图层 Alpha 载入", LoadAlphaSelectionAsync, document: true));
        toolbar.Children.Add(Command("ClearSelection", "清除选区", ClearSelectionAsync, document: true));
        pixelGrid.IsCheckedChanged += (_, _) => { canvas.PixelGridEnabled = pixelGrid.IsChecked == true; canvas.InvalidateVisual(); };
        selectionShape.SelectionChanged += (_, _) =>
        {
            canvas.LassoEnabled = selectionShape.SelectedIndex == 3;
            bool magic = selectionShape.SelectedIndex == 2;
            wandRadius.IsEnabled = magic; wandTolerance.IsEnabled = magic; wandContiguous.IsEnabled = magic;
        };
        wandRadius.IsEnabled = wandTolerance.IsEnabled = wandContiguous.IsEnabled = false;
        rectangleSelect.IsCheckedChanged += (_, _) =>
        {
            canvas.SelectionEnabled = rectangleSelect.IsChecked == true;
            if (canvas.SelectionEnabled) { paint.IsChecked = false; moveSelection.IsChecked = false; }
            canvas.PaintEnabled = selectedId is not null && paint.IsChecked == true;
        };
        moveSelection.IsCheckedChanged += (_, _) =>
        {
            canvas.SelectionMoveEnabled = moveSelection.IsChecked == true;
            if (canvas.SelectionMoveEnabled) { rectangleSelect.IsChecked = false; paint.IsChecked = false; }
            canvas.PaintEnabled = selectedId is not null && paint.IsChecked == true;
        };
        DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar);
        brushOptions.Children.Add(paint);
        brushOptions.Children.Add(new TextBlock { Text = "直径", VerticalAlignment = VerticalAlignment.Center });
        brushOptions.Children.Add(diameter);
        brushOptions.Children.Add(new TextBlock { Text = "不透明度 %", VerticalAlignment = VerticalAlignment.Center });
        brushOptions.Children.Add(opacity); brushOptions.Children.Add(brushType); brushOptions.Children.Add(color);
        brushOptions.Children.Add(new TextBlock { Text = "滚轮缩放 · 空格/中键平移 · Esc 取消笔划", VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(brushOptions, Dock.Top); layout.Children.Add(brushOptions);
        var footer = new StackPanel { Spacing = 5, Margin = new Thickness(0, 10, 0, 0), Children =
        {
            status,
            new TextBlock { Text = "内部集成版 · 当前支持全画布普通图层与软笔。选区、变换和其他工具仍在开发。", Foreground = Brushes.DimGray }
        } };
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var heading = new TextBlock { Text = "图层", FontSize = 17, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(heading, Dock.Top); sidebar.Children.Add(heading);
        var actions = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var structure = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        structure.Children.Add(Command("AddLayer", "新增图层", AddLayerAsync, document: true));
        structure.Children.Add(Command("DuplicateLayer", "复制", DuplicateLayerAsync, layer: true));
        structure.Children.Add(Command("DeleteLayer", "删除", DeleteLayerAsync, layer: true));
        structure.Children.Add(Command("FlipLayerHorizontal", "水平翻转", () => FlipLayerAsync(true), layer: true));
        structure.Children.Add(Command("FlipLayerVertical", "垂直翻转", () => FlipLayerAsync(false), layer: true));
        actions.Children.Add(structure);
        actions.Children.Add(layerName);
        actions.Children.Add(Command("Rename", "应用名称", RenameAsync, layer: true));
        var appearance = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        appearance.Children.Add(new TextBlock { Text = "透明度 %", VerticalAlignment = VerticalAlignment.Center });
        appearance.Children.Add(layerOpacity);
        appearance.Children.Add(layerBlendMode);
        actions.Children.Add(appearance);
        var move = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        move.Children.Add(new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center });
        move.Children.Add(layerMoveX);
        move.Children.Add(new TextBlock { Text = "Y", VerticalAlignment = VerticalAlignment.Center });
        move.Children.Add(layerMoveY);
        move.Children.Add(Command("MoveLayer", "移动图层", MoveLayerAsync, layer: true));
        actions.Children.Add(move);
        actions.Children.Add(Command("ApplyAppearance", "应用外观", AppearanceAsync, layer: true));
        actions.Children.Add(Command("Visibility", "显示 / 隐藏", VisibilityAsync, layer: true));
        var reorder = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        reorder.Children.Add(Command("MoveUp", "上移", () => MoveAsync(1), layer: true));
        reorder.Children.Add(Command("MoveDown", "下移", () => MoveAsync(-1), layer: true));
        actions.Children.Add(reorder);
        DockPanel.SetDock(actions, Dock.Bottom); sidebar.Children.Add(actions);
        layers.ItemTemplate = new FuncDataTemplate<FlatLayerInfo>((item, _) => new TextBlock
        {
            Text = item is null ? "" : (item.IsVisible ? "●  " : "○  ") + item.Name,
            Margin = new Thickness(5), TextTrimming = TextTrimming.CharacterEllipsis
        });
        layers.SelectionChanged += (_, _) => { if (!refreshing) UpdateSelection(); };
        layerBlendMode.ItemsSource = ProjectSession.SupportedBlendModes;
        sidebar.Children.Add(layers);
        DockPanel.SetDock(sidebar, Dock.Right); layout.Children.Add(sidebar);
        layout.Children.Add(canvas);
        canvas.StrokeStarted += point => PaintStep(() =>
        {
            if (selectedId is not { } id) return;
            double[] selectedColor = color.SelectedIndex switch
            {
                1 => [1, 1, 1], 2 => [0.1, 0.3, 0.9], 3 => [1, 0.3, 0.1], _ => [0, 0, 0]
            };
            Workspace.BeginStroke(id, new SoftBrushSettings((int)(diameter.Value ?? 40),
                (double)(opacity.Value ?? 100) / 100, selectedColor, brushType.SelectedIndex == 1 ? 1 : 0),
                new BrushPoint(point.X, point.Y));
        });
        canvas.StrokeMoved += point => PaintStep(() => Workspace.AppendStroke(new BrushPoint(point.X, point.Y)));
        canvas.StrokeFinished += point => PaintStep(() => Workspace.CommitStroke(new BrushPoint(point.X, point.Y)));
        canvas.StrokeCanceled += () => PaintStep(Workspace.CancelStroke);
        paint.IsCheckedChanged += (_, _) =>
        {
            if (paint.IsChecked == true) rectangleSelect.IsChecked = false;
            if (paint.IsChecked == true) moveSelection.IsChecked = false;
            canvas.PaintEnabled = selectedId is not null && paint.IsChecked == true;
        };
        canvas.SelectionFinished += rectangle =>
        {
            try
            {
                var operation = selectionOperation.SelectedIndex switch
                {
                    1 => GraySelectionOperation.Add,
                    2 => GraySelectionOperation.Subtract,
                    _ => GraySelectionOperation.Replace
                };
                if (selectionShape.SelectedIndex == 2)
                    Workspace.SelectMagicWand(rectangle.Position, (int)(wandTolerance.Value ?? 0), wandRadius.SelectedIndex,
                        wandContiguous.IsChecked == true, operation);
                else if (selectionShape.SelectedIndex == 1) Workspace.SelectEllipse(rectangle, operation);
                else Workspace.SelectRectangle(rectangle, operation);
                canvas.SetSelectionRect(Workspace.SelectionBounds); canvas.SetSelectionOutline(Workspace.SelectionOutline);
                moveSelection.IsEnabled = Workspace.HasSelection; status.Text = "选区已更新。";
            }
            catch (Exception error)
            {
                canvas.SetSelectionRect(Workspace.SelectionBounds); canvas.SetSelectionOutline(Workspace.SelectionOutline);
                status.Text = "选区未完成：" + error.Message;
            }
        };
        canvas.LassoFinished += points =>
        {
            try
            {
                Workspace.SelectLasso(points, SelectionOperation());
                canvas.SetSelectionRect(Workspace.SelectionBounds); canvas.SetSelectionOutline(Workspace.SelectionOutline);
                moveSelection.IsEnabled = Workspace.HasSelection; status.Text = "选区已更新。";
            }
            catch (Exception error) { canvas.SetSelectionRect(Workspace.SelectionBounds); status.Text = "选区未完成：" + error.Message; }
        };
        canvas.SelectionMoveFinished += (start, end) =>
        {
            try
            {
                int offsetX = (int)Math.Round(end.X - start.X), offsetY = (int)Math.Round(end.Y - start.Y);
                Workspace.MoveSelection(offsetX, offsetY);
                canvas.SetSelectionRect(Workspace.SelectionBounds); canvas.SetSelectionOutline(Workspace.SelectionOutline);
                moveSelection.IsEnabled = Workspace.HasSelection; status.Text = "选区像素已移动。";
            }
            catch (Exception error) { canvas.SetSelectionRect(Workspace.SelectionBounds); status.Text = "选区未移动：" + error.Message; }
        };
        canvas.SelectionCanceled += () => canvas.SetSelectionRect(Workspace.SelectionBounds);
        Content = layout;
        Deactivated += (_, _) => canvas.Cancel();
        Closing += (_, e) =>
        {
            if (allowClose) return;
            if (IsBusy) { e.Cancel = true; return; }
            if (Workspace.HasActiveStroke) { canvas.Cancel(); Workspace.CancelStroke(); }
            if (!Workspace.IsDirty) return;
            e.Cancel = true;
            _ = ExecuteAsync(async () =>
            {
                if (await ConfirmDiscardAsync()) { allowClose = true; Close(); }
            });
        };
        Closed += (_, _) => { canvas.Cancel(); canvas.SetBitmap(null); preview?.Dispose(); preview = null; };
        KeyDown += (_, e) =>
        {
            if (IsBusy || Workspace.HasActiveStroke) return;
            // Text fields retain their own editing shortcuts and IME behavior.
            if (e.Source is TextBox || layerName.IsKeyboardFocusWithin) return;
            Func<Task>? command = e.KeyModifiers switch
            {
                KeyModifiers.Control => e.Key switch
                {
                    Key.N => NewAsync,
                    Key.S when Workspace.Session is not null => SaveAsync,
                    Key.Z when Workspace.Session is not null => () => Task.Run(() => Workspace.Undo()),
                    Key.Y when Workspace.Session is not null => () => Task.Run(() => Workspace.Redo()),
                    Key.O => OpenAsync,
                    Key.A when Workspace.Session is not null => SelectAllAsync,
                    _ => null
                },
                KeyModifiers.Control | KeyModifiers.Shift when e.Key == Key.I && Workspace.HasSelection => InvertSelectionAsync,
                _ => null
            };
            if (command is not null) { e.Handled = true; _ = ExecuteAsync(command); }
        };
        Refresh();
        status.Text = Workspace.Session is null ? "新建画布、打开 .comp 工程文件夹，或导入 PNG / JPEG 图片开始。" : "工程已打开。";
    }

    private Button Command(string name, string title, Func<Task> action, bool document = false, bool layer = false)
    {
        var button = new Button { Name = name, Content = title };
        button.Click += async (_, _) => await ExecuteAsync(action);
        if (document) documentButtons.Add(button);
        if (layer) layerButtons.Add(button);
        return button;
    }

    private async Task ExecuteAsync(Func<Task> operation)
    {
        if (IsBusy || Workspace.HasActiveStroke) return;
        IsBusy = true; layout.IsEnabled = false; status.Text = "处理中…";
        string message;
        try { await operation(); message = "操作完成。"; }
        catch (Exception error) { message = "操作未完成：" + error.Message; }
        finally { IsBusy = false; layout.IsEnabled = true; }
        if (allowClose) return;
        try { Refresh(); }
        catch (Exception error) { message = "预览未完成：" + error.Message; }
        status.Text = message;
    }

    private void RefreshPreview()
    {
        var next = Workspace.Preview is { } raster ? RasterBitmap.Create(raster) : null;
        canvas.SetBitmap(next);
        preview?.Dispose(); preview = next;
    }

    private void PaintStep(Action step)
    {
        try
        {
            step();
            if (Workspace.HasActiveStroke) { RefreshPreview(); status.Text = "绘制中… 松开鼠标提交，Esc 取消。"; }
            else { Refresh(); status.Text = "笔划已结束。"; }
        }
        catch (Exception error)
        {
            canvas.Cancel(); Workspace.CancelStroke(); Refresh();
            status.Text = "绘制未完成：" + error.Message;
        }
        toolbar.IsEnabled = sidebar.IsEnabled = brushOptions.IsEnabled = !Workspace.HasActiveStroke;
    }

    private void Refresh()
    {
        Title = (Workspace.IsDirty ? "● " : "") +
            (Workspace.ProjectDirectory is { } path ? Path.GetFileName(path) + " — " : Workspace.Session is not null ? "未命名 — " : "") + "Compositor";
        RefreshPreview();
        if (!ReferenceEquals(displayedSession, Workspace.Session)) canvas.Fit();
        displayedSession = Workspace.Session;
        canvas.SetSelectionRect(Workspace.SelectionBounds);
        canvas.SetSelectionOutline(Workspace.SelectionOutline);
        refreshing = true;
        var items = Workspace.Session?.Layers.Reverse().ToArray() ?? [];
        layers.ItemsSource = items;
        layers.SelectedItem = items.FirstOrDefault(layer => layer.Id == Workspace.Session?.ActiveLayerId) ?? items.FirstOrDefault();
        refreshing = false;
        foreach (var button in documentButtons) button.IsEnabled = Workspace.Session is not null;
        pixelGrid.IsEnabled = Workspace.Session is not null;
        rectangleSelect.IsEnabled = Workspace.Session is not null;
        moveSelection.IsEnabled = Workspace.Session is not null && Workspace.HasSelection;
        selectionShape.IsEnabled = Workspace.Session is not null;
        selectionOperation.IsEnabled = Workspace.Session is not null;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selected = layers.SelectedItem as FlatLayerInfo;
        selectedId = selected?.Id;
        refreshing = true;
        try
        {
            if (selectedId is { } id) Workspace.Session!.SelectLayer(id);
            layerName.Text = selected?.Name ?? "";
            layerOpacity.Value = selected is null ? 100 : (decimal)(selected.Opacity * 100);
            layerBlendMode.SelectedItem = selected?.BlendMode ?? "Normal";
        }
        finally { refreshing = false; }
        layerName.IsEnabled = selected is not null;
        layerOpacity.IsEnabled = selected is not null;
        layerBlendMode.IsEnabled = selected is not null;
        canvas.PaintEnabled = selected is not null && paint.IsChecked == true;
        foreach (var button in layerButtons) button.IsEnabled = selected is not null;
    }

    private Task EditAsync(Action<ProjectSession> edit) => Task.Run(() => Workspace.Edit(edit));
    private Task AddLayerAsync() => EditAsync(session =>
    {
        int index = selectedId is { } id ? session.Layers.ToList().FindIndex(layer => layer.Id == id) + 1 : session.Layers.Count;
        session.AddBlankLayer("Layer " + (session.Layers.Count + 1), index);
    });
    private Task DuplicateLayerAsync()
    {
        Guid id = selectedId!.Value;
        return EditAsync(session => session.DuplicateLayer(id, session.Layers.Single(layer => layer.Id == id).Name));
    }
    private Task DeleteLayerAsync()
    {
        Guid id = selectedId!.Value;
        return EditAsync(session => session.DeleteLayer(id));
    }

    private Task FlipLayerAsync(bool horizontal) => Task.Run(() => Workspace.FlipActiveLayer(horizontal));

    private Task MoveLayerAsync()
    {
        int offsetX = (int)(layerMoveX.Value ?? 0), offsetY = (int)(layerMoveY.Value ?? 0);
        return Task.Run(() => Workspace.MoveActiveLayer(offsetX, offsetY));
    }
    private Task RenameAsync()
    {
        Guid id = selectedId!.Value;
        string name = layerName.Text ?? "";
        return EditAsync(s => s.RenameLayer(id, name));
    }
    private Task AppearanceAsync()
    {
        Guid id = selectedId!.Value;
        double opacityValue = (double)(layerOpacity.Value ?? 100) / 100;
        string mode = layerBlendMode.SelectedItem as string ?? "Normal";
        return EditAsync(session =>
        {
            session.SetLayerOpacity(id, opacityValue);
            session.SetLayerBlendMode(id, mode);
        });
    }
    private Task VisibilityAsync()
    {
        Guid id = selectedId!.Value;
        return EditAsync(s => s.SetLayerVisible(id, !s.Layers.Single(layer => layer.Id == id).IsVisible));
    }
    private Task ClearSelectionAsync()
    {
        Workspace.ClearSelection();
        canvas.SetSelectionRect(null);
        moveSelection.IsEnabled = false;
        return Task.CompletedTask;
    }

    private Task SelectAllAsync()
    {
        Workspace.SelectAll();
        canvas.SetSelectionRect(Workspace.SelectionBounds);
        canvas.SetSelectionOutline(Workspace.SelectionOutline);
        moveSelection.IsEnabled = Workspace.HasSelection;
        return Task.CompletedTask;
    }

    private Task InvertSelectionAsync()
    {
        Workspace.InvertSelection();
        canvas.SetSelectionRect(Workspace.SelectionBounds);
        canvas.SetSelectionOutline(Workspace.SelectionOutline);
        moveSelection.IsEnabled = Workspace.HasSelection;
        return Task.CompletedTask;
    }

    private Task CopySelectionAsync()
    {
        Workspace.CopySelection();
        return Task.CompletedTask;
    }

    private Task CopyMergedSelectionAsync()
    {
        Workspace.CopyMergedSelection();
        return Task.CompletedTask;
    }

    private Task CutSelectionAsync() => Task.Run(Workspace.CutSelection);

    private Task PasteSelectionAsync() => Task.Run(Workspace.PasteSelection);

    private Task LoadAlphaSelectionAsync()
    {
        Workspace.SelectLayerAlpha();
        canvas.SetSelectionRect(Workspace.SelectionBounds);
        moveSelection.IsEnabled = Workspace.HasSelection;
        return Task.CompletedTask;
    }

    private GraySelectionOperation SelectionOperation() => selectionOperation.SelectedIndex switch
    {
        1 => GraySelectionOperation.Add,
        2 => GraySelectionOperation.Subtract,
        _ => GraySelectionOperation.Replace
    };
    private Task MoveAsync(int offset)
    {
        Guid id = selectedId!.Value;
        return EditAsync(s =>
        {
            int index = s.Layers.ToList().FindIndex(layer => layer.Id == id);
            int destination = index + offset;
            if (destination >= 0 && destination < s.Layers.Count) s.MoveLayer(id, destination);
        });
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!Workspace.IsDirty) return true;
        string? decision = await ChoiceAsync("未保存的修改", "是否保存当前工程的修改？",
            ("保存", "save"), ("不保存", "discard"), ("取消", "cancel"));
        if (decision == "save") { await SaveAsync(); return !Workspace.IsDirty; }
        return decision == "discard";
    }

    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var dialog = Dialog("新建画布");
        var width = new NumericUpDown { Name = "NewWidth", Minimum = 1, Maximum = 30000, Value = 1920, Width = 220 };
        var height = new NumericUpDown { Name = "NewHeight", Minimum = 1, Maximum = 30000, Value = 1080, Width = 220 };
        var resolution = new NumericUpDown { Name = "NewResolution", Minimum = 1, Maximum = 9600, Value = 72, Width = 220 };
        var error = new TextBlock { Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
        var submit = new Button { Name = "CreateDocument", Content = "创建" };
        var cancel = new Button { Content = "取消" };
        bool creating = false;
        cancel.Click += (_, _) => dialog.Close();
        dialog.Closing += (_, e) => { if (creating) e.Cancel = true; };
        submit.Click += async (_, _) =>
        {
            if (width.Value is not { } w || height.Value is not { } h || resolution.Value is not { } r ||
                w != decimal.Truncate(w) || h != decimal.Truncate(h))
            { error.Text = "请输入整数像素宽高和有效分辨率。"; return; }
            if (w * h > 100_000_000) { error.Text = "画布总像素不得超过一亿。"; return; }
            creating = true; submit.IsEnabled = cancel.IsEnabled = false;
            try { await Task.Run(() => Workspace.New((int)w, (int)h, (double)r)); creating = false; dialog.Close(); }
            catch (Exception exception) { error.Text = exception.Message; }
            finally { creating = false; submit.IsEnabled = cancel.IsEnabled = true; }
        };
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 10, Children =
        {
            new TextBlock { Text = "宽度（像素）" }, width, new TextBlock { Text = "高度（像素）" }, height,
            new TextBlock { Text = "分辨率（DPI）" }, resolution, error,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { submit, cancel } }
        } };
        await dialog.ShowDialog(this);
    }

    private async Task ResizeAsync(bool scale)
    {
        if (Workspace.Session is not { } session) return;
        var dialog = Dialog(scale ? "图像尺寸" : "画布尺寸");
        var width = new NumericUpDown { Name = "ResizeWidth", Minimum = 1, Maximum = 30000, Value = session.Width, Width = 220 };
        var height = new NumericUpDown { Name = "ResizeHeight", Minimum = 1, Maximum = 30000, Value = session.Height, Width = 220 };
        var error = new TextBlock { Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
        var submit = new Button { Name = "ApplyResize", Content = "应用" };
        var cancel = new Button { Content = "取消" };
        bool resizing = false;
        cancel.Click += (_, _) => dialog.Close();
        dialog.Closing += (_, e) => { if (resizing) e.Cancel = true; };
        submit.Click += async (_, _) =>
        {
            if (width.Value is not { } w || height.Value is not { } h || w != decimal.Truncate(w) || h != decimal.Truncate(h))
            { error.Text = "请输入整数像素宽高。"; return; }
            if (w * h > 100_000_000) { error.Text = "画布总像素不得超过一亿。"; return; }
            resizing = true; submit.IsEnabled = cancel.IsEnabled = false;
            try
            {
                if (scale) await Task.Run(() => Workspace.ResizeImage((int)w, (int)h));
                else await Task.Run(() => Workspace.ResizeCanvas((int)w, (int)h));
                resizing = false; dialog.Close();
            }
            catch (Exception exception) { error.Text = exception.Message; }
            finally { resizing = false; submit.IsEnabled = cancel.IsEnabled = true; }
        };
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 10, Children =
        {
            new TextBlock { Text = "宽度（像素）" }, width, new TextBlock { Text = "高度（像素）" }, height, error,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { submit, cancel } }
        } };
        await dialog.ShowDialog(this);
    }

    private Task SaveAsync() => Workspace.ProjectDirectory is null ? SaveAsAsync() : Task.Run(Workspace.Save);

    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "选择 .comp 工程文件夹", AllowMultiple = false });
        if (selected.Count != 0) await Task.Run(() => Workspace.Open(LocalPath(selected[0])));
    }

    private async Task ImportAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入图片", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG / JPEG") { Patterns = ["*.png", "*.jpg", "*.jpeg"] }]
        });
        if (selected.Count == 0) return;
        string source = LocalPath(selected[0]);
        string? target = await NewProjectPathAsync(Path.GetFileNameWithoutExtension(source));
        if (target is not null) await Task.Run(() => Workspace.Import(source, target));
    }

    private async Task SaveAsAsync()
    {
        string? path = await NewProjectPathAsync(Workspace.ProjectDirectory is { } current ? Path.GetFileNameWithoutExtension(current) + " 副本" : "未命名");
        if (path is not null) await Task.Run(() => Workspace.SaveAs(path));
    }

    private async Task<string?> NewProjectPathAsync(string suggestion)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "选择新工程的存放位置", AllowMultiple = false });
        if (folders.Count == 0) return null;
        var input = new TextBox { Text = suggestion, MinWidth = 320 };
        var dialog = Dialog("工程名称");
        var submit = new Button { Content = "创建工程" };
        var error = new TextBlock { Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap };
        submit.Click += (_, _) =>
        {
            string name = (input.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 ||
                name.Any(char.IsControl) || name.EndsWith('.') || name is "." or "..")
            { error.Text = "请输入有效的工程名称。"; return; }
            if (!name.EndsWith(".comp", StringComparison.OrdinalIgnoreCase)) name += ".comp";
            string path = Path.Combine(LocalPath(folders[0]), name);
            if (Path.Exists(path)) { error.Text = "同名工程已存在，请更换名称。"; return; }
            dialog.Close(path);
        };
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children = { input, error, submit } };
        return await dialog.ShowDialog<string?>(this);
    }

    private async Task ExportAsync(bool jpeg)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = jpeg ? "导出 JPEG（白色背景，质量 95）" : "导出透明 PNG",
            SuggestedFileName = (Path.GetFileNameWithoutExtension(Workspace.ProjectDirectory) ?? "未命名") + (jpeg ? ".jpg" : ".png"),
            DefaultExtension = jpeg ? "jpg" : "png", ShowOverwritePrompt = false,
            FileTypeChoices = [new FilePickerFileType(jpeg ? "JPEG" : "PNG") { Patterns = [jpeg ? "*.jpg" : "*.png"] }]
        });
        if (file is null) return;
        string path = LocalPath(file);
        if (Path.Exists(path)) throw new IOException("文件已存在，请选择新的导出文件名。");
        await Task.Run(() => Workspace.Export(path, jpeg));
    }

    private async Task<string?> ChoiceAsync(string title, string message, params (string Label, string Value)[] choices)
    {
        var dialog = Dialog(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var choice in choices)
        {
            var button = new Button { Content = choice.Label };
            button.Click += (_, _) => dialog.Close(choice.Value);
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 18, Children =
            { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, buttons } };
        return await dialog.ShowDialog<string?>(this);
    }

    private Window Dialog(string title) => new()
    {
        Title = title, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily, FontSize = FontSize
    };

    private static string LocalPath(IStorageItem item) => item.TryGetLocalPath()
        ?? throw new NotSupportedException("请选择本地文件或文件夹。");
}
