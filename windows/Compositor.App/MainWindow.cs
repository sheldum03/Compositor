using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Compositor.Core;

namespace Compositor.App;

public sealed class MainWindow : Window
{
    private readonly DockPanel layout = new() { Margin = new Thickness(12) };
    private readonly Image canvas = new() { Stretch = Stretch.Uniform, Margin = new Thickness(20) };
    private readonly ListBox layers = new() { Name = "Layers" };
    private readonly TextBox layerName = new() { Name = "LayerName", Watermark = "图层名称" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<Button> documentButtons = [];
    private readonly List<Button> layerButtons = [];
    private WriteableBitmap? preview;
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
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        toolbar.Children.Add(Command("Open", "打开工程", OpenAsync));
        toolbar.Children.Add(Command("Import", "导入图片", ImportAsync));
        toolbar.Children.Add(Command("Save", "保存", () => Task.Run(Workspace.Save), document: true));
        toolbar.Children.Add(Command("SaveAs", "另存为", SaveAsAsync, document: true));
        toolbar.Children.Add(Command("Undo", "撤销", () => EditAsync(s => s.Undo()), document: true));
        toolbar.Children.Add(Command("Redo", "重做", () => EditAsync(s => s.Redo()), document: true));
        toolbar.Children.Add(Command("ExportPng", "导出 PNG", () => ExportAsync(false), document: true));
        toolbar.Children.Add(Command("ExportJpeg", "导出 JPEG", () => ExportAsync(true), document: true));
        DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar);
        var footer = new StackPanel { Spacing = 5, Margin = new Thickness(0, 10, 0, 0), Children =
        {
            status,
            new TextBlock { Text = "内部集成版 · 当前支持全画布普通图层的工程操作。画布工具仍在开发。", Foreground = Brushes.DimGray }
        } };
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var sidebar = new DockPanel { Width = 260, Margin = new Thickness(12, 0, 0, 0) };
        var heading = new TextBlock { Text = "图层", FontSize = 17, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(heading, Dock.Top); sidebar.Children.Add(heading);
        var actions = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(layerName);
        actions.Children.Add(Command("Rename", "应用名称", RenameAsync, layer: true));
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
        sidebar.Children.Add(layers);
        DockPanel.SetDock(sidebar, Dock.Right); layout.Children.Add(sidebar);
        layout.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#D4D4D4")), Child = canvas });
        Content = layout;
        Closing += (_, e) =>
        {
            if (allowClose) return;
            if (IsBusy) { e.Cancel = true; return; }
            if (!Workspace.IsDirty) return;
            e.Cancel = true;
            _ = ExecuteAsync(async () =>
            {
                if (await ConfirmDiscardAsync()) { allowClose = true; Close(); }
            });
        };
        Closed += (_, _) => { canvas.Source = null; preview?.Dispose(); preview = null; };
        KeyDown += (_, e) =>
        {
            if (IsBusy || e.KeyModifiers != KeyModifiers.Control) return;
            // Text fields retain their own editing shortcuts and IME behavior.
            if (e.Source is TextBox || layerName.IsKeyboardFocusWithin) return;
            Func<Task>? command = e.Key switch
            {
                Key.S when Workspace.Session is not null => () => Task.Run(Workspace.Save),
                Key.Z when Workspace.Session is not null => () => EditAsync(s => s.Undo()),
                Key.Y when Workspace.Session is not null => () => EditAsync(s => s.Redo()),
                Key.O => OpenAsync,
                _ => null
            };
            if (command is not null) { e.Handled = true; _ = ExecuteAsync(command); }
        };
        Refresh();
        status.Text = Workspace.Session is null ? "打开 .comp 工程文件夹，或导入 PNG / JPEG 图片开始。" : "工程已打开。";
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
        if (IsBusy) return;
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

    private void Refresh()
    {
        Title = (Workspace.IsDirty ? "● " : "") +
            (Workspace.ProjectDirectory is { } path ? Path.GetFileName(path) + " — " : "") + "Compositor";
        var nextPreview = Workspace.Preview is { } raster ? RasterBitmap.Create(raster) : null;
        canvas.Source = nextPreview;
        preview?.Dispose(); preview = nextPreview;
        refreshing = true;
        var items = Workspace.Session?.Layers.Reverse().ToArray() ?? [];
        layers.ItemsSource = items;
        layers.SelectedItem = items.FirstOrDefault(layer => layer.Id == selectedId) ?? items.FirstOrDefault();
        refreshing = false;
        foreach (var button in documentButtons) button.IsEnabled = Workspace.Session is not null;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selected = layers.SelectedItem as FlatLayerInfo;
        selectedId = selected?.Id;
        layerName.Text = selected?.Name ?? "";
        layerName.IsEnabled = selected is not null;
        foreach (var button in layerButtons) button.IsEnabled = selected is not null;
    }

    private Task EditAsync(Action<ProjectSession> edit) => Task.Run(() => Workspace.Edit(edit));
    private Task RenameAsync()
    {
        Guid id = selectedId!.Value;
        string name = layerName.Text ?? "";
        return EditAsync(s => s.RenameLayer(id, name));
    }
    private Task VisibilityAsync()
    {
        Guid id = selectedId!.Value;
        return EditAsync(s => s.SetLayerVisible(id, !s.Layers.Single(layer => layer.Id == id).IsVisible));
    }
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
        if (decision == "save") { await Task.Run(Workspace.Save); return !Workspace.IsDirty; }
        return decision == "discard";
    }

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
        string? path = await NewProjectPathAsync(Path.GetFileNameWithoutExtension(Workspace.ProjectDirectory) + " 副本");
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
            SuggestedFileName = Path.GetFileNameWithoutExtension(Workspace.ProjectDirectory) + (jpeg ? ".jpg" : ".png"),
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
