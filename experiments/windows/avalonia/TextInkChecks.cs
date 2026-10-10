using System.Text.Json;

// Catastrophic glyph-loss guard for the fixed blue F11 corpus. This is not a
// cross-platform pixel tolerance: full reference differences remain unaccepted.
internal static class TextInkChecks
{
    internal sealed record Result(string Fixture, double OrdinaryInk, double ReferenceInk, double Ratio, bool Passed);

    internal static Result Compare(string fixtures, string output, string name)
    {
        double expected = Ink(Path.Combine(fixtures, name + "-mac.png"));
        double actual = Ink(Path.Combine(output, name + "-export.png"));
        double ratio = expected > 0 ? actual / expected : 0;
        return new(name, actual, expected, ratio, expected > 0 && ratio >= 0.5);
    }

    private static double Ink(string path)
    {
        byte[] pixels = Program.Pixels(path);
        long alpha = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int a = pixels[i + 3];
            // Compare unpremultiplied channel differences without division.
            // The warm-colored emoji must not satisfy ordinary-text visibility.
            if ((pixels[i + 2] - pixels[i]) * 255 > 20 * a &&
                (pixels[i + 2] - pixels[i + 1]) * 255 > 10 * a)
                alpha += a;
        }
        return alpha / 255.0;
    }

    internal static void Verify(string fixtures, string output, bool saveReport = false)
    {
        var results = Directory.GetDirectories(fixtures, "F11-*.comp").Order()
            .Select(path => Compare(fixtures, output, Path.GetFileNameWithoutExtension(path))).ToArray();
        bool passed = results.Length == 12 && results.All(result => result.Passed);
        string json = JsonSerializer.Serialize(new { status = passed ? "passed" : "failed", results },
            new JsonSerializerOptions { WriteIndented = true });
        if (saveReport) File.WriteAllText(Path.Combine(output, "ordinary-ink.json"), json + "\n");
        Console.WriteLine(json);
        if (!passed) throw new InvalidDataException("Ordinary text lost over half its ink; preview/export equality is insufficient.");
    }
}
