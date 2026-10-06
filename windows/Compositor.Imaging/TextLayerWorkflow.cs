using System.Globalization;
using System.Runtime.InteropServices;
using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

public sealed record TextLayerStatus(TextLayerMetadata Metadata, bool FontAvailable, string Message);

public sealed record TextLayerRenderResult(TextLayerStatus Status, TileRaster Raster, bool UsedCache);

public sealed record TextHitTestResult(int CharacterIndex, int LineIndex, bool IsInside);

public static class TextLayerWorkflow
{
    private static readonly object FontGate = new();
    private static readonly Dictionary<string, SKTypeface> ImportedTypefaces = new(StringComparer.Ordinal);

    public static IReadOnlyList<string> AvailableFonts
    {
        get
        {
            lock (FontGate)
            {
                return SKFontManager.Default.GetFontFamilies()
                    .Concat(ImportedTypefaces.Values.Select(typeface => typeface.FamilyName))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(family => family, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }

    public static string RegisterImportedTypeface(string path, int faceIndex = 0)
    {
        SKTypeface typeface = SKTypeface.FromFile(path, faceIndex)
            ?? throw new InvalidDataException("The font face could not be loaded.");
        lock (FontGate) ImportedTypefaces.TryAdd(Normalize(typeface.FamilyName), typeface);
        return typeface.FamilyName;
    }

    public static IReadOnlyList<TextLayerStatus> Inspect(ProjectSession session) =>
        session.TextLayers.Select(metadata => Status(metadata)).ToArray();

    public static void Update(ProjectSession session, TextLayerMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(metadata);
        TextLayerStatus status = Status(metadata);
        if (!status.FontAvailable)
            throw new NotSupportedException(status.Message);
        TileRaster cache = session.GetLayerRaster(metadata.Id);
        session.UpdateTextLayer(metadata, RenderRaster(metadata, cache, session.Resolution));
    }

    public static TileRaster RenderText(TextLayerMetadata metadata, int width, int height, double resolution)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        return RenderRaster(metadata, new TileRaster(width, height), resolution);
    }

    public static TextHitTestResult HitTest(TextLayerMetadata metadata, double resolution, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        SKTypeface typeface = FindTypeface(metadata.FontPostScriptName)
            ?? throw new NotSupportedException("The original text font became unavailable while hit testing.");
        if (!double.IsFinite(resolution) || resolution <= 0) throw new InvalidDataException("Invalid document resolution.");
        float scale = (float)(resolution / 72);
        using var paint = new SKPaint
        {
            Typeface = typeface,
            TextSize = (float)(metadata.FontSizePoints * scale),
            IsAntialias = true,
            SubpixelText = true,
            LcdRenderText = false
        };
        float tracking = (float)(metadata.TrackingPoints * scale);
        float lineSpacing = (float)(metadata.LineSpacingPoints * scale);
        float lineHeight = Math.Max(1, paint.FontMetrics.Descent - paint.FontMetrics.Ascent + lineSpacing);
        float layoutWidth = metadata.Layout == "box" ? (float)metadata.BoxWidth!.Value : float.PositiveInfinity;
        string[] lines = LayoutLines(metadata.Content, paint, tracking, layoutWidth);
        int lineIndex = Math.Clamp((int)MathF.Floor(Math.Max(0, y) / lineHeight), 0, lines.Length - 1);
        string line = lines[lineIndex];
        float lineWidth = Measure(line, paint, tracking);
        float start = metadata.Alignment switch
        {
            "center" when float.IsFinite(layoutWidth) => (layoutWidth - lineWidth) / 2,
            "right" when float.IsFinite(layoutWidth) => layoutWidth - lineWidth,
            _ => 0
        };
        float localX = x - start;
        int localIndex = 0;
        float cursor = 0;
        foreach (string element in Elements(line))
        {
            float advance = paint.MeasureText(element) + tracking;
            if (localX < cursor + advance / 2) break;
            cursor += advance;
            localIndex += element.Length;
        }
        int contentIndex = 0;
        for (int index = 0; index < lineIndex; index++)
        {
            contentIndex += lines[index].Length;
            if (contentIndex < metadata.Content.Length && metadata.Content[contentIndex] == '\n') contentIndex++;
        }
        contentIndex = Math.Clamp(contentIndex + localIndex, 0, metadata.Content.Length);
        bool inside = y >= 0 && y < lines.Length * lineHeight && x >= start && x <= start + lineWidth;
        return new TextHitTestResult(contentIndex, lineIndex, inside);
    }

    public static TextLayerRenderResult Render(ProjectSession session, Guid layerId)
    {
        TextLayerMetadata metadata = session.TextLayers.SingleOrDefault(layer => layer.Id == layerId)
            ?? throw new ArgumentException("The selected layer is not a text layer.", nameof(layerId));
        TileRaster cache = LoadCache(session, metadata);
        TextLayerStatus status = Status(metadata);
        if (!status.FontAvailable) return new TextLayerRenderResult(status, cache, true);
        return new TextLayerRenderResult(status, RenderRaster(metadata, cache, session.Resolution), false);
    }

    internal static TileRaster RenderRaster(TextLayerMetadata metadata, TileRaster cache, double resolution)
    {
        SKTypeface typeface = FindTypeface(metadata.FontPostScriptName)
            ?? throw new NotSupportedException("The original text font became unavailable while rendering.");
        if (!double.IsFinite(resolution) || resolution <= 0) throw new InvalidDataException("Invalid document resolution.");
        using var bitmap = new SKBitmap(new SKImageInfo(cache.Width, cache.Height, SKColorType.Rgba8888,
            SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        float scale = (float)(resolution / 72);
        using var paint = new SKPaint
        {
            Typeface = typeface,
            TextSize = (float)(metadata.FontSizePoints * scale),
            Color = new SKColor(ToByte(metadata.Red), ToByte(metadata.Green), ToByte(metadata.Blue), ToByte(metadata.Alpha)),
            IsAntialias = true,
            SubpixelText = true,
            LcdRenderText = false
        };
        var metrics = paint.FontMetrics;
        float tracking = (float)(metadata.TrackingPoints * scale);
        float lineSpacing = (float)(metadata.LineSpacingPoints * scale);
        float lineHeight = Math.Max(1, metrics.Descent - metrics.Ascent + lineSpacing);
        float layoutWidth = metadata.Layout == "box" ? (float)metadata.BoxWidth!.Value : cache.Width;
        string[] lines = LayoutLines(metadata.Content, paint, tracking, metadata.Layout == "box" ? layoutWidth : float.PositiveInfinity);
        float baseline = -metrics.Ascent;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            float lineWidth = Measure(line, paint, tracking);
            float x = metadata.Alignment switch
            {
                "center" => (layoutWidth - lineWidth) / 2,
                "right" => layoutWidth - lineWidth,
                _ => 0
            };
            DrawTracked(canvas, line, x, baseline + index * lineHeight, paint, tracking);
        }
        return FromBitmap(bitmap, cache.Width, cache.Height);
    }

    private static TextLayerStatus Status(TextLayerMetadata metadata)
    {
        bool available = FindTypeface(metadata.FontPostScriptName) is not null;
        string message = available
            ? "The original font is available; Windows can redraw this text from its v8 metadata."
            : $"The original font '{metadata.FontPostScriptName}' is unavailable; choose a font. The cached pixels and text metadata are preserved.";
        return new TextLayerStatus(metadata, available, message);
    }

    private static TileRaster LoadCache(ProjectSession session, TextLayerMetadata metadata)
    {
        if (session.TryGetLoadedLayerRaster(metadata.Id, out TileRaster loaded)) return loaded;
        if (!session.HasBeenSaved || metadata.ImageFile.Length == 0)
            throw new InvalidOperationException("The text layer cache has not been saved yet.");
        string path = Path.Combine(session.SourceDirectory, "images", metadata.ImageFile);
        if (session.CanEdit) ProjectStore.CheckAssetHash(session, metadata.ImageFile, path);
        TileRaster result = ImageCodec.Load(path);
        if (session.CanEdit) ProjectStore.CheckAssetHash(session, metadata.ImageFile, path);
        return result;
    }

    private static SKTypeface? FindTypeface(string postScriptName)
    {
        string requested = Normalize(postScriptName);
        lock (FontGate)
            if (ImportedTypefaces.TryGetValue(requested, out SKTypeface? imported)) return imported;
        foreach (string family in SKFontManager.Default.GetFontFamilies())
        {
            if (Normalize(family) is not { } normalized || normalized != requested) continue;
            return SKTypeface.FromFamilyName(family);
        }
        return null;
    }

    private static string Normalize(string value)
    {
        string normalized = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        foreach (string suffix in new[] { "regular", "normal", "roman", "book", "medium", "semibold", "bold", "italic" })
            if (normalized.EndsWith(suffix, StringComparison.Ordinal) && normalized.Length > suffix.Length)
                return normalized[..^suffix.Length];
        return normalized;
    }

    private static string[] LayoutLines(string content, SKPaint paint, float tracking, float maximumWidth)
    {
        var output = new List<string>();
        foreach (string source in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string current = "";
            foreach (string element in Elements(source))
            {
                string candidate = current + element;
                if (current.Length > 0 && Measure(candidate, paint, tracking) > maximumWidth)
                {
                    output.Add(current);
                    current = element;
                }
                else current = candidate;
            }
            output.Add(current);
        }
        return output.ToArray();
    }

    private static IEnumerable<string> Elements(string value)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext()) yield return (string)enumerator.Current!;
    }

    private static float Measure(string value, SKPaint paint, float tracking)
    {
        string[] elements = Elements(value).ToArray();
        float width = 0;
        for (int index = 0; index < elements.Length; index++)
            width += paint.MeasureText(elements[index]) + (index + 1 == elements.Length ? 0 : tracking);
        return width;
    }

    private static void DrawTracked(SKCanvas canvas, string value, float x, float baseline, SKPaint paint, float tracking)
    {
        foreach (string element in Elements(value))
        {
            canvas.DrawText(element, x, baseline, paint);
            x += paint.MeasureText(element) + tracking;
        }
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

    private static TileRaster FromBitmap(SKBitmap bitmap, int width, int height)
    {
        var result = new TileRaster(width, height);
        for (int row = 0; row * TileRaster.TileSize < height; row++)
        for (int column = 0; column * TileRaster.TileSize < width; column++)
        {
            var size = result.TileDimensions(column, row);
            byte[] tile = new byte[size.Width * size.Height * 4];
            for (int y = 0; y < size.Height; y++)
                Marshal.Copy(bitmap.GetPixels() + (row * TileRaster.TileSize + y) * bitmap.RowBytes + column * TileRaster.TileSize * 4,
                    tile, y * size.Width * 4, size.Width * 4);
            result = result.ReplaceTile(column, row, tile);
        }
        return result;
    }
}
