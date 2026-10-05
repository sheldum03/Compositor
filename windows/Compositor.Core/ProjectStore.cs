using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Compositor.Core;

internal enum SaveStage { Prepared, OldMoved, NewMoved }

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
            string name = layer["name"]?.GetValue<string>() ?? throw new InvalidDataException("Missing layer name.");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Blank layer name.");
        }
        if (manifest["activeLayerID"] is { } active &&
            (!Guid.TryParse(active.GetValue<string>(), out var activeId) || !ids.Contains(activeId)))
            throw new InvalidDataException("Invalid active layer.");
        ValidateRelationships(layers);

        string images = Path.Combine(source, "images");
        CheckPlain(images);
        string imageName = "";
        if (layers.Count == 1) imageName = layers[0]?["imageFile"]?.GetValue<string>() ?? "";
        bool allImageSizesMatch = true;
        bool allMaskSizesMatch = true;
        foreach (var layerNode in layers)
        {
            var layer = layerNode!.AsObject();
            foreach (string key in new[] { "imageFile", "maskFile" })
            {
                if (layer[key] is not { } fileNode) continue;
                string name = fileNode.GetValue<string>();
                if (Path.GetFileName(name) != name || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unsafe asset name.");
                string expected = Guid.Parse(layer["id"]!.GetValue<string>()).ToString("D") +
                    (key == "maskFile" ? ".mask.png" : ".png");
                if (!string.Equals(name, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Asset name does not match its layer ID.");
                string asset = Path.Combine(images, name);
                CheckPlain(asset);
                if (new FileInfo(asset).Length > 512L * 1024 * 1024)
                    throw new InvalidDataException("Asset exceeds 512 MiB.");
                var dimensions = CheckPng(asset, key == "maskFile");
                if (dimensions != ((uint)width, (uint)height))
                {
                    if (key == "imageFile") allImageSizesMatch = false;
                    else allMaskSizesMatch = false;
                }
            }
        }
        bool canEdit = version is 1 or 8 && (version == 8 || layers.Count == 1) &&
            (version == 8 || layers.All(layer => layer!["maskFile"] is null)) &&
            (version == 8 || layers.All(layer => (layer!["opacity"]?.GetValue<double>() ?? 1) == 1 &&
                (layer["blendMode"]?.GetValue<string>() ?? "Normal") == "Normal")) &&
            (long)layers.Count * width * height <= 100_000_000 &&
            manifest.All(pair => new[] { "activeLayerID", "colorSpace", "documentID", "format", "height", "layers", "resolution", "version", "width" }.Contains(pair.Key)) &&
            layers.All(node => IsFlatEditableLayer(node!.AsObject(), width, height)) &&
            allImageSizesMatch && allMaskSizesMatch &&
            Directory.GetFiles(images).Length == layers.Count + layers.Count(node => node!["maskFile"] is not null) &&
            Directory.GetDirectories(images).Length == 0;
        var assetHashes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        ReadOnlyMemory<byte> imageHash = default;
        if (canEdit)
        {
            foreach (var layerNode in layers)
            {
                string name = layerNode!["imageFile"]!.GetValue<string>();
                using var stream = File.OpenRead(Path.Combine(images, name));
                assetHashes.Add(name, SHA256.HashData(stream));
                if (layerNode["maskFile"] is { } maskNode)
                {
                    string maskName = maskNode.GetValue<string>();
                    using var maskStream = File.OpenRead(Path.Combine(images, maskName));
                    assetHashes.Add(maskName, SHA256.HashData(maskStream));
                }
            }
            if (layers.Count == 1) imageHash = assetHashes[imageName];
            else imageName = "";
        }
        return new ProjectSession(source, manifest, imageName, canEdit, imageHash, assetHashes);
    }

    public static void Save(ProjectSession session, string directory) =>
        Save(session, directory, path => Directory.Delete(path, recursive: true));

    internal static void Save(ProjectSession session, string directory, Action<TileRaster, string> encodeRaster) =>
        Save(session, directory, path => Directory.Delete(path, recursive: true), encodeRaster: encodeRaster);

    internal static void Save(ProjectSession session, string directory, Action<TileRaster, string> encodeRaster,
        Action<GrayTileRaster, string> encodeMask) =>
        Save(session, directory, path => Directory.Delete(path, recursive: true), encodeRaster: encodeRaster, encodeMask: encodeMask);

    public static void SaveNew(ProjectSession session, string directory) =>
        Save(session, directory, path => Directory.Delete(path, recursive: true), requireNew: true);

    internal static void Save(ProjectSession session, string directory, Action<string> deleteBackup,
        Action<SaveStage>? afterStage = null, bool requireNew = false,
        Action<TileRaster, string>? encodeRaster = null,
        Action<GrayTileRaster, string>? encodeMask = null)
    {
        if (!session.CanEdit) throw new NotSupportedException("This project cannot be edited yet.");
        requireNew |= !session.HasBeenSaved;
        string destination = Path.GetFullPath(directory);
        if (requireNew && (Directory.Exists(destination) || File.Exists(destination)))
            throw new IOException("Destination already exists.");
        string backup = destination + ".backup";
        if (Directory.Exists(backup) || File.Exists(backup))
            throw new IOException("A previous save backup exists; inspect it before saving.");
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        bool movedOld = false;
        bool movedNew = false;
        bool committed = false;
        try
        {
            Directory.CreateDirectory(Path.Combine(temporary, "images"));
            foreach (string name in session.CurrentImageNames)
            {
                string copiedImage = Path.Combine(temporary, "images", name);
                string? sourceImage = session.AssetHashes.ContainsKey(name)
                    ? Path.Combine(session.SourceDirectory, "images", name) : null;
                if (session.TryGetRasterForEncoding(name, out TileRaster raster))
                {
                    if (encodeRaster is null) throw new NotSupportedException("Raster encoder is required for pixel edits.");
                    if (sourceImage is not null) CheckAssetHash(session, name, sourceImage);
                    encodeRaster(raster, copiedImage);
                    if (sourceImage is not null) CheckAssetHash(session, name, sourceImage);
                }
                else
                {
                    File.Copy(sourceImage ?? throw new InvalidOperationException("Unsaved layer pixels have not been loaded."), copiedImage);
                    CheckAssetHash(session, name, copiedImage);
                }
            }
            foreach (string name in session.CurrentMaskNames)
            {
                string copiedMask = Path.Combine(temporary, "images", name);
                string? sourceMask = session.AssetHashes.ContainsKey(name)
                    ? Path.Combine(session.SourceDirectory, "images", name) : null;
                if (session.TryGetMaskForEncoding(name, out GrayTileRaster mask))
                {
                    if (encodeMask is null) throw new NotSupportedException("Mask encoder is required for mask edits.");
                    if (sourceMask is not null) CheckAssetHash(session, name, sourceMask);
                    encodeMask(mask, copiedMask);
                    if (sourceMask is not null) CheckAssetHash(session, name, sourceMask);
                }
                else
                {
                    File.Copy(sourceMask ?? throw new InvalidOperationException("Unsaved layer masks have not been loaded."), copiedMask);
                    CheckAssetHash(session, name, copiedMask);
                }
            }
            File.WriteAllText(Path.Combine(temporary, "manifest.json"), session.Current.ToJsonString(JsonOptions));
            if (!Open(temporary).CanEdit) throw new InvalidDataException("Saved project failed validation.");
            afterStage?.Invoke(SaveStage.Prepared);
            if (Directory.Exists(destination))
            {
                if (requireNew) throw new IOException("Destination already exists.");
                Directory.Move(destination, backup);
                movedOld = true;
                afterStage?.Invoke(SaveStage.OldMoved);
            }
            Directory.Move(temporary, destination);
            movedNew = true;
            afterStage?.Invoke(SaveStage.NewMoved);
            var verified = Open(destination);
            if (!verified.CanEdit) throw new InvalidDataException("Final project failed validation.");
            committed = true;
            session.MarkSaved(destination, verified.AssetHashes);
            if (movedOld) deleteBackup(backup);
        }
        catch
        {
            if (!committed)
            {
                if (movedNew && Directory.Exists(destination))
                    Directory.Move(destination, destination + ".failed-" + Guid.NewGuid().ToString("N"));
                if (movedOld && Directory.Exists(backup)) Directory.Move(backup, destination);
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
        if (!session.CanEdit || session.ImageName.Length == 0)
            throw new NotSupportedException("Only single-layer projects can copy their image as an export.");
        if (session.RequiresRasterEncoding)
            throw new NotSupportedException("Pixel edits require the image encoder for export.");
        string temporary = Path.GetFullPath(output) + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(Path.Combine(session.SourceDirectory, "images", session.ImageName), temporary);
            CheckAssetHash(session, session.ImageName, temporary);
            File.Move(temporary, output);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static void CheckAssetHash(ProjectSession session, string name, string image)
    {
        using var stream = File.OpenRead(image);
        if (!SHA256.HashData(stream).AsSpan().SequenceEqual(session.AssetHashes[name]))
            throw new IOException("Source image changed after opening the project.");
    }

    private static void ValidateRelationships(JsonArray layers)
    {
        var byId = layers.Select(node => node!.AsObject())
            .ToDictionary(layer => Guid.Parse(layer["id"]!.GetValue<string>()));
        var order = layers.Select(node => Guid.Parse(node!["id"]!.GetValue<string>()))
            .Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
        foreach (var (id, layer) in byId)
        {
            if (layer["isGroup"]?.GetValue<bool>() == true && layer["imageFile"] is not null)
                throw new InvalidDataException("Group cannot have an image asset.");
            var seenParents = new HashSet<Guid> { id };
            for (Guid? parent = OptionalId(layer, "parentID"); parent is { } parentId;)
            {
                if (!seenParents.Add(parentId) || seenParents.Count > 64 ||
                    !byId.TryGetValue(parentId, out var parentLayer) ||
                    parentLayer["isGroup"]?.GetValue<bool>() != true)
                    throw new InvalidDataException("Invalid layer hierarchy.");
                parent = OptionalId(parentLayer, "parentID");
            }

            var seenMasks = new HashSet<Guid>();
            for (Guid? current = id; current is { } currentId;)
            {
                if (!seenMasks.Add(currentId) || seenMasks.Count > 256)
                    throw new InvalidDataException("Invalid mask source cycle.");
                var currentLayer = byId[currentId];
                Guid? source = OptionalId(currentLayer, "maskSourceID");
                if (source is null) break;
                if (currentLayer["isGroup"]?.GetValue<bool>() == true ||
                    !byId.TryGetValue(source.Value, out var sourceLayer) ||
                    sourceLayer["isGroup"]?.GetValue<bool>() == true ||
                    order[source.Value] >= order[id] ||
                    sourceLayer["adjustment"] is not null)
                    throw new InvalidDataException("Invalid mask source.");
                current = source;
            }
        }
    }

    private static Guid? OptionalId(JsonObject layer, string key)
    {
        if (layer[key] is not { } value) return null;
        if (!Guid.TryParse(value.GetValue<string>(), out var id))
            throw new InvalidDataException($"Invalid {key}.");
        return id;
    }

    private static bool IsFlatEditableLayer(JsonObject layer, int width, int height)
    {
        if (!layer.All(pair => new[] { "blendMode", "id", "imageFile", "isGroup", "isVisible", "maskEnabled", "maskFile", "maskSourceID", "name", "opacity", "transform" }.Contains(pair.Key)) ||
            layer["isVisible"] is null || !Guid.TryParse(layer["id"]?.GetValue<string>(), out var id) ||
            layer["isGroup"] is { } group && group.GetValue<bool>() ||
            layer["maskEnabled"] is not null && layer["maskFile"] is null ||
            layer["maskSourceID"] is { } source && !Guid.TryParse(source.GetValue<string>(), out _) ||
            layer["maskFile"] is { } mask && !string.Equals(mask.GetValue<string>(), id.ToString("D") + ".mask.png", StringComparison.OrdinalIgnoreCase) ||
            layer["opacity"] is { } opacity && (!double.IsFinite(opacity.GetValue<double>()) || opacity.GetValue<double>() is < 0 or > 1) ||
            layer["blendMode"] is { } blend && !ProjectSession.SupportedBlendModes.Contains(blend.GetValue<string>()) ||
            !string.Equals(layer["imageFile"]?.GetValue<string>(), id.ToString("D") + ".png", StringComparison.OrdinalIgnoreCase)) return false;
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

    private static (uint Width, uint Height) CheckPng(string path, bool grayMask = false)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[26];
        if (stream.Read(header) != header.Length || !header[..8].SequenceEqual(PngSignature) ||
            !header[12..16].SequenceEqual("IHDR"u8)) throw new InvalidDataException("Invalid PNG header.");
        if (grayMask && (header[24] != 8 || header[25] != 0))
            throw new InvalidDataException("Mask must be an 8-bit grayscale PNG without alpha.");
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
