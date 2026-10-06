using System.Text.Json;
using Compositor.Core;
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
            !TextLayerWorkflow.AvailableFonts.Contains(first.SelectionName, StringComparer.OrdinalIgnoreCase))
            throw new Exception("Imported TTF was not persisted or made available to the text renderer.");
        if (!ReferenceEquals(first, library.Import(source)) || library.Entries.Count != 1)
            throw new Exception("Identical font imports were not deduplicated.");
        string? ttcSource = SKTypeface.FromFile(Path.Combine(fonts, "two-faces.ttc"), 1) is not null
            ? Path.Combine(fonts, "two-faces.ttc")
            : Directory.GetFiles("/System/Library/Fonts", "*.ttc")
                .FirstOrDefault(path => SKTypeface.FromFile(path, 1) is not null);
        if (ttcSource is not null)
        {
            IReadOnlyList<FontFace> enumeratedFaces = FontLibrary.EnumerateFaces(ttcSource);
            if (enumeratedFaces.Count < 2 || enumeratedFaces[0].FaceIndex != 0 || enumeratedFaces[1].FaceIndex != 1 ||
                enumeratedFaces[0].SelectionName == enumeratedFaces[1].SelectionName)
                throw new Exception("Skia face enumeration did not expose distinct face-index selections.");
            string faceRoot = Path.Combine(output, "FontFaceLibrary");
            var faceLibrary = new FontLibrary(faceRoot);
            ImportedFont firstFace = faceLibrary.Import(ttcSource, faceIndex: 0);
            ImportedFont secondFace = faceLibrary.Import(ttcSource, faceIndex: 1);
            if (firstFace.SelectionName == secondFace.SelectionName ||
                !TextLayerWorkflow.AvailableFonts.Contains(firstFace.SelectionName, StringComparer.OrdinalIgnoreCase) ||
                !TextLayerWorkflow.AvailableFonts.Contains(secondFace.SelectionName, StringComparer.OrdinalIgnoreCase))
                throw new Exception("TTC faces did not expose distinct, selectable identities.");
            var faceSession = ProjectSession.CreateBlank(160, 80);
            var firstMetadata = new TextLayerMetadata(Guid.Empty, "", "A", firstFace.SelectionName, 40,
                0, 0, 0, 1, "left", 0, 0, "point", null);
            var secondMetadata = firstMetadata with { FontPostScriptName = secondFace.SelectionName };
            var legacySession = ProjectSession.CreateBlank(160, 80);
            legacySession.AddTextLayer("Ambiguous legacy face", firstMetadata with { FontPostScriptName = firstFace.FamilyName },
                new TileRaster(160, 80), 0);
            if (TextLayerWorkflow.Inspect(legacySession).Single().FontAvailable)
                throw new Exception("A legacy family-only text identity silently selected one of multiple faces.");
            Guid firstId = faceSession.AddTextLayer("Regular face", firstMetadata,
                TextLayerWorkflow.RenderText(firstMetadata, faceSession.Width, faceSession.Height, faceSession.Resolution), 0);
            faceSession.AddTextLayer("Second face", secondMetadata,
                TextLayerWorkflow.RenderText(secondMetadata, faceSession.Width, faceSession.Height, faceSession.Resolution), 1);
            string faceProject = Path.Combine(output, "FontFaces.comp");
            ImageProjectWorkflow.Save(faceSession, faceProject);
            var reopenedFaces = ProjectStore.Open(faceProject);
            if (reopenedFaces.TextLayers.Single(text => text.Id == firstId).FontPostScriptName != firstFace.SelectionName ||
                reopenedFaces.TextLayers.Single(text => text.Id != firstId).FontPostScriptName != secondFace.SelectionName)
                throw new Exception("TTC face selection identity was not preserved through save and reopen.");
        }
        var restored = new FontLibrary(root);
        if (restored.Entries.Count != 1)
            throw new Exception("Font catalog did not restore the imported face.");
        string damagedRoot = Path.Combine(output, "DamagedFontLibrary");
        var damagedLibrary = new FontLibrary(damagedRoot);
        try
        {
            _ = damagedLibrary.Import(Path.Combine(fonts, "damaged.ttf"));
            throw new Exception("Damaged font was accepted.");
        }
        catch (InvalidDataException) { }
        if (Directory.GetFiles(damagedRoot).Length != 0)
            throw new Exception("Rejected damaged font left a file in the user font directory.");
        int entriesBeforeConflict = library.Entries.Count;
        try
        {
            _ = library.Import(Path.Combine(fonts, "conflict.ttf"));
            throw new Exception("A different font with the same family and face was accepted.");
        }
        catch (InvalidDataException exception) when (exception.Message.Contains("same family and face", StringComparison.Ordinal)) { }
        if (library.Entries.Count != entriesBeforeConflict ||
            Directory.GetFiles(root, "*.tmp-*", SearchOption.TopDirectoryOnly).Length != 0 ||
            !File.Exists(Path.Combine(root, first.FileName)))
            throw new Exception("Rejecting a conflicting font changed the existing font identity or left a temporary asset.");
        try
        {
            _ = library.Import(Path.Combine(fonts, "wrong-extension.zip"));
            throw new Exception("Unsupported font extension was accepted.");
        }
        catch (NotSupportedException) { }
        string recoveryRoot = Path.Combine(output, "RecoveredFontLibrary");
        var recoverySource = new FontLibrary(recoveryRoot);
        ImportedFont recoveryFont = recoverySource.Import(source);
        string recoveryCatalog = Path.Combine(recoveryRoot, "fonts.json");
        File.WriteAllText(recoveryCatalog,
            File.ReadAllText(recoveryCatalog).Replace(recoveryFont.Sha256, new string('0', 64), StringComparison.Ordinal));
        var recovered = new FontLibrary(recoveryRoot);
        if (recovered.Entries.Count != 0 || JsonSerializer.Deserialize<List<ImportedFont>>(File.ReadAllText(recoveryCatalog))?.Count != 0)
            throw new Exception("A damaged persisted font was not removed from the restored catalog.");
        Console.WriteLine("PASS: font library persists, deduplicates, restores, and selects TTC faces while rejecting invalid inputs");
    }
}
