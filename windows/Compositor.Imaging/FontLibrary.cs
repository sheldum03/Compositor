using System.Security.Cryptography;
using System.Text.Json;

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
        if (!File.Exists(destination)) File.WriteAllBytes(destination, bytes);
        string family = TextLayerWorkflow.RegisterImportedTypeface(destination, faceIndex);
        var existing = entries.FirstOrDefault(entry =>
            entry.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase) && entry.FaceIndex == faceIndex);
        if (existing is not null) return existing;
        var imported = new ImportedFont(fileName, family, faceIndex, hash);
        entries.Add(imported);
        SaveCatalog();
        return imported;
    }

    private void Restore()
    {
        foreach (ImportedFont entry in entries)
        {
            string path = Path.Combine(root, entry.FileName);
            if (!File.Exists(path)) continue;
            try { _ = TextLayerWorkflow.RegisterImportedTypeface(path, entry.FaceIndex); }
            catch (Exception) when (entry.FaceIndex >= 0) { }
        }
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
