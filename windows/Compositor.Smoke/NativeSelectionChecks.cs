using System.Text.Json;
using Compositor.Core;

internal static class NativeSelectionChecks
{
    public static void Run(string output)
    {
        const int width = 3, height = 2, stride = 16;
        byte[] rgba = Enumerable.Repeat((byte)199, stride * height).ToArray();
        byte[] colors = [100, 0, 100, 0, 100, 100];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = y * stride + x * 4;
            rgba[offset] = colors[y * width + x];
            rgba[offset + 1] = 0;
            rgba[offset + 2] = (byte)(100 - rgba[offset]);
            rgba[offset + 3] = 255;
        }
        byte[] original = (byte[])rgba.Clone();
        CheckMask(NativeSelections.Select(rgba, width, height, stride, 0, 0, 0, 0, true), [255, 0, 0, 0, 0, 0]);
        var global = NativeSelections.Select(rgba, width, height, stride, 0, 0, 0, 0, false);
        CheckMask(global, [255, 0, 255, 0, 255, 255]);
        CheckMask(NativeSelections.Select(rgba, width, height, stride, -1, 0, 0, 0, true), new byte[6]);
        CheckMask(NativeSelections.Select(rgba, width, height, stride, 3, 2, 0, 0, true), new byte[6]);
        Require(rgba.SequenceEqual(original), "Native selection changed input or row padding.");

        byte[] ramp = [0, 0, 0, 255, 10, 10, 10, 255, 20, 20, 20, 255, 30, 30, 30, 255, 40, 40, 40, 255];
        CheckMask(NativeSelections.Select(ramp, 5, 1, 20, 0, 0, 1, 5, true), [255, 255, 0, 0, 0]);
        CheckMask(NativeSelections.Select(ramp, 5, 1, 20, 0, 0, 2, 0, true), new byte[5]);
        CheckMask(NativeSelections.Select(ramp, 5, 1, 20, 0, 0, 2, 0, false), [0, 255, 0, 0, 0]);
        byte[] alpha = [0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 255];
        CheckMask(NativeSelections.Select(alpha, 3, 1, 12, 0, 0, 0, -1, false), [255, 0, 0]);
        CheckMask(NativeSelections.Select(alpha, 3, 1, 12, 0, 0, 0, 1, false), [255, 255, 0]);
        CheckMask(NativeSelections.Select(alpha, 3, 1, 12, 0, 0, 0, 300, false), [255, 255, 255]);

        Reject<ArgumentException>(() => NativeSelections.Select(rgba, 3, 2, 11, 0, 0, 0, 0, false));
        Reject<ArgumentException>(() => NativeSelections.Select(rgba.AsSpan(0, 31), 3, 2, 16, 0, 0, 0, 0, false));
        Reject<ArgumentOutOfRangeException>(() => NativeSelections.Select(rgba, 3, 2, 16, 0, 0, 3, 0, false));
        Reject<ArgumentOutOfRangeException>(() => NativeSelections.Trace([], int.MaxValue, 2));
        Reject<ArgumentException>(() => NativeSelections.Trace([255], 3, 2));

        byte[] ring = [255, 255, 255, 255, 0, 255, 255, 255, 255];
        var outline = NativeSelections.Trace(ring, 3, 3);
        Require(outline.LoopLengths.Length == 2, "Ring must have an outer loop and a hole.");
        CheckOutline(ring, 3, 3, outline);
        byte[] diagonal = [255, 0, 0, 0, 255, 0, 0, 0, 255];
        var separate = NativeSelections.Trace(diagonal, 3, 3);
        Require(separate.LoopLengths.Length == 3, "Corner-touching pixels must remain separate loops.");
        CheckOutline(diagonal, 3, 3, separate);
        var empty = NativeSelections.Trace(new byte[9], 3, 3);
        Require(empty.Coordinates.Length == 0 && empty.LoopLengths.Length == 0, "Empty mask produced an outline.");
        byte[] irregular = Enumerable.Range(0, 17 * 13).Select(i => (byte)((i * 37 % 11) < 5 ? 128 : 0)).ToArray();
        for (int repeat = 0; repeat < 100; repeat++)
            CheckOutline(irregular, 17, 13, NativeSelections.Trace(irregular, 17, 13));

        // More than 8 million edges must use the recoverable 'too detailed' path.
        const int edgeSize = 2002;
        byte[] detailed = new byte[edgeSize * edgeSize];
        for (int y = 0; y < edgeSize; y++)
        for (int x = 0; x < edgeSize; x++) detailed[y * edgeSize + x] = (byte)((x + y) % 2 == 0 ? 255 : 0);
        Reject<InvalidOperationException>(() => NativeSelections.Trace(detailed, edgeSize, edgeSize));
        CheckOutline(ring, 3, 3, NativeSelections.Trace(ring, 3, 3));

        string evidence = Path.Combine(output, "native-selection");
        Directory.CreateDirectory(evidence);
        File.WriteAllBytes(Path.Combine(evidence, "padded-rgba.bin"), rgba);
        File.WriteAllBytes(Path.Combine(evidence, "global-mask.bin"), global.Mask);
        File.WriteAllText(Path.Combine(evidence, "results.json"), JsonSerializer.Serialize(new
        {
            passed = true, maskCases = 10, invalidInputs = 5, repeatedTraceChecks = 100,
            detailedOutlineRejected = true, inputPreserved = true, ringOutline = outline,
            limits = "Native interface checks; no editor selection transaction, canvas interaction or Windows claim unless run there."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: native wand sampling, connectivity, alpha, padded rows, bounds, hole winding and owned outline memory");
    }

    private static void CheckMask(WandSelection result, byte[] expected)
    {
        Require(result.Mask.SequenceEqual(expected), "Wand mask differs from the expected selection.");
        Require(result.SelectedPixels == expected.Count(value => value != 0), "Wand selected count differs from its mask.");
    }

    private static void CheckOutline(byte[] mask, int width, int height, SelectionOutline outline)
    {
        Require(outline.LoopLengths.Sum() * 2 == outline.Coordinates.Length, "Invalid native point/loop counts.");
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int winding = 0, start = 0;
            foreach (int length in outline.LoopLengths)
            {
                Require(length >= 4, "Degenerate outline loop.");
                for (int i = 0; i < length; i++)
                {
                    int a = (start + i) * 2, b = (start + (i + 1) % length) * 2;
                    int ax = outline.Coordinates[a], ay = outline.Coordinates[a + 1];
                    int bx = outline.Coordinates[b], by = outline.Coordinates[b + 1];
                    Require(ax >= 0 && ax <= width && ay >= 0 && ay <= height, "Outline corner outside the canvas.");
                    Require(ax == bx || ay == by, "Outline does not follow pixel edges.");
                    double cross = (bx - ax) * (y + 0.5 - ay) - (by - ay) * (x + 0.5 - ax);
                    if (ay <= y + 0.5 && by > y + 0.5 && cross > 0) winding++;
                    if (ay > y + 0.5 && by <= y + 0.5 && cross < 0) winding--;
                }
                start += length;
            }
            Require((winding != 0) == (mask[y * width + x] != 0), "Winding outline does not cover exactly the mask pixels.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
