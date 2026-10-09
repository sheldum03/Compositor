using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;

public sealed class MainWindow : Window
{
    private readonly DockPanel layout = new() { Margin = new Thickness(12) };
    private readonly StackPanel projectTabs = new() { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 0, 0, 8) };
    private readonly CanvasView canvas = new();
    private readonly StackPanel toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
    private readonly DockPanel sidebar = new() { Width = 260, Margin = new Thickness(12, 0, 0, 0) };
    private readonly CheckBox paint = new() { Content = "软笔", Name = "Paint", IsChecked = true };
    private readonly CheckBox maskPaint = new() { Content = "蒙版笔刷", Name = "MaskPaint" };
    private readonly ComboBox maskPaintMode = new() { Name = "MaskPaintMode", Width = 75,
        ItemsSource = new[] { "隐藏", "显示" }, SelectedIndex = 0 };
    private readonly NumericUpDown diameter = new() { Name = "BrushDiameter", Minimum = 1, Maximum = 2000, Value = 40, Width = 90 };
    private readonly NumericUpDown opacity = new() { Name = "BrushOpacity", Minimum = 1, Maximum = 100, Value = 100, Width = 90 };
    private readonly ComboBox color = new() { Name = "BrushColor", ItemsSource = new[] { "黑色", "白色", "蓝色", "橙色" }, SelectedIndex = 0, Width = 90 };
    private readonly NumericUpDown shapeCornerRadius = new() { Name = "ShapeCornerRadius", Minimum = 0, Maximum = 30000, Value = 0, Width = 70 };
    private readonly ComboBox brushType = new() { Name = "BrushType", ItemsSource = new[] { "软笔", "硬笔" }, SelectedIndex = 0, Width = 75 };
    private readonly NumericUpDown maskRadius = new() { Name = "MaskRadius", Minimum = 1, Maximum = 200, Value = 3, Width = 65 };
    private readonly StackPanel brushOptions = new() { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 12) };
    private readonly ListBox layers = new() { Name = "Layers", SelectionMode = SelectionMode.Multiple };
    private readonly TextBox layerName = new() { Name = "LayerName", Watermark = "图层名称" };
    private readonly StackPanel textEditorPanel = new() { Spacing = 6, IsVisible = false };
    private readonly Button resolveTextFont = null!;
    private readonly TextBox textContent = new() { Name = "TextContent", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        Height = 72, Watermark = "文字内容" };
    private readonly ComboBox textFont = new() { Name = "TextFont", Width = 220 };
    private readonly NumericUpDown textSize = new() { Name = "TextSize", Minimum = 1, Maximum = 2000, Value = 18, Width = 80 };
    private readonly ComboBox textColor = new() { Name = "TextColor", ItemsSource = new[] { "黑色", "白色", "蓝色", "橙色" }, SelectedIndex = 0, Width = 80 };
    private readonly ComboBox textAlignment = new() { Name = "TextAlignment", Width = 90,
        ItemsSource = new[] { "left", "center", "right" }, SelectedIndex = 0 };
    private readonly NumericUpDown textLineSpacing = new() { Name = "TextLineSpacing", Minimum = -2000, Maximum = 2000, Value = 0, Width = 80 };
    private readonly NumericUpDown textTracking = new() { Name = "TextTracking", Minimum = -2000, Maximum = 2000, Value = 0, Width = 80 };
    private readonly NumericUpDown textBoxWidth = new() { Name = "TextBoxWidth", Minimum = 1, Maximum = 30000, Value = 360, Width = 80 };
    private readonly NumericUpDown layerOpacity = new() { Name = "LayerOpacity", Minimum = 0, Maximum = 100, Value = 100, Width = 90 };
    private readonly NumericUpDown layerMoveX = new() { Name = "LayerMoveX", Minimum = -30000, Maximum = 30000, Value = 0, Width = 70 };
    private readonly NumericUpDown layerMoveY = new() { Name = "LayerMoveY", Minimum = -30000, Maximum = 30000, Value = 0, Width = 70 };
    private readonly NumericUpDown layerRotation = new() { Name = "LayerRotation", Minimum = -3600, Maximum = 3600, Value = 15, Width = 70 };
    private readonly ComboBox layerBlendMode = new() { Name = "LayerBlendMode", Width = 150 };
    private readonly NumericUpDown adjustmentExposure = new() { Name = "AdjustmentExposure", Minimum = -20, Maximum = 20, Value = 0, Width = 70 };
    private readonly NumericUpDown adjustmentOffset = new() { Name = "AdjustmentOffset", Minimum = -0.5m, Maximum = 0.5m, Value = 0, Width = 70 };
    private readonly NumericUpDown adjustmentGamma = new() { Name = "AdjustmentGamma", Minimum = 0.01m, Maximum = 9.99m, Value = 1, Width = 70 };
    private readonly StackPanel adjustmentEditor = new() { Name = "AdjustmentEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown levelsInputBlack = new() { Name = "LevelsInputBlack", Minimum = 0, Maximum = 254, Value = 0, Width = 62 };
    private readonly NumericUpDown levelsInputWhite = new() { Name = "LevelsInputWhite", Minimum = 1, Maximum = 255, Value = 255, Width = 62 };
    private readonly NumericUpDown levelsGamma = new() { Name = "LevelsGamma", Minimum = 0.1m, Maximum = 9.99m, Value = 1, Width = 62 };
    private readonly NumericUpDown levelsOutputBlack = new() { Name = "LevelsOutputBlack", Minimum = 0, Maximum = 255, Value = 0, Width = 62 };
    private readonly NumericUpDown levelsOutputWhite = new() { Name = "LevelsOutputWhite", Minimum = 0, Maximum = 255, Value = 255, Width = 62 };
    private readonly ComboBox levelsChannel = new() { Name = "LevelsChannel", Width = 82,
        ItemsSource = new[] { "RGB", "红", "绿", "蓝" }, SelectedIndex = 0 };
    private readonly StackPanel levelsAdjustmentEditor = new() { Name = "LevelsAdjustmentEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown adjustmentHue = new() { Name = "AdjustmentHue", Minimum = -360, Maximum = 360, Value = 0, Width = 70 };
    private readonly NumericUpDown adjustmentSaturation = new() { Name = "AdjustmentSaturation", Minimum = -100, Maximum = 100, Value = 0, Width = 70 };
    private readonly NumericUpDown adjustmentLightness = new() { Name = "AdjustmentLightness", Minimum = -100, Maximum = 100, Value = 0, Width = 70 };
    private readonly CheckBox adjustmentColorize = new() { Name = "AdjustmentColorize", Content = "着色" };
    private readonly StackPanel hueSaturationAdjustmentEditor = new() { Name = "HueSaturationAdjustmentEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown curvesShadow = new() { Name = "CurvesShadow", Minimum = 0, Maximum = 255, Value = 0, Width = 62 };
    private readonly NumericUpDown curvesMid = new() { Name = "CurvesMid", Minimum = 0, Maximum = 255, Value = 128, Width = 62 };
    private readonly NumericUpDown curvesHighlight = new() { Name = "CurvesHighlight", Minimum = 0, Maximum = 255, Value = 255, Width = 62 };
    private readonly ComboBox curvesChannel = new() { Name = "CurvesChannel", Width = 82,
        ItemsSource = new[] { "RGB", "红", "绿", "蓝" }, SelectedIndex = 0 };
    private readonly StackPanel curvesAdjustmentEditor = new() { Name = "CurvesAdjustmentEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown gradientShadowRed = new() { Name = "GradientShadowRed", Minimum = 0, Maximum = 255, Value = 0, Width = 48 };
    private readonly NumericUpDown gradientShadowGreen = new() { Name = "GradientShadowGreen", Minimum = 0, Maximum = 255, Value = 0, Width = 48 };
    private readonly NumericUpDown gradientShadowBlue = new() { Name = "GradientShadowBlue", Minimum = 0, Maximum = 255, Value = 0, Width = 48 };
    private readonly NumericUpDown gradientHighlightRed = new() { Name = "GradientHighlightRed", Minimum = 0, Maximum = 255, Value = 255, Width = 48 };
    private readonly NumericUpDown gradientHighlightGreen = new() { Name = "GradientHighlightGreen", Minimum = 0, Maximum = 255, Value = 255, Width = 48 };
    private readonly NumericUpDown gradientHighlightBlue = new() { Name = "GradientHighlightBlue", Minimum = 0, Maximum = 255, Value = 255, Width = 48 };
    private readonly StackPanel gradientMapAdjustmentEditor = new() { Name = "GradientMapAdjustmentEditor", Orientation = Orientation.Horizontal, Spacing = 6, IsVisible = false };
    private readonly NumericUpDown gaussianBlurRadius = new() { Name = "GaussianBlurRadius", Minimum = 1, Maximum = 32, Value = 1, Width = 62 };
    private readonly StackPanel gaussianBlurAdjustmentEditor = new() { Name = "GaussianBlurAdjustmentEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown selectionGaussianBlurRadius = new() { Name = "SelectionGaussianBlurRadius", Minimum = 1, Maximum = 32, Value = 1, Width = 62 };
    private readonly StackPanel selectionGaussianBlurEditor = new() { Name = "SelectionGaussianBlurEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown selectionMotionBlurAngle = new() { Name = "SelectionMotionBlurAngle", Minimum = -90, Maximum = 90, Value = 0, Width = 62 };
    private readonly NumericUpDown selectionMotionBlurDistance = new() { Name = "SelectionMotionBlurDistance", Minimum = 1, Maximum = 32, Value = 1, Width = 62 };
    private readonly StackPanel selectionMotionBlurEditor = new() { Name = "SelectionMotionBlurEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown selectionNoiseAmount = new() { Name = "SelectionNoiseAmount", Minimum = 0.1m, Maximum = 400, Value = 10, Width = 62 };
    private readonly CheckBox selectionNoiseGaussian = new() { Name = "SelectionNoiseGaussian", Content = "高斯" };
    private readonly CheckBox selectionNoiseMonochromatic = new() { Name = "SelectionNoiseMonochromatic", Content = "单色" };
    private readonly StackPanel selectionNoiseEditor = new() { Name = "SelectionNoiseEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown selectionLensCorrectionDistortion = new() { Name = "SelectionLensCorrectionDistortion", Minimum = -100, Maximum = 100, Value = 0, Width = 62 };
    private readonly StackPanel selectionLensCorrectionEditor = new() { Name = "SelectionLensCorrectionEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown selectionExposure = new() { Name = "SelectionExposure", Minimum = -20, Maximum = 20, Value = 0, Width = 62 };
    private readonly NumericUpDown selectionExposureOffset = new() { Name = "SelectionExposureOffset", Minimum = -0.5m, Maximum = 0.5m, Value = 0, Width = 62 };
    private readonly NumericUpDown selectionExposureGamma = new() { Name = "SelectionExposureGamma", Minimum = 0.01m, Maximum = 9.99m, Value = 1, Width = 62 };
    private readonly StackPanel selectionExposureEditor = new() { Name = "SelectionExposureEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown selectionLevelsInputBlack = new() { Name = "SelectionLevelsInputBlack", Minimum = 0, Maximum = 254, Value = 0, Width = 54 };
    private readonly NumericUpDown selectionLevelsInputWhite = new() { Name = "SelectionLevelsInputWhite", Minimum = 1, Maximum = 255, Value = 255, Width = 54 };
    private readonly NumericUpDown selectionLevelsGamma = new() { Name = "SelectionLevelsGamma", Minimum = 0.1m, Maximum = 9.99m, Value = 1, Width = 54 };
    private readonly NumericUpDown selectionLevelsOutputBlack = new() { Name = "SelectionLevelsOutputBlack", Minimum = 0, Maximum = 255, Value = 0, Width = 54 };
    private readonly NumericUpDown selectionLevelsOutputWhite = new() { Name = "SelectionLevelsOutputWhite", Minimum = 0, Maximum = 255, Value = 255, Width = 54 };
    private readonly ComboBox selectionLevelsChannel = new() { Name = "SelectionLevelsChannel", Width = 82,
        ItemsSource = new[] { "RGB", "红", "绿", "蓝" }, SelectedIndex = 0 };
    private readonly StackPanel selectionLevelsEditor = new() { Name = "SelectionLevelsEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown selectionHue = new() { Name = "SelectionHue", Minimum = -360, Maximum = 360, Value = 0, Width = 62 };
    private readonly NumericUpDown selectionSaturation = new() { Name = "SelectionSaturation", Minimum = -100, Maximum = 100, Value = 0, Width = 62 };
    private readonly NumericUpDown selectionLightness = new() { Name = "SelectionLightness", Minimum = -100, Maximum = 100, Value = 0, Width = 62 };
    private readonly CheckBox selectionColorize = new() { Name = "SelectionColorize", Content = "着色" };
    private readonly StackPanel selectionHueSaturationEditor = new() { Name = "SelectionHueSaturationEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown selectionCurvesShadow = new() { Name = "SelectionCurvesShadow", Minimum = 0, Maximum = 255, Value = 0, Width = 62 };
    private readonly NumericUpDown selectionCurvesMid = new() { Name = "SelectionCurvesMid", Minimum = 0, Maximum = 255, Value = 128, Width = 62 };
    private readonly NumericUpDown selectionCurvesHighlight = new() { Name = "SelectionCurvesHighlight", Minimum = 0, Maximum = 255, Value = 255, Width = 62 };
    private readonly ComboBox selectionCurvesChannel = new() { Name = "SelectionCurvesChannel", Width = 82,
        ItemsSource = new[] { "RGB", "红", "绿", "蓝" }, SelectedIndex = 0 };
    private readonly StackPanel selectionCurvesEditor = new() { Name = "SelectionCurvesEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown selectionGradientShadowRed = new() { Name = "SelectionGradientShadowRed", Minimum = 0, Maximum = 255, Value = 0, Width = 48 };
    private readonly NumericUpDown selectionGradientShadowGreen = new() { Name = "SelectionGradientShadowGreen", Minimum = 0, Maximum = 255, Value = 0, Width = 48 };
    private readonly NumericUpDown selectionGradientShadowBlue = new() { Name = "SelectionGradientShadowBlue", Minimum = 0, Maximum = 255, Value = 0, Width = 48 };
    private readonly NumericUpDown selectionGradientHighlightRed = new() { Name = "SelectionGradientHighlightRed", Minimum = 0, Maximum = 255, Value = 255, Width = 48 };
    private readonly NumericUpDown selectionGradientHighlightGreen = new() { Name = "SelectionGradientHighlightGreen", Minimum = 0, Maximum = 255, Value = 255, Width = 48 };
    private readonly NumericUpDown selectionGradientHighlightBlue = new() { Name = "SelectionGradientHighlightBlue", Minimum = 0, Maximum = 255, Value = 255, Width = 48 };
    private readonly StackPanel selectionGradientMapEditor = new() { Name = "SelectionGradientMapEditor", Orientation = Orientation.Horizontal, Spacing = 6, IsVisible = false };
    private readonly NumericUpDown selectionGrainAmount = new() { Name = "SelectionGrainAmount", Minimum = 0, Maximum = 100, Value = 25, Width = 62 };
    private readonly NumericUpDown selectionGrainSize = new() { Name = "SelectionGrainSize", Minimum = 0.5m, Maximum = 20, Value = 1.5m, Width = 62 };
    private readonly NumericUpDown selectionGrainRoughness = new() { Name = "SelectionGrainRoughness", Minimum = 0, Maximum = 100, Value = 50, Width = 62 };
    private readonly StackPanel selectionGrainEditor = new() { Name = "SelectionGrainEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown motionBlurAngle = new() { Name = "MotionBlurAngle", Minimum = -90, Maximum = 90, Value = 0, Width = 62 };
    private readonly NumericUpDown motionBlurDistance = new() { Name = "MotionBlurDistance", Minimum = 1, Maximum = 32, Value = 1, Width = 62 };
    private readonly StackPanel motionBlurAdjustmentEditor = new() { Name = "MotionBlurAdjustmentEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown noiseAmount = new() { Name = "NoiseAmount", Minimum = 0.1m, Maximum = 400, Value = 10, Width = 62 };
    private readonly CheckBox noiseGaussian = new() { Name = "NoiseGaussian", Content = "高斯" };
    private readonly CheckBox noiseMonochromatic = new() { Name = "NoiseMonochromatic", Content = "单色" };
    private readonly StackPanel noiseAdjustmentEditor = new() { Name = "NoiseAdjustmentEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown lensCorrectionDistortion = new() { Name = "LensCorrectionDistortion", Minimum = -100, Maximum = 100, Value = 0, Width = 62 };
    private readonly StackPanel lensCorrectionAdjustmentEditor = new() { Name = "LensCorrectionAdjustmentEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly NumericUpDown grainAmount = new() { Name = "GrainAmount", Minimum = 0, Maximum = 100, Value = 25, Width = 62 };
    private readonly NumericUpDown grainSize = new() { Name = "GrainSize", Minimum = 0.5m, Maximum = 20, Value = 1.5m, Width = 62 };
    private readonly NumericUpDown grainRoughness = new() { Name = "GrainRoughness", Minimum = 0, Maximum = 100, Value = 50, Width = 62 };
    private readonly StackPanel grainAdjustmentEditor = new() { Name = "GrainAdjustmentEditor", Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
    private readonly CheckBox pixelGrid = new() { Name = "PixelGrid", Content = "像素网格" };
    private readonly CheckBox rectangleSelect = new() { Name = "RectSelect", Content = "矩形选区" };
    private readonly CheckBox moveSelection = new() { Name = "MoveSelection", Content = "移动选区" };
    private readonly ComboBox selectionShape = new() { Name = "SelectionShape", Width = 90, ItemsSource = new[] { "矩形", "椭圆", "魔棒", "套索" }, SelectedIndex = 0 };
    private readonly ComboBox wandRadius = new() { Name = "WandRadius", Width = 70, ItemsSource = new[] { "点", "3×3", "5×5" }, SelectedIndex = 0 };
    private readonly NumericUpDown wandTolerance = new() { Name = "WandTolerance", Minimum = 0, Maximum = 255, Value = 0, Width = 65 };
    private readonly CheckBox wandContiguous = new() { Name = "WandContiguous", Content = "连续" , IsChecked = true };
    private readonly ComboBox selectionOperation = new() { Name = "SelectionOperation", Width = 90, ItemsSource = new[] { "替换", "加选", "减选" }, SelectedIndex = 0 };
    private readonly NumericUpDown selectionFeatherRadius = new() { Name = "SelectionFeatherRadius", Minimum = 1, Maximum = 200, Value = 3, Width = 65 };
    private readonly TextBlock status = new() { Name = "Status", TextWrapping = TextWrapping.Wrap };
    private readonly List<Button> documentButtons = [];
    private readonly List<Button> layerButtons = [];
    private readonly List<Button> maskButtons = [];
    private readonly List<EditorWorkspace> projects = [];
    private WriteableBitmap? preview;
    private ProjectSession? displayedSession;
    private Guid? selectedId;
    private int activeProjectIndex;
    private bool refreshing, allowClose;
    private EditorWorkspace? clipboardProject;
    private FontLibrary? fontLibrary;
    private FlatLayerInfo? draggingLayer;
    private Point layerDragStart;
    private bool draggingLayers;
    public EditorWorkspace Workspace => projects[activeProjectIndex];
    public int ProjectCount => projects.Count;
    public int ActiveProjectIndex => activeProjectIndex;
    public bool IsBusy { get; private set; }

    public MainWindow() : this(new EditorWorkspace()) { }

    public MainWindow(EditorWorkspace workspace, FontLibrary? fontLibrary = null)
    {
        this.fontLibrary = fontLibrary;
        projects.Add(workspace);
        Width = 1120; Height = 760; MinWidth = 850; MinHeight = 540;
        FontFamily = new FontFamily("avares://Compositor.App/Fonts/SourceHanSansSC-Regular.otf#Source Han Sans SC");
        FontSize = 13;
        RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        DockPanel.SetDock(projectTabs, Dock.Top); layout.Children.Add(projectTabs);
        toolbar.Children.Add(Command("New", "新建", NewAsync));
        toolbar.Children.Add(Command("Open", "打开工程", OpenAsync));
        toolbar.Children.Add(Command("Import", "导入图片", ImportAsync));
        toolbar.Children.Add(Command("ImportFont", "导入字体", ImportFontAsync));
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
        toolbar.Children.Add(Command("RotateClockwise", "顺时针90°", () => Task.Run(() => Workspace.RotateDocument90(true)), document: true));
        toolbar.Children.Add(Command("RotateCounterClockwise", "逆时针90°", () => Task.Run(() => Workspace.RotateDocument90(false)), document: true));
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
        toolbar.Children.Add(new TextBlock { Text = "羽化半径", VerticalAlignment = VerticalAlignment.Center });
        toolbar.Children.Add(selectionFeatherRadius);
        toolbar.Children.Add(Command("FeatherSelection", "羽化选区", FeatherSelectionAsync, document: true));
        toolbar.Children.Add(Command("CopySelection", "复制选区", CopySelectionAsync, document: true));
        toolbar.Children.Add(Command("CopyMergedSelection", "合并复制", CopyMergedSelectionAsync, document: true));
        toolbar.Children.Add(Command("CutSelection", "剪切选区", CutSelectionAsync, document: true));
        toolbar.Children.Add(Command("PasteSelection", "粘贴选区", PasteSelectionAsync, document: true));
        toolbar.Children.Add(Command("CommitFloatingSelection", "提交浮动选区", CommitFloatingSelectionAsync, document: true));
        toolbar.Children.Add(Command("CancelFloatingSelection", "取消浮动选区", CancelFloatingSelectionAsync, document: true));
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
            if (canvas.SelectionEnabled) { paint.IsChecked = false; moveSelection.IsChecked = false; maskPaint.IsChecked = false; }
            UpdatePaintMode();
        };
        moveSelection.IsCheckedChanged += (_, _) =>
        {
            canvas.SelectionMoveEnabled = moveSelection.IsChecked == true;
            if (canvas.SelectionMoveEnabled) { rectangleSelect.IsChecked = false; paint.IsChecked = false; maskPaint.IsChecked = false; }
            UpdatePaintMode();
        };
        DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar);
        brushOptions.Children.Add(paint);
        brushOptions.Children.Add(maskPaint);
        brushOptions.Children.Add(maskPaintMode);
        brushOptions.Children.Add(new TextBlock { Text = "直径", VerticalAlignment = VerticalAlignment.Center });
        brushOptions.Children.Add(diameter);
        brushOptions.Children.Add(new TextBlock { Text = "不透明度 %", VerticalAlignment = VerticalAlignment.Center });
        brushOptions.Children.Add(opacity); brushOptions.Children.Add(brushType); brushOptions.Children.Add(color);
        brushOptions.Children.Add(new TextBlock { Text = "滚轮缩放 · 空格/中键平移 · Esc 取消笔划", VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(brushOptions, Dock.Top); layout.Children.Add(brushOptions);
        var footer = new StackPanel { Spacing = 5, Margin = new Thickness(0, 10, 0, 0), Children =
        {
            status,
            new TextBlock { Text = "内部集成版 · 变换保留原始图层资产；逐层复制、剪切或粘贴需匹配变换快照，或先烘焙。", Foreground = Brushes.DimGray }
        } };
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var heading = new TextBlock { Text = "图层", FontSize = 17, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(heading, Dock.Top); sidebar.Children.Add(heading);
        var actions = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var structure = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        structure.Children.Add(Command("AddLayer", "新增图层", AddLayerAsync, document: true));
        structure.Children.Add(Command("AddTextLayer", "新增文字", AddTextLayerAsync, document: true));
        structure.Children.Add(Command("AddBoxTextLayer", "新增框文字", AddBoxTextLayerAsync, document: true));
        structure.Children.Add(Command("AddRectangleShape", "新增矩形", AddRectangleShapeAsync, layer: true));
        structure.Children.Add(Command("AddEllipseShape", "新增椭圆", AddEllipseShapeAsync, layer: true));
        structure.Children.Add(new TextBlock { Text = "圆角", VerticalAlignment = VerticalAlignment.Center });
        structure.Children.Add(shapeCornerRadius);
        structure.Children.Add(Command("ApplyShape", "应用形状参数", ApplyShapeAsync, layer: true));
        structure.Children.Add(Command("AddExposureAdjustment", "新增曝光调整", AddExposureAdjustmentAsync, layer: true));
        structure.Children.Add(Command("AddLevelsAdjustment", "新增色阶调整", AddLevelsAdjustmentAsync, layer: true));
        structure.Children.Add(Command("AddHueSaturationAdjustment", "新增色相/饱和度", AddHueSaturationAdjustmentAsync, layer: true));
        structure.Children.Add(Command("AddCurvesAdjustment", "新增曲线调整", AddCurvesAdjustmentAsync, layer: true));
        structure.Children.Add(Command("AddGradientMapAdjustment", "新增渐变映射", AddGradientMapAdjustmentAsync, layer: true));
        structure.Children.Add(Command("AddGaussianBlurAdjustment", "新增高斯模糊", AddGaussianBlurAdjustmentAsync, layer: true));
        structure.Children.Add(Command("AddMotionBlurAdjustment", "新增动感模糊", AddMotionBlurAdjustmentAsync, layer: true));
        structure.Children.Add(Command("AddNoiseAdjustment", "新增添加杂色", AddNoiseAdjustmentAsync, layer: true));
        structure.Children.Add(Command("AddLensCorrectionAdjustment", "新增镜头校正", AddLensCorrectionAdjustmentAsync, layer: true));
        structure.Children.Add(Command("AddGrainAdjustment", "新增颗粒", AddGrainAdjustmentAsync, layer: true));
        structure.Children.Add(Command("DuplicateLayer", "复制", DuplicateLayerAsync, layer: true));
        structure.Children.Add(Command("LayerViaCopy", "选区复制为图层", LayerViaCopyAsync, layer: true));
        structure.Children.Add(Command("DeleteLayer", "删除", DeleteLayerAsync, layer: true));
        structure.Children.Add(Command("MergeLayerDown", "向下合并", MergeLayerDownAsync, layer: true));
        structure.Children.Add(Command("FlipLayerHorizontal", "水平翻转", () => FlipLayerAsync(true), layer: true));
        structure.Children.Add(Command("FlipLayerVertical", "垂直翻转", () => FlipLayerAsync(false), layer: true));
        structure.Children.Add(Command("ScaleGroupDown", "组缩小", () => ScaleGroupAsync(false), layer: true));
        structure.Children.Add(Command("ScaleGroupUp", "组放大", () => ScaleGroupAsync(true), layer: true));
        structure.Children.Add(Command("RotateGroupCounterClockwise", "组左转90°", () => RotateGroupAsync(false), layer: true));
        structure.Children.Add(Command("RotateGroupClockwise", "组右转90°", () => RotateGroupAsync(true), layer: true));
        structure.Children.Add(Command("RotateLayerCounterClockwise", "左转15°", () => RotateLayerAsync(false), layer: true));
        structure.Children.Add(Command("RotateLayerClockwise", "右转15°", () => RotateLayerAsync(true), layer: true));
        structure.Children.Add(new TextBlock { Text = "角度", VerticalAlignment = VerticalAlignment.Center });
        structure.Children.Add(layerRotation);
        structure.Children.Add(Command("RotateLayerCustom", "应用旋转", RotateLayerCustomAsync, layer: true));
        structure.Children.Add(Command("AddMask", "添加蒙版", AddMaskAsync, layer: true, mask: true));
        structure.Children.Add(Command("ToggleMask", "启用/停用蒙版", ToggleMaskAsync, layer: true, mask: true));
        structure.Children.Add(Command("InvertMask", "反相蒙版", InvertMaskAsync, layer: true, mask: true));
        structure.Children.Add(Command("FillMaskWhite", "蒙版填白", () => FillMaskAsync(true), layer: true, mask: true));
        structure.Children.Add(Command("FillMaskBlack", "蒙版填黑", () => FillMaskAsync(false), layer: true, mask: true));
        structure.Children.Add(new TextBlock { Text = "模糊半径", VerticalAlignment = VerticalAlignment.Center });
        structure.Children.Add(maskRadius);
        structure.Children.Add(Command("BlurMask", "模糊蒙版", BlurMaskAsync, layer: true, mask: true));
        structure.Children.Add(Command("SetClippingMask", "设为剪贴层", () => SetClippingMaskAsync(true), layer: true));
        structure.Children.Add(Command("ReleaseClippingMask", "释放剪贴", () => SetClippingMaskAsync(false), layer: true));
        structure.Children.Add(Command("GroupLayer", "建立组", GroupLayerAsync, layer: true));
        structure.Children.Add(Command("UngroupLayer", "解组", UngroupLayerAsync, layer: true));
        structure.Children.Add(Command("BakeUngroupLayer", "烘焙解组", BakeUngroupLayerAsync, layer: true));
        structure.Children.Add(Command("BakeLayerTransform", "烘焙图层变换", BakeLayerTransformAsync, layer: true));
        actions.Children.Add(structure);
        actions.Children.Add(layerName);
        actions.Children.Add(Command("Rename", "应用名称", RenameAsync, layer: true));
        textEditorPanel.Children.Add(new TextBlock { Text = "文字", Margin = new Thickness(0, 8, 0, 0) });
        textEditorPanel.Children.Add(textContent);
        var textOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        textOptions.Children.Add(new TextBlock { Text = "字体", VerticalAlignment = VerticalAlignment.Center });
        textOptions.Children.Add(textFont);
        textOptions.Children.Add(new TextBlock { Text = "字号", VerticalAlignment = VerticalAlignment.Center });
        textOptions.Children.Add(textSize);
        textOptions.Children.Add(new TextBlock { Text = "颜色", VerticalAlignment = VerticalAlignment.Center });
        textOptions.Children.Add(textColor);
        textOptions.Children.Add(textAlignment);
        textEditorPanel.Children.Add(textOptions);
        var textLayoutOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        textLayoutOptions.Children.Add(new TextBlock { Text = "行距", VerticalAlignment = VerticalAlignment.Center });
        textLayoutOptions.Children.Add(textLineSpacing);
        textLayoutOptions.Children.Add(new TextBlock { Text = "字距", VerticalAlignment = VerticalAlignment.Center });
        textLayoutOptions.Children.Add(textTracking);
        textLayoutOptions.Children.Add(new TextBlock { Text = "框宽", VerticalAlignment = VerticalAlignment.Center });
        textLayoutOptions.Children.Add(textBoxWidth);
        textEditorPanel.Children.Add(textLayoutOptions);
        textEditorPanel.Children.Add(Command("ApplyText", "应用文字", ApplyTextAsync, layer: true));
        resolveTextFont = Command("ResolveTextFont", "选择字体并解锁", ResolveTextFontAsync, layer: true);
        textEditorPanel.Children.Add(resolveTextFont);
        actions.Children.Add(textEditorPanel);
        var appearance = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        appearance.Children.Add(new TextBlock { Text = "透明度 %", VerticalAlignment = VerticalAlignment.Center });
        appearance.Children.Add(layerOpacity);
        appearance.Children.Add(layerBlendMode);
        actions.Children.Add(appearance);
        adjustmentEditor.Children.Add(new TextBlock { Text = "曝光", VerticalAlignment = VerticalAlignment.Center });
        adjustmentEditor.Children.Add(adjustmentExposure);
        adjustmentEditor.Children.Add(new TextBlock { Text = "偏移", VerticalAlignment = VerticalAlignment.Center });
        adjustmentEditor.Children.Add(adjustmentOffset);
        adjustmentEditor.Children.Add(new TextBlock { Text = "伽马", VerticalAlignment = VerticalAlignment.Center });
        adjustmentEditor.Children.Add(adjustmentGamma);
        adjustmentEditor.Children.Add(Command("ApplyExposureAdjustment", "应用调整", ApplyExposureAdjustmentAsync, layer: true));
        actions.Children.Add(adjustmentEditor);
        levelsAdjustmentEditor.Children.Add(new TextBlock { Text = "输入黑", VerticalAlignment = VerticalAlignment.Center });
        levelsAdjustmentEditor.Children.Add(levelsInputBlack);
        levelsAdjustmentEditor.Children.Add(new TextBlock { Text = "输入白", VerticalAlignment = VerticalAlignment.Center });
        levelsAdjustmentEditor.Children.Add(levelsInputWhite);
        levelsAdjustmentEditor.Children.Add(new TextBlock { Text = "伽马", VerticalAlignment = VerticalAlignment.Center });
        levelsAdjustmentEditor.Children.Add(levelsGamma);
        levelsAdjustmentEditor.Children.Add(new TextBlock { Text = "输出黑", VerticalAlignment = VerticalAlignment.Center });
        levelsAdjustmentEditor.Children.Add(levelsOutputBlack);
        levelsAdjustmentEditor.Children.Add(new TextBlock { Text = "输出白", VerticalAlignment = VerticalAlignment.Center });
        levelsAdjustmentEditor.Children.Add(levelsOutputWhite);
        levelsAdjustmentEditor.Children.Add(levelsChannel);
        levelsAdjustmentEditor.Children.Add(Command("AutoLevelsContrast", "自动对比度", () => ApplyAutoLevelsAsync(false, LevelsAutoMode.Contrast), layer: true));
        levelsAdjustmentEditor.Children.Add(Command("AutoLevelsColor", "自动颜色", () => ApplyAutoLevelsAsync(false, LevelsAutoMode.Color), layer: true));
        levelsAdjustmentEditor.Children.Add(Command("AutoLevelsNeutral", "自动中性色", () => ApplyAutoLevelsAsync(false, LevelsAutoMode.Neutral), layer: true));
        levelsAdjustmentEditor.Children.Add(Command("ApplyLevelsAdjustment", "应用色阶", ApplyLevelsAdjustmentAsync, layer: true));
        actions.Children.Add(levelsAdjustmentEditor);
        hueSaturationAdjustmentEditor.Children.Add(new TextBlock { Text = "色相", VerticalAlignment = VerticalAlignment.Center });
        hueSaturationAdjustmentEditor.Children.Add(adjustmentHue);
        hueSaturationAdjustmentEditor.Children.Add(new TextBlock { Text = "饱和度", VerticalAlignment = VerticalAlignment.Center });
        hueSaturationAdjustmentEditor.Children.Add(adjustmentSaturation);
        hueSaturationAdjustmentEditor.Children.Add(new TextBlock { Text = "明度", VerticalAlignment = VerticalAlignment.Center });
        hueSaturationAdjustmentEditor.Children.Add(adjustmentLightness);
        hueSaturationAdjustmentEditor.Children.Add(adjustmentColorize);
        hueSaturationAdjustmentEditor.Children.Add(Command("ApplyHueSaturationAdjustment", "应用色相/饱和度", ApplyHueSaturationAdjustmentAsync, layer: true));
        actions.Children.Add(hueSaturationAdjustmentEditor);
        curvesAdjustmentEditor.Children.Add(curvesChannel);
        curvesAdjustmentEditor.Children.Add(new TextBlock { Text = "暗部", VerticalAlignment = VerticalAlignment.Center });
        curvesAdjustmentEditor.Children.Add(curvesShadow);
        curvesAdjustmentEditor.Children.Add(new TextBlock { Text = "中间调", VerticalAlignment = VerticalAlignment.Center });
        curvesAdjustmentEditor.Children.Add(curvesMid);
        curvesAdjustmentEditor.Children.Add(new TextBlock { Text = "高光", VerticalAlignment = VerticalAlignment.Center });
        curvesAdjustmentEditor.Children.Add(curvesHighlight);
        curvesAdjustmentEditor.Children.Add(Command("ApplyCurvesAdjustment", "应用曲线", ApplyCurvesAdjustmentAsync, layer: true));
        actions.Children.Add(curvesAdjustmentEditor);
        curvesChannel.SelectionChanged += (_, _) => { if (!refreshing) UpdateCurvesControls(); };
        gradientMapAdjustmentEditor.Children.Add(new TextBlock { Text = "暗部 RGB", VerticalAlignment = VerticalAlignment.Center });
        gradientMapAdjustmentEditor.Children.Add(gradientShadowRed);
        gradientMapAdjustmentEditor.Children.Add(gradientShadowGreen);
        gradientMapAdjustmentEditor.Children.Add(gradientShadowBlue);
        gradientMapAdjustmentEditor.Children.Add(new TextBlock { Text = "高光 RGB", VerticalAlignment = VerticalAlignment.Center });
        gradientMapAdjustmentEditor.Children.Add(gradientHighlightRed);
        gradientMapAdjustmentEditor.Children.Add(gradientHighlightGreen);
        gradientMapAdjustmentEditor.Children.Add(gradientHighlightBlue);
        gradientMapAdjustmentEditor.Children.Add(Command("ApplyGradientMapAdjustment", "应用渐变映射", ApplyGradientMapAdjustmentAsync, layer: true));
        actions.Children.Add(gradientMapAdjustmentEditor);
        gaussianBlurAdjustmentEditor.Children.Add(new TextBlock { Text = "半径", VerticalAlignment = VerticalAlignment.Center });
        gaussianBlurAdjustmentEditor.Children.Add(gaussianBlurRadius);
        gaussianBlurAdjustmentEditor.Children.Add(Command("ApplyGaussianBlurAdjustment", "应用高斯模糊", ApplyGaussianBlurAdjustmentAsync, layer: true));
        actions.Children.Add(gaussianBlurAdjustmentEditor);
        selectionGaussianBlurEditor.Children.Add(new TextBlock { Text = "选区高斯半径", VerticalAlignment = VerticalAlignment.Center });
        selectionGaussianBlurEditor.Children.Add(selectionGaussianBlurRadius);
        selectionGaussianBlurEditor.Children.Add(Command("PreviewSelectionGaussianBlur", "预览选区模糊", PreviewSelectionGaussianBlurAsync, layer: true));
        selectionGaussianBlurEditor.Children.Add(Command("CommitSelectionGaussianBlur", "提交选区模糊", CommitSelectionGaussianBlurAsync, layer: true));
        selectionGaussianBlurEditor.Children.Add(Command("CancelSelectionGaussianBlur", "取消滤镜预览", CancelSelectionGaussianBlurAsync, layer: true));
        actions.Children.Add(selectionGaussianBlurEditor);
        selectionMotionBlurEditor.Children.Add(new TextBlock { Text = "选区动感角度", VerticalAlignment = VerticalAlignment.Center });
        selectionMotionBlurEditor.Children.Add(selectionMotionBlurAngle);
        selectionMotionBlurEditor.Children.Add(new TextBlock { Text = "距离", VerticalAlignment = VerticalAlignment.Center });
        selectionMotionBlurEditor.Children.Add(selectionMotionBlurDistance);
        selectionMotionBlurEditor.Children.Add(Command("PreviewSelectionMotionBlur", "预览选区动感模糊", PreviewSelectionMotionBlurAsync, layer: true));
        selectionMotionBlurEditor.Children.Add(Command("CommitSelectionMotionBlur", "提交选区动感模糊", CommitSelectionMotionBlurAsync, layer: true));
        selectionMotionBlurEditor.Children.Add(Command("CancelSelectionMotionBlur", "取消滤镜预览", CancelSelectionMotionBlurAsync, layer: true));
        actions.Children.Add(selectionMotionBlurEditor);
        selectionNoiseEditor.Children.Add(new TextBlock { Text = "选区杂色数量", VerticalAlignment = VerticalAlignment.Center });
        selectionNoiseEditor.Children.Add(selectionNoiseAmount);
        selectionNoiseEditor.Children.Add(selectionNoiseGaussian);
        selectionNoiseEditor.Children.Add(selectionNoiseMonochromatic);
        selectionNoiseEditor.Children.Add(Command("PreviewSelectionNoise", "预览选区杂色", PreviewSelectionNoiseAsync, layer: true));
        selectionNoiseEditor.Children.Add(Command("CommitSelectionNoise", "提交选区杂色", CommitSelectionNoiseAsync, layer: true));
        selectionNoiseEditor.Children.Add(Command("CancelSelectionNoise", "取消滤镜预览", CancelSelectionNoiseAsync, layer: true));
        actions.Children.Add(selectionNoiseEditor);
        selectionLensCorrectionEditor.Children.Add(new TextBlock { Text = "选区畸变", VerticalAlignment = VerticalAlignment.Center });
        selectionLensCorrectionEditor.Children.Add(selectionLensCorrectionDistortion);
        selectionLensCorrectionEditor.Children.Add(Command("PreviewSelectionLensCorrection", "预览选区镜头校正", PreviewSelectionLensCorrectionAsync, layer: true));
        selectionLensCorrectionEditor.Children.Add(Command("CommitSelectionLensCorrection", "提交选区镜头校正", CommitSelectionLensCorrectionAsync, layer: true));
        selectionLensCorrectionEditor.Children.Add(Command("CancelSelectionLensCorrection", "取消滤镜预览", CancelSelectionLensCorrectionAsync, layer: true));
        actions.Children.Add(selectionLensCorrectionEditor);
        selectionExposureEditor.Children.Add(new TextBlock { Text = "选区曝光", VerticalAlignment = VerticalAlignment.Center });
        selectionExposureEditor.Children.Add(selectionExposure);
        selectionExposureEditor.Children.Add(new TextBlock { Text = "偏移", VerticalAlignment = VerticalAlignment.Center });
        selectionExposureEditor.Children.Add(selectionExposureOffset);
        selectionExposureEditor.Children.Add(new TextBlock { Text = "伽马", VerticalAlignment = VerticalAlignment.Center });
        selectionExposureEditor.Children.Add(selectionExposureGamma);
        selectionExposureEditor.Children.Add(Command("PreviewSelectionExposure", "预览选区曝光", PreviewSelectionExposureAsync, layer: true));
        selectionExposureEditor.Children.Add(Command("CommitSelectionExposure", "提交选区曝光", CommitSelectionExposureAsync, layer: true));
        selectionExposureEditor.Children.Add(Command("CancelSelectionExposure", "取消滤镜预览", CancelSelectionExposureAsync, layer: true));
        actions.Children.Add(selectionExposureEditor);
        selectionLevelsEditor.Children.Add(new TextBlock { Text = "选区输入黑", VerticalAlignment = VerticalAlignment.Center });
        selectionLevelsEditor.Children.Add(selectionLevelsInputBlack);
        selectionLevelsEditor.Children.Add(new TextBlock { Text = "输入白", VerticalAlignment = VerticalAlignment.Center });
        selectionLevelsEditor.Children.Add(selectionLevelsInputWhite);
        selectionLevelsEditor.Children.Add(new TextBlock { Text = "伽马", VerticalAlignment = VerticalAlignment.Center });
        selectionLevelsEditor.Children.Add(selectionLevelsGamma);
        selectionLevelsEditor.Children.Add(new TextBlock { Text = "输出黑", VerticalAlignment = VerticalAlignment.Center });
        selectionLevelsEditor.Children.Add(selectionLevelsOutputBlack);
        selectionLevelsEditor.Children.Add(new TextBlock { Text = "输出白", VerticalAlignment = VerticalAlignment.Center });
        selectionLevelsEditor.Children.Add(selectionLevelsOutputWhite);
        selectionLevelsEditor.Children.Add(selectionLevelsChannel);
        selectionLevelsEditor.Children.Add(Command("AutoSelectionLevelsContrast", "自动对比度", () => ApplyAutoLevelsAsync(true, LevelsAutoMode.Contrast), layer: true));
        selectionLevelsEditor.Children.Add(Command("AutoSelectionLevelsColor", "自动颜色", () => ApplyAutoLevelsAsync(true, LevelsAutoMode.Color), layer: true));
        selectionLevelsEditor.Children.Add(Command("AutoSelectionLevelsNeutral", "自动中性色", () => ApplyAutoLevelsAsync(true, LevelsAutoMode.Neutral), layer: true));
        selectionLevelsEditor.Children.Add(Command("PreviewSelectionLevels", "预览选区色阶", PreviewSelectionLevelsAsync, layer: true));
        selectionLevelsEditor.Children.Add(Command("CommitSelectionLevels", "提交选区色阶", CommitSelectionLevelsAsync, layer: true));
        selectionLevelsEditor.Children.Add(Command("CancelSelectionLevels", "取消滤镜预览", CancelSelectionLevelsAsync, layer: true));
        actions.Children.Add(selectionLevelsEditor);
        selectionHueSaturationEditor.Children.Add(new TextBlock { Text = "选区色相", VerticalAlignment = VerticalAlignment.Center });
        selectionHueSaturationEditor.Children.Add(selectionHue);
        selectionHueSaturationEditor.Children.Add(new TextBlock { Text = "饱和度", VerticalAlignment = VerticalAlignment.Center });
        selectionHueSaturationEditor.Children.Add(selectionSaturation);
        selectionHueSaturationEditor.Children.Add(new TextBlock { Text = "明度", VerticalAlignment = VerticalAlignment.Center });
        selectionHueSaturationEditor.Children.Add(selectionLightness);
        selectionHueSaturationEditor.Children.Add(selectionColorize);
        selectionHueSaturationEditor.Children.Add(Command("PreviewSelectionHueSaturation", "预览选区色相/饱和度", PreviewSelectionHueSaturationAsync, layer: true));
        selectionHueSaturationEditor.Children.Add(Command("CommitSelectionHueSaturation", "提交选区色相/饱和度", CommitSelectionHueSaturationAsync, layer: true));
        selectionHueSaturationEditor.Children.Add(Command("CancelSelectionHueSaturation", "取消滤镜预览", CancelSelectionHueSaturationAsync, layer: true));
        actions.Children.Add(selectionHueSaturationEditor);
        selectionCurvesEditor.Children.Add(new TextBlock { Text = "选区暗部", VerticalAlignment = VerticalAlignment.Center });
        selectionCurvesEditor.Children.Add(selectionCurvesShadow);
        selectionCurvesEditor.Children.Add(new TextBlock { Text = "中间调", VerticalAlignment = VerticalAlignment.Center });
        selectionCurvesEditor.Children.Add(selectionCurvesMid);
        selectionCurvesEditor.Children.Add(new TextBlock { Text = "高光", VerticalAlignment = VerticalAlignment.Center });
        selectionCurvesEditor.Children.Add(selectionCurvesHighlight);
        selectionCurvesEditor.Children.Add(selectionCurvesChannel);
        selectionCurvesEditor.Children.Add(Command("PreviewSelectionCurves", "预览选区曲线", PreviewSelectionCurvesAsync, layer: true));
        selectionCurvesEditor.Children.Add(Command("CommitSelectionCurves", "提交选区曲线", CommitSelectionCurvesAsync, layer: true));
        selectionCurvesEditor.Children.Add(Command("CancelSelectionCurves", "取消滤镜预览", CancelSelectionCurvesAsync, layer: true));
        actions.Children.Add(selectionCurvesEditor);
        selectionGradientMapEditor.Children.Add(new TextBlock { Text = "选区暗部 RGB", VerticalAlignment = VerticalAlignment.Center });
        selectionGradientMapEditor.Children.Add(selectionGradientShadowRed);
        selectionGradientMapEditor.Children.Add(selectionGradientShadowGreen);
        selectionGradientMapEditor.Children.Add(selectionGradientShadowBlue);
        selectionGradientMapEditor.Children.Add(new TextBlock { Text = "高光 RGB", VerticalAlignment = VerticalAlignment.Center });
        selectionGradientMapEditor.Children.Add(selectionGradientHighlightRed);
        selectionGradientMapEditor.Children.Add(selectionGradientHighlightGreen);
        selectionGradientMapEditor.Children.Add(selectionGradientHighlightBlue);
        selectionGradientMapEditor.Children.Add(Command("PreviewSelectionGradientMap", "预览选区渐变映射", PreviewSelectionGradientMapAsync, layer: true));
        selectionGradientMapEditor.Children.Add(Command("CommitSelectionGradientMap", "提交选区渐变映射", CommitSelectionGradientMapAsync, layer: true));
        selectionGradientMapEditor.Children.Add(Command("CancelSelectionGradientMap", "取消滤镜预览", CancelSelectionGradientMapAsync, layer: true));
        actions.Children.Add(selectionGradientMapEditor);
        selectionGrainEditor.Children.Add(new TextBlock { Text = "选区强度", VerticalAlignment = VerticalAlignment.Center });
        selectionGrainEditor.Children.Add(selectionGrainAmount);
        selectionGrainEditor.Children.Add(new TextBlock { Text = "尺寸", VerticalAlignment = VerticalAlignment.Center });
        selectionGrainEditor.Children.Add(selectionGrainSize);
        selectionGrainEditor.Children.Add(new TextBlock { Text = "粗糙度", VerticalAlignment = VerticalAlignment.Center });
        selectionGrainEditor.Children.Add(selectionGrainRoughness);
        selectionGrainEditor.Children.Add(Command("PreviewSelectionGrain", "预览选区颗粒", PreviewSelectionGrainAsync, layer: true));
        selectionGrainEditor.Children.Add(Command("CommitSelectionGrain", "提交选区颗粒", CommitSelectionGrainAsync, layer: true));
        selectionGrainEditor.Children.Add(Command("CancelSelectionGrain", "取消滤镜预览", CancelSelectionGrainAsync, layer: true));
        actions.Children.Add(selectionGrainEditor);
        motionBlurAdjustmentEditor.Children.Add(new TextBlock { Text = "角度", VerticalAlignment = VerticalAlignment.Center });
        motionBlurAdjustmentEditor.Children.Add(motionBlurAngle);
        motionBlurAdjustmentEditor.Children.Add(new TextBlock { Text = "距离", VerticalAlignment = VerticalAlignment.Center });
        motionBlurAdjustmentEditor.Children.Add(motionBlurDistance);
        motionBlurAdjustmentEditor.Children.Add(Command("ApplyMotionBlurAdjustment", "应用动感模糊", ApplyMotionBlurAdjustmentAsync, layer: true));
        actions.Children.Add(motionBlurAdjustmentEditor);
        noiseAdjustmentEditor.Children.Add(new TextBlock { Text = "数量", VerticalAlignment = VerticalAlignment.Center });
        noiseAdjustmentEditor.Children.Add(noiseAmount);
        noiseAdjustmentEditor.Children.Add(noiseGaussian);
        noiseAdjustmentEditor.Children.Add(noiseMonochromatic);
        noiseAdjustmentEditor.Children.Add(Command("ApplyNoiseAdjustment", "应用添加杂色", ApplyNoiseAdjustmentAsync, layer: true));
        actions.Children.Add(noiseAdjustmentEditor);
        lensCorrectionAdjustmentEditor.Children.Add(new TextBlock { Text = "畸变", VerticalAlignment = VerticalAlignment.Center });
        lensCorrectionAdjustmentEditor.Children.Add(lensCorrectionDistortion);
        lensCorrectionAdjustmentEditor.Children.Add(Command("ApplyLensCorrectionAdjustment", "应用镜头校正", ApplyLensCorrectionAdjustmentAsync, layer: true));
        actions.Children.Add(lensCorrectionAdjustmentEditor);
        grainAdjustmentEditor.Children.Add(new TextBlock { Text = "强度", VerticalAlignment = VerticalAlignment.Center });
        grainAdjustmentEditor.Children.Add(grainAmount);
        grainAdjustmentEditor.Children.Add(new TextBlock { Text = "尺寸", VerticalAlignment = VerticalAlignment.Center });
        grainAdjustmentEditor.Children.Add(grainSize);
        grainAdjustmentEditor.Children.Add(new TextBlock { Text = "粗糙度", VerticalAlignment = VerticalAlignment.Center });
        grainAdjustmentEditor.Children.Add(grainRoughness);
        grainAdjustmentEditor.Children.Add(Command("ApplyGrainAdjustment", "应用颗粒", ApplyGrainAdjustmentAsync, layer: true));
        actions.Children.Add(grainAdjustmentEditor);
        _ = FontLibrary;
        textFont.ItemsSource = TextLayerWorkflow.AvailableFonts;
        textContent.PropertyChanged += (_, change) =>
        {
            if (!refreshing && (change.Property == TextBox.TextProperty || change.Property == TextBox.CaretIndexProperty ||
                change.Property == TextBox.SelectionStartProperty || change.Property == TextBox.SelectionEndProperty))
                RefreshTextOverlay();
        };
        var move = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        move.Children.Add(new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center });
        move.Children.Add(layerMoveX);
        move.Children.Add(new TextBlock { Text = "Y", VerticalAlignment = VerticalAlignment.Center });
        move.Children.Add(layerMoveY);
        move.Children.Add(Command("MoveLayer", "移动图层/组", MoveLayerAsync, layer: true));
        actions.Children.Add(move);
        actions.Children.Add(Command("ApplyAppearance", "应用外观", AppearanceAsync, layer: true));
        actions.Children.Add(Command("InvertLayer", "反相图层", InvertLayerAsync, layer: true));
        actions.Children.Add(Command("Visibility", "显示 / 隐藏", VisibilityAsync, layer: true));
        var maskEdit = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        maskEdit.Children.Add(Command("RevealMaskSelection", "选区显示", () => ApplyMaskSelectionAsync(true), layer: true, mask: true));
        maskEdit.Children.Add(Command("HideMaskSelection", "选区隐藏", () => ApplyMaskSelectionAsync(false), layer: true, mask: true));
        actions.Children.Add(maskEdit);
        var reorder = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        reorder.Children.Add(Command("MoveUp", "上移", () => MoveAsync(1), layer: true));
        reorder.Children.Add(Command("MoveDown", "下移", () => MoveAsync(-1), layer: true));
        actions.Children.Add(reorder);
        DockPanel.SetDock(actions, Dock.Bottom); sidebar.Children.Add(actions);
        layers.ItemTemplate = new FuncDataTemplate<FlatLayerInfo>((item, _) => new TextBlock
        {
            Text = item is null ? "" : (item.IsVisible ? "●  " : "○  ") +
                (item.MaskSourceId is not null ? "[剪贴] " : "") + (item.IsAdjustment ? "[调整] " : "") +
                (item.IsText ? "[文字] " : "") + (item.IsShape ? "[形状] " : "") + item.Name,
            Margin = new Thickness(5), TextTrimming = TextTrimming.CharacterEllipsis
        });
        layers.SelectionChanged += (_, _) => { if (!refreshing) UpdateSelection(); };
        layers.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            draggingLayer = LayerFromVisual(e.Source as Visual);
            layerDragStart = e.GetPosition(layers);
            draggingLayers = false;
        }, RoutingStrategies.Tunnel);
        layers.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (draggingLayer is null || draggingLayers) return;
            Point current = e.GetPosition(layers);
            if (Math.Abs(current.X - layerDragStart.X) < 6 && Math.Abs(current.Y - layerDragStart.Y) < 6) return;
            int sourceIndex = Workspace.Session?.Layers.ToList().FindIndex(layer => layer.Id == draggingLayer.Id) ?? -1;
            bool canReorder = Workspace.CanMoveLayerTo(draggingLayer.Id, sourceIndex);
            bool canCopyToProject = projects.Where((_, index) => index != activeProjectIndex)
                .Any(project => Workspace.CanCopyLayerTo(project, draggingLayer.Id));
            if (!canReorder && !canCopyToProject)
            {
                draggingLayer = null;
                return;
            }
            draggingLayers = true;
            e.Pointer.Capture(layers);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        layers.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            if (!draggingLayers || draggingLayer is null) { draggingLayer = null; return; }
            FlatLayerInfo source = draggingLayer;
            EditorWorkspace sourceWorkspace = Workspace;
            int? targetProjectIndex = ProjectTabAt(e.GetPosition(this));
            Point position = e.GetPosition(layers);
            FlatLayerInfo? target = LayerFromVisual(layers.InputHitTest(position) as Visual);
            draggingLayer = null; draggingLayers = false; e.Pointer.Capture(null); e.Handled = true;
            if (targetProjectIndex is { } projectIndex && projectIndex != activeProjectIndex)
            {
                EditorWorkspace targetWorkspace = projects[projectIndex];
                if (sourceWorkspace.CanCopyLayerTo(targetWorkspace, source.Id))
                    _ = ExecuteAsync(() => CopyLayerToProjectAsync(sourceWorkspace, targetWorkspace, projectIndex, source.Id));
                return;
            }
            if (target is null || target.Id == source.Id || Workspace.Session is not { } session) return;
            int destination = session.Layers.ToList().FindIndex(layer => layer.Id == target.Id);
            if (destination >= 0 && Workspace.CanMoveLayerTo(source.Id, destination))
                _ = ExecuteAsync(() => MoveLayerToAsync(source.Id, destination));
        }, RoutingStrategies.Tunnel);
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
            var settings = new SoftBrushSettings((int)(diameter.Value ?? 40),
                (double)(opacity.Value ?? 100) / 100, selectedColor, brushType.SelectedIndex == 1 ? 1 : 0);
            if (maskPaint.IsChecked == true)
                Workspace.BeginMaskStroke(id, settings, point, maskPaintMode.SelectedIndex == 1);
            else Workspace.BeginStroke(id, settings, point);
        });
        canvas.StrokeMoved += point => PaintStep(() =>
        {
            if (maskPaint.IsChecked == true) Workspace.AppendMaskStroke(point);
            else Workspace.AppendStroke(point);
        });
        canvas.StrokeFinished += point => PaintStep(() =>
        {
            if (maskPaint.IsChecked == true) Workspace.CommitMaskStroke(point);
            else Workspace.CommitStroke(point);
        });
        canvas.StrokeCanceled += () => PaintStep(Workspace.CancelStroke);
        canvas.TextHitTest = TextCaretAt;
        canvas.TextCaretPressed += (characterIndex, extend) =>
        {
            if (!textContent.IsEffectivelyEnabled) return;
            if (extend) textContent.SelectionEnd = characterIndex;
            else textContent.SelectionStart = textContent.SelectionEnd = characterIndex;
            textContent.CaretIndex = characterIndex;
            textContent.Focus();
            RefreshTextOverlay();
            status.Text = "文字光标已定位。";
        };
        paint.IsCheckedChanged += (_, _) =>
        {
            if (paint.IsChecked == true) { rectangleSelect.IsChecked = false; maskPaint.IsChecked = false; }
            if (paint.IsChecked == true) moveSelection.IsChecked = false;
            UpdatePaintMode();
        };
        maskPaint.IsCheckedChanged += (_, _) =>
        {
            if (maskPaint.IsChecked == true) { paint.IsChecked = false; rectangleSelect.IsChecked = false; moveSelection.IsChecked = false; }
            maskPaintMode.IsEnabled = maskPaint.IsChecked == true;
            UpdatePaintMode();
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
                moveSelection.IsEnabled = Workspace.HasSelection; UpdateSelection(); status.Text = "选区已更新。";
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
                moveSelection.IsEnabled = Workspace.HasSelection; UpdateSelection(); status.Text = "选区已更新。";
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
                moveSelection.IsEnabled = Workspace.HasSelection; UpdateSelectionControls(); status.Text = "选区像素已移动。";
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
            if (!projects.Any(project => project.IsDirty)) return;
            e.Cancel = true;
            _ = ExecuteAsync(async () =>
            {
                if (await ConfirmDiscardAllAsync()) { allowClose = true; Close(); }
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
                    Key.C when Workspace.Session is not null => CopySelectionAsync,
                    Key.X when Workspace.Session is not null => CutSelectionAsync,
                    Key.V when Workspace.Session is not null => PasteSelectionAsync,
                    _ => null
                },
                KeyModifiers.Control | KeyModifiers.Shift => e.Key switch
                {
                    Key.S => SaveAsAsync,
                    Key.Z when Workspace.Session is not null => () => Task.Run(() => Workspace.Redo()),
                    Key.I when Workspace.HasSelection => InvertSelectionAsync,
                    _ => null
                },
                _ => null
            };
            if (command is not null) { e.Handled = true; _ = ExecuteAsync(command); }
        };
        Refresh();
        status.Text = Workspace.Session is null ? "新建画布、打开 .comp 工程文件夹，或导入 PNG / JPEG 图片开始。" : "工程已打开。";
        if (FontLibrary.RecoveryReport.HasIssues) status.Text = FontLibrary.RecoveryReport.Message;
    }

    private Button Command(string name, string title, Func<Task> action, bool document = false, bool layer = false, bool mask = false)
    {
        var button = new Button { Name = name, Content = title };
        button.Click += async (_, _) => await ExecuteAsync(action);
        if (document) documentButtons.Add(button);
        if (layer) layerButtons.Add(button);
        if (mask) maskButtons.Add(button);
        return button;
    }

    private async Task ExecuteAsync(Func<Task> operation)
    {
        if (IsBusy || Workspace.HasActiveStroke) return;
        IsBusy = true; layout.IsEnabled = false; status.Text = "处理中…";
        string message;
        try
        {
            await operation();
            message = Workspace.CanEdit
                ? Workspace.Session?.LegacyUpgradePending == true
                    ? "操作完成；保存时将把兼容工程升级为 v8。"
                    : "操作完成。"
                : Workspace.ReadOnlyNotice;
        }
        catch (Exception error) { message = "操作未完成：" + error.Message; }
        finally { IsBusy = false; layout.IsEnabled = true; }
        if (allowClose) return;
        try { Refresh(); }
        catch (Exception error) { message = "预览未完成：" + error.Message; }
        status.Text = message;
    }

    private void RefreshPreview()
    {
        double dpi = Workspace.Session?.Resolution ?? 96;
        var next = Workspace.Preview is { } raster ? RasterBitmap.Create(raster, dpi) : null;
        canvas.SetBitmap(next);
        preview?.Dispose(); preview = next;
    }

    private void RefreshProjectTabs()
    {
        projectTabs.Children.Clear();
        for (int index = 0; index < projects.Count; index++)
        {
            int tabIndex = index;
            EditorWorkspace project = projects[index];
            var button = new Button
            {
                Name = $"ProjectTab{index}",
                Content = (project.IsDirty ? "● " : "") +
                    (project.ProjectDirectory is { } path ? Path.GetFileName(path) : project.Session is null ? "空白工程" : "未命名工程"),
                IsEnabled = !IsBusy && !Workspace.HasActiveStroke
            };
            button.Click += (_, _) => ActivateProjectTab(tabIndex);
            projectTabs.Children.Add(button);
        }
        var close = new Button { Name = "CloseProject", Content = "关闭项目", IsEnabled = !IsBusy && Workspace.Session is not null };
        close.Click += async (_, _) => await ExecuteAsync(CloseProjectTabAsync);
        projectTabs.Children.Add(close);
    }

    private int? ProjectTabAt(Point windowPoint)
    {
        for (int index = 0; index < projects.Count; index++)
        {
            if (projectTabs.Children[index] is not Control tab || tab.TranslatePoint(new Point(0, 0), this) is not { } origin)
                continue;
            if (new Rect(origin, tab.Bounds.Size).Contains(windowPoint)) return index;
        }
        return null;
    }

    public void AddProjectTab(EditorWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (IsBusy || Workspace.HasActiveStroke) throw new InvalidOperationException("请先结束或取消当前笔划。");
        projects.Add(workspace);
        activeProjectIndex = projects.Count - 1;
        selectedId = null; displayedSession = null;
        Refresh();
    }

    public void ActivateProjectTab(int index)
    {
        if (index < 0 || index >= projects.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == activeProjectIndex) return;
        if (IsBusy || Workspace.HasActiveStroke) throw new InvalidOperationException("请先结束或取消当前笔划。");
        activeProjectIndex = index;
        selectedId = null; displayedSession = null;
        Refresh();
        status.Text = "已切换工程。";
    }

    private async Task CloseProjectTabAsync()
    {
        if (Workspace.HasActiveStroke) throw new InvalidOperationException("请先结束或取消当前笔划。");
        if (!await ConfirmDiscardAsync()) return;
        if (projects.Count == 1)
        {
            if (ReferenceEquals(clipboardProject, Workspace)) clipboardProject = null;
            projects[0] = new EditorWorkspace();
            activeProjectIndex = 0;
        }
        else
        {
            if (ReferenceEquals(clipboardProject, Workspace)) clipboardProject = null;
            projects.RemoveAt(activeProjectIndex);
            activeProjectIndex = Math.Min(activeProjectIndex, projects.Count - 1);
        }
        selectedId = null; displayedSession = null;
        Refresh();
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
        RefreshProjectTabs();
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
        bool groupedProject = Workspace.Session?.HasGroups == true;
        foreach (var button in documentButtons)
        {
            button.IsEnabled = Workspace.Session is not null &&
                (Workspace.CanEdit || button.Name is "ExportPng" or "ExportJpeg" or "Fit" or "ActualSize");
            if (groupedProject && button.Name is "AddLayer" or "AddTextLayer" or "AddBoxTextLayer" or "CanvasSize" or "ImageSize" or "RotateClockwise" or "RotateCounterClockwise")
                button.IsEnabled = false;
        }
        if (Workspace.HasFloatingSelection)
            foreach (var button in documentButtons.Where(button => button.Name is not "CommitFloatingSelection" and not "CancelFloatingSelection"))
                button.IsEnabled = false;
        foreach (var button in documentButtons.Where(button => button.Name is "CommitFloatingSelection" or "CancelFloatingSelection"))
            button.IsEnabled = Workspace.HasFloatingSelection;
        layers.IsEnabled = !Workspace.HasFloatingSelection;
        pixelGrid.IsEnabled = Workspace.Session is not null;
        rectangleSelect.IsEnabled = Workspace.CanEdit && !Workspace.HasFloatingSelection;
        moveSelection.IsEnabled = Workspace.CanEdit && (Workspace.HasSelection || Workspace.HasFloatingSelection);
        selectionShape.IsEnabled = Workspace.CanEdit && !Workspace.HasFloatingSelection;
        selectionOperation.IsEnabled = Workspace.CanEdit && !Workspace.HasFloatingSelection;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selectedItems = (layers.SelectedItems?.OfType<FlatLayerInfo>() ?? Enumerable.Empty<FlatLayerInfo>()).ToArray();
        var selected = selectedItems.FirstOrDefault();
        bool multiple = selectedItems.Length > 1;
        selectedId = selected?.Id;
        TextLayerMetadata? text = null;
        ExposureSettings? exposure = null;
        LevelsSettings? levels = null;
        HueSaturationSettings? hueSaturation = null;
        CurvesSettings? curves = null;
        GradientMapSettings? gradientMap = null;
        ShapeSettings? shape = null;
        GaussianBlurSettings? gaussianBlur = null;
        MotionBlurSettings? motionBlur = null;
        NoiseSettings? noise = null;
        LensCorrectionSettings? lensCorrection = null;
        GrainSettings? grain = null;
        refreshing = true;
        try
        {
            if (selectedId is { } id) Workspace.Session!.SelectLayer(id);
            layerName.Text = selected?.Name ?? "";
            layerOpacity.Value = selected is null ? 100 : (decimal)(selected.Opacity * 100);
            layerBlendMode.SelectedItem = selected?.BlendMode ?? "Normal";
            text = selected?.IsText == true
                ? Workspace.Session!.TextLayers.SingleOrDefault(item => item.Id == selected.Id)
                : null;
            textContent.Text = text?.Content ?? "";
            textSize.Value = text is null ? 18 : (decimal)text.FontSizePoints;
            textColor.SelectedIndex = text is null ? 0 : TextColorIndex(text);
            textAlignment.SelectedItem = text?.Alignment ?? "left";
            textLineSpacing.Value = text is null ? 0 : (decimal)text.LineSpacingPoints;
            textTracking.Value = text is null ? 0 : (decimal)text.TrackingPoints;
            textBoxWidth.Value = text?.BoxWidth is { } width ? (decimal)width : 360;
            textFont.SelectedItem = text is null
                ? null
                : textFont.Items.OfType<string>().FirstOrDefault(font =>
                    string.Equals(font, text.FontPostScriptName, StringComparison.OrdinalIgnoreCase));
            if (selected?.IsAdjustment == true && selected.AdjustmentKind == "Exposure")
                exposure = Workspace.Session!.GetExposureAdjustment(selected.Id);
            if (selected?.IsAdjustment == true && selected.AdjustmentKind == "Levels")
                levels = Workspace.Session!.GetLevelsAdjustment(selected.Id);
            if (selected?.IsAdjustment == true && selected.AdjustmentKind == "Hue/Saturation")
                hueSaturation = Workspace.Session!.GetHueSaturationAdjustment(selected.Id);
            if (selected?.IsAdjustment == true && selected.AdjustmentKind == "Curves")
                curves = Workspace.Session!.GetCurvesAdjustment(selected.Id);
            if (selected?.IsAdjustment == true && selected.AdjustmentKind == "Gradient Map")
                gradientMap = Workspace.Session!.GetGradientMapAdjustment(selected.Id);
            if (selected?.IsAdjustment == true && selected.AdjustmentKind == "Gaussian Blur")
                gaussianBlur = Workspace.Session!.GetGaussianBlurAdjustment(selected.Id);
            if (selected?.IsAdjustment == true && selected.AdjustmentKind == "Motion Blur")
                motionBlur = Workspace.Session!.GetMotionBlurAdjustment(selected.Id);
            if (selected?.IsAdjustment == true && selected.AdjustmentKind == "Add Noise")
                noise = Workspace.Session!.GetNoiseAdjustment(selected.Id);
            if (selected?.IsAdjustment == true && selected.AdjustmentKind == "Lens Correction")
                lensCorrection = Workspace.Session!.GetLensCorrectionAdjustment(selected.Id);
            if (selected?.IsAdjustment == true && selected.AdjustmentKind == "Grain")
                grain = Workspace.Session!.GetGrainAdjustment(selected.Id);
            if (selected?.IsShape == true)
                shape = Workspace.Session!.GetShape(selected.Id);
            adjustmentExposure.Value = exposure is null ? 0 : (decimal)exposure.Exposure;
            adjustmentOffset.Value = exposure is null ? 0 : (decimal)exposure.Offset;
            adjustmentGamma.Value = exposure is null ? 1 : (decimal)exposure.Gamma;
            LevelRange rgb = levels?.Rgb ?? new LevelRange();
            levelsInputBlack.Value = (decimal)rgb.InputBlack;
            levelsInputWhite.Value = (decimal)rgb.InputWhite;
            levelsGamma.Value = (decimal)rgb.Gamma;
            levelsOutputBlack.Value = (decimal)rgb.OutputBlack;
            levelsOutputWhite.Value = (decimal)rgb.OutputWhite;
            adjustmentHue.Value = hueSaturation is null ? 0 : (decimal)hueSaturation.Hue;
            adjustmentSaturation.Value = hueSaturation is null ? 0 : (decimal)hueSaturation.Saturation;
            adjustmentLightness.Value = hueSaturation is null ? 0 : (decimal)hueSaturation.Lightness;
            adjustmentColorize.IsChecked = hueSaturation?.Colorize == true;
            curvesChannel.SelectedIndex = 0;
            curvesShadow.Value = curves is null ? 0 : (decimal)curves.Shadow;
            curvesMid.Value = curves is null ? 128 : (decimal)curves.Mid;
            curvesHighlight.Value = curves is null ? 255 : (decimal)curves.Highlight;
            gradientShadowRed.Value = gradientMap is null ? 0 : gradientMap.Shadow.Red;
            gradientShadowGreen.Value = gradientMap is null ? 0 : gradientMap.Shadow.Green;
            gradientShadowBlue.Value = gradientMap is null ? 0 : gradientMap.Shadow.Blue;
            gradientHighlightRed.Value = gradientMap is null ? 255 : gradientMap.Highlight.Red;
            gradientHighlightGreen.Value = gradientMap is null ? 255 : gradientMap.Highlight.Green;
            gradientHighlightBlue.Value = gradientMap is null ? 255 : gradientMap.Highlight.Blue;
            gaussianBlurRadius.Value = gaussianBlur is null ? 1 : gaussianBlur.Radius;
            motionBlurAngle.Value = motionBlur is null ? 0 : (decimal)motionBlur.Angle;
            motionBlurDistance.Value = motionBlur is null ? 1 : motionBlur.Distance;
            noiseAmount.Value = noise is null ? 10 : (decimal)noise.Amount;
            noiseGaussian.IsChecked = noise?.Gaussian == true;
            noiseMonochromatic.IsChecked = noise?.Monochromatic == true;
            lensCorrectionDistortion.Value = lensCorrection is null ? 0 : (decimal)lensCorrection.Distortion;
            grainAmount.Value = grain is null ? 25 : (decimal)grain.Amount;
            grainSize.Value = grain is null ? 1.5m : (decimal)grain.Size;
            grainRoughness.Value = grain is null ? 50 : (decimal)grain.Roughness;
            shapeCornerRadius.Value = shape is null ? 0 : (decimal)shape.CornerRadius;
        }
        finally { refreshing = false; }
        bool missingFont = text is not null && !TextLayerWorkflow.Inspect(Workspace.Session!).Single(status => status.Metadata.Id == text.Id).FontAvailable;
        bool showTextEditor = selected?.IsText == true && !multiple && (Workspace.CanEdit || missingFont);
        layerName.IsEnabled = Workspace.CanEdit && selected is not null && !multiple;
        textEditorPanel.IsVisible = showTextEditor;
        textContent.IsEnabled = showTextEditor && Workspace.CanEdit;
        textFont.IsEnabled = showTextEditor;
        textSize.IsEnabled = textContent.IsEnabled;
        textColor.IsEnabled = textContent.IsEnabled;
        textAlignment.IsEnabled = textContent.IsEnabled;
        textLineSpacing.IsEnabled = textContent.IsEnabled;
        textTracking.IsEnabled = textContent.IsEnabled;
        textBoxWidth.IsEnabled = textContent.IsEnabled && selected?.IsText == true &&
            Workspace.Session!.TextLayers.Single(item => item.Id == selected.Id).Layout == "box";
        bool showExposureEditor = selected?.IsAdjustment == true && selected.AdjustmentKind == "Exposure" &&
            !multiple && Workspace.CanEdit;
        bool showLevelsEditor = selected?.IsAdjustment == true && selected.AdjustmentKind == "Levels" &&
            !multiple && Workspace.CanEdit;
        bool showHueSaturationEditor = selected?.IsAdjustment == true && selected.AdjustmentKind == "Hue/Saturation" &&
            !multiple && Workspace.CanEdit;
        bool showCurvesEditor = selected?.IsAdjustment == true && selected.AdjustmentKind == "Curves" &&
            !multiple && Workspace.CanEdit;
        bool showGradientMapEditor = selected?.IsAdjustment == true && selected.AdjustmentKind == "Gradient Map" &&
            !multiple && Workspace.CanEdit;
        bool showGaussianBlurEditor = selected?.IsAdjustment == true && selected.AdjustmentKind == "Gaussian Blur" &&
            !multiple && Workspace.CanEdit;
        bool showSelectionGaussianBlurEditor = selected is { IsGroup: false, IsText: false, IsAdjustment: false } &&
            !multiple && Workspace.CanEdit && !Workspace.HasFloatingSelection &&
            (Workspace.HasSelection || Workspace.HasFilterPreview);
        bool showSelectionMotionBlurEditor = showSelectionGaussianBlurEditor;
        bool showSelectionNoiseEditor = showSelectionGaussianBlurEditor;
        bool showSelectionLensCorrectionEditor = showSelectionGaussianBlurEditor;
        bool showSelectionExposureEditor = showSelectionGaussianBlurEditor;
        bool showSelectionLevelsEditor = showSelectionGaussianBlurEditor;
        bool showSelectionHueSaturationEditor = showSelectionGaussianBlurEditor;
        bool showSelectionCurvesEditor = showSelectionGaussianBlurEditor;
        bool showSelectionGradientMapEditor = showSelectionGaussianBlurEditor;
        bool showSelectionGrainEditor = showSelectionGaussianBlurEditor;
        bool showMotionBlurEditor = selected?.IsAdjustment == true && selected.AdjustmentKind == "Motion Blur" &&
            !multiple && Workspace.CanEdit;
        bool showNoiseEditor = selected?.IsAdjustment == true && selected.AdjustmentKind == "Add Noise" &&
            !multiple && Workspace.CanEdit;
        bool showLensCorrectionEditor = selected?.IsAdjustment == true && selected.AdjustmentKind == "Lens Correction" &&
            !multiple && Workspace.CanEdit;
        bool showGrainEditor = selected?.IsAdjustment == true && selected.AdjustmentKind == "Grain" &&
            !multiple && Workspace.CanEdit;
        adjustmentEditor.IsVisible = showExposureEditor;
        levelsAdjustmentEditor.IsVisible = showLevelsEditor;
        hueSaturationAdjustmentEditor.IsVisible = showHueSaturationEditor;
        curvesAdjustmentEditor.IsVisible = showCurvesEditor;
        gradientMapAdjustmentEditor.IsVisible = showGradientMapEditor;
        gaussianBlurAdjustmentEditor.IsVisible = showGaussianBlurEditor;
        selectionGaussianBlurEditor.IsVisible = showSelectionGaussianBlurEditor;
        selectionMotionBlurEditor.IsVisible = showSelectionMotionBlurEditor;
        selectionNoiseEditor.IsVisible = showSelectionNoiseEditor;
        selectionLensCorrectionEditor.IsVisible = showSelectionLensCorrectionEditor;
        selectionExposureEditor.IsVisible = showSelectionExposureEditor;
        selectionLevelsEditor.IsVisible = showSelectionLevelsEditor;
        selectionHueSaturationEditor.IsVisible = showSelectionHueSaturationEditor;
        selectionCurvesEditor.IsVisible = showSelectionCurvesEditor;
        selectionGradientMapEditor.IsVisible = showSelectionGradientMapEditor;
        selectionGrainEditor.IsVisible = showSelectionGrainEditor;
        motionBlurAdjustmentEditor.IsVisible = showMotionBlurEditor;
        noiseAdjustmentEditor.IsVisible = showNoiseEditor;
        lensCorrectionAdjustmentEditor.IsVisible = showLensCorrectionEditor;
        grainAdjustmentEditor.IsVisible = showGrainEditor;
        adjustmentExposure.IsEnabled = adjustmentOffset.IsEnabled = adjustmentGamma.IsEnabled = showExposureEditor;
        levelsInputBlack.IsEnabled = levelsInputWhite.IsEnabled = levelsGamma.IsEnabled =
            levelsOutputBlack.IsEnabled = levelsOutputWhite.IsEnabled = showLevelsEditor;
        levelsChannel.IsEnabled = showLevelsEditor;
        adjustmentHue.IsEnabled = adjustmentSaturation.IsEnabled = adjustmentLightness.IsEnabled =
            adjustmentColorize.IsEnabled = showHueSaturationEditor;
        curvesShadow.IsEnabled = curvesMid.IsEnabled = curvesHighlight.IsEnabled = showCurvesEditor;
        curvesChannel.IsEnabled = showCurvesEditor;
        gradientShadowRed.IsEnabled = gradientShadowGreen.IsEnabled = gradientShadowBlue.IsEnabled =
        gradientHighlightRed.IsEnabled = gradientHighlightGreen.IsEnabled = gradientHighlightBlue.IsEnabled = showGradientMapEditor;
        gaussianBlurRadius.IsEnabled = showGaussianBlurEditor;
        selectionGaussianBlurRadius.IsEnabled = showSelectionGaussianBlurEditor && !Workspace.HasFilterPreview;
        selectionMotionBlurAngle.IsEnabled = selectionMotionBlurDistance.IsEnabled =
            showSelectionMotionBlurEditor && !Workspace.HasFilterPreview;
        selectionNoiseAmount.IsEnabled = selectionNoiseGaussian.IsEnabled = selectionNoiseMonochromatic.IsEnabled =
            showSelectionNoiseEditor && !Workspace.HasFilterPreview;
        selectionLensCorrectionDistortion.IsEnabled = showSelectionLensCorrectionEditor && !Workspace.HasFilterPreview;
        selectionExposure.IsEnabled = selectionExposureOffset.IsEnabled = selectionExposureGamma.IsEnabled =
            showSelectionExposureEditor && !Workspace.HasFilterPreview;
        selectionLevelsInputBlack.IsEnabled = selectionLevelsInputWhite.IsEnabled = selectionLevelsGamma.IsEnabled =
            selectionLevelsOutputBlack.IsEnabled = selectionLevelsOutputWhite.IsEnabled =
            showSelectionLevelsEditor && !Workspace.HasFilterPreview;
        selectionLevelsChannel.IsEnabled = showSelectionLevelsEditor && !Workspace.HasFilterPreview;
        selectionHue.IsEnabled = selectionSaturation.IsEnabled = selectionLightness.IsEnabled = selectionColorize.IsEnabled =
            showSelectionHueSaturationEditor && !Workspace.HasFilterPreview;
        selectionCurvesShadow.IsEnabled = selectionCurvesMid.IsEnabled = selectionCurvesHighlight.IsEnabled =
            showSelectionCurvesEditor && !Workspace.HasFilterPreview;
        selectionCurvesChannel.IsEnabled = showSelectionCurvesEditor && !Workspace.HasFilterPreview;
        selectionGradientShadowRed.IsEnabled = selectionGradientShadowGreen.IsEnabled = selectionGradientShadowBlue.IsEnabled =
        selectionGradientHighlightRed.IsEnabled = selectionGradientHighlightGreen.IsEnabled = selectionGradientHighlightBlue.IsEnabled =
            showSelectionGradientMapEditor && !Workspace.HasFilterPreview;
        selectionGrainAmount.IsEnabled = selectionGrainSize.IsEnabled = selectionGrainRoughness.IsEnabled =
            showSelectionGrainEditor && !Workspace.HasFilterPreview;
        motionBlurAngle.IsEnabled = motionBlurDistance.IsEnabled = showMotionBlurEditor;
        noiseAmount.IsEnabled = noiseGaussian.IsEnabled = noiseMonochromatic.IsEnabled = showNoiseEditor;
        lensCorrectionDistortion.IsEnabled = showLensCorrectionEditor;
        grainAmount.IsEnabled = grainSize.IsEnabled = grainRoughness.IsEnabled = showGrainEditor;
        layerOpacity.IsEnabled = Workspace.CanEdit && selected is not null && !multiple;
        layerBlendMode.IsEnabled = Workspace.CanEdit && selected is not null && !multiple && selected.IsAdjustment == false;
        layerRotation.IsEnabled = Workspace.CanEdit && selected is not null && !multiple;
        UpdatePaintMode();
        bool groupedProject = Workspace.Session?.HasGroups == true;
        ProjectSession? currentSession = Workspace.Session;
        foreach (var button in layerButtons)
        {
            button.IsEnabled = Workspace.CanEdit && selected is not null && !Workspace.HasFloatingSelection;
            if (multiple && button.Name is not ("GroupLayer" or "MergeLayerDown")) button.IsEnabled = false;
            if (groupedProject && button.Name is "DuplicateLayer" or "DeleteLayer" or "SetClippingMask" or "ReleaseClippingMask" or "MoveUp" or "MoveDown")
                button.IsEnabled = false;
            if (selected?.IsAdjustment == true && button.Name is "LayerViaCopy" or "GroupLayer" or "SetClippingMask" or "ReleaseClippingMask")
                button.IsEnabled = false;
            if (button.Name == "LayerViaCopy")
                button.IsEnabled = Workspace.CanLayerViaCopy;
            if (button.Name == "ApplyText")
                button.IsEnabled = Workspace.CanEdit && selected?.IsText == true && !multiple && !Workspace.HasFloatingSelection;
            if (button.Name == "InvertLayer")
                button.IsEnabled = Workspace.CanEdit && selected is { IsGroup: false, IsText: false, IsAdjustment: false } &&
                    !multiple && !Workspace.HasFloatingSelection;
            if (button.Name == "AddExposureAdjustment")
                button.IsEnabled = Workspace.CanEdit && !groupedProject && selected is not null && !multiple &&
                    !Workspace.HasFloatingSelection;
            if (button.Name is "AddRectangleShape" or "AddEllipseShape")
                button.IsEnabled = Workspace.CanEdit && !groupedProject && selected is not null && !multiple &&
                    !Workspace.HasFloatingSelection;
            if (button.Name == "ApplyShape")
                button.IsEnabled = Workspace.CanEdit && !groupedProject && selected?.IsShape == true && !multiple &&
                    !Workspace.HasFloatingSelection;
            if (button.Name == "ApplyExposureAdjustment")
                button.IsEnabled = showExposureEditor && !Workspace.HasFloatingSelection;
            if (button.Name == "AddLevelsAdjustment")
                button.IsEnabled = Workspace.CanEdit && !groupedProject && selected is not null && !multiple &&
                    !Workspace.HasFloatingSelection;
            if (button.Name == "ApplyLevelsAdjustment")
                button.IsEnabled = showLevelsEditor && !Workspace.HasFloatingSelection;
            if (button.Name is "AutoLevelsContrast" or "AutoLevelsColor" or "AutoLevelsNeutral")
                button.IsEnabled = showLevelsEditor && !Workspace.HasFloatingSelection;
            if (button.Name == "AddHueSaturationAdjustment")
                button.IsEnabled = Workspace.CanEdit && !groupedProject && selected is not null && !multiple &&
                    !Workspace.HasFloatingSelection;
            if (button.Name == "ApplyHueSaturationAdjustment")
                button.IsEnabled = showHueSaturationEditor && !Workspace.HasFloatingSelection;
            if (button.Name == "AddCurvesAdjustment")
                button.IsEnabled = Workspace.CanEdit && !groupedProject && selected is not null && !multiple &&
                    !Workspace.HasFloatingSelection;
            if (button.Name == "ApplyCurvesAdjustment")
                button.IsEnabled = showCurvesEditor && !Workspace.HasFloatingSelection;
            if (button.Name == "AddGradientMapAdjustment")
                button.IsEnabled = Workspace.CanEdit && !groupedProject && selected is not null && !multiple &&
                    !Workspace.HasFloatingSelection;
            if (button.Name == "ApplyGradientMapAdjustment")
                button.IsEnabled = showGradientMapEditor && !Workspace.HasFloatingSelection;
            if (button.Name == "AddGaussianBlurAdjustment")
                button.IsEnabled = Workspace.CanEdit && !groupedProject && selected is not null && !multiple &&
                    !Workspace.HasFloatingSelection;
            if (button.Name == "ApplyGaussianBlurAdjustment")
                button.IsEnabled = showGaussianBlurEditor && !Workspace.HasFloatingSelection;
            if (button.Name == "PreviewSelectionGaussianBlur")
                button.IsEnabled = showSelectionGaussianBlurEditor && Workspace.HasSelection && !Workspace.HasFilterPreview;
            if (button.Name is "CommitSelectionGaussianBlur" or "CancelSelectionGaussianBlur")
                button.IsEnabled = Workspace.HasFilterPreview;
            if (button.Name == "PreviewSelectionMotionBlur")
                button.IsEnabled = showSelectionMotionBlurEditor && Workspace.HasSelection && !Workspace.HasFilterPreview;
            if (button.Name is "CommitSelectionMotionBlur" or "CancelSelectionMotionBlur")
                button.IsEnabled = Workspace.HasFilterPreview;
            if (button.Name == "PreviewSelectionNoise")
                button.IsEnabled = showSelectionNoiseEditor && Workspace.HasSelection && !Workspace.HasFilterPreview;
            if (button.Name is "CommitSelectionNoise" or "CancelSelectionNoise")
                button.IsEnabled = Workspace.HasFilterPreview;
            if (button.Name == "PreviewSelectionLensCorrection")
                button.IsEnabled = showSelectionLensCorrectionEditor && Workspace.HasSelection && !Workspace.HasFilterPreview;
            if (button.Name is "CommitSelectionLensCorrection" or "CancelSelectionLensCorrection")
                button.IsEnabled = Workspace.HasFilterPreview;
            if (button.Name == "PreviewSelectionExposure")
                button.IsEnabled = showSelectionExposureEditor && Workspace.HasSelection && !Workspace.HasFilterPreview;
            if (button.Name is "CommitSelectionExposure" or "CancelSelectionExposure")
                button.IsEnabled = Workspace.HasFilterPreview;
            if (button.Name == "PreviewSelectionLevels")
                button.IsEnabled = showSelectionLevelsEditor && Workspace.HasSelection && !Workspace.HasFilterPreview;
            if (button.Name is "AutoSelectionLevelsContrast" or "AutoSelectionLevelsColor" or "AutoSelectionLevelsNeutral")
                button.IsEnabled = showSelectionLevelsEditor && Workspace.HasSelection && !Workspace.HasFilterPreview;
            if (button.Name is "CommitSelectionLevels" or "CancelSelectionLevels")
                button.IsEnabled = Workspace.HasFilterPreview;
            if (button.Name == "PreviewSelectionHueSaturation")
                button.IsEnabled = showSelectionHueSaturationEditor && Workspace.HasSelection && !Workspace.HasFilterPreview;
            if (button.Name is "CommitSelectionHueSaturation" or "CancelSelectionHueSaturation")
                button.IsEnabled = Workspace.HasFilterPreview;
            if (button.Name == "PreviewSelectionCurves")
                button.IsEnabled = showSelectionCurvesEditor && Workspace.HasSelection && !Workspace.HasFilterPreview;
            if (button.Name is "CommitSelectionCurves" or "CancelSelectionCurves")
                button.IsEnabled = Workspace.HasFilterPreview;
            if (button.Name == "PreviewSelectionGradientMap")
                button.IsEnabled = showSelectionGradientMapEditor && Workspace.HasSelection && !Workspace.HasFilterPreview;
            if (button.Name is "CommitSelectionGradientMap" or "CancelSelectionGradientMap")
                button.IsEnabled = Workspace.HasFilterPreview;
            if (button.Name == "PreviewSelectionGrain")
                button.IsEnabled = showSelectionGrainEditor && Workspace.HasSelection && !Workspace.HasFilterPreview;
            if (button.Name is "CommitSelectionGrain" or "CancelSelectionGrain")
                button.IsEnabled = Workspace.HasFilterPreview;
            if (button.Name == "AddMotionBlurAdjustment")
                button.IsEnabled = Workspace.CanEdit && !groupedProject && selected is not null && !multiple &&
                    !Workspace.HasFloatingSelection;
            if (button.Name == "ApplyMotionBlurAdjustment")
                button.IsEnabled = showMotionBlurEditor && !Workspace.HasFloatingSelection;
            if (button.Name == "AddNoiseAdjustment")
                button.IsEnabled = Workspace.CanEdit && !groupedProject && selected is not null && !multiple &&
                    !Workspace.HasFloatingSelection;
            if (button.Name == "ApplyNoiseAdjustment")
                button.IsEnabled = showNoiseEditor && !Workspace.HasFloatingSelection;
            if (button.Name == "AddLensCorrectionAdjustment")
                button.IsEnabled = Workspace.CanEdit && !groupedProject && selected is not null && !multiple &&
                    !Workspace.HasFloatingSelection;
            if (button.Name == "ApplyLensCorrectionAdjustment")
                button.IsEnabled = showLensCorrectionEditor && !Workspace.HasFloatingSelection;
            if (button.Name == "AddGrainAdjustment")
                button.IsEnabled = Workspace.CanEdit && !groupedProject && selected is not null && !multiple &&
                    !Workspace.HasFloatingSelection;
            if (button.Name == "ApplyGrainAdjustment")
                button.IsEnabled = showGrainEditor && !Workspace.HasFloatingSelection;
            if (selected is not null && button.Name == "MoveUp")
                button.IsEnabled = Workspace.CanMoveLayer(selected.Id, 1);
            if (selected is not null && button.Name == "MoveDown")
                button.IsEnabled = Workspace.CanMoveLayer(selected.Id, -1);
            if (button.Name is "ScaleGroupDown" or "ScaleGroupUp" or "RotateGroupCounterClockwise" or "RotateGroupClockwise" or
                "RotateLayerCounterClockwise" or "RotateLayerClockwise")
                button.IsEnabled = Workspace.CanEdit && selected is not null && !selected.IsAdjustment && !multiple &&
                    (selected.IsGroup || !groupedProject) && !Workspace.HasFloatingSelection;
            if (button.Name == "RotateLayerCustom")
                button.IsEnabled = Workspace.CanEdit && selected is not null && !selected.IsAdjustment && !multiple &&
                    (selected.IsGroup || !groupedProject) && !Workspace.HasFloatingSelection &&
                    layerRotation.Value is not null;
            if (button.Name == "GroupLayer")
                button.IsEnabled = Workspace.CanEdit && selectedItems.Length > 0 && selectedItems.All(item => !item.IsAdjustment) && !Workspace.HasFloatingSelection;
            if (button.Name == "UngroupLayer")
                button.IsEnabled = Workspace.CanEdit && selected?.IsGroup == true &&
                    Workspace.Session!.IsGroupTransformIdentity(selected.Id) && !Workspace.HasFloatingSelection;
            if (button.Name == "BakeUngroupLayer")
                button.IsEnabled = Workspace.CanEdit && selected?.IsGroup == true &&
                    !Workspace.Session!.IsGroupTransformIdentity(selected.Id) && !Workspace.HasFloatingSelection;
            if (button.Name == "BakeLayerTransform")
                button.IsEnabled = Workspace.CanEdit && selected is not null && !selected.IsGroup && !selected.IsAdjustment && !multiple &&
                    !groupedProject && !Workspace.Session!.IsLayerTransformIdentity(selected.Id) && !Workspace.HasFloatingSelection;
            if (button.Name == "MergeLayerDown")
            {
                var layerList = currentSession?.Layers.ToList() ?? [];
                var ordered = selectedItems
                    .Select(item => (Layer: item, Index: layerList.FindIndex(layer => layer.Id == item.Id)))
                    .OrderBy(item => item.Index)
                    .ToArray();
                Guid[] mergeIds;
                if (multiple) mergeIds = selectedItems.Select(item => item.Id).ToArray();
                else if (ordered.Length == 1 && currentSession?.PreviousSiblingId(ordered[0].Layer.Id) is { } previousId)
                    mergeIds = [previousId, ordered[0].Layer.Id];
                else mergeIds = [];
                button.IsEnabled = Workspace.CanEdit && !Workspace.HasFloatingSelection &&
                    Workspace.CanMergeSelectedLayers(mergeIds);
            }
        }
        if (Workspace.Session is { } session && selected is not null)
        {
            int index = session.Layers.ToList().FindIndex(layer => layer.Id == selected.Id);
            foreach (var button in layerButtons.Where(button => button.Name is "SetClippingMask" or "ReleaseClippingMask"))
                button.IsEnabled = Workspace.CanEdit && !multiple && !selected.IsAdjustment && (button.Name == "ReleaseClippingMask"
                    ? selected.MaskSourceId is not null
                    : selected.MaskSourceId is null && index > 0);
        }
        if (selected?.IsGroup == true || selected?.IsAdjustment == true)
            foreach (var button in documentButtons.Where(button => button.Name is "CopySelection" or "CutSelection" or "PasteSelection" or "LoadAlphaSelection"))
                button.IsEnabled = false;
        if (selected is { IsGroup: false, IsAdjustment: false } && Workspace.Session is { } selectedSession &&
            !selectedSession.IsLayerTransformIdentity(selected.Id))
        foreach (var button in documentButtons.Where(button => button.Name == "PasteSelection"))
            button.IsEnabled = Workspace.CanPasteSelection ||
                (clipboardProject is { } source && Workspace.CanPasteSelectionFrom(source));
        if (multiple)
            foreach (var button in documentButtons.Where(button => button.Name is "CopySelection" or "CutSelection" or "PasteSelection" or "LoadAlphaSelection"))
                button.IsEnabled = false;
        foreach (var button in maskButtons)
            button.IsEnabled = Workspace.CanEdit && !multiple && selected is not null && selected.IsAdjustment == false &&
                (button.Name == "AddMask" || selected.HasMask);
        maskRadius.IsEnabled = Workspace.CanEdit && !multiple && selected?.HasMask == true;
        resolveTextFont.IsEnabled = missingFont && !Workspace.HasFloatingSelection;
        UpdateSelectionControls();
        RefreshTextOverlay();
    }

    private void UpdateCurvesControls()
    {
        if (selectedId is not { } id || Workspace.Session is not { } session ||
            session.Layers.Single(layer => layer.Id == id).AdjustmentKind != "Curves")
        {
            curvesShadow.Value = 0;
            curvesMid.Value = 128;
            curvesHighlight.Value = 255;
            return;
        }
        CurvesSettings settings = session.GetCurvesAdjustment(id);
        CurveChannelSettings channel = curvesChannel.SelectedIndex switch
        {
            1 => settings.Red ?? new CurveChannelSettings(settings.Shadow, settings.Mid, settings.Highlight),
            2 => settings.Green ?? new CurveChannelSettings(settings.Shadow, settings.Mid, settings.Highlight),
            3 => settings.Blue ?? new CurveChannelSettings(settings.Shadow, settings.Mid, settings.Highlight),
            _ => new CurveChannelSettings(settings.Shadow, settings.Mid, settings.Highlight)
        };
        curvesShadow.Value = (decimal)channel.Shadow;
        curvesMid.Value = (decimal)channel.Mid;
        curvesHighlight.Value = (decimal)channel.Highlight;
    }

    private void UpdateSelectionControls()
    {
        bool enabled = Workspace.CanEdit && Workspace.HasSelection && !Workspace.HasFloatingSelection;
        selectionFeatherRadius.IsEnabled = enabled;
        foreach (var button in documentButtons.Where(button => button.Name == "FeatherSelection"))
            button.IsEnabled = enabled;
    }

    private Task EditAsync(Action<ProjectSession> edit) => Task.Run(() => Workspace.Edit(edit));

    private void UpdatePaintMode()
    {
        bool multiple = (layers.SelectedItems?.OfType<FlatLayerInfo>() ?? Enumerable.Empty<FlatLayerInfo>()).Take(2).Count() > 1;
        bool editable = Workspace.CanEdit && selectedId is not null && !multiple;
        bool selectedGroup = editable && Workspace.Session!.Layers.Single(layer => layer.Id == selectedId).IsGroup;
        bool hasMask = editable && Workspace.Session!.Layers.Single(layer => layer.Id == selectedId).HasMask;
        bool textMode = editable && !selectedGroup && Workspace.Session!.TextLayers.SingleOrDefault(text => text.Id == selectedId) is { } text &&
            TextLayerWorkflow.Inspect(Workspace.Session).Single(status => status.Metadata.Id == text.Id).FontAvailable &&
            !Workspace.HasFloatingSelection && rectangleSelect.IsChecked != true && moveSelection.IsChecked != true && maskPaint.IsChecked != true;
        maskPaint.IsEnabled = hasMask;
        maskPaintMode.IsEnabled = hasMask && maskPaint.IsChecked == true;
        bool selectedAdjustment = editable && Workspace.Session!.Layers.Single(layer => layer.Id == selectedId).IsAdjustment;
        paint.IsEnabled = editable && !selectedGroup && !selectedAdjustment;
        canvas.TextEditEnabled = textMode;
        canvas.PaintEnabled = editable && !selectedAdjustment && !textMode && !Workspace.HasFloatingSelection &&
            ((!selectedGroup && paint.IsChecked == true) ||
             maskPaint.IsChecked == true);
        canvas.SelectionEnabled = editable && !selectedGroup && !selectedAdjustment && !Workspace.HasFloatingSelection && rectangleSelect.IsChecked == true;
        canvas.SelectionMoveEnabled = editable && !selectedGroup && !selectedAdjustment &&
            (Workspace.HasSelection || Workspace.HasFloatingSelection) && moveSelection.IsChecked == true;
    }

    private int? TextCaretAt(Point document)
    {
        if (!Workspace.CanEdit || selectedId is not { } id || Workspace.Session is not { } session)
            return null;
        TextLayerMetadata? metadata = session.TextLayers.SingleOrDefault(text => text.Id == id);
        if (metadata is null || !TextLayerWorkflow.Inspect(session).Single(status => status.Metadata.Id == id).FontAvailable)
            return null;
        TileRaster raster = session.GetLayerRaster(id);
        Compositor.Imaging.TextHitTestResult hit = TextLayerWorkflow.HitTest(metadata, session.GetLayerTransform(id),
            raster.Width, raster.Height, session.Resolution, (float)document.X, (float)document.Y);
        return hit.IsInside ? hit.CharacterIndex : null;
    }

    private void RefreshTextOverlay()
    {
        if (!canvas.TextEditEnabled || selectedId is not { } id || Workspace.Session is not { } session)
        {
            canvas.SetTextOverlay(null, null, null);
            return;
        }
        TextLayerMetadata? metadata = session.TextLayers.SingleOrDefault(text => text.Id == id);
        if (metadata is null) { canvas.SetTextOverlay(null, null, null); return; }
        TileRaster raster = session.GetLayerRaster(id);
        TextLayoutSnapshot layout;
        try
        {
            string content = textContent.Text ?? metadata.Content;
            layout = TextLayerWorkflow.Layout(metadata with { Content = content }, session.Resolution, raster.Width);
        }
        catch (NotSupportedException)
        {
            canvas.SetTextOverlay(null, null, null);
            return;
        }
        LayerTransformInfo transform = session.GetLayerTransform(id);
        Point ToDocument(Point source)
        {
            double localX = source.X * transform.Width / raster.Width - transform.Width / 2;
            double localY = source.Y * transform.Height / raster.Height - transform.Height / 2;
            if (transform.FlipX) localX = -localX;
            if (transform.FlipY) localY = -localY;
            double radians = transform.Rotation * Math.PI / 180;
            return new Point(transform.X + transform.Width / 2 + localX * Math.Cos(radians) - localY * Math.Sin(radians),
                transform.Y + transform.Height / 2 + localX * Math.Sin(radians) + localY * Math.Cos(radians));
        }
        TextCaretPosition caret = layout.Caret(textContent.CaretIndex);
        var polygons = layout.Selection(textContent.SelectionStart, textContent.SelectionEnd)
            .Select(rectangle => (IReadOnlyList<Point>)[
                ToDocument(new Point(rectangle.X, rectangle.Y)),
                ToDocument(new Point(rectangle.X + rectangle.Width, rectangle.Y)),
                ToDocument(new Point(rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height)),
                ToDocument(new Point(rectangle.X, rectangle.Y + rectangle.Height))])
            .ToArray();
        canvas.SetTextOverlay(polygons, ToDocument(new Point(caret.X, caret.Y)),
            ToDocument(new Point(caret.X, caret.Y + caret.Height)));
    }
    private Task AddLayerAsync() => EditAsync(session =>
    {
        int index = selectedId is { } id ? session.Layers.ToList().FindIndex(layer => layer.Id == id) + 1 : session.Layers.Count;
        session.AddBlankLayer("Layer " + (session.Layers.Count + 1), index);
    });
    private Task AddTextLayerAsync() => Task.Run(() => Workspace.AddTextLayer());
    private Task AddBoxTextLayerAsync() => Task.Run(() => Workspace.AddTextLayer("文字", box: true));
    private Task AddRectangleShapeAsync()
    {
        var selected = ShapeColor(color.SelectedIndex);
        double radius = (double)(shapeCornerRadius.Value ?? 0);
        return Task.Run(() => Workspace.AddShapeLayer(new ShapeSettings(
            "Rectangle", selected.Red, selected.Green, selected.Blue, radius)));
    }
    private Task AddEllipseShapeAsync()
    {
        var selected = ShapeColor(color.SelectedIndex);
        return Task.Run(() => Workspace.AddShapeLayer(new ShapeSettings(
            "Ellipse", selected.Red, selected.Green, selected.Blue)));
    }
    private Task ApplyShapeAsync()
    {
        if (selectedId is not { } id || Workspace.Session is not { } session)
            throw new InvalidOperationException("当前工程没有活动形状图层。");
        ShapeSettings current = session.GetShape(id);
        var selected = ShapeColor(color.SelectedIndex);
        double radius = (double)(shapeCornerRadius.Value ?? (decimal)current.CornerRadius);
        return Task.Run(() => Workspace.Edit(editSession => editSession.SetShape(id, current with
        {
            Red = selected.Red,
            Green = selected.Green,
            Blue = selected.Blue,
            CornerRadius = radius
        })));
    }
    private Task AddExposureAdjustmentAsync() => Task.Run(() => Workspace.AddExposureAdjustment());
    private Task AddLevelsAdjustmentAsync() => Task.Run(() => Workspace.AddLevelsAdjustment());
    private Task AddHueSaturationAdjustmentAsync() => Task.Run(() => Workspace.AddHueSaturationAdjustment());
    private Task AddCurvesAdjustmentAsync() => Task.Run(() => Workspace.AddCurvesAdjustment());
    private Task AddGradientMapAdjustmentAsync() => Task.Run(() => Workspace.AddGradientMapAdjustment());
    private Task AddGaussianBlurAdjustmentAsync() => Task.Run(() => Workspace.AddGaussianBlurAdjustment());
    private Task AddMotionBlurAdjustmentAsync() => Task.Run(() => Workspace.AddMotionBlurAdjustment());
    private Task AddNoiseAdjustmentAsync() => Task.Run(() => Workspace.AddNoiseAdjustment());
    private Task AddLensCorrectionAdjustmentAsync() => Task.Run(() => Workspace.AddLensCorrectionAdjustment());
    private Task AddGrainAdjustmentAsync() => Task.Run(() => Workspace.AddGrainAdjustment());
    private async Task ApplyAutoLevelsAsync(bool selectionOnly, LevelsAutoMode mode)
    {
        double[][] histogram = await Task.Run(() => Workspace.GetLevelsHistogram(selectionOnly));
        LevelsSettings settings = LevelsSettings.FromHistogram(histogram, mode);
        if (selectionOnly) Workspace.PreviewLevelsFilter(settings);
        else Workspace.ApplyActiveLevelsAdjustment(settings);
    }
    private Task ApplyExposureAdjustmentAsync()
    {
        var settings = new ExposureSettings((double)(adjustmentExposure.Value ?? 0),
            (double)(adjustmentOffset.Value ?? 0), (double)(adjustmentGamma.Value ?? 1));
        return Task.Run(() => Workspace.ApplyActiveExposureAdjustment(settings));
    }
    private Task ApplyLevelsAdjustmentAsync()
    {
        var range = new LevelRange((double)(levelsInputBlack.Value ?? 0),
            (double)(levelsInputWhite.Value ?? 255), (double)(levelsGamma.Value ?? 1),
            (double)(levelsOutputBlack.Value ?? 0), (double)(levelsOutputWhite.Value ?? 255));
        var current = selectedId is { } id ? Workspace.Session!.GetLevelsAdjustment(id) : new LevelsSettings();
        var settings = levelsChannel.SelectedIndex switch
        {
            1 => current with { Red = range },
            2 => current with { Green = range },
            3 => current with { Blue = range },
            _ => current with { Rgb = range }
        };
        return Task.Run(() => Workspace.ApplyActiveLevelsAdjustment(settings));
    }
    private Task ApplyHueSaturationAdjustmentAsync()
    {
        var settings = new HueSaturationSettings((double)(adjustmentHue.Value ?? 0),
            (double)(adjustmentSaturation.Value ?? 0), (double)(adjustmentLightness.Value ?? 0),
            adjustmentColorize.IsChecked == true);
        return Task.Run(() => Workspace.ApplyActiveHueSaturationAdjustment(settings));
    }
    private Task ApplyCurvesAdjustmentAsync()
    {
        var current = selectedId is { } id ? Workspace.Session!.GetCurvesAdjustment(id) : new CurvesSettings();
        var channel = new CurveChannelSettings((double)(curvesShadow.Value ?? 0),
            (double)(curvesMid.Value ?? 128), (double)(curvesHighlight.Value ?? 255));
        var settings = curvesChannel.SelectedIndex switch
        {
            1 => current with { Red = channel },
            2 => current with { Green = channel },
            3 => current with { Blue = channel },
            _ => current with { Shadow = channel.Shadow, Mid = channel.Mid, Highlight = channel.Highlight }
        };
        return Task.Run(() => Workspace.ApplyActiveCurvesAdjustment(settings));
    }
    private Task ApplyGradientMapAdjustmentAsync()
    {
        var settings = new GradientMapSettings(
            new GradientMapStop((int)(gradientShadowRed.Value ?? 0), (int)(gradientShadowGreen.Value ?? 0),
                (int)(gradientShadowBlue.Value ?? 0)),
            new GradientMapStop((int)(gradientHighlightRed.Value ?? 255), (int)(gradientHighlightGreen.Value ?? 255),
                (int)(gradientHighlightBlue.Value ?? 255)));
        return Task.Run(() => Workspace.ApplyActiveGradientMapAdjustment(settings));
    }
    private Task ApplyGaussianBlurAdjustmentAsync()
    {
        var settings = new GaussianBlurSettings((int)(gaussianBlurRadius.Value ?? 1));
        return Task.Run(() => Workspace.ApplyActiveGaussianBlurAdjustment(settings));
    }
    private Task PreviewSelectionGaussianBlurAsync()
    {
        var settings = new GaussianBlurSettings((int)(selectionGaussianBlurRadius.Value ?? 1));
        return Task.Run(() => Workspace.PreviewGaussianBlurFilter(settings));
    }
    private Task CommitSelectionGaussianBlurAsync() => Task.Run(Workspace.CommitFilterPreview);
    private Task CancelSelectionGaussianBlurAsync() => Task.Run(Workspace.CancelFilterPreview);
    private Task PreviewSelectionMotionBlurAsync()
    {
        var settings = new MotionBlurSettings((double)(selectionMotionBlurAngle.Value ?? 0),
            (int)(selectionMotionBlurDistance.Value ?? 1));
        return Task.Run(() => Workspace.PreviewMotionBlurFilter(settings));
    }
    private Task CommitSelectionMotionBlurAsync() => Task.Run(Workspace.CommitFilterPreview);
    private Task CancelSelectionMotionBlurAsync() => Task.Run(Workspace.CancelFilterPreview);
    private Task PreviewSelectionNoiseAsync()
    {
        var settings = new NoiseSettings((double)(selectionNoiseAmount.Value ?? 10),
            selectionNoiseGaussian.IsChecked == true, selectionNoiseMonochromatic.IsChecked == true);
        return Task.Run(() => Workspace.PreviewNoiseFilter(settings));
    }
    private Task CommitSelectionNoiseAsync() => Task.Run(Workspace.CommitFilterPreview);
    private Task CancelSelectionNoiseAsync() => Task.Run(Workspace.CancelFilterPreview);
    private Task PreviewSelectionLensCorrectionAsync()
    {
        var settings = new LensCorrectionSettings((double)(selectionLensCorrectionDistortion.Value ?? 0));
        return Task.Run(() => Workspace.PreviewLensCorrectionFilter(settings));
    }
    private Task CommitSelectionLensCorrectionAsync() => Task.Run(Workspace.CommitFilterPreview);
    private Task CancelSelectionLensCorrectionAsync() => Task.Run(Workspace.CancelFilterPreview);
    private Task PreviewSelectionExposureAsync()
    {
        var settings = new ExposureSettings((double)(selectionExposure.Value ?? 0),
            (double)(selectionExposureOffset.Value ?? 0), (double)(selectionExposureGamma.Value ?? 1));
        return Task.Run(() => Workspace.PreviewExposureFilter(settings));
    }
    private Task CommitSelectionExposureAsync() => Task.Run(Workspace.CommitFilterPreview);
    private Task CancelSelectionExposureAsync() => Task.Run(Workspace.CancelFilterPreview);
    private Task PreviewSelectionLevelsAsync()
    {
        var range = new LevelRange((double)(selectionLevelsInputBlack.Value ?? 0),
            (double)(selectionLevelsInputWhite.Value ?? 255), (double)(selectionLevelsGamma.Value ?? 1),
            (double)(selectionLevelsOutputBlack.Value ?? 0), (double)(selectionLevelsOutputWhite.Value ?? 255));
        var settings = selectionLevelsChannel.SelectedIndex switch
        {
            1 => new LevelsSettings(new(), range, new(), new()),
            2 => new LevelsSettings(new(), new(), range, new()),
            3 => new LevelsSettings(new(), new(), new(), range),
            _ => new LevelsSettings(range, new(), new(), new())
        };
        return Task.Run(() => Workspace.PreviewLevelsFilter(settings));
    }
    private Task CommitSelectionLevelsAsync() => Task.Run(Workspace.CommitFilterPreview);
    private Task CancelSelectionLevelsAsync() => Task.Run(Workspace.CancelFilterPreview);
    private Task PreviewSelectionHueSaturationAsync()
    {
        var settings = new HueSaturationSettings((double)(selectionHue.Value ?? 0),
            (double)(selectionSaturation.Value ?? 0), (double)(selectionLightness.Value ?? 0),
            selectionColorize.IsChecked == true);
        return Task.Run(() => Workspace.PreviewHueSaturationFilter(settings));
    }
    private Task CommitSelectionHueSaturationAsync() => Task.Run(Workspace.CommitFilterPreview);
    private Task CancelSelectionHueSaturationAsync() => Task.Run(Workspace.CancelFilterPreview);
    private Task PreviewSelectionCurvesAsync()
    {
        var channel = new CurveChannelSettings((double)(selectionCurvesShadow.Value ?? 0),
            (double)(selectionCurvesMid.Value ?? 128), (double)(selectionCurvesHighlight.Value ?? 255));
        var settings = selectionCurvesChannel.SelectedIndex switch
        {
            1 => new CurvesSettings(Red: channel),
            2 => new CurvesSettings(Green: channel),
            3 => new CurvesSettings(Blue: channel),
            _ => new CurvesSettings(channel.Shadow, channel.Mid, channel.Highlight)
        };
        return Task.Run(() => Workspace.PreviewCurvesFilter(settings));
    }
    private Task CommitSelectionCurvesAsync() => Task.Run(Workspace.CommitFilterPreview);
    private Task CancelSelectionCurvesAsync() => Task.Run(Workspace.CancelFilterPreview);
    private Task PreviewSelectionGradientMapAsync()
    {
        var settings = new GradientMapSettings(
            new GradientMapStop((int)(selectionGradientShadowRed.Value ?? 0),
                (int)(selectionGradientShadowGreen.Value ?? 0), (int)(selectionGradientShadowBlue.Value ?? 0)),
            new GradientMapStop((int)(selectionGradientHighlightRed.Value ?? 255),
                (int)(selectionGradientHighlightGreen.Value ?? 255), (int)(selectionGradientHighlightBlue.Value ?? 255)));
        return Task.Run(() => Workspace.PreviewGradientMapFilter(settings));
    }
    private Task CommitSelectionGradientMapAsync() => Task.Run(Workspace.CommitFilterPreview);
    private Task CancelSelectionGradientMapAsync() => Task.Run(Workspace.CancelFilterPreview);
    private Task PreviewSelectionGrainAsync()
    {
        var settings = new GrainSettings((double)(selectionGrainAmount.Value ?? 25),
            (double)(selectionGrainSize.Value ?? 1.5m), (double)(selectionGrainRoughness.Value ?? 50), 7);
        return Task.Run(() => Workspace.PreviewGrainFilter(settings));
    }
    private Task CommitSelectionGrainAsync() => Task.Run(Workspace.CommitFilterPreview);
    private Task CancelSelectionGrainAsync() => Task.Run(Workspace.CancelFilterPreview);
    private Task ApplyMotionBlurAdjustmentAsync()
    {
        var settings = new MotionBlurSettings((double)(motionBlurAngle.Value ?? 0),
            (int)(motionBlurDistance.Value ?? 1));
        return Task.Run(() => Workspace.ApplyActiveMotionBlurAdjustment(settings));
    }
    private Task ApplyNoiseAdjustmentAsync()
    {
        var settings = new NoiseSettings((double)(noiseAmount.Value ?? 10),
            noiseGaussian.IsChecked == true, noiseMonochromatic.IsChecked == true);
        return Task.Run(() => Workspace.ApplyActiveNoiseAdjustment(settings));
    }
    private Task ApplyLensCorrectionAdjustmentAsync()
    {
        var settings = new LensCorrectionSettings((double)(lensCorrectionDistortion.Value ?? 0));
        return Task.Run(() => Workspace.ApplyActiveLensCorrectionAdjustment(settings));
    }
    private Task ApplyGrainAdjustmentAsync()
    {
        var settings = new GrainSettings((double)(grainAmount.Value ?? 25),
            (double)(grainSize.Value ?? 1.5m), (double)(grainRoughness.Value ?? 50));
        return Task.Run(() => Workspace.ApplyActiveGrainAdjustment(settings));
    }
    private Task DuplicateLayerAsync()
    {
        Guid id = selectedId!.Value;
        return EditAsync(session => session.DuplicateLayer(id, session.Layers.Single(layer => layer.Id == id).Name));
    }

    private Task LayerViaCopyAsync() => Task.Run(() => Workspace.LayerViaCopy());

    private async Task CopyLayerToProjectAsync(EditorWorkspace source, EditorWorkspace target, int targetIndex, Guid layerId)
    {
        await Task.Run(() => source.CopyLayerTo(target, layerId));
        activeProjectIndex = targetIndex;
        selectedId = null;
        displayedSession = null;
    }

    private Task DeleteLayerAsync()
    {
        Guid id = selectedId!.Value;
        return EditAsync(session => session.DeleteLayer(id));
    }

    private Task MergeLayerDownAsync()
    {
        Guid[] ids = (layers.SelectedItems?.OfType<FlatLayerInfo>() ?? Enumerable.Empty<FlatLayerInfo>())
            .Select(layer => layer.Id).ToArray();
        return ids.Length > 1
            ? Task.Run(() => Workspace.MergeSelectedLayers(ids))
            : Task.Run(Workspace.MergeActiveLayerDown);
    }

    private Task GroupLayerAsync()
    {
        Guid[] ids = (layers.SelectedItems?.OfType<FlatLayerInfo>() ?? Enumerable.Empty<FlatLayerInfo>()).Select(layer => layer.Id).ToArray();
        if (ids.Length == 0 && selectedId is { } id) ids = [id];
        return EditAsync(session => session.GroupLayers(ids,
            session.Layers.Single(layer => layer.Id == ids[0]).Name + " Group"));
    }

    private Task UngroupLayerAsync() => EditAsync(session => session.UngroupLayer(selectedId!.Value));

    private Task BakeUngroupLayerAsync() => Task.Run(() => Workspace.BakeGroupTransform(selectedId!.Value));
    private Task BakeLayerTransformAsync() => Task.Run(() => Workspace.BakeLayerTransform(selectedId!.Value));

    private Task FlipLayerAsync(bool horizontal) => Task.Run(() => Workspace.FlipActiveLayer(horizontal));

    private Task ScaleGroupAsync(bool enlarge)
    {
        return Task.Run(() => Workspace.ScaleActiveLayer(enlarge));
    }

    private Task RotateGroupAsync(bool clockwise)
    {
        return Task.Run(() => Workspace.RotateActiveLayer90(clockwise));
    }

    private Task RotateLayerAsync(bool clockwise)
    {
        return Task.Run(() => Workspace.RotateActiveLayer(clockwise ? 15 : -15));
    }

    private Task RotateLayerCustomAsync()
    {
        double degrees = (double)(layerRotation.Value ?? 0);
        return Task.Run(() => Workspace.RotateActiveLayer(degrees));
    }

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
    private Task ApplyTextAsync()
    {
        Guid id = selectedId!.Value;
        ProjectSession session = Workspace.Session!;
        TextLayerMetadata current = session.TextLayers.Single(item => item.Id == id);
        string font = textFont.SelectedItem as string ?? current.FontPostScriptName;
        (double red, double green, double blue) = TextColor(textColor.SelectedIndex);
        TextLayerMetadata edited = current with
        {
            Content = textContent.Text ?? "",
            FontPostScriptName = font,
            FontSizePoints = (double)(textSize.Value ?? (decimal)current.FontSizePoints),
            Red = red,
            Green = green,
            Blue = blue,
            Alignment = textAlignment.SelectedItem as string ?? current.Alignment,
            LineSpacingPoints = (double)(textLineSpacing.Value ?? (decimal)current.LineSpacingPoints),
            TrackingPoints = (double)(textTracking.Value ?? (decimal)current.TrackingPoints),
            BoxWidth = current.Layout == "box"
                ? (double)(textBoxWidth.Value ?? (decimal)current.BoxWidth!.Value)
                : null
        };
        return Task.Run(() => Workspace.UpdateText(edited));
    }

    private Task ResolveTextFontAsync()
    {
        Guid id = selectedId!.Value;
        string font = textFont.SelectedItem as string
            ?? throw new InvalidOperationException("请先选择要使用的字体。");
        return Task.Run(() => Workspace.ResolveTextFont(id, font));
    }

    private static (double Red, double Green, double Blue) TextColor(int index) => index switch
    {
        1 => (1, 1, 1),
        2 => (0.1, 0.3, 0.9),
        3 => (1, 0.3, 0.1),
        _ => (0, 0, 0)
    };

    private static (double Red, double Green, double Blue) ShapeColor(int index) => TextColor(index);

    private static int TextColorIndex(TextLayerMetadata metadata)
    {
        (double red, double green, double blue) = (metadata.Red, metadata.Green, metadata.Blue);
        if (red == 1 && green == 1 && blue == 1) return 1;
        if (red == 0.1 && green == 0.3 && blue == 0.9) return 2;
        if (red == 1 && green == 0.3 && blue == 0.1) return 3;
        return 0;
    }
    private Task AppearanceAsync()
    {
        Guid id = selectedId!.Value;
        double opacityValue = (double)(layerOpacity.Value ?? 100) / 100;
        string mode = layerBlendMode.SelectedItem as string ?? "Normal";
        return EditAsync(session =>
        {
            session.SetLayerOpacity(id, opacityValue);
            if (!session.Layers.Single(layer => layer.Id == id).IsAdjustment)
                session.SetLayerBlendMode(id, mode);
        });
    }
    private Task InvertLayerAsync() => Task.Run(Workspace.InvertActiveLayer);
    private Task VisibilityAsync()
    {
        Guid id = selectedId!.Value;
        return EditAsync(s => s.SetLayerVisible(id, !s.Layers.Single(layer => layer.Id == id).IsVisible));
    }
    private Task AddMaskAsync() => Task.Run(Workspace.AddActiveLayerMask);
    private Task ToggleMaskAsync() => Task.Run(Workspace.ToggleActiveLayerMask);
    private Task InvertMaskAsync() => Task.Run(Workspace.InvertActiveLayerMask);
    private Task FillMaskAsync(bool reveal) => Task.Run(() => Workspace.FillActiveLayerMask(reveal));
    private Task BlurMaskAsync() => Task.Run(() => Workspace.BlurActiveLayerMask((int)(maskRadius.Value ?? 3)));
    private Task SetClippingMaskAsync(bool enabled) => Task.Run(() => Workspace.SetActiveLayerClipping(enabled));
    private Task ApplyMaskSelectionAsync(bool reveal) => Task.Run(() => Workspace.ApplySelectionToActiveLayerMask(reveal));
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

    private Task FeatherSelectionAsync()
    {
        Workspace.FeatherSelection((int)(selectionFeatherRadius.Value ?? 3));
        canvas.SetSelectionRect(Workspace.SelectionBounds);
        canvas.SetSelectionOutline(Workspace.SelectionOutline);
        moveSelection.IsEnabled = Workspace.HasSelection;
        return Task.CompletedTask;
    }

    private async Task CopySelectionAsync()
    {
        Workspace.CopySelection();
        clipboardProject = Workspace;
        await PublishSystemClipboardAsync();
    }

    private async Task CopyMergedSelectionAsync()
    {
        Workspace.CopyMergedSelection();
        clipboardProject = Workspace;
        await PublishSystemClipboardAsync();
    }

    private async Task CutSelectionAsync()
    {
        EditorWorkspace workspace = Workspace;
        await Task.Run(workspace.CutSelection);
        clipboardProject = workspace;
        await PublishSystemClipboardAsync();
    }

    private async Task PasteSelectionAsync()
    {
        EditorWorkspace? source = clipboardProject;
        if (source is not null && !ReferenceEquals(source, Workspace) && Workspace.CanPasteSelectionFrom(source))
        {
            await Task.Run(() => Workspace.PasteSelectionFrom(source));
            return;
        }
        if (source is not null)
        {
            await Task.Run(Workspace.PasteSelection);
            return;
        }
        if (Clipboard is not { } systemClipboard)
            throw new NotSupportedException("当前窗口没有可用的系统剪贴板。");
        using Bitmap? bitmap = await systemClipboard.TryGetBitmapAsync();
        if (bitmap is null)
            throw new InvalidOperationException("系统剪贴板没有可粘贴的图像。");
        TileRaster raster = RasterBitmap.ToRaster(bitmap);
        Point? position = canvas.LastDocumentPointer is { } pointer && Workspace.Session is { } session &&
            pointer.X >= 0 && pointer.Y >= 0 && pointer.X < session.Width && pointer.Y < session.Height
            ? pointer : null;
        await Task.Run(() => Workspace.PasteBitmapAsLayer(raster, position));
    }

    private async Task PublishSystemClipboardAsync()
    {
        if (Clipboard is not { } systemClipboard || Workspace.ClipboardRaster is not { } raster) return;
        using var bitmap = RasterBitmap.Create(raster);
        try { await systemClipboard.SetBitmapAsync(bitmap); }
        catch (Exception) { }
    }
    private Task CommitFloatingSelectionAsync() => Task.Run(Workspace.CommitFloatingSelection);
    private Task CancelFloatingSelectionAsync() => Task.Run(Workspace.CancelFloatingSelection);

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
    private Task MoveLayerToAsync(Guid layerId, int destinationIndex) =>
        Task.Run(() => Workspace.MoveLayerTo(layerId, destinationIndex));

    private static FlatLayerInfo? LayerFromVisual(Visual? visual) =>
        visual?.GetSelfAndVisualAncestors().OfType<ListBoxItem>()
            .Select(item => item.DataContext).OfType<FlatLayerInfo>().FirstOrDefault();

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!Workspace.IsDirty) return true;
        string? decision = await ChoiceAsync("未保存的修改", "是否保存当前工程的修改？",
            ("保存", "save"), ("不保存", "discard"), ("取消", "cancel"));
        if (decision == "save") { await SaveAsync(); return !Workspace.IsDirty; }
        return decision == "discard";
    }

    private async Task<bool> ConfirmDiscardAllAsync()
    {
        for (int index = 0; index < projects.Count; index++)
        {
            EditorWorkspace project = projects[index];
            if (!project.IsDirty) continue;
            activeProjectIndex = index; selectedId = null; displayedSession = null; Refresh();
            string? decision = await ChoiceAsync("未保存的修改", "是否保存当前工程的修改？",
                ("保存", "save"), ("不保存", "discard"), ("取消", "cancel"));
            if (decision == "cancel" || decision is null) return false;
            if (decision == "save")
            {
                await SaveAsync();
                if (project.IsDirty) return false;
            }
        }
        return true;
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
        var filter = new ComboBox { Name = "ResizeFilter", ItemsSource = new[] { "双线性", "Lanczos3" },
            SelectedIndex = 0, Width = 220, IsEnabled = scale };
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
                if (scale)
                {
                    ResizeFilter selectedFilter = filter.SelectedIndex == 1 ? ResizeFilter.Lanczos3 : ResizeFilter.Bilinear;
                    await Task.Run(() => Workspace.ResizeImage((int)w, (int)h, selectedFilter));
                }
                else await Task.Run(() => Workspace.ResizeCanvas((int)w, (int)h));
                resizing = false; dialog.Close();
            }
            catch (Exception exception) { error.Text = exception.Message; }
            finally { resizing = false; submit.IsEnabled = cancel.IsEnabled = true; }
        };
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 10, Children =
        {
            new TextBlock { Text = "宽度（像素）" }, width, new TextBlock { Text = "高度（像素）" }, height,
            new TextBlock { Text = "缩放算法" }, filter, error,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { submit, cancel } }
        } };
        await dialog.ShowDialog(this);
    }

    private Task SaveAsync() => Workspace.ProjectDirectory is null ? SaveAsAsync() : Task.Run(Workspace.Save);

    private async Task OpenAsync()
    {
        var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "选择 .comp 工程文件夹", AllowMultiple = false });
        if (selected.Count == 0) return;
        string path = LocalPath(selected[0]);
        if (Workspace.Session is null)
            await Task.Run(() => Workspace.Open(path));
        else
        {
            var next = new EditorWorkspace();
            await Task.Run(() => next.Open(path));
            AddProjectTab(next);
        }
    }

    private async Task ImportAsync()
    {
        var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入图片", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG / JPEG") { Patterns = ["*.png", "*.jpg", "*.jpeg"] }]
        });
        if (selected.Count == 0) return;
        string source = LocalPath(selected[0]);
        string? target = await NewProjectPathAsync(Path.GetFileNameWithoutExtension(source));
        if (target is null) return;
        if (Workspace.Session is null)
            await Task.Run(() => Workspace.Import(source, target));
        else
        {
            var next = new EditorWorkspace();
            await Task.Run(() => next.Import(source, target));
            AddProjectTab(next);
        }
    }

    private async Task ImportFontAsync()
    {
        var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入字体", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("字体") { Patterns = ["*.otf", "*.ttf", "*.ttc"] }]
        });
        if (selected.Count == 0) return;
        await ImportFontFileAsync(LocalPath(selected[0]));
    }

    internal async Task<ImportedFont?> ImportFontFileAsync(string sourcePath)
    {
        IReadOnlyList<FontFace> faces = await Task.Run(() => FontLibrary.EnumerateFaces(sourcePath));
        int? faceIndex = faces.Count == 1 ? faces[0].FaceIndex : await SelectFontFaceAsync(faces);
        if (faceIndex is null) return null;
        ImportedFont imported = await Task.Run(() => FontLibrary.Import(sourcePath, faceIndex.Value));
        textFont.ItemsSource = TextLayerWorkflow.AvailableFonts;
        textFont.SelectedItem = imported.SelectionName;
        status.Text = $"已导入字体：{imported.SelectionName}";
        return imported;
    }

    private FontLibrary FontLibrary => fontLibrary ??= new FontLibrary(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Compositor", "fonts"));

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

    private async Task<int?> SelectFontFaceAsync(IReadOnlyList<FontFace> faces)
    {
        var dialog = Dialog("选择字体面");
        var face = new ComboBox
        {
            Name = "FontFace", Width = 360, SelectedIndex = 0,
            ItemsSource = faces.Select(item => $"{item.SelectionName}（face-index {item.FaceIndex}，{item.FamilyName}）").ToArray()
        };
        var import = new Button { Name = "ImportFontFace", Content = "导入" };
        var cancel = new Button { Name = "CancelFontFace", Content = "取消" };
        cancel.Click += (_, _) => dialog.Close();
        import.Click += (_, _) =>
        {
            if (face.SelectedIndex >= 0) dialog.Close(faces[face.SelectedIndex].FaceIndex);
        };
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 14, Children =
        {
            new TextBlock { Text = "检测到多个字体面，请选择要导入的 face-index。取消将不写入字体库。", TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
            face,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { import, cancel } }
        } };
        return await dialog.ShowDialog<int?>(this);
    }

    private Window Dialog(string title) => new()
    {
        Title = title, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily, FontSize = FontSize
    };

    private static string LocalPath(IStorageItem item) => item.TryGetLocalPath()
        ?? throw new NotSupportedException("请选择本地文件或文件夹。");
}
