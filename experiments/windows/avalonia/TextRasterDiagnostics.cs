using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

// Opt-in, one-variable-at-a-time Windows glyph-raster diagnostic. No fallback
// font or alternate production renderer is selected by these observations.
internal static class TextRasterDiagnostics
{
    internal static void Run(FontFamily family, string fontUri, string output)
    {
        var results = new List<object>();
        foreach (string variant in new[] { "baseline", "antialias", "alias", "no-flip", "no-rotation", "opaque", "plain" })
        {
            var root = new Canvas { Width = 420, Height = 160 };
            var block = new TextBlock
            {
                Text = "中文 Ag", FontFamily = family, FontSize = 48,
                Foreground = new SolidColorBrush(Color.FromArgb(204, 38, 102, 179)),
                Opacity = variant == "opaque" ? 1 : 0.65,
                RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute)
            };
            double sx = variant is "no-flip" or "plain" ? 1 : -1;
            double angle = variant is "no-rotation" or "plain" ? 0 : 13 * Math.PI / 180;
            block.RenderTransform = new MatrixTransform(Matrix.CreateScale(sx, 1)
                * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(sx < 0 ? 320 : 30, 40));
            if (variant == "antialias") RenderOptions.SetTextRenderingMode(root, TextRenderingMode.Antialias);
            if (variant == "alias") RenderOptions.SetTextRenderingMode(root, TextRenderingMode.Alias);
            root.Children.Add(block);
            root.Measure(new Size(420, 160)); root.Arrange(new Rect(0, 0, 420, 160));
            string path = Path.Combine(output, "diagnostic-avalonia-" + variant + ".png");
            using var image = new RenderTargetBitmap(new PixelSize(420, 160), new Vector(96, 96));
            image.Render(root); image.Save(path);
            results.Add(new { route = "Avalonia TextBlock", variant, alphaMass = AlphaMass(path) });
        }

        using var stream = AssetLoader.Open(new Uri(fontUri));
        using var typeface = SKTypeface.FromStream(stream);
        foreach (string variant in new[] { "baseline", "no-hinting", "no-subpixel", "alias", "no-flip", "no-rotation", "outline" })
        {
            using var bitmap = new SKBitmap(420, 160, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            using var font = new SKFont(typeface, 48)
            {
                Edging = variant == "alias" ? SKFontEdging.Alias : SKFontEdging.Antialias,
                Hinting = variant == "no-hinting" ? SKFontHinting.None : SKFontHinting.Full,
                Subpixel = variant != "no-subpixel"
            };
            using var paint = new SKPaint { Color = new SKColor(38, 102, 179, 133), IsAntialias = true };
            canvas.Translate(variant == "no-flip" ? 30 : 320, 100);
            if (variant != "no-rotation") canvas.RotateDegrees(13);
            if (variant != "no-flip") canvas.Scale(-1, 1);
            ushort[] glyphs = typeface.GetGlyphs("中文Ag");
            var coverage = new List<object>();
            for (int i = 0; i < glyphs.Length; i++)
            {
                using var path = font.GetGlyphPath(glyphs[i]);
                if (path is null || path.IsEmpty) throw new InvalidDataException("Missing ordinary glyph outline");
                coverage.Add(new { character = "中文Ag"[i].ToString(), glyph = glyphs[i], outlineBounds = path.Bounds.ToString() });
                if (variant == "outline")
                {
                    canvas.Save(); canvas.Translate(i * 60, 0); canvas.DrawPath(path, paint); canvas.Restore();
                }
                else canvas.DrawText("中文Ag"[i].ToString(), i * 60, 0, font, paint);
            }
            string file = Path.Combine(output, "diagnostic-skia-" + variant + ".png");
            using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
            using (var target = File.Create(file)) data.SaveTo(target);
            results.Add(new { route = "direct Skia", variant, alphaMass = AlphaMass(file), glyphs = coverage });
        }
        File.WriteAllText(Path.Combine(output, "glyph-diagnostics.json"), JsonSerializer.Serialize(new
        {
            status = "observations only; not a verified fix", windowsExecuted = OperatingSystem.IsWindows(),
            input = "ordinary Chinese and Latin glyphs without emoji", results
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static double AlphaMass(string path)
    {
        byte[] pixels = Program.Pixels(path);
        long alpha = 0;
        for (int i = 3; i < pixels.Length; i += 4) alpha += pixels[i];
        return alpha / 255.0;
    }
}
