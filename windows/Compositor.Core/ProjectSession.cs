using System.Text.Json.Nodes;

namespace Compositor.Core;

public sealed record FlatLayerInfo(Guid Id, string Name, bool IsVisible)
{
    public double Opacity { get; init; } = 1;
    public string BlendMode { get; init; } = "Normal";
    public bool HasMask { get; init; }
    public bool MaskEnabled { get; init; }
    public Guid? MaskSourceId { get; init; }
}

public sealed class ProjectSession
{
    public static IReadOnlyList<string> SupportedBlendModes { get; } = Array.AsReadOnly(new[]
    {
        "Normal", "Multiply", "Screen", "Overlay", "Darken", "Lighten", "Difference",
        "Color Dodge", "Color Burn", "Hue", "Saturation", "Color", "Luminosity"
    });
    private const int MaxUndoSteps = 100;
    private const long MaxHistoryImageBytes = 256L * 1024 * 1024;
    private sealed record Snapshot(JsonObject Manifest, IReadOnlyDictionary<Guid, TileRaster>? LayerRasters,
        IReadOnlyDictionary<Guid, GrayTileRaster>? LayerMasks, long Revision);
    private readonly List<Snapshot> snapshots;
    private int cursor;
    private long nextRevision;
    private long savedRevision;
    private IReadOnlyDictionary<Guid, TileRaster>? sourceLayerRasters;
    private IReadOnlyDictionary<Guid, GrayTileRaster>? sourceLayerMasks;

    internal ProjectSession(string? sourceDirectory, JsonObject manifest, string imageName, bool canEdit,
        ReadOnlyMemory<byte> imageHash, IReadOnlyDictionary<string, byte[]>? assetHashes = null)
    {
        SavedDirectory = sourceDirectory;
        var hashes = assetHashes is null
            ? new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, byte[]>(assetHashes, StringComparer.OrdinalIgnoreCase);
        if (canEdit && imageName.Length != 0 && !hashes.ContainsKey(imageName))
            hashes.Add(imageName, imageHash.ToArray());
        AssetHashes = hashes;
        CanEdit = canEdit;
        snapshots = [new Snapshot(manifest, null, null, 0)];
    }

    public string? SavedDirectory { get; private set; }
    public bool HasBeenSaved => SavedDirectory is not null;
    public bool HasTextLayers => Current["layers"]!.AsArray().Any(layer => layer?["text"] is not null);
    public string SourceDirectory => SavedDirectory ?? throw new InvalidOperationException("This document has not been saved yet.");
    public int Width => Current["width"]!.GetValue<int>();
    public int Height => Current["height"]!.GetValue<int>();
    public string ImageName => Current["layers"]!.AsArray().Count == 1
        ? Current["layers"]![0]?["imageFile"]?.GetValue<string>() ?? "" : "";
    internal IReadOnlyDictionary<string, byte[]> AssetHashes { get; private set; }
    public bool CanEdit { get; }
    public bool IsDirty => !HasBeenSaved || snapshots[cursor].Revision != savedRevision;
    public string LayerName => Current["layers"]!.AsArray().Count > 0
        ? Current["layers"]![0]!["name"]!.GetValue<string>()
        : throw new InvalidOperationException("This document has no layers.");
    public Guid? ActiveLayerId => Current["activeLayerID"] is { } active ? Guid.Parse(active.GetValue<string>()) : null;
    public IReadOnlyList<FlatLayerInfo> Layers => Current["layers"]!.AsArray()
        .Select(layer => new FlatLayerInfo(Guid.Parse(layer!["id"]!.GetValue<string>()),
            layer["name"]!.GetValue<string>(), layer["isVisible"]?.GetValue<bool>() ?? true)
        {
            Opacity = layer["opacity"]?.GetValue<double>() ?? 1,
            BlendMode = layer["blendMode"]?.GetValue<string>() ?? "Normal",
            HasMask = layer["maskFile"] is not null,
            MaskEnabled = layer["maskFile"] is not null && (layer["maskEnabled"]?.GetValue<bool>() ?? true),
            MaskSourceId = layer["maskSourceID"] is { } source ? Guid.Parse(source.GetValue<string>()) : null
        }).ToArray();
    public TileRaster? Raster => ImageName.Length != 0 &&
        TryGetLoadedLayerRaster(Guid.Parse(Current["layers"]![0]!["id"]!.GetValue<string>()), out var raster) ? raster : null;
    internal bool RequiresRasterEncoding => ImageName.Length != 0 && TryGetRasterForEncoding(ImageName, out _);
    internal JsonObject Current => snapshots[cursor].Manifest;
    internal IEnumerable<string> CurrentImageNames => Current["layers"]!.AsArray()
        .Select(layer => layer!["imageFile"]!.GetValue<string>());
    internal IEnumerable<string> CurrentMaskNames => Current["layers"]!.AsArray()
        .Where(layer => layer!["maskFile"] is not null)
        .Select(layer => layer!["maskFile"]!.GetValue<string>());
    internal bool TryGetLoadedLayerRaster(Guid layerId, out TileRaster raster)
    {
        if (snapshots[cursor].LayerRasters is { } layers && layers.TryGetValue(layerId, out raster!)) return true;
        raster = null!;
        return false;
    }

    internal bool TryGetLoadedLayerMask(Guid layerId, out GrayTileRaster mask)
    {
        if (snapshots[cursor].LayerMasks is { } masks && masks.TryGetValue(layerId, out mask!)) return true;
        mask = null!;
        return false;
    }

    internal bool TryGetRasterForEncoding(string imageName, out TileRaster raster)
    {
        Guid id = Guid.Parse(Path.GetFileNameWithoutExtension(imageName));
        if (snapshots[cursor].LayerRasters is { } layers && layers.TryGetValue(id, out var current) &&
            (!AssetHashes.ContainsKey(imageName) || sourceLayerRasters is null || !sourceLayerRasters.TryGetValue(id, out var original) ||
                !ReferenceEquals(current, original)))
        {
            raster = current;
            return true;
        }
        raster = null!;
        return false;
    }

    internal bool TryGetMaskForEncoding(string maskName, out GrayTileRaster mask)
    {
        Guid id = Guid.Parse(maskName[..^".mask.png".Length]);
        if (snapshots[cursor].LayerMasks is { } masks && masks.TryGetValue(id, out var current) &&
            (!AssetHashes.ContainsKey(maskName) || sourceLayerMasks is null || !sourceLayerMasks.TryGetValue(id, out var original) ||
                !ReferenceEquals(current, original)))
        {
            mask = current;
            return true;
        }
        mask = null!;
        return false;
    }

    internal long HistoryExclusiveBytes
    {
        get
        {
            var currentBuffers = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
            foreach (byte[] buffer in SnapshotBuffers(snapshots[cursor])) currentBuffers.Add(buffer);
            var historyBuffers = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
            long total = 0;
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (i == cursor) continue;
                foreach (byte[] buffer in SnapshotBuffers(snapshots[i]))
                    if (!currentBuffers.Contains(buffer) && historyBuffers.Add(buffer)) total += buffer.Length;
            }
            return total;
        }
    }

    private static IEnumerable<byte[]> SnapshotBuffers(Snapshot snapshot)
    {
        if (snapshot.LayerRasters is { } layers)
            foreach (TileRaster layer in layers.Values)
            foreach (byte[] buffer in layer.Buffers) yield return buffer;
        if (snapshot.LayerMasks is { } masks)
            foreach (GrayTileRaster mask in masks.Values)
            foreach (byte[] buffer in mask.Buffers) yield return buffer;
    }

    internal void AttachRaster(TileRaster raster, GrayTileRaster? mask = null)
    {
        if (!CanEdit || ImageName.Length == 0 || snapshots.Count != 1 || Raster is not null)
            throw new InvalidOperationException("Raster can only be attached to a newly opened editable project.");
        CheckRasterSize(raster);
        var attached = new Dictionary<Guid, TileRaster>
        {
            [Guid.Parse(Current["layers"]![0]!["id"]!.GetValue<string>())] = raster
        };
        if (mask is not null) CheckMaskSize(mask);
        var masks = mask is null ? null : new Dictionary<Guid, GrayTileRaster>
        {
            [Guid.Parse(Current["layers"]![0]!["id"]!.GetValue<string>())] = mask
        };
        snapshots[0] = new Snapshot(Current, attached, masks, 0);
        sourceLayerRasters = attached;
        sourceLayerMasks = masks;
    }

    internal void AttachLayerRasters(IReadOnlyDictionary<Guid, TileRaster> rasters,
        IReadOnlyDictionary<Guid, GrayTileRaster>? masks = null)
    {
        if (!CanEdit || ImageName.Length != 0 || snapshots.Count != 1 || snapshots[0].LayerRasters is not null ||
            rasters.Count != Layers.Count || Layers.Any(layer => !rasters.ContainsKey(layer.Id)))
            throw new InvalidOperationException("Layer rasters can only be attached to a newly opened flat project.");
        foreach (TileRaster raster in rasters.Values) CheckRasterSize(raster);
        if (masks is not null)
        {
            if (Layers.Any(layer => layer.HasMask != masks.ContainsKey(layer.Id)))
                throw new InvalidOperationException("Loaded layer masks do not match the project manifest.");
            foreach (GrayTileRaster mask in masks.Values) CheckMaskSize(mask);
        }
        var attached = new Dictionary<Guid, TileRaster>(rasters);
        var attachedMasks = masks is null ? null : new Dictionary<Guid, GrayTileRaster>(masks);
        snapshots[0] = new Snapshot(Current, attached, attachedMasks, 0);
        sourceLayerRasters = attached;
        sourceLayerMasks = attachedMasks;
    }

    public TileRaster GetLayerRaster(Guid layerId)
    {
        FindLayer(layerId);
        return TryGetLoadedLayerRaster(layerId, out var raster) ? raster
            : throw new InvalidOperationException("Layer rasters have not been loaded.");
    }

    public GrayTileRaster? GetLayerMask(Guid layerId)
    {
        int index = FindLayer(layerId);
        if (Current["layers"]![index]!["maskFile"] is null) return null;
        return TryGetLoadedLayerMask(layerId, out var mask)
            ? mask : throw new InvalidOperationException("Layer masks have not been loaded.");
    }

    public void EnsureLayerMask(Guid layerId)
    {
        RequireLayerStructureEditing();
        int index = FindLayer(layerId);
        if (Current["layers"]![index]!["maskFile"] is not null) return;
        var next = (JsonObject)Current.DeepClone();
        var layer = next["layers"]![index]!.AsObject();
        layer["maskFile"] = Guid.Parse(layer["id"]!.GetValue<string>()).ToString("D").ToUpperInvariant() + ".mask.png";
        layer["maskEnabled"] = true;
        var masks = new Dictionary<Guid, GrayTileRaster>(snapshots[cursor].LayerMasks ?? new Dictionary<Guid, GrayTileRaster>())
        {
            [layerId] = GrayTileRaster.Rectangle(Width, Height, 0, 0, Width, Height)
        };
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, masks, ++nextRevision));
    }

    public void ReplaceLayerMask(Guid layerId, GrayTileRaster mask)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only.");
        int index = FindLayer(layerId);
        if (Current["layers"]![index]!["maskFile"] is null)
            throw new InvalidOperationException("Layer does not have a raster mask.");
        CheckMaskSize(mask);
        var current = snapshots[cursor].LayerMasks
            ?? throw new InvalidOperationException("Layer masks have not been loaded.");
        if (ReferenceEquals(current[layerId], mask)) return;
        var next = new Dictionary<Guid, GrayTileRaster>(current) { [layerId] = mask };
        Commit(new Snapshot(Current, snapshots[cursor].LayerRasters, next, ++nextRevision));
    }

    public bool IsLayerMaskEnabled(Guid layerId)
    {
        int index = FindLayer(layerId);
        return Current["layers"]![index]!["maskFile"] is not null &&
            (Current["layers"]![index]!["maskEnabled"]?.GetValue<bool>() ?? true);
    }

    public void SetLayerMaskEnabled(Guid layerId, bool enabled)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only.");
        int index = FindLayer(layerId);
        var layer = Current["layers"]![index]!;
        if (layer["maskFile"] is null) throw new InvalidOperationException("Layer does not have a raster mask.");
        if ((layer["maskEnabled"]?.GetValue<bool>() ?? true) == enabled) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["maskEnabled"] = enabled;
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void ReplaceLayerRaster(Guid layerId, TileRaster raster)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        FindLayer(layerId);
        CheckRasterSize(raster);
        var current = snapshots[cursor].LayerRasters
            ?? throw new InvalidOperationException("Layer rasters have not been loaded.");
        if (ReferenceEquals(current[layerId], raster)) return;
        var next = new Dictionary<Guid, TileRaster>(current) { [layerId] = raster };
        Commit(new Snapshot(Current, next, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void ReplaceLayerRasterAndMask(Guid layerId, TileRaster raster, GrayTileRaster mask)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        int index = FindLayer(layerId);
        if (Current["layers"]![index]!["maskFile"] is null)
            throw new InvalidOperationException("Layer does not have a raster mask.");
        CheckRasterSize(raster);
        CheckMaskSize(mask);
        var currentRasters = snapshots[cursor].LayerRasters
            ?? throw new InvalidOperationException("Layer rasters have not been loaded.");
        var currentMasks = snapshots[cursor].LayerMasks
            ?? throw new InvalidOperationException("Layer masks have not been loaded.");
        if (ReferenceEquals(currentRasters[layerId], raster) && ReferenceEquals(currentMasks[layerId], mask)) return;
        var nextRasters = new Dictionary<Guid, TileRaster>(currentRasters) { [layerId] = raster };
        var nextMasks = new Dictionary<Guid, GrayTileRaster>(currentMasks) { [layerId] = mask };
        Commit(new Snapshot(Current, nextRasters, nextMasks, ++nextRevision));
    }

    public void ReplaceRaster(TileRaster raster)
    {
        if (!CanEdit || ImageName.Length == 0)
            throw new NotSupportedException("Pixel replacement requires a single-layer editable project.");
        ReplaceLayerRaster(Guid.Parse(Current["layers"]![0]!["id"]!.GetValue<string>()), raster);
    }

    public void RenameLayer(string name)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        if (Current["layers"]!.AsArray().Count == 0) throw new InvalidOperationException("This document has no layers.");
        RenameLayer(Guid.Parse(Current["layers"]![0]!["id"]!.GetValue<string>()), name);
    }

    public void RenameLayer(Guid layerId, string name)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Layer name cannot be blank.", nameof(name));
        if (name.Length > 1000) throw new ArgumentOutOfRangeException(nameof(name));
        int index = FindLayer(layerId);
        if (Current["layers"]![index]!["name"]!.GetValue<string>() == name) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["name"] = name;
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void SetLayerVisible(Guid layerId, bool visible)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        int index = FindLayer(layerId);
        if (Current["layers"]![index]!["isVisible"]!.GetValue<bool>() == visible) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["isVisible"] = visible;
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void SetLayerOpacity(Guid layerId, double opacity)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only.");
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(opacity));
        int index = FindLayer(layerId);
        if ((Current["layers"]![index]!["opacity"]?.GetValue<double>() ?? 1) == opacity) return;
        if (Current["version"]!.GetValue<int>() != 8) throw new NotSupportedException("Appearance edits require an editable v8 project.");
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["opacity"] = opacity;
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void SetLayerBlendMode(Guid layerId, string mode)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only.");
        if (!SupportedBlendModes.Contains(mode)) throw new ArgumentException("Unknown blend mode.", nameof(mode));
        int index = FindLayer(layerId);
        if ((Current["layers"]![index]!["blendMode"]?.GetValue<string>() ?? "Normal") == mode) return;
        if (Current["version"]!.GetValue<int>() != 8) throw new NotSupportedException("Appearance edits require an editable v8 project.");
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["blendMode"] = mode;
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void MoveLayer(Guid layerId, int destinationIndex)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        var layers = Current["layers"]!.AsArray();
        if ((uint)destinationIndex >= layers.Count) throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        int sourceIndex = FindLayer(layerId);
        if (sourceIndex == destinationIndex) return;
        if (layers.Any(layer => layer!["maskSourceID"] is not null))
            throw new NotSupportedException("Reordering layers with clipping masks is not supported in this slice.");
        var next = (JsonObject)Current.DeepClone();
        var reordered = next["layers"]!.AsArray();
        JsonNode layer = reordered[sourceIndex]!;
        reordered.RemoveAt(sourceIndex);
        reordered.Insert(destinationIndex, layer);
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void SetLayerMaskSource(Guid layerId, Guid? sourceLayerId)
    {
        RequireLayerStructureEditing();
        int targetIndex = FindLayer(layerId);
        var layers = Current["layers"]!.AsArray();
        if (sourceLayerId is { } sourceId)
        {
            if (sourceId == layerId) throw new ArgumentException("A layer cannot clip itself.", nameof(sourceLayerId));
            int sourceIndex = FindLayer(sourceId);
            if (sourceIndex >= targetIndex)
                throw new InvalidOperationException("剪贴源必须位于目标图层下方。");
            if (layers[targetIndex]!["isGroup"]?.GetValue<bool>() == true ||
                layers[sourceIndex]!["isGroup"]?.GetValue<bool>() == true)
                throw new NotSupportedException("组图层不能作为当前剪贴关系。");
        }
        var current = layers[targetIndex]!;
        Guid? existing = current["maskSourceID"] is { } value ? Guid.Parse(value.GetValue<string>()) : null;
        if (existing == sourceLayerId) return;
        var next = (JsonObject)Current.DeepClone();
        var nextLayer = next["layers"]![targetIndex]!.AsObject();
        if (sourceLayerId is { } id) nextLayer["maskSourceID"] = id.ToString("D");
        else nextLayer.Remove("maskSourceID");
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void ResizeDocument(int width, int height, IReadOnlyDictionary<Guid, TileRaster> rasters,
        IReadOnlyDictionary<Guid, GrayTileRaster>? masks = null)
    {
        RequireLayerStructureEditing();
        if (width is < 1 or > 30000 || height is < 1 or > 30000 || (long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (rasters.Count != Layers.Count || rasters.Any(pair => !Layers.Any(layer => layer.Id == pair.Key) ||
                pair.Value.Width != width || pair.Value.Height != height))
            throw new ArgumentException("Resized layer rasters do not match the document.", nameof(rasters));
        if (snapshots[cursor].LayerMasks is { } currentMasks &&
            (masks is null || masks.Count != currentMasks.Count ||
             masks.Any(pair => !currentMasks.ContainsKey(pair.Key) || pair.Value.Width != width || pair.Value.Height != height)))
            throw new ArgumentException("Resized layer masks do not match the document.", nameof(masks));
        if (Current["width"]!.GetValue<int>() == width && Current["height"]!.GetValue<int>() == height &&
            rasters.All(pair => ReferenceEquals(pair.Value, GetLayerRaster(pair.Key)))) return;
        var next = (JsonObject)Current.DeepClone();
        next["width"] = width; next["height"] = height;
        foreach (JsonNode? node in next["layers"]!.AsArray())
        {
            var transform = node!["transform"]?.AsObject();
            if (transform is null) throw new NotSupportedException("Layer transform data is missing.");
            transform["origin"] = new JsonArray(0d, 0d);
            transform["size"] = new JsonArray((double)width, (double)height);
        }
        Commit(new Snapshot(next, new Dictionary<Guid, TileRaster>(rasters),
            masks is null ? null : new Dictionary<Guid, GrayTileRaster>(masks), ++nextRevision));
    }

    public void SelectLayer(Guid layerId)
    {
        FindLayer(layerId);
        if (ActiveLayerId == layerId) return;
        var next = (JsonObject)Current.DeepClone();
        next["activeLayerID"] = layerId.ToString("D");
        snapshots[cursor] = snapshots[cursor] with { Manifest = next };
    }

    public static ProjectSession CreateBlank(int width, int height, double resolution = 72)
    {
        if (width is < 1 or > 30000) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is < 1 or > 30000 || (long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (!double.IsFinite(resolution) || resolution is < 1 or > 9600)
            throw new ArgumentOutOfRangeException(nameof(resolution));
        Guid layerId = Guid.NewGuid();
        var layer = CreateBlankLayer("Layer 1", width, height);
        layer["id"] = layerId.ToString("D");
        layer["imageFile"] = layerId.ToString("D").ToUpperInvariant() + ".png";
        var manifest = new JsonObject
        {
            ["format"] = "com.compositor.project", ["version"] = 8,
            ["documentID"] = Guid.NewGuid().ToString("D"), ["colorSpace"] = "sRGB",
            ["resolution"] = resolution, ["width"] = width, ["height"] = height,
            ["activeLayerID"] = layerId.ToString("D"), ["layers"] = new JsonArray(layer)
        };
        var session = new ProjectSession(null, manifest, "", true, ReadOnlyMemory<byte>.Empty);
        session.AttachRaster(new TileRaster(width, height));
        return session;
    }

    public Guid AddBlankLayer(string name, int destinationIndex)
    {
        RequireLayerStructureEditing();
        int width = Current["width"]!.GetValue<int>(), height = Current["height"]!.GetValue<int>();
        return InsertLayer(CreateBlankLayer(name, width, height), new TileRaster(width, height), destinationIndex);
    }

    private static JsonObject CreateBlankLayer(string name, int width, int height) =>
        new JsonObject
        {
            ["name"] = name, ["isVisible"] = true,
            ["transform"] = new JsonObject
            {
                ["origin"] = new JsonArray(0d, 0d), ["size"] = new JsonArray((double)width, (double)height),
                ["rotation"] = 0d, ["flipX"] = false, ["flipY"] = false, ["sampling"] = "High quality"
            }
        };

    public Guid DuplicateLayer(Guid layerId, string name)
    {
        RequireLayerStructureEditing();
        int index = FindLayer(layerId);
        if (Current["layers"]![index]!["maskFile"] is not null)
            throw new NotSupportedException("Duplicating a masked layer is not supported in this slice.");
        var layer = (JsonObject)Current["layers"]![index]!.DeepClone();
        layer["name"] = name;
        return InsertLayer(layer, GetLayerRaster(layerId), index + 1);
    }

    public void DeleteLayer(Guid layerId)
    {
        RequireLayerStructureEditing();
        int index = FindLayer(layerId);
        if (Current["layers"]!.AsArray().Any(layer => layer!["maskSourceID"]?.GetValue<string>() is { } source &&
            Guid.TryParse(source, out var sourceId) && sourceId == layerId))
            throw new NotSupportedException("Deleting a clipping-mask source is not supported in this slice.");
        var next = (JsonObject)Current.DeepClone();
        var layers = next["layers"]!.AsArray();
        layers.RemoveAt(index);
        if (ActiveLayerId == layerId)
        {
            if (layers.Count == 0) next.Remove("activeLayerID");
            else next["activeLayerID"] = layers[Math.Min(index, layers.Count - 1)]!["id"]!.DeepClone();
        }
        var rasters = new Dictionary<Guid, TileRaster>(snapshots[cursor].LayerRasters!);
        rasters.Remove(layerId);
        var masks = new Dictionary<Guid, GrayTileRaster>(snapshots[cursor].LayerMasks ?? new Dictionary<Guid, GrayTileRaster>());
        masks.Remove(layerId);
        Commit(new Snapshot(next, rasters, masks.Count == 0 ? null : masks, ++nextRevision));
    }

    private Guid InsertLayer(JsonObject layer, TileRaster raster, int destinationIndex)
    {
        string name = layer["name"]!.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 1000)
            throw new ArgumentException("Layer name must contain 1 to 1000 characters.", nameof(layer));
        int count = Current["layers"]!.AsArray().Count;
        if (destinationIndex < 0 || destinationIndex > count)
            throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        if (count >= 10000 || (long)(count + 1) * raster.Width * raster.Height > 100_000_000)
            throw new NotSupportedException("Adding a layer exceeds the flat source pixel limit.");
        Guid id = Guid.NewGuid();
        layer["id"] = id.ToString("D");
        layer["imageFile"] = id.ToString("D").ToUpperInvariant() + ".png";
        var next = (JsonObject)Current.DeepClone();
        next["layers"]!.AsArray().Insert(destinationIndex, layer);
        next["activeLayerID"] = id.ToString("D");
        var rasters = new Dictionary<Guid, TileRaster>(snapshots[cursor].LayerRasters!) { [id] = raster };
        var masks = snapshots[cursor].LayerMasks is { } currentMasks
            ? new Dictionary<Guid, GrayTileRaster>(currentMasks)
            : null;
        Commit(new Snapshot(next, rasters, masks, ++nextRevision));
        return id;
    }

    private void RequireLayerStructureEditing()
    {
        if (!CanEdit || Current["version"]!.GetValue<int>() != 8)
            throw new NotSupportedException("Layer structure changes require a flat editable v8 project.");
        if (snapshots[cursor].LayerRasters is null)
            throw new InvalidOperationException("Open the editable project through ImageProjectWorkflow first.");
    }

    private int FindLayer(Guid layerId)
    {
        var layers = Current["layers"]!.AsArray();
        for (int i = 0; i < layers.Count; i++)
            if (Guid.Parse(layers[i]!["id"]!.GetValue<string>()) == layerId) return i;
        throw new ArgumentException("Layer does not belong to this project.", nameof(layerId));
    }

    private void Commit(Snapshot next)
    {
        snapshots.RemoveRange(cursor + 1, snapshots.Count - cursor - 1);
        snapshots.Add(next);
        cursor++;
        while (cursor > 0 && (snapshots.Count > MaxUndoSteps + 1 || HistoryExclusiveBytes > MaxHistoryImageBytes))
        {
            snapshots.RemoveAt(0);
            cursor--;
        }
        if (sourceLayerRasters is not null)
            sourceLayerRasters = sourceLayerRasters.Where(pair => snapshots.Any(snapshot =>
                snapshot.LayerRasters is { } layers && layers.TryGetValue(pair.Key, out var raster) &&
                ReferenceEquals(raster, pair.Value))).ToDictionary(pair => pair.Key, pair => pair.Value);
        if (sourceLayerMasks is not null)
            sourceLayerMasks = sourceLayerMasks.Where(pair => snapshots.Any(snapshot =>
                snapshot.LayerMasks is { } masks && masks.TryGetValue(pair.Key, out var mask) &&
                ReferenceEquals(mask, pair.Value))).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private void CheckRasterSize(TileRaster raster)
    {
        if (raster.Width != Current["width"]!.GetValue<int>() ||
            raster.Height != Current["height"]!.GetValue<int>())
            throw new ArgumentException("Raster dimensions do not match the canvas.", nameof(raster));
    }

    private void CheckMaskSize(GrayTileRaster mask)
    {
        if (mask.Width != Current["width"]!.GetValue<int>() || mask.Height != Current["height"]!.GetValue<int>())
            throw new ArgumentException("Mask dimensions do not match the canvas.", nameof(mask));
    }

    public bool Undo()
    {
        if (cursor == 0) return false;
        cursor--;
        return true;
    }

    public bool Redo()
    {
        if (cursor + 1 == snapshots.Count) return false;
        cursor++;
        return true;
    }

    internal void MarkSaved(string directory, IReadOnlyDictionary<string, byte[]> assetHashes)
    {
        SavedDirectory = directory;
        AssetHashes = new Dictionary<string, byte[]>(assetHashes, StringComparer.OrdinalIgnoreCase);
        sourceLayerRasters = snapshots[cursor].LayerRasters;
        sourceLayerMasks = snapshots[cursor].LayerMasks;
        savedRevision = snapshots[cursor].Revision;
    }
}
