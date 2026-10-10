using System.Runtime.InteropServices;

namespace Compositor.Core;

public static unsafe class NativePixels
{
    private const string Library = "compositor_native";

    [DllImport(Library, EntryPoint = "rgba_clamp_premultiplied", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ClampNative(byte* pixels, nuint count);

    [DllImport(Library, EntryPoint = "layer_extract_alpha", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ExtractAlphaNative(byte* rgba, nuint stride, byte* gray,
        nuint grayStride, nuint width, nuint height);

    public static void ClampPremultiplied(Span<byte> rgba)
    {
        if (rgba.Length % 4 != 0) throw new ArgumentException("RGBA length must be divisible by four.", nameof(rgba));
        fixed (byte* pixels = rgba) ClampNative(pixels, (nuint)(rgba.Length / 4));
    }

    public static byte[] ExtractAlpha(ReadOnlySpan<byte> rgba, int width, int height, int stride)
    {
        if (width < 1 || height < 1 || stride < (long)width * 4 || rgba.Length < (long)stride * height ||
            (long)width * height > 100_000_000)
            throw new ArgumentException("Invalid RGBA dimensions or stride.");
        byte[] gray = new byte[checked(width * height)];
        fixed (byte* pixels = rgba)
        fixed (byte* output = gray)
            ExtractAlphaNative(pixels, (nuint)stride, output, (nuint)width, (nuint)width, (nuint)height);
        return gray;
    }
}
