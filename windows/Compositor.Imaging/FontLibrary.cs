using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

namespace Compositor.Imaging;

public sealed record ImportedFont(string FileName, string FamilyName, int FaceIndex, string Sha256);

public sealed class FontLibrary
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".otf", ".ttf", ".ttc" };
    private readonly string root;
    private readonly string catalogPath;
    private readonly List<ImportedFont> entries;

    public FontLibrary(string rootDirectory)
    {
        root = Path.GetFullPath(rootDirectory);
        catalogPath = Path.Combine(root, "fonts.json");
        Directory.CreateDirectory(root);
        entries = LoadCatalog();
        Restore();
    }

    public IReadOnlyList<ImportedFont> Entries => entries.AsReadOnly();

    public ImportedFont Import(string sourcePath, int faceIndex = 0)
    {
        string extension = Path.GetExtension(sourcePath);
        if (!SupportedExtensions.Contains(extension))
            throw new NotSupportedException("Only OTF, TTF, and TTC fonts can be imported.");
        byte[] bytes = File.ReadAllBytes(sourcePath);
        if (bytes.Length == 0) throw new InvalidDataException("The font file is empty.");
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string fileName = hash + extension.ToLowerInvariant();
        string destination = Path.Combine(root, fileName);
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            SKTypeface typeface = SKTypeface.FromFile(temporary, faceIndex)
                ?? throw new InvalidDataException("The font face could not be loaded.");
            string family = typeface.FamilyName;
            ImportedFont? conflict = entries.FirstOrDefault(entry =>
                entry.FaceIndex == faceIndex &&
                NormalizeFamily(entry.FamilyName) == NormalizeFamily(family) &&
                !entry.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase));
            if (conflict is not null)
                throw new InvalidDataException($"A different font with the same family and face is already imported: {family}.");
            family = TextLayerWorkflow.RegisterImportedTypeface(temporary, faceIndex);
            File.Move(temporary, destination, overwrite: true);
            var existing = entries.FirstOrDefault(entry =>
                entry.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase) && entry.FaceIndex == faceIndex);
            if (existing is not null) return existing;
            var imported = new ImportedFont(fileName, family, faceIndex, hash);
            entries.Add(imported);
            SaveCatalog();
            return imported;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private void Restore()
    {
        var restored = new List<ImportedFont>();
        foreach (ImportedFont entry in entries)
        {
            string path = Path.Combine(root, entry.FileName);
            if (!File.Exists(path) || !MatchesHash(path, entry.Sha256)) continue;
            try
            {
                _ = TextLayerWorkflow.RegisterImportedTypeface(path, entry.FaceIndex);
                if (!restored.Any(item => item.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase) &&
                    item.FaceIndex == entry.FaceIndex)) restored.Add(entry);
            }
            catch (Exception) when (entry.FaceIndex >= 0) { }
        }
        if (restored.Count == entries.Count) return;
        entries.Clear();
        entries.AddRange(restored);
        SaveCatalog();
    }

    private static bool MatchesHash(string path, string expected)
    {
        try
        {
            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string NormalizeFamily(string value)
    {
        string normalized = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        foreach (string suffix in new[] { "regular", "normal", "roman", "book", "medium", "semibold", "bold", "italic" })
            if (normalized.EndsWith(suffix, StringComparison.Ordinal) && normalized.Length > suffix.Length)
                return normalized[..^suffix.Length];
        return normalized;
    }

    private List<ImportedFont> LoadCatalog()
    {
        if (!File.Exists(catalogPath)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<ImportedFont>>(File.ReadAllText(catalogPath)) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The font catalog is invalid.", exception);
        }
    }

    private void SaveCatalog()
    {
        string temporary = catalogPath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, catalogPath, overwrite: true);
    }
}
