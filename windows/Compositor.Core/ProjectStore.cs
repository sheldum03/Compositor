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
        if (manifest["resolution"] is { } resolution &&
            (!double.IsFinite(resolution.GetValue<double>()) || resolution.GetValue<double>() is < 1 or > 9600))
            throw new InvalidDataException("Invalid document resolution.");
        var layers = manifest["layers"]?.AsArray() ?? throw new InvalidDataException("Missing layers.");
        if (layers.Count > 10000) throw new InvalidDataException("Too many layers.");
        var ids = new HashSet<Guid>();
        foreach (var node in layers)
        {
            var layer = node?.AsObject() ?? throw new InvalidDataException("Invalid layer.");
            if (!Guid.TryParse(layer["id"]?.GetValue<string>(), out var id) || !ids.Add(id))
                throw new InvalidDataException("Invalid layer ID.");
            string name = layer["name"]?.GetValue<string>() ?? throw new InvalidDataException("Missing layer name.");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 16_384)
                throw new InvalidDataException("Invalid layer name.");
            if (layer["text"] is { } textNode &&
                (version < 8 || layer["isGroup"]?.GetValue<bool>() == true || layer["imageFile"] is null ||
                 textNode is not JsonObject text || !IsValidTextMetadata(text)))
                throw new InvalidDataException("Invalid text layer metadata.");
        }
        if (manifest["activeLayerID"] is { } active &&
            (!Guid.TryParse(active.GetValue<string>(), out var activeId) || !ids.Contains(activeId)))
            throw new InvalidDataException("Invalid active layer.");
        ValidateRelationships(layers);
        if (manifest["groupCoordinateSpace"] is { } groupSpace &&
            groupSpace.GetValue<string>() is not "document")
            throw new InvalidDataException("Invalid group coordinate space.");

        string images = Path.Combine(source, "images");
        CheckPlain(images);
        string imageName = "";
        if (layers.Count == 1) imageName = layers[0]?["imageFile"]?.GetValue<string>() ?? "";
        foreach (var layerNode in layers)
        {
            var layer = layerNode!.AsObject();
            foreach (string key in new[] { "imageFile", "maskFile" })
            {
                if (layer[key] is not { } fileNode) continue;
                string name = fileNode.GetValue<string>();
                if (!IsSafeAssetName(name) || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unsafe asset name.");
                string expected = Guid.Parse(layer["id"]!.GetValue<string>()).ToString("D") +
                    (key == "maskFile" ? ".mask.png" : ".png");
                if (!string.Equals(name, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Asset name does not match its layer ID.");
                string asset = Path.Combine(images, name);
                CheckPlain(asset);
                if (new FileInfo(asset).Length > 512L * 1024 * 1024)
                    throw new InvalidDataException("Asset exceeds 512 MiB.");
                _ = CheckPng(asset, key == "maskFile");
            }
        }
        int imageLayerCount = layers.Count(layer => layer!["imageFile"] is not null);
        int maskCount = layers.Count(layer => layer!["maskFile"] is not null);
        bool hasGroups = layers.Any(layer => layer!["isGroup"]?.GetValue<bool>() == true);
        bool explicitDocumentGroupSpace = manifest["groupCoordinateSpace"]?.GetValue<string>() == "document";
        bool legacyNestedGroupTransforms = hasGroups && !explicitDocumentGroupSpace &&
            layers.Any(layer => layer!["isGroup"]?.GetValue<bool>() == true &&
                !IsIdentityDocumentTransform(layer["transform"]?.AsObject(), width, height));
        bool canEdit = !legacyNestedGroupTransforms &&
            layers.All(layer => IsVersionCompatible(layer!.AsObject(), version)) &&
            (long)imageLayerCount * width * height <= 100_000_000 &&
            manifest.All(pair => new[] { "activeLayerID", "colorSpace", "documentID", "format", "groupCoordinateSpace", "height", "layers", "resolution", "version", "width" }.Contains(pair.Key)) &&
            layers.All(node => IsEditableLayer(node!.AsObject(), width, height, allowGroups: true)) &&
            Directory.GetFiles(images).Length == imageLayerCount + maskCount &&
            Directory.GetDirectories(images).Length == 0;
        var assetHashes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        ReadOnlyMemory<byte> imageHash = default;
        if (canEdit)
        {
            // Editing an older compatible document upgrades only the in-memory
            // manifest. The original package is left untouched until the user
            // explicitly saves, at which point the v8 schema is written.
            if (version != 8) manifest["version"] = 8;
            foreach (var layerNode in layers)
            {
                if (layerNode!["imageFile"] is { } imageNode)
                {
                    string name = imageNode.GetValue<string>();
                    using var stream = File.OpenRead(Path.Combine(images, name));
                    assetHashes.Add(name, SHA256.HashData(stream));
                }
                if (layerNode["maskFile"] is { } maskNode)
                {
                    string maskName = maskNode.GetValue<string>();
                    using var maskStream = File.OpenRead(Path.Combine(images, maskName));
                    assetHashes.Add(maskName, SHA256.HashData(maskStream));
                }
            }
            if (layers.Count == 1 && imageName.Length != 0) imageHash = assetHashes[imageName];
            else imageName = "";
        }
        return new ProjectSession(source, manifest, imageName, canEdit, imageHash, assetHashes, version);
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
        if (session.Layers.Any(layer => !session.IsLayerTransformIdentity(layer.Id)))
            throw new NotSupportedException("Transformed layers require the rendered export path.");
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

    private static bool IsEditableLayer(JsonObject layer, int width, int height, bool allowGroups)
    {
        if (!layer.All(pair => new[] { "adjustment", "blendMode", "gradient", "id", "imageFile", "isGroup", "isVisible", "maskEnabled", "maskFile", "maskLinked", "maskPlacement", "maskSourceID", "name", "opacity", "parentID", "shape", "text", "transform" }.Contains(pair.Key)) ||
            layer["isVisible"] is null || !Guid.TryParse(layer["id"]?.GetValue<string>(), out var id) ||
            !allowGroups && (layer["isGroup"]?.GetValue<bool>() == true || layer["parentID"] is not null) ||
            layer["isGroup"]?.GetValue<bool>() == true && layer["maskSourceID"] is not null ||
            layer["maskEnabled"] is not null && layer["maskFile"] is null ||
            layer["maskLinked"] is not null && layer["maskFile"] is null ||
            layer["maskLinked"] is { } linked && (linked is not JsonValue linkedValue || !linkedValue.TryGetValue<bool>(out _)) ||
            layer["maskPlacement"] is not null && layer["maskFile"] is null ||
            layer["maskPlacement"] is { } placement && (placement is not JsonObject maskPlacement || !IsValidMaskPlacement(maskPlacement)) ||
            layer["maskSourceID"] is { } source && !Guid.TryParse(source.GetValue<string>(), out _) ||
            layer["maskFile"] is { } mask && !string.Equals(mask.GetValue<string>(), id.ToString("D") + ".mask.png", StringComparison.OrdinalIgnoreCase) ||
            layer["opacity"] is { } opacity && (!double.IsFinite(opacity.GetValue<double>()) || opacity.GetValue<double>() is < 0 or > 1) ||
            layer["blendMode"] is { } blend && !ProjectSession.SupportedBlendModes.Contains(blend.GetValue<string>()) ||
            layer["text"] is { } text && !IsValidTextMetadata(text.AsObject()) ||
            layer["shape"] is { } shape && (!ShapeSettings.TryRead(shape, out _) ||
                layer["isGroup"]?.GetValue<bool>() == true || layer["adjustment"] is not null ||
                layer["text"] is not null || layer["gradient"] is not null || layer["imageFile"] is null) ||
            layer["gradient"] is { } gradient && (!GradientSettings.TryRead(gradient, out _) ||
                layer["isGroup"]?.GetValue<bool>() == true || layer["adjustment"] is not null ||
                layer["text"] is not null || layer["shape"] is not null || layer["imageFile"] is null) ||
            layer["adjustment"] is { } adjustment && !IsValidEditableAdjustment(layer, adjustment.AsObject(), width, height) ||
            layer["isGroup"]?.GetValue<bool>() != true && layer["adjustment"] is null &&
            !string.Equals(layer["imageFile"]?.GetValue<string>(), id.ToString("D") + ".png", StringComparison.OrdinalIgnoreCase) ||
            layer["isGroup"]?.GetValue<bool>() == true && (layer["imageFile"] is not null || layer["adjustment"] is not null)) return false;
        var transform = layer["transform"]?.AsObject();
        if (transform is null || !transform.All(pair => new[] { "flipX", "flipY", "origin", "rotation", "sampling", "size" }.Contains(pair.Key))) return false;
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        if (allowGroups)
        {
            return origin?.Count == 2 && size?.Count == 2 &&
                origin.All(value => double.IsFinite(value!.GetValue<double>())) &&
                size.All(value => double.IsFinite(value!.GetValue<double>()) && value.GetValue<double>() > 0) &&
                double.IsFinite(transform["rotation"]?.GetValue<double>() ?? 0);
        }
        return origin?.Count == 2 && size?.Count == 2 &&
            origin[0]!.GetValue<double>() == 0 && origin[1]!.GetValue<double>() == 0 &&
            size[0]!.GetValue<double>() == width && size[1]!.GetValue<double>() == height &&
            transform["rotation"]?.GetValue<double>() == 0 &&
            transform["flipX"]?.GetValue<bool>() == false && transform["flipY"]?.GetValue<bool>() == false;
    }

    private static bool IsVersionCompatible(JsonObject layer, int version)
    {
        bool isGroup = layer["isGroup"]?.GetValue<bool>() == true;
        double opacity = layer["opacity"]?.GetValue<double>() ?? 1;
        string blendMode = layer["blendMode"]?.GetValue<string>() ?? "Normal";

        // Appearance arrived in v3, and groups remain pass-through in every
        // version. Rejecting newer fields here prevents silently changing an
        // older document's meaning while still allowing compatible v1-v7
        // documents to be upgraded to v8 on save.
        if (version < 2 && (layer["parentID"] is not null || isGroup)) return false;
        if (version < 3 && (opacity != 1 || blendMode != "Normal")) return false;
        if (isGroup && (opacity != 1 || blendMode != "Normal")) return false;
        if (version < 4 && (layer["maskFile"] is not null || layer["maskEnabled"] is not null)) return false;
        if (version < 5 && layer["maskSourceID"] is not null) return false;
        if (version < 6 && isGroup && layer["maskFile"] is not null) return false;
        if (version < 7 && layer["adjustment"] is not null) return false;
        if (version < 8 && (layer["text"] is not null || layer["shape"] is not null || layer["gradient"] is not null)) return false;
        return true;
    }

    private static bool IsValidMaskPlacement(JsonObject placement)
    {
        if (!placement.All(pair => new[] { "flipX", "flipY", "origin", "rotation", "sampling", "size" }.Contains(pair.Key)))
            return false;
        var origin = placement["origin"] as JsonArray;
        var size = placement["size"] as JsonArray;
        return origin?.Count == 2 && size?.Count == 2 &&
            origin.All(value => value is JsonValue number && number.TryGetValue<double>(out double n) && double.IsFinite(n) && Math.Abs(n) <= 1000000) &&
            size.All(value => value is JsonValue number && number.TryGetValue<double>(out double n) && double.IsFinite(n) && n is >= 1 and <= 300000) &&
            placement["rotation"] is JsonValue rotation && rotation.TryGetValue<double>(out double angle) && double.IsFinite(angle) &&
            (placement["flipX"] is null || placement["flipX"] is JsonValue flipX && flipX.TryGetValue<bool>(out _)) &&
            (placement["flipY"] is null || placement["flipY"] is JsonValue flipY && flipY.TryGetValue<bool>(out _)) &&
            (placement["sampling"] is null || placement["sampling"] is JsonValue sampling &&
                sampling.TryGetValue<string>(out string? mode) && mode is "Nearest" or "Smooth" or "High quality");
    }

    private static bool IsIdentityDocumentTransform(JsonObject? transform, int width, int height)
    {
        var origin = transform?["origin"]?.AsArray();
        var size = transform?["size"]?.AsArray();
        return origin?.Count == 2 && size?.Count == 2 &&
            origin[0]!.GetValue<double>() == 0 && origin[1]!.GetValue<double>() == 0 &&
            size[0]!.GetValue<double>() == width && size[1]!.GetValue<double>() == height &&
            (transform?["rotation"]?.GetValue<double>() ?? 0) == 0 &&
            (transform?["flipX"]?.GetValue<bool>() ?? false) == false &&
            (transform?["flipY"]?.GetValue<bool>() ?? false) == false;
    }

    private static bool IsValidEditableAdjustment(JsonObject layer, JsonObject adjustment, int width, int height)
    {
        string? kind = adjustment["kind"]?.GetValue<string>();
        bool settingsValid = kind switch
        {
            "Exposure" => ExposureSettings.TryRead(adjustment["exposureSettings"], out _),
            "Levels" => LevelsSettings.TryRead(adjustment["levelsSettings"], out _),
            "Hue/Saturation" => HueSaturationSettings.TryRead(adjustment["hueSaturationSettings"], out _),
            "Curves" => CurvesSettings.TryRead(adjustment["curvesSettings"], out _),
            "Gradient Map" => GradientMapSettings.TryRead(adjustment["gradientMapSettings"], out _),
            "Gaussian Blur" => GaussianBlurSettings.TryRead(adjustment["gaussianBlurSettings"], out _),
            "Motion Blur" => MotionBlurSettings.TryRead(adjustment["motionBlurSettings"], out _),
            "Add Noise" => NoiseSettings.TryRead(adjustment["noiseSettings"], out _),
            "Lens Correction" => LensCorrectionSettings.TryRead(adjustment["lensCorrectionSettings"], out _),
            "Grain" => GrainSettings.TryRead(adjustment["grainSettings"], out _),
            _ => false
        };
        if (!settingsValid ||
            layer["blendMode"]?.GetValue<string>() is { } blend && blend != "Normal" ||
            layer["isGroup"]?.GetValue<bool>() == true || layer["imageFile"] is not null ||
            layer["text"] is not null || layer["maskSourceID"] is not null) return false;
        var transform = layer["transform"]?.AsObject();
        var origin = transform?["origin"]?.AsArray();
        var size = transform?["size"]?.AsArray();
        return origin?.Count == 2 && size?.Count == 2 &&
            origin[0]!.GetValue<double>() == 0 && origin[1]!.GetValue<double>() == 0 &&
            size[0]!.GetValue<double>() == width && size[1]!.GetValue<double>() == height &&
            transform!["rotation"]?.GetValue<double>() == 0 &&
            transform["flipX"]?.GetValue<bool>() == false && transform["flipY"]?.GetValue<bool>() == false;
    }

    private static bool IsValidTextMetadata(JsonObject text)
    {
        if (!text.All(pair => new[] { "alignment", "alpha", "blue", "content", "fontPostScriptName",
                "fontSizePoints", "green", "layout", "lineSpacingPoints", "red", "trackingPoints" }.Contains(pair.Key)) ||
            text["content"]?.GetValue<string>() is not { Length: > 0 } content || content.Length > 1_000_000 ||
            text["fontPostScriptName"]?.GetValue<string>() is not { Length: > 0 } font || font.Length > 1024 ||
            text["alignment"]?.GetValue<string>() is not ("left" or "center" or "right") ||
            !FiniteInRange(text["fontSizePoints"], 1, 2000) ||
            !FiniteInRange(text["red"], 0, 1) || !FiniteInRange(text["green"], 0, 1) ||
            !FiniteInRange(text["blue"], 0, 1) || !FiniteInRange(text["alpha"], 0, 1) ||
            !FiniteInRange(text["lineSpacingPoints"], -2000, 2000) ||
            !FiniteInRange(text["trackingPoints"], -2000, 2000)) return false;
        var layout = text["layout"]?.AsObject();
        if (layout is null || layout.Count != 1) return false;
        if (layout["point"] is not null) return layout["point"] is JsonObject point && point.Count == 0;
        return layout["box"] is JsonObject box && box.Count == 1 && FiniteInRange(box["width"], 1, 30000);
    }

    private static bool FiniteInRange(JsonNode? value, double minimum, double maximum) =>
        value is not null && double.IsFinite(value.GetValue<double>()) &&
        value.GetValue<double>() >= minimum && value.GetValue<double>() <= maximum;

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

    private static bool IsSafeAssetName(string name) =>
        name.Length > 0 && name.IndexOfAny(['/', '\\']) < 0 && Path.GetFileName(name) == name;
}
