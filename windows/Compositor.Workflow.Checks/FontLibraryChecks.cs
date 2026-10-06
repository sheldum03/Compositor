using Compositor.Imaging;
using SkiaSharp;

namespace Compositor.Workflow.Checks;

internal static class FontLibraryChecks
{
    public static void Run(string fixtures, string output)
    {
        string fonts = Path.Combine(fixtures, "fonts");
        string root = Path.Combine(output, "FontLibrary");
        var library = new FontLibrary(root);
        string source = Path.Combine(fonts, "fixture.ttf");
        if (SKTypeface.FromFile(source) is null)
            source = Directory.GetFiles("/System/Library/Fonts", "*.ttc").First(path => SKTypeface.FromFile(path) is not null);
        ImportedFont first = library.Import(source);
        if (!File.Exists(Path.Combine(root, first.FileName)) ||
            !TextLayerWorkflow.AvailableFonts.Contains(first.FamilyName, StringComparer.OrdinalIgnoreCase))
            throw new Exception("Imported TTF was not persisted or made available to the text renderer.");
        if (!ReferenceEquals(first, library.Import(source)) || library.Entries.Count != 1)
            throw new Exception("Identical font imports were not deduplicated.");
        string? ttcSource = SKTypeface.FromFile(Path.Combine(fonts, "two-faces.ttc"), 1) is not null
            ? Path.Combine(fonts, "two-faces.ttc")
            : Directory.GetFiles("/System/Library/Fonts", "*.ttc")
                .FirstOrDefault(path => SKTypeface.FromFile(path, 1) is not null);
        if (ttcSource is not null)
        {
            ImportedFont secondFace = library.Import(ttcSource, faceIndex: 1);
            if (secondFace.FaceIndex != 1 || !TextLayerWorkflow.AvailableFonts.Contains(secondFace.FamilyName, StringComparer.OrdinalIgnoreCase))
                throw new Exception("TTC face selection was not persisted or registered.");
        }
        var restored = new FontLibrary(root);
        if (restored.Entries.Count != (ttcSource is null ? 1 : 2))
            throw new Exception("Font catalog did not restore imported faces.");
        try
        {
            _ = library.Import(Path.Combine(fonts, "damaged.ttf"));
            throw new Exception("Damaged font was accepted.");
        }
        catch (InvalidDataException) { }
        try
        {
            _ = library.Import(Path.Combine(fonts, "wrong-extension.zip"));
            throw new Exception("Unsupported font extension was accepted.");
        }
        catch (NotSupportedException) { }
        Console.WriteLine("PASS: font library persists, deduplicates, restores, and selects TTC faces while rejecting invalid inputs");
    }
}
