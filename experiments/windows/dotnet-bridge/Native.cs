using System.Runtime.InteropServices;

internal static unsafe class Native
{
    private const string Library = "compositor_native";
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void brush_alpha_bounds(byte* pixels, nuint width, nuint height, nuint stride, nuint* bounds);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void layer_extract_alpha(byte* pixels, nuint stride, byte* gray, nuint grayStride, nuint width, nuint height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void layer_unpremultiply_opaque(byte* pixels, nuint stride, nuint width, nuint height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void layer_restore_alpha(byte* pixels, nuint stride, byte* gray, nuint grayStride, nuint width, nuint height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void adjust_gradient_map(byte* pixels, nuint width, nuint height, nuint stride, byte* table);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void adjust_grain(byte* pixels, nuint width, nuint height, nuint stride, double amount, double size, double roughness, uint seed, double x, double y, double scale);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void rgba_clamp_premultiplied(byte* pixels, nuint count);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int content_fill(byte* pixels, nuint stride, byte* mask, nuint maskStride, int width, int height);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void compositor_heal_bounds(byte* gray, nuint width, nuint height, nuint stride, long* bounds);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int spot_heal(byte* pixels, byte* coverage, nuint width, nuint height, nuint stride, float opacity, int mode, uint seed);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void lens_distort(byte* source, byte* destination, nuint width, nuint height, nuint stride, double k);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void levels_apply(byte* pixels, nuint count, float* tables);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void levels_histogram(byte* pixels, byte* coverage, nuint count, double* bins);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void noise_add(byte* pixels, nuint width, nuint height, nuint stride, float amount, int gaussian, int monochromatic, uint seed);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern long compositor_wand_mask(byte* pixels, nuint width, nuint height, nuint stride, nuint x, nuint y, nuint radius, int tolerance, int contiguous, byte* mask);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int wand_trace(byte* mask, nuint width, nuint height, out nint points, out nuint pointCount, out nint loops, out nuint loopCount);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void compositor_free(nint allocation);
}
