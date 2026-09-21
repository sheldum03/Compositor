using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;

internal static class TextProbe
{
    internal const string FontUri = "avares://Compositor.AvaloniaProbe/Fonts/SourceHanSansSC-Regular.otf";
    public static void Run(string fixtures, string output, bool diagnostics = false)
    {
        Verify(fixtures);
        using var fontStream = AssetLoader.Open(new Uri(FontUri));
        string fontHash = Convert.ToHexString(SHA256.HashData(fontStream)).ToLowerInvariant();
        Check(fontHash == "f1d8611151880c6c336aabeac4640ef434fa13cbfbf1ffe82d0a71b2a5637256", "Bundled font hash");
        var family = new FontFamily(FontUri + "#Source Han Sans SC");
        var face = new Typeface(family).GlyphTypeface;
        Check(face.FamilyName == "Source Han Sans SC" && face.GetGlyph('中') != 0, "Resolve embedded Chinese typeface");
        if (diagnostics) TextRasterDiagnostics.Run(family, FontUri, output);
        CheckSpacing(family);
        var results = new List<object>();
        foreach (string package in Directory.GetDirectories(fixtures, "F11-*.comp").Order())
        {
            string name = Path.GetFileNameWithoutExtension(package);
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(package, "manifest.json")))!;
            var layer = manifest["layers"]![0]!;
            var style = layer["text"]!;
            double ppp = manifest["resolution"]!.GetValue<double>() / 72;
            double? width = style["layout"]!["box"]?["width"]?.GetValue<double>();
            var presenter = new SpacedTextPresenter(width, style["lineSpacingPoints"]!.GetValue<double>() * ppp);
            byte Channel(string key) => (byte)Math.Round(style[key]!.GetValue<double>() * 255);
            var box = Editor(presenter, family, style["content"]!.GetValue<string>(),
                style["fontSizePoints"]!.GetValue<double>() * ppp,
                style["trackingPoints"]!.GetValue<double>() * ppp,
                Enum.Parse<TextAlignment>(style["alignment"]!.GetValue<string>(), true),
                new SolidColorBrush(Color.FromArgb(Channel("alpha"), Channel("red"), Channel("green"), Channel("blue"))));
            box.Measure(Size.Infinity);
            box.Arrange(new Rect(box.DesiredSize));
            TextLayout shared = presenter.TextLayout;
            double naturalWidth = shared.MaxWidth, naturalHeight = shared.Height;
            box.Width = naturalWidth; box.Height = naturalHeight;
            var t = layer["transform"]!;
            double targetWidth = t["size"]![0]!.GetValue<double>(), targetHeight = t["size"]![1]!.GetValue<double>();
            double sx = targetWidth / naturalWidth * (t["flipX"]!.GetValue<bool>() ? -1 : 1);
            double sy = targetHeight / naturalHeight * (t["flipY"]!.GetValue<bool>() ? -1 : 1);
            var map = Matrix.CreateTranslation(-naturalWidth / 2, -naturalHeight / 2)
                * Matrix.CreateScale(sx, sy) * Matrix.CreateRotation(t["rotation"]!.GetValue<double>() * Math.PI / 180)
                * Matrix.CreateTranslation(t["origin"]![0]!.GetValue<double>() + targetWidth / 2,
                    t["origin"]![1]!.GetValue<double>() + targetHeight / 2);
            box.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
            box.RenderTransform = new MatrixTransform(map);
            double opacity = layer["opacity"]!.GetValue<double>();
            box.Opacity = opacity;
            int canvasWidth = manifest["width"]!.GetValue<int>(), canvasHeight = manifest["height"]!.GetValue<int>();
            var root = new Canvas { Width = canvasWidth, Height = canvasHeight };
            root.Children.Add(box);
            Arrange(root);
            shared = presenter.TextLayout;
            int lineCount = shared.TextLines.Count;
            string[] resolvedFonts = shared.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>()
                .Select(run => run.GlyphRun.GlyphTypeface.FamilyName).Distinct().Order().ToArray();
            string preview = Path.Combine(output, name + "-preview.png"), export = Path.Combine(output, name + "-export.png");
            Save(root, preview);
            var exported = new LayoutControl(shared);
            var exportPlacement = new Decorator
            {
                Child = exported,
                ClipToBounds = box.ClipToBounds,
                Width = naturalWidth, Height = naturalHeight, Opacity = opacity,
                RenderTransformOrigin = box.RenderTransformOrigin, RenderTransform = new MatrixTransform(map)
            };
            var exportRoot = new Canvas { Width = canvasWidth, Height = canvasHeight };
            exportRoot.Children.Add(exportPlacement);
            Arrange(exportRoot);
            Save(exportRoot, export);
            Check(ReferenceEquals(shared, presenter.TextLayout), "Export must use the editor's existing layout object");
            var difference = Program.Compare(preview, export);
            Check(difference.DifferentPixels == 0 && exported.DrawCalls == 1,
                $"{name}: editor/export pixel equality: {JsonSerializer.Serialize(difference)}, box={box.Bounds}, presenter={presenter.Bounds}, natural={naturalWidth},{naturalHeight}, layout={shared.MaxWidth},{shared.Height}");
            Check(Program.Pixels(export).Any(b => b != 0), name + ": nonempty output");
            var hits = CheckHits(presenter, map);
            var request = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
            box.RaiseEvent(request);
            var client = request.Client ?? throw new InvalidDataException("TextBox did not provide its IME client");
            Check(client.SupportsPreedit && client.SupportsSurroundingText && ReferenceEquals(client.TextViewVisual, presenter), "Real TextBox input client");
            var ime = CheckInput(box, presenter, client, root, map, output, name);
            results.Add(new
            {
                fixture = name, fontSizePixels = box.FontSize, letterSpacingPixels = box.LetterSpacing,
                lineSpacingPixels = style["lineSpacingPoints"]!.GetValue<double>() * ppp,
                naturalWidth, naturalHeight, lines = lineCount, resolvedFonts, scaleX = sx, scaleY = sy,
                previewExport = difference, transformedHits = hits, syntheticInput = ime,
                macReference = Program.Compare(export, Path.Combine(fixtures, name + "-mac.png"), Path.Combine(output, name + "-diff.png"))
            });
        }
        Check(results.Count == 12, "Expected twelve fixed F11 styles");
        Verify(fixtures);
        string json = JsonSerializer.Serialize(new
        {
            status = "local preparation only; synthetic input is not Windows IME acceptance",
            platform = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription, avalonia = typeof(Application).Assembly.GetName().Version?.ToString(),
            windowsExecuted = OperatingSystem.IsWindows(), nativeImeExecuted = false,
            font = face.FamilyName, fontSha256 = fontHash, corpusHashesVerified = 47,
            spacingChecks = "positive/negative additive spacing, tracking and layout invalidation passed",
            placement = "rerasterized layout fitted to each existing fixture rectangle; no cache or scale-policy change",
            results
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "text-report.json"), json + "\n");
        Console.WriteLine(json);
        TextInkChecks.Verify(fixtures, output, saveReport: true);
    }

    internal static TextBox Editor(SpacedTextPresenter presenter, FontFamily family, string text,
        double size, double tracking, TextAlignment alignment, IBrush foreground)
    {
        var box = new TextBox
        {
            Text = text, FontFamily = family, FontSize = size, LetterSpacing = tracking,
            TextAlignment = alignment, Foreground = foreground, AcceptsReturn = true,
            Padding = new Thickness(0), Background = null, BorderThickness = new Thickness(0),
            Template = new FuncControlTemplate<TextBox>((owner, scope) =>
            {
                scope.Register("PART_TextPresenter", presenter);
                presenter.Bind(TextPresenter.TextProperty, new Binding(nameof(TextBox.Text)) { Source = owner, Mode = BindingMode.TwoWay });
                presenter.Bind(TextPresenter.CaretIndexProperty, new Binding(nameof(TextBox.CaretIndex)) { Source = owner });
                presenter.Bind(TextPresenter.SelectionStartProperty, new Binding(nameof(TextBox.SelectionStart)) { Source = owner });
                presenter.Bind(TextPresenter.SelectionEndProperty, new Binding(nameof(TextBox.SelectionEnd)) { Source = owner });
                presenter.Bind(TextPresenter.TextAlignmentProperty, new Binding(nameof(TextBox.TextAlignment)) { Source = owner });
                presenter.Bind(TextPresenter.LetterSpacingProperty, new Binding(nameof(TextBox.LetterSpacing)) { Source = owner });
                return presenter;
            })
        };
        box.ApplyTemplate();
        return box;
    }

    private static object CheckHits(TextPresenter presenter, Matrix map)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(presenter.Text!).Append(presenter.Text!.Length).ToHashSet();
        int samples = 0;
        double maximumInverseError = 0;
        var inverse = map.Invert();
        double top = 0;
        foreach (var line in presenter.TextLayout.TextLines)
        {
            for (double x = 0.37; x < presenter.TextLayout.MaxWidth; x += 7.13)
            {
                var local = new Point(x, top + line.Height / 2);
                var document = local.Transform(map);
                var restored = document.Transform(inverse);
                maximumInverseError = Math.Max(maximumInverseError, Math.Max(Math.Abs(local.X - restored.X), Math.Abs(local.Y - restored.Y)));
                Check(Math.Abs(local.X - restored.X) < 0.001 && Math.Abs(local.Y - restored.Y) < 0.001,
                    $"Transform inverse: local={local}, restored={restored}, matrix={map}");
                int expected = presenter.TextLayout.HitTestPoint(local).TextPosition;
                presenter.MoveCaretToPoint(restored);
                Check(presenter.CaretIndex == expected, "Transformed click must resolve to layout caret");
                Check(boundaries.Contains(presenter.CaretIndex), "Caret must not split UTF-16 surrogate or combining cluster");
                samples++;
            }
            top += line.Height;
        }
        return new { samples, maximumInverseError, inverseTolerancePixels = 0.001,
            inverseHasPerspective = inverse.ContainsPerspective(), inverseM33 = inverse.M33, clusterBoundaryViolations = 0 };
    }

    private static void CheckSpacing(FontFamily family)
    {
        SpacedTextPresenter Sample(double spacing, double tracking = 0) => new(360, spacing)
        { Text = "AA\nBB", FontFamily = family, FontSize = 18, Foreground = Brushes.Black, LetterSpacing = tracking };
        var normal = Sample(0); var positive = Sample(3); var negative = Sample(-3); var tracked = Sample(0, 1.25);
        double y = normal.TextLayout.HitTestTextPosition(3).Y;
        Check(Math.Abs(positive.TextLayout.HitTestTextPosition(3).Y - y - 3) < 1e-8, "Add positive line spacing");
        Check(Math.Abs(negative.TextLayout.HitTestTextPosition(3).Y - y + 3) < 1e-8, "Add negative line spacing");
        Check(Math.Abs(tracked.TextLayout.HitTestTextPosition(1).X - normal.TextLayout.HitTestTextPosition(1).X - 1.25) < 1e-8,
            "Tracking changes glyph advance");
        var old = tracked.TextLayout;
        tracked.LetterSpacing = 2;
        Check(!ReferenceEquals(old, tracked.TextLayout), "Changing tracking invalidates editor layout");
        foreach (var item in new[] { normal, positive, negative, tracked }) item.TextLayout.Dispose();
    }

    private static object CheckInput(TextBox box, TextPresenter presenter, TextInputMethodClient client,
        Control root, Matrix map, string output, string name)
    {
        string original = box.Text!;
        box.CaretIndex = 2; box.SelectionStart = 2; box.SelectionEnd = 2;
        var before = presenter.TextLayout;
        client.SetPreeditText("输入法", 2);
        Check(box.Text == original && presenter.PreeditText == "输入法", "Preedit must stay out of committed text");
        Check(!ReferenceEquals(before, presenter.TextLayout), "Preedit invalidates shared layout");
        Arrange(root);
        string preeditImage = Path.Combine(output, name + "-preedit.png");
        Save(root, preeditImage);
        Check(Program.Compare(preeditImage, Path.Combine(output, name + "-preview.png")).DifferentPixels > 0, "Preedit must render");
        var cursor = client.CursorRectangle;
        Check(double.IsFinite(cursor.X) && double.IsFinite(cursor.Y) && cursor.Height > 0, "IME cursor rectangle");
        var expectedCaret = presenter.TextLayout.HitTestTextPosition(4);
        Check(Math.Abs(cursor.X - expectedCaret.X) < 1e-8 && Math.Abs(cursor.Y - expectedCaret.Y) < 1e-8,
            "Input client caret must use preedit cursor position in shared layout");
        var viewToRoot = presenter.TransformToVisual(root) ?? throw new InvalidDataException("No visual transform to canvas");
        foreach (var corner in new[] { cursor.TopLeft, cursor.TopRight, cursor.BottomLeft, cursor.BottomRight })
        {
            var actual = corner.Transform(viewToRoot); var expected = corner.Transform(map);
            Check(Math.Abs(actual.X - expected.X) < 0.001 && Math.Abs(actual.Y - expected.Y) < 0.001,
                "Input cursor visual-to-canvas transform must match layer placement");
        }
        client.SetPreeditText(null);
        Check(box.Text == original && string.IsNullOrEmpty(presenter.PreeditText), "Preedit cancellation");
        Arrange(root);
        string cancelled = Path.Combine(output, name + "-cancelled.png");
        Save(root, cancelled);
        Check(Program.Compare(cancelled, Path.Combine(output, name + "-preview.png")).DifferentPixels == 0,
            "Cancelled preedit must restore original pixels");
        client.SetPreeditText("中文", 2);
        client.SetPreeditText(null);
        box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "中文" });
        Check(box.Text == original.Insert(2, "中文") && presenter.Text == box.Text, "Routed commit changes TextBox and shared presenter");
        box.Undo(); Check(box.Text == original, "Committed input undo");
        box.Redo(); Check(box.Text == original.Insert(2, "中文"), "Committed input redo");
        // Exercise selection replacement through the public routed input path.
        box.SelectionStart = 0; box.SelectionEnd = 2;
        box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "替换" });
        Check(box.Text == "替换" + original.Insert(2, "中文")[2..], "Routed selection replacement");
        return new { preeditCancelCommitUndoRedoSelection = "passed", cancelledPixelsExact = true,
            cursor = new { cursor.X, cursor.Y, cursor.Width, cursor.Height } };
    }

    private static void Arrange(Control control)
    {
        control.Measure(new Size(control.Width, control.Height));
        control.Arrange(new Rect(0, 0, control.Width, control.Height));
    }
    internal static void Save(Control control, string path)
    {
        // Transparent, transformed text needs grayscale coverage, not LCD subpixel masks.
        RenderOptions.SetTextRenderingMode(control, TextRenderingMode.Antialias);
        using var image = new RenderTargetBitmap(new PixelSize((int)control.Width, (int)control.Height), new Vector(96, 96));
        image.Render(control); image.Save(path);
    }
    private static void Verify(string root)
    {
        var files = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "checksums.json")))!.AsArray();
        Check(files.Count == 47, "Fixed extended corpus count");
        foreach (var file in files)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, file!["path"]!.GetValue<string>()));
            Check(bytes.Length == file["bytes"]!.GetValue<int>() && Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                == file["sha256"]!.GetValue<string>(), "Fixed corpus hash");
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    internal sealed class LayoutControl(TextLayout layout) : Control
    {
        public int DrawCalls { get; private set; }
        public override void Render(DrawingContext context)
        {
            layout.Draw(context, new Point());
            DrawCalls++;
        }
    }
}
