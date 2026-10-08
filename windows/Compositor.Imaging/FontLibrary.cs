using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace Compositor.Imaging;

public sealed record ImportedFont(string FileName, string FamilyName, int FaceIndex, string Sha256)
{
    [JsonIgnore]
    public string SelectionName => TextLayerWorkflow.ImportedFontSelectionName(FamilyName, FaceIndex);
}

public sealed record FontFace(int FaceIndex, string FamilyName)
{
    public string SelectionName => TextLayerWorkflow.ImportedFontSelectionName(FamilyName, FaceIndex);
}

public sealed record FontRecoveryIssue(string FileName, string Reason);

public sealed record FontRecoveryReport(IReadOnlyList<FontRecoveryIssue> Issues)
{
    public bool HasIssues => Issues.Count > 0;

    public string Message => Issues.Count == 0
        ? string.Empty
        : $"字体库恢复：已清理 {Issues.Count} 个失效条目：" +
          string.Join("；", Issues.Select(issue => $"{issue.FileName}（{issue.Reason}）"));
}

public sealed class FontLibrary
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".otf", ".ttf", ".ttc" };
    private readonly string root;
    private readonly string catalogPath;
    private readonly List<ImportedFont> entries;
    private readonly List<FontRecoveryIssue> catalogIssues = [];

    public FontLibrary(string rootDirectory)
    {
        root = Path.GetFullPath(rootDirectory);
        catalogPath = Path.Combine(root, "fonts.json");
        Directory.CreateDirectory(root);
        entries = LoadCatalog();
        RecoveryReport = Restore();
    }

    public IReadOnlyList<ImportedFont> Entries => entries.AsReadOnly();
    public FontRecoveryReport RecoveryReport { get; }

    public static IReadOnlyList<FontFace> EnumerateFaces(string sourcePath)
    {
        string extension = Path.GetExtension(sourcePath);
        if (!SupportedExtensions.Contains(extension))
            throw new NotSupportedException("Only OTF, TTF, and TTC fonts can be imported.");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The font file was not found.", sourcePath);
        if (new FileInfo(sourcePath).Length == 0)
            throw new InvalidDataException("The font file is empty.");

        var faces = new List<FontFace>();
        for (int faceIndex = 0; faceIndex < 256; faceIndex++)
        {
            SKTypeface? typeface;
            try { typeface = SKTypeface.FromFile(sourcePath, faceIndex); }
            catch (Exception exception) when (faceIndex == 0)
            {
                throw new InvalidDataException("The font face could not be loaded.", exception);
            }
            catch (Exception) { break; }
            if (typeface is null) break;
            using (typeface) faces.Add(new FontFace(faceIndex, typeface.FamilyName));
        }
        if (faces.Count == 0)
            throw new InvalidDataException("The font face could not be loaded.");
        return faces;
    }

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

    private FontRecoveryReport Restore()
    {
        var restored = new List<ImportedFont>();
        var issues = new List<FontRecoveryIssue>(catalogIssues);
        foreach (ImportedFont entry in entries)
        {
            if (!TryResolveFontPath(entry.FileName, out string path, out string pathReason))
            {
                issues.Add(new FontRecoveryIssue(entry.FileName, pathReason));
                continue;
            }
            if (!File.Exists(path))
            {
                issues.Add(new FontRecoveryIssue(entry.FileName, "文件缺失"));
                continue;
            }
            if (!MatchesHash(path, entry.Sha256))
            {
                issues.Add(new FontRecoveryIssue(entry.FileName, "文件哈希不匹配"));
                continue;
            }
            try
            {
                _ = TextLayerWorkflow.RegisterImportedTypeface(path, entry.FaceIndex);
                if (!restored.Any(item => item.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase) &&
                    item.FaceIndex == entry.FaceIndex)) restored.Add(entry);
                else issues.Add(new FontRecoveryIssue(entry.FileName, "catalog 重复条目"));
            }
            catch (Exception) when (entry.FaceIndex >= 0)
            {
                issues.Add(new FontRecoveryIssue(entry.FileName, "字体面不可用"));
            }
        }
        if (issues.Count == 0 && restored.Count == entries.Count)
            return new FontRecoveryReport([]);
        entries.Clear();
        entries.AddRange(restored);
        SaveCatalog();
        return new FontRecoveryReport(issues.AsReadOnly());
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
            List<ImportedFont?> loaded = JsonSerializer.Deserialize<List<ImportedFont?>>(File.ReadAllText(catalogPath)) ?? [];
            var valid = new List<ImportedFont>(loaded.Count);
            foreach (ImportedFont? entry in loaded)
            {
                string fileName = entry?.FileName ?? "<null>";
                if (entry is null)
                {
                    catalogIssues.Add(new FontRecoveryIssue(fileName, "catalog 条目无效"));
                    continue;
                }
                if (!TryResolveFontPath(entry.FileName, out _, out string reason))
                {
                    catalogIssues.Add(new FontRecoveryIssue(fileName, reason));
                    continue;
                }
                valid.Add(entry);
            }
            return valid;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The font catalog is invalid.", exception);
        }
    }

    private bool TryResolveFontPath(string fileName, out string path, out string reason)
    {
        path = string.Empty;
        reason = "字体文件路径不安全";
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName is "." or ".." ||
            Path.IsPathRooted(fileName) ||
            fileName.IndexOf(':') >= 0 ||
            fileName.IndexOf('/') >= 0 ||
            fileName.IndexOf('\\') >= 0)
            return false;

        try
        {
            string candidate = Path.GetFullPath(Path.Combine(root, fileName));
            string canonicalRoot = CanonicalRootPath();
            string canonicalCandidate = CanonicalFilePath(candidate);
            if (!IsInside(canonicalRoot, canonicalCandidate))
                return false;
            path = candidate;
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    private string CanonicalRootPath()
    {
        string fullRoot = Path.GetFullPath(root);
        try
        {
            FileSystemInfo? target = new DirectoryInfo(fullRoot).ResolveLinkTarget(returnFinalTarget: true);
            return Path.GetFullPath(target?.FullName ?? fullRoot);
        }
        catch (PlatformNotSupportedException) { return fullRoot; }
    }

    private static string CanonicalFilePath(string path)
    {
        if (!File.Exists(path))
            return Path.GetFullPath(path);
        try
        {
            FileSystemInfo? target = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true);
            return Path.GetFullPath(target?.FullName ?? path);
        }
        catch (PlatformNotSupportedException) { return Path.GetFullPath(path); }
    }

    private static bool IsInside(string rootPath, string candidatePath)
    {
        string relative = Path.GetRelativePath(rootPath, candidatePath);
        return relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private void SaveCatalog()
    {
        string temporary = catalogPath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, catalogPath, overwrite: true);
    }
}
