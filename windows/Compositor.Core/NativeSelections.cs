using System.Runtime.InteropServices;

namespace Compositor.Core;

public sealed record WandSelection(byte[] Mask, long SelectedPixels);
public sealed record SelectionOutline(int[] Coordinates, int[] LoopLengths);

public static unsafe class NativeSelections
{
    private const string Library = "compositor_native";

    // The bridge fixes C long's different widths on Windows and macOS.
    [DllImport(Library, EntryPoint = "compositor_wand_mask", CallingConvention = CallingConvention.Cdecl)]
    private static extern long SelectNative(byte* rgba, nuint width, nuint height, nuint stride,
        nuint x, nuint y, nuint radius, int tolerance, int contiguous, byte* mask);

    [DllImport(Library, EntryPoint = "wand_trace", CallingConvention = CallingConvention.Cdecl)]
    private static extern int TraceNative(byte* mask, nuint width, nuint height,
        out nint points, out nuint pointCount, out nint loops, out nuint loopCount);

    [DllImport(Library, EntryPoint = "compositor_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void FreeNative(nint allocation);

    public static WandSelection Select(ReadOnlySpan<byte> rgba, int width, int height, int stride,
        int x, int y, int radius, int tolerance, bool contiguous)
    {
        ValidateDimensions(width, height);
        if (stride < (long)width * 4 || rgba.Length < (long)stride * height)
            throw new ArgumentException("Invalid RGBA buffer or stride.", nameof(rgba));
        // Match the Mac product's point, 3-by-3 and 5-by-5 sampling options.
        if (radius is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(radius));
        byte[] mask = new byte[checked(width * height)];
        if (x < 0 || x >= width || y < 0 || y >= height) return new(mask, 0);
        long count;
        fixed (byte* input = rgba)
        fixed (byte* output = mask)
            count = SelectNative(input, (nuint)width, (nuint)height, (nuint)stride,
                (nuint)x, (nuint)y, (nuint)radius, Math.Clamp(tolerance, 0, 255), contiguous ? 1 : 0, output);
        if (count < 0) throw new OutOfMemoryException("Not enough memory to make the selection.");
        return new(mask, count);
    }

    // Coordinates are top-left pixel-edge x/y pairs. Each loop is closed implicitly;
    // outer loops run clockwise and holes counterclockwise for nonzero winding fill.
    public static SelectionOutline Trace(ReadOnlySpan<byte> mask, int width, int height)
    {
        ValidateDimensions(width, height);
        if (mask.Length != (long)width * height)
            throw new ArgumentException("Selection mask length does not match its dimensions.", nameof(mask));
        nint points = 0, loops = 0;
        try
        {
            int status;
            nuint pointCount, loopCount;
            fixed (byte* input = mask)
                status = TraceNative(input, (nuint)width, (nuint)height,
                    out points, out pointCount, out loops, out loopCount);
            if (status == -2)
                throw new InvalidOperationException("The selection is too detailed to outline. Change tolerance or use contiguous selection.");
            if (status != 0) throw new OutOfMemoryException("Not enough memory to outline the selection.");
            int[] coordinates = new int[checked((int)pointCount * 2)];
            int[] lengths = new int[checked((int)loopCount)];
            if (coordinates.Length > 0) Marshal.Copy(points, coordinates, 0, coordinates.Length);
            if (lengths.Length > 0) Marshal.Copy(loops, lengths, 0, lengths.Length);
            return new(coordinates, lengths);
        }
        finally
        {
            FreeNative(points);
            FreeNative(loops);
        }
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width));
    }
}
