using System.Globalization;
using System.Runtime.InteropServices;
using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

public sealed record TextLayerStatus(TextLayerMetadata Metadata, bool FontAvailable, string Message);

public sealed record TextLayerRenderResult(TextLayerStatus Status, TileRaster Raster, bool UsedCache);

public sealed record TextHitTestResult(int CharacterIndex, int LineIndex, bool IsInside);

public sealed record TextCaretStop(int CharacterIndex, float X);

public sealed record TextLayoutLine(int StartIndex, int EndIndex, string Text, float X, float Y,
    float Baseline, float Width, float Height, IReadOnlyList<TextCaretStop> CaretStops);

public sealed record TextCaretPosition(int CharacterIndex, int LineIndex, float X, float Y, float Height);

public sealed record TextSelectionRectangle(int LineIndex, float X, float Y, float Width, float Height);

public sealed class TextLayoutSnapshot
{
    public TextLayoutSnapshot(IReadOnlyList<TextLayoutLine> lines, int contentLength, float lineHeight)
    {
        Lines = lines;
        ContentLength = contentLength;
        LineHeight = lineHeight;
    }

    public IReadOnlyList<TextLayoutLine> Lines { get; }
    public int ContentLength { get; }
    public float LineHeight { get; }

    public TextCaretPosition Caret(int characterIndex)
    {
        int target = Math.Clamp(characterIndex, 0, ContentLength);
        TextLayoutLine line = Lines.FirstOrDefault(candidate => target <= candidate.EndIndex) ?? Lines[^1];
        TextCaretStop stop = CaretStop(line, target);
        int lineIndex = 0;
        while (!ReferenceEquals(Lines[lineIndex], line)) lineIndex++;
        return new TextCaretPosition(stop.CharacterIndex, lineIndex, stop.X, line.Y, line.Height);
    }

    public IReadOnlyList<TextSelectionRectangle> Selection(int anchorIndex, int activeIndex)
    {
        int start = Math.Clamp(Math.Min(anchorIndex, activeIndex), 0, ContentLength);
        int end = Math.Clamp(Math.Max(anchorIndex, activeIndex), 0, ContentLength);
        if (start == end) return Array.Empty<TextSelectionRectangle>();
        var rectangles = new List<TextSelectionRectangle>();
        for (int lineIndex = 0; lineIndex < Lines.Count; lineIndex++)
        {
            TextLayoutLine line = Lines[lineIndex];
            int lineStart = Math.Max(start, line.StartIndex);
            int lineEnd = Math.Min(end, line.EndIndex);
            if (lineEnd <= lineStart) continue;
            float left = CaretStop(line, lineStart).X;
            float right = CaretStop(line, lineEnd).X;
            rectangles.Add(new TextSelectionRectangle(lineIndex, Math.Min(left, right), line.Y,
                Math.Abs(right - left), line.Height));
        }
        return rectangles;
    }

    public TextHitTestResult HitTest(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        int lineIndex = Math.Clamp((int)MathF.Floor(Math.Max(0, y) / LineHeight), 0, Lines.Count - 1);
        TextLayoutLine line = Lines[lineIndex];
        TextCaretStop stop = line.CaretStops.Count == 1
            ? line.CaretStops[0]
            : line.CaretStops.OrderBy(candidate => Math.Abs(candidate.X - x)).First();
        bool inside = y >= 0 && y < Lines.Count * LineHeight && x >= line.X && x <= line.X + line.Width;
        return new TextHitTestResult(stop.CharacterIndex, lineIndex, inside);
    }

    private static TextCaretStop CaretStop(TextLayoutLine line, int characterIndex) =>
        line.CaretStops.MinBy(candidate => Math.Abs(candidate.CharacterIndex - characterIndex))!;
}

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
        return Layout(metadata, resolution).HitTest(x, y);
    }

    public static TextHitTestResult HitTest(TextLayerMetadata metadata, LayerTransformInfo transform,
        int rasterWidth, int rasterHeight, double resolution, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (rasterWidth < 1 || rasterHeight < 1) throw new ArgumentOutOfRangeException(nameof(rasterWidth));
        if (!double.IsFinite(transform.X) || !double.IsFinite(transform.Y) ||
            !double.IsFinite(transform.Width) || !double.IsFinite(transform.Height) ||
            !double.IsFinite(transform.Rotation) || transform.Width <= 0 || transform.Height <= 0)
            throw new InvalidDataException("Invalid text layer transform.");
        double centeredX = x - (transform.X + transform.Width / 2);
        double centeredY = y - (transform.Y + transform.Height / 2);
        double radians = -transform.Rotation * Math.PI / 180;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        double localX = centeredX * cos - centeredY * sin;
        double localY = centeredX * sin + centeredY * cos;
        if (transform.FlipX) localX = -localX;
        if (transform.FlipY) localY = -localY;
        localX += transform.Width / 2;
        localY += transform.Height / 2;
        float sourceX = (float)(localX * rasterWidth / transform.Width);
        float sourceY = (float)(localY * rasterHeight / transform.Height);
        return Layout(metadata, resolution, rasterWidth).HitTest(sourceX, sourceY);
    }

    public static TextLayoutSnapshot Layout(TextLayerMetadata metadata, double resolution, int? rasterWidth = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        SKTypeface typeface = FindTypeface(metadata.FontPostScriptName)
            ?? throw new NotSupportedException("The original text font became unavailable while laying out text.");
        if (!double.IsFinite(resolution) || resolution <= 0) throw new InvalidDataException("Invalid document resolution.");
        if (rasterWidth is < 1) throw new ArgumentOutOfRangeException(nameof(rasterWidth));
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
        float layoutWidth = metadata.Layout == "box"
            ? (float)metadata.BoxWidth!.Value
            : rasterWidth is { } width ? width : float.PositiveInfinity;
        string normalizedContent = NormalizeNewLines(metadata.Content, out int[] originalOffsets);
        string[] lines = LayoutLines(normalizedContent, paint, tracking, layoutWidth);
        var result = new List<TextLayoutLine>(lines.Length);
        int normalizedCursor = 0;
        float baselineOffset = -paint.FontMetrics.Ascent;
        foreach (string line in lines)
        {
            int startOffset = normalizedCursor;
            int endOffset = normalizedCursor + line.Length;
            float lineWidth = Measure(line, paint, tracking);
            float start = metadata.Alignment switch
            {
                "center" when float.IsFinite(layoutWidth) => (layoutWidth - lineWidth) / 2,
                "right" when float.IsFinite(layoutWidth) => layoutWidth - lineWidth,
                _ => 0
            };
            var stops = new List<TextCaretStop> { new(OriginalBoundary(originalOffsets, startOffset, metadata.Content.Length), start) };
            float cursor = start;
            int lineOffset = 0;
            foreach (string element in Elements(line))
            {
                cursor += paint.MeasureText(element) + tracking;
                lineOffset += element.Length;
                stops.Add(new TextCaretStop(
                    OriginalBoundary(originalOffsets, startOffset + lineOffset, metadata.Content.Length), cursor));
            }
            result.Add(new TextLayoutLine(
                OriginalBoundary(originalOffsets, startOffset, metadata.Content.Length),
                OriginalBoundary(originalOffsets, endOffset, metadata.Content.Length),
                line, start, result.Count * lineHeight, baselineOffset + result.Count * lineHeight,
                lineWidth, lineHeight, stops));
            normalizedCursor = endOffset;
            if (normalizedCursor < normalizedContent.Length && normalizedContent[normalizedCursor] == '\n') normalizedCursor++;
        }
        return new TextLayoutSnapshot(result, metadata.Content.Length, lineHeight);
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
        float tracking = (float)(metadata.TrackingPoints * scale);
        TextLayoutSnapshot layout = Layout(metadata, resolution, cache.Width);
        foreach (TextLayoutLine line in layout.Lines)
            DrawTracked(canvas, line.Text, line.X, line.Baseline, paint, tracking);
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

    private static string NormalizeNewLines(string value, out int[] originalOffsets)
    {
        var normalized = new System.Text.StringBuilder(value.Length);
        var offsets = new List<int>(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '\r' && index + 1 < value.Length && value[index + 1] == '\n')
            {
                normalized.Append('\n');
                offsets.Add(index);
                index++;
            }
            else
            {
                normalized.Append(value[index]);
                offsets.Add(index);
            }
        }
        originalOffsets = offsets.ToArray();
        return normalized.ToString();
    }

    private static int OriginalBoundary(IReadOnlyList<int> originalOffsets, int normalizedOffset, int contentLength) =>
        normalizedOffset < originalOffsets.Count ? originalOffsets[normalizedOffset] : contentLength;

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
