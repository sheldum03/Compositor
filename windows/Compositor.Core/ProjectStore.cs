using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Compositor.Core;

public static class ProjectStore
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ProjectSession Open(string directory)
    {
        string source = Path.GetFullPath(directory);
        string backup = source + ".backup";
        if (!Directory.Exists(source) && Directory.Exists(backup))
        {
            _ = OpenProject(backup);
            Directory.Move(backup, source);
        }
        return OpenProject(source);
    }

    private static ProjectSession OpenProject(string source)
    {
        CheckPlain(source);
        string manifestPath = Path.Combine(source, "manifest.json");
        CheckPlain(manifestPath);
        if (new FileInfo(manifestPath).Length > 4 * 1024 * 1024)
            throw new InvalidDataException("Manifest exceeds 4 MiB.");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidDataException("Missing manifest.");
        if (manifest["format"]?.GetValue<string>() != "com.compositor.project" ||
            manifest["colorSpace"]?.GetValue<string>() != "sRGB")
            throw new InvalidDataException("Invalid project format.");
        int version = manifest["version"]?.GetValue<int>() ?? 0;
        if (version is < 1 or > 8) throw new NotSupportedException("Unsupported project version.");
        int width = manifest["width"]?.GetValue<int>() ?? 0;
        int height = manifest["height"]?.GetValue<int>() ?? 0;
        if (width is < 1 or > 30000 || height is < 1 or > 30000 || (long)width * height > 100_000_000)
            throw new InvalidDataException("Invalid canvas size.");
        if (!Guid.TryParse(manifest["documentID"]?.GetValue<string>(), out _))
            throw new InvalidDataException("Invalid document ID.");
        var layers = manifest["layers"]?.AsArray() ?? throw new InvalidDataException("Missing layers.");
        if (layers.Count > 10000) throw new InvalidDataException("Too many layers.");
        var ids = new HashSet<Guid>();
        foreach (var node in layers)
        {
            var layer = node?.AsObject() ?? throw new InvalidDataException("Invalid layer.");
            if (!Guid.TryParse(layer["id"]?.GetValue<string>(), out var id) || !ids.Add(id))
                throw new InvalidDataException("Invalid layer ID.");
            _ = layer["name"]?.GetValue<string>() ?? throw new InvalidDataException("Missing layer name.");
        }
        if (manifest["activeLayerID"] is { } active &&
            (!Guid.TryParse(active.GetValue<string>(), out var activeId) || !ids.Contains(activeId)))
            throw new InvalidDataException("Invalid active layer.");

        string images = Path.Combine(source, "images");
        CheckPlain(images);
        string imageName = "";
        (uint Width, uint Height) imageSize = default;
        if (layers.Count == 1) imageName = layers[0]?["imageFile"]?.GetValue<string>() ?? "";
        foreach (var layerNode in layers)
        {
            var layer = layerNode!.AsObject();
            foreach (string key in new[] { "imageFile", "maskFile" })
            {
                if (layer[key] is not { } fileNode) continue;
                string name = fileNode.GetValue<string>();
                if (Path.GetFileName(name) != name || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unsafe asset name.");
                string asset = Path.Combine(images, name);
                CheckPlain(asset);
                if (new FileInfo(asset).Length > 512L * 1024 * 1024)
                    throw new InvalidDataException("Asset exceeds 512 MiB.");
                var dimensions = CheckPng(asset);
                if (layers.Count == 1 && key == "imageFile") imageSize = dimensions;
            }
        }
        bool canEdit = version == 1 && layers.Count == 1 &&
            manifest.All(pair => new[] { "activeLayerID", "colorSpace", "documentID", "format", "height", "layers", "resolution", "version", "width" }.Contains(pair.Key)) &&
            IsSimpleLayer(layers[0]!.AsObject(), width, height, imageName) &&
            imageSize == ((uint)width, (uint)height) &&
            Directory.GetFiles(images).Length == 1 && Directory.GetDirectories(images).Length == 0;
        ReadOnlyMemory<byte> imageHash = default;
        if (canEdit)
        {
            using var stream = File.OpenRead(Path.Combine(images, imageName));
            imageHash = SHA256.HashData(stream);
        }
        return new ProjectSession(source, manifest, imageName, canEdit, imageHash);
    }

    public static void Save(ProjectSession session, string directory) =>
        Save(session, directory, path => Directory.Delete(path, recursive: true));

    internal static void Save(ProjectSession session, string directory, Action<string> deleteBackup)
    {
        if (!session.CanEdit) throw new NotSupportedException("This project cannot be edited yet.");
        string destination = Path.GetFullPath(directory);
        string backup = destination + ".backup";
        if (Directory.Exists(backup) || File.Exists(backup))
            throw new IOException("A previous save backup exists; inspect it before saving.");
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        bool movedOld = false;
        bool committed = false;
        try
        {
            Directory.CreateDirectory(Path.Combine(temporary, "images"));
            string copiedImage = Path.Combine(temporary, "images", session.ImageName);
            File.Copy(Path.Combine(session.SourceDirectory, "images", session.ImageName), copiedImage);
            CheckImageHash(session, copiedImage);
            File.WriteAllText(Path.Combine(temporary, "manifest.json"), session.Current.ToJsonString(JsonOptions));
            if (!Open(temporary).CanEdit) throw new InvalidDataException("Saved project failed validation.");
            if (Directory.Exists(destination))
            {
                Directory.Move(destination, backup);
                movedOld = true;
            }
            Directory.Move(temporary, destination);
            if (!Open(destination).CanEdit) throw new InvalidDataException("Final project failed validation.");
            committed = true;
            session.MarkSaved(destination);
            if (movedOld) deleteBackup(backup);
        }
        catch
        {
            if (!committed && movedOld && Directory.Exists(backup))
            {
                if (Directory.Exists(destination))
                    Directory.Move(destination, destination + ".failed-" + Guid.NewGuid().ToString("N"));
                Directory.Move(backup, destination);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    public static void ExportPng(ProjectSession session, string output)
    {
        if (!session.CanEdit) throw new NotSupportedException("This project cannot be exported yet.");
        string temporary = Path.GetFullPath(output) + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(Path.Combine(session.SourceDirectory, "images", session.ImageName), temporary);
            CheckImageHash(session, temporary);
            File.Move(temporary, output);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void CheckImageHash(ProjectSession session, string image)
    {
        using var stream = File.OpenRead(image);
        if (!SHA256.HashData(stream).AsSpan().SequenceEqual(session.ImageHash.Span))
            throw new IOException("Source image changed after opening the project.");
    }

    private static bool IsSimpleLayer(JsonObject layer, int width, int height, string imageName)
    {
        if (!layer.All(pair => new[] { "id", "name", "isVisible", "imageFile", "transform" }.Contains(pair.Key)) ||
            layer["isVisible"]?.GetValue<bool>() != true || !Guid.TryParse(layer["id"]?.GetValue<string>(), out var id) ||
            !string.Equals(imageName, id.ToString("D") + ".png", StringComparison.OrdinalIgnoreCase)) return false;
        var transform = layer["transform"]?.AsObject();
        if (transform is null || !transform.All(pair => new[] { "flipX", "flipY", "origin", "rotation", "sampling", "size" }.Contains(pair.Key))) return false;
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        return origin?.Count == 2 && size?.Count == 2 &&
            origin[0]!.GetValue<double>() == 0 && origin[1]!.GetValue<double>() == 0 &&
            size[0]!.GetValue<double>() == width && size[1]!.GetValue<double>() == height &&
            transform["rotation"]?.GetValue<double>() == 0 &&
            transform["flipX"]?.GetValue<bool>() == false && transform["flipY"]?.GetValue<bool>() == false;
    }

    private static (uint Width, uint Height) CheckPng(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        if (stream.Read(header) != header.Length || !header[..8].SequenceEqual(PngSignature) ||
            !header[12..16].SequenceEqual("IHDR"u8)) throw new InvalidDataException("Invalid PNG header.");
        uint width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        if (width == 0 || height == 0 || width > 30000 || height > 30000 || (ulong)width * height > 100_000_000)
            throw new InvalidDataException("Invalid PNG dimensions.");
        return (width, height);
    }

    private static void CheckPlain(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Links are not allowed inside a project.");
    }
}
