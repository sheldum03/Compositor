using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Simple;
using Avalonia.Threading;

internal static class WindowProbe
{
    internal static void Run(string fixtures, string output, string native)
    {
        AppBuilder.Configure(() => new ProbeApplication(fixtures, output, native))
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software], CompositionMode = [Win32CompositionMode.RedirectionSurface] })
            .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
            .StartWithClassicDesktopLifetime([]);
    }

    private sealed class ProbeApplication(string fixtures, string output, string native) : Application
    {
        public override void Initialize() => Styles.Add(new SimpleTheme());
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new ProbeWindow(fixtures, output, native);
            base.OnFrameworkInitializationCompleted();
        }
    }

    private sealed class ProbeWindow : Window
    {
        private readonly string fixtures, output, native;
        private readonly ConcurrentQueue<object> events = new();
        private readonly TextBlock status = new() { Text = "请选择文字、笔刷或合成页进行测试。", TextWrapping = TextWrapping.Wrap };
        private readonly object sceneGate = new();
        private readonly FixtureScene scene;
        private readonly WindowBrushView brush;
        private readonly Canvas textCanvas = new() { Width = 760, Height = 520 };
        private readonly SpacedTextPresenter presenter = new(420, 3);
        private readonly TextBox editor;
        private bool closed, compositionThreadRecorded;
        private int saveIndex, preeditEvents, textInputEvents;
        private double angle = 13, zoom = 1;
        private bool flipped;

        internal ProbeWindow(string fixtures, string output, string native)
        {
            this.fixtures = fixtures; this.output = output; this.native = native;
            Title = "Compositor — Windows 原型验证";
            Width = 1000; Height = 880; MinWidth = 700; MinHeight = 520;
            scene = FixtureScene.Read(Path.Combine(fixtures, "F04.comp"));
            brush = new WindowBrushView(Record); brush.Initialize();
            editor = TextProbe.Editor(presenter, new FontFamily(TextProbe.FontUri + "#Source Han Sans SC"),
                "中文 English 🙂\n请切换微软拼音，在这里输入、取消和确认。", 24, 1.25, TextAlignment.Left,
                new SolidColorBrush(Color.FromArgb(204, 38, 102, 179)));
            editor.Width = 420; editor.Height = 260; editor.Opacity = 0.65;
            editor.IsUndoEnabled = true; editor.ClipToBounds = false;
            editor.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
            textCanvas.Children.Add(editor);
            RenderOptions.SetTextRenderingMode(textCanvas, TextRenderingMode.Antialias);
            TransformText();
            presenter.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextPresenter.PreeditTextProperty)
                {
                    preeditEvents++;
                    Record("native-preedit-change", new { length = presenter.PreeditText?.Length ?? 0 });
                }
            };
            editor.AddHandler(InputElement.TextInputEvent, (_, e) =>
            {
                textInputEvents++; Record("text-input", new { length = e.Text?.Length ?? 0 });
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            editor.GotFocus += (_, _) => Record("text-focus", true);
            editor.LostFocus += (_, _) => Record("text-focus", false);

            var tabs = new TabControl
            {
                ItemsSource = new[]
                {
                    new TabItem { Header = "文字 / 输入法", Content = Scroll(TextPage()) },
                    new TabItem { Header = "4K 笔刷", Content = Scroll(BrushPage()) },
                    new TabItem { Header = "合成 / 工程", Content = Scroll(CompositionPage()) }
                }
            };
            tabs.SelectionChanged += (_, _) => { brush.Cancel(); Record("tab-selection", tabs.SelectedIndex); };
            var layout = new DockPanel { Margin = new Thickness(16) };
            var footer = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0), Children =
                { status, new TextBlock { Text = "输出目录：" + output, TextWrapping = TextWrapping.Wrap } } };
            DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer); layout.Children.Add(tabs);
            Content = layout;
            Opened += (_, _) =>
            {
                Record("native-window-opened", new { platform = Environment.OSVersion.ToString(), scaling = RenderScaling });
                editor.Focus(); SaveReport();
            };
            ScalingChanged += (_, _) => { Record("display-scaling", RenderScaling); SaveReport(); };
            Closed += (_, _) =>
            {
                brush.Close();
                lock (sceneGate) { closed = true; scene.Dispose(); }
                Record("native-window-closed", null); SaveReport();
            };
        }

        private static ScrollViewer Scroll(Control content) => new()
        {
            Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        private Control TextPage()
        {
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            toolbar.Children.Add(Button("旋转 0° / 13°", () => { angle = angle == 0 ? 13 : 0; TransformText(); }));
            toolbar.Children.Add(Button("水平翻转", () => { flipped = !flipped; TransformText(); }));
            toolbar.Children.Add(Button("缩放 50% / 100% / 150%", () => { zoom = zoom == 1 ? 1.5 : zoom == 1.5 ? 0.5 : 1; TransformText(); }));
            toolbar.Children.Add(Button("导出并校验", SaveText));
            return new StackPanel { Spacing = 12, Children = { toolbar,
                new TextBlock { Text = "点击文字输入。测试拼音候选、Esc 取消、Enter 确认、Ctrl+Z 撤销，再改变变换重试。", TextWrapping = TextWrapping.Wrap },
                new Border { Background = Brushes.WhiteSmoke, Child = textCanvas } } };
        }

        private Control BrushPage() => new StackPanel { Spacing = 12, Children =
        {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                Button("撤销", () => brush.History(false)), Button("重做", () => brush.History(true)),
                Button("取消笔划", brush.Cancel), Button("保存工程并重开", () => brush.Save(fixtures, NewSave("brush")))
            } },
            new TextBlock { Text = "按住鼠标左键绘制；松开提交一笔。绘制中按 Esc 取消。800 px 软笔，40% 不透明度。" },
            new Border { Background = Brushes.WhiteSmoke, Child = brush }
        } };

        private Control CompositionPage()
        {
            var control = new SceneControl(scene.Width, scene.Height, canvas =>
            {
                lock (sceneGate)
                {
                    if (closed) return;
                    if (!compositionThreadRecorded) { Record("composition-render-thread", Environment.CurrentManagedThreadId); compositionThreadRecorded = true; }
                    scene.Paint(canvas);
                }
            }) { Width = scene.Width, Height = scene.Height };
            return new StackPanel { Spacing = 12, Children =
            {
                new TextBlock { Text = "F04 固定样本：保留图层、蒙版和工程内容，仅重命名后另存。" },
                Button("另存工程并校验", () =>
                {
                    string save = NewSave("composition");
                    lock (sceneGate)
                    {
                        scene.Export(Path.Combine(save, "before.png"));
                        string project = Path.Combine(save, "F04.comp");
                        scene.RenameActiveAndSaveCopy(project, "Windows 窗口重命名");
                        using var reopened = FixtureScene.Read(project);
                        reopened.Export(Path.Combine(save, "reopened.png"));
                        if (Program.Compare(Path.Combine(save, "before.png"), Path.Combine(save, "reopened.png")).DifferentPixels != 0)
                            throw new InvalidDataException("Composition changed pixels on reopen");
                    }
                    Record("composition-save-reopen", true);
                }),
                new Viewbox { Width = 760, Height = 520, Stretch = Stretch.Uniform, Child = control }
            } };
        }

        private void TransformText()
        {
            editor.RenderTransform = new MatrixTransform(Matrix.CreateTranslation(-210, -130)
                * Matrix.CreateScale(flipped ? -zoom : zoom, zoom) * Matrix.CreateRotation(angle * Math.PI / 180)
                * Matrix.CreateTranslation(380, 260));
            Record("text-transform", new { angle, zoom, flipped });
        }

        private void SaveText()
        {
            if (!string.IsNullOrEmpty(presenter.PreeditText)) throw new InvalidOperationException("请先确认或取消正在输入的拼音。");
            string save = NewSave("text");
            var caret = presenter.CaretBrush; var selection = presenter.SelectionBrush; var foreground = presenter.SelectionForegroundBrush;
            try
            {
                // UI-only overlays are excluded without changing text or selection/history.
                presenter.CaretBrush = Brushes.Transparent; presenter.SelectionBrush = Brushes.Transparent; presenter.SelectionForegroundBrush = null;
                textCanvas.UpdateLayout();
                TextProbe.Save(textCanvas, Path.Combine(save, "preview.png"));
                var shared = presenter.TextLayout;
                var root = new Canvas { Width = 760, Height = 520 };
                root.Children.Add(new Decorator
                {
                    Width = editor.Width, Height = editor.Height, Opacity = editor.Opacity, ClipToBounds = editor.ClipToBounds,
                    RenderTransformOrigin = editor.RenderTransformOrigin, RenderTransform = editor.RenderTransform,
                    Child = new TextProbe.LayoutControl(shared)
                });
                root.Measure(new Size(760, 520)); root.Arrange(new Rect(0, 0, 760, 520));
                TextProbe.Save(root, Path.Combine(save, "export.png"));
                var difference = Program.Compare(Path.Combine(save, "preview.png"), Path.Combine(save, "export.png"));
                Record("text-preview-export", difference);
                if (difference.DifferentPixels != 0) throw new InvalidDataException("文字预览和导出不一致，请保留输出文件。");
            }
            finally { presenter.CaretBrush = caret; presenter.SelectionBrush = selection; presenter.SelectionForegroundBrush = foreground; }
        }

        private Button Button(string label, Action action)
        {
            var button = new Button { Content = label };
            button.Click += (_, _) =>
            {
                try { action(); status.Text = label + "：完成"; }
                catch (Exception e) { status.Text = e.Message; Record("action-error", new { label, error = e.ToString() }); }
                SaveReport();
            };
            return button;
        }

        private string NewSave(string mode)
        {
            string path = Path.Combine(output, $"{++saveIndex:000}-{mode}");
            Directory.CreateDirectory(path); return path;
        }
        private void Record(string name, object? data) => events.Enqueue(new
        {
            utc = DateTime.UtcNow, name, thread = Environment.CurrentManagedThreadId,
            onUiThread = Dispatcher.UIThread.CheckAccess(), data
        });
        private void SaveReport() => File.WriteAllText(Path.Combine(output, "window-report.json"), JsonSerializer.Serialize(new
        {
            status = "native-window observations; manual IME/DPI/visual acceptance required",
            windowsExecuted = OperatingSystem.IsWindows(), nativeWindow = true, requestedRendering = "Software",
            nativeLibrarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native))).ToLowerInvariant(),
            nativePreeditChanges = preeditEvents, textInputEvents, injectedInputMethodCalls = 0,
            renderScaling = RenderScaling, events = events.ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
