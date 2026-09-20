using SkiaSharp;

internal sealed record MaskPlacement(SKBitmap Image, SKRect Destination, SKFilterQuality Sampling);
internal sealed record SceneLayer(string Id, string? Parent, SKBitmap? Image, MaskPlacement? Mask,
    SKRect Destination, SKFilterQuality Sampling, float Opacity, SKBlendMode Blend, bool Visible,
    string? ClipSource, double? Saturation, MaskPlacement[] FolderMasks);

// CPU reference path for small M1 specimens, not the later tiled/performance implementation.
internal static class Composite
{
    public static unsafe void Paint(List<SceneLayer> layers, int width, int height, SKCanvas target)
    {
        var visible = layers.Where(l => l.Visible).ToArray();
        for (int index = 0; index < visible.Length; index++)
        {
            var layer = visible[index];
            int end = index + 1;
            while (end < visible.Length && visible[end].ClipSource == layer.Id) end++;
            if (end == index + 1 && layer.Mask is null && layer.FolderMasks.Length == 0)
            {
                DrawImage(layer, target, layer.Blend);
                continue;
            }
            using var pixels = Bitmap(width, height);
            using var canvas = new SKCanvas(pixels);
            DrawOwn(layer, canvas, width, height);
            if (end > index + 1)
            {
                // These are the exact C entry points used by Mac LiveMaskRenderer.
                byte[] alpha = new byte[width * height];
                canvas.Flush();
                fixed (byte* coverage = alpha)
                {
                    Native.layer_extract_alpha((byte*)pixels.GetPixels(), (nuint)pixels.RowBytes,
                        coverage, (nuint)width, (nuint)width, (nuint)height);
                    Native.layer_unpremultiply_opaque((byte*)pixels.GetPixels(), (nuint)pixels.RowBytes,
                        (nuint)width, (nuint)height);
                    pixels.NotifyPixelsChanged();
                    for (int child = index + 1; child < end; child++)
                    {
                        if (visible[child].Saturation is { } saturation)
                        {
                            canvas.Flush();
                            Desaturate(pixels, saturation, visible[child].Opacity);
                        }
                        else DrawOwn(visible[child], canvas, width, height);
                    }
                    canvas.Flush();
                    Native.layer_restore_alpha((byte*)pixels.GetPixels(), (nuint)pixels.RowBytes,
                        coverage, (nuint)width, (nuint)width, (nuint)height);
                    pixels.NotifyPixelsChanged();
                }
            }
            // Group masks affect the finished stack, never its base-alpha dependency.
            canvas.Flush();
            foreach (var mask in layer.FolderMasks) ApplyMask(mask, pixels);
            using var paint = new SKPaint { BlendMode = layer.Blend };
            target.DrawBitmap(pixels, 0, 0, paint);
            index = end - 1;
        }
    }

    private static SKBitmap Bitmap(int width, int height)
    {
        using var srgb = SKColorSpace.CreateSrgb();
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, srgb));
        bitmap.Erase(SKColors.Transparent);
        return bitmap;
    }
    private static void DrawImage(SceneLayer layer, SKCanvas canvas, SKBlendMode blend)
    {
        using var paint = new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Round(layer.Opacity * 255)),
            BlendMode = blend, FilterQuality = layer.Sampling
        };
        canvas.DrawBitmap(layer.Image!, layer.Destination, paint);
    }
    private static void DrawOwn(SceneLayer layer, SKCanvas target, int width, int height)
    {
        if (layer.Mask is null) { DrawImage(layer, target, layer.Blend); return; }
        using var pixels = Bitmap(width, height);
        using var canvas = new SKCanvas(pixels);
        DrawImage(layer, canvas, SKBlendMode.SrcOver);
        canvas.Flush();
        ApplyMask(layer.Mask, pixels);
        using var paint = new SKPaint { BlendMode = layer.Blend };
        target.DrawBitmap(pixels, 0, 0, paint);
    }
    private static unsafe void ApplyMask(MaskPlacement mask, SKBitmap target)
    {
        using var coverage = Bitmap(target.Width, target.Height);
        using (var canvas = new SKCanvas(coverage))
        using (var paint = new SKPaint { Color = SKColors.White, FilterQuality = mask.Sampling })
            canvas.DrawBitmap(mask.Image, mask.Destination, paint);
        // Skia DstIn uses different integer rounding in this build (73 × 97 / 255 became 27).
        // Use the same rounded /255 convention as BrushPixels instead: the result is 28.
        for (int y = 0; y < target.Height; y++)
        for (int x = 0; x < target.Width; x++)
        {
            byte* p = (byte*)target.GetPixels() + y * target.RowBytes + x * 4;
            int alpha = *((byte*)coverage.GetPixels() + y * coverage.RowBytes + x * 4 + 3);
            for (int c = 0; c < 4; c++) p[c] = (byte)((p[c] * alpha + 127) / 255);
        }
        target.NotifyPixelsChanged();
    }

    private static unsafe void Desaturate(SKBitmap pixels, double saturation, float opacity)
    {
        // Mac samples a 33³ HSL cube with color management disabled. For master saturation <= 0,
        // HSL reduces to L + (component - L) * factor. Interpolate the same eight cube corners.
        double factor = 1 + saturation / 100;
        for (int y = 0; y < pixels.Height; y++)
        for (int x = 0; x < pixels.Width; x++)
        {
            byte* p = (byte*)pixels.GetPixels() + y * pixels.RowBytes + x * 4;
            double r = p[0] * 32.0 / 255, g = p[1] * 32.0 / 255, b = p[2] * 32.0 / 255;
            int ri = Math.Min(31, (int)r), gi = Math.Min(31, (int)g), bi = Math.Min(31, (int)b);
            double lightness = 0;
            for (int dz = 0; dz < 2; dz++)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                double weight = (dx == 0 ? 1 - (r - ri) : r - ri) *
                    (dy == 0 ? 1 - (g - gi) : g - gi) * (dz == 0 ? 1 - (b - bi) : b - bi);
                lightness += weight * (Math.Max(ri + dx, Math.Max(gi + dy, bi + dz)) +
                    Math.Min(ri + dx, Math.Min(gi + dy, bi + dz))) / 64;
            }
            for (int c = 0; c < 3; c++)
            {
                double adjusted = Math.Floor((lightness * 255 + (p[c] - lightness * 255) * factor) + 0.5);
                p[c] = (byte)Math.Clamp(Math.Floor(p[c] * (1 - opacity) + adjusted * opacity + 0.5), 0, 255);
            }
        }
        pixels.NotifyPixelsChanged();
    }
}
