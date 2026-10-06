using System.Text.Json.Nodes;

namespace Compositor.Core;

public sealed record FlatLayerInfo(Guid Id, string Name, bool IsVisible)
{
    public bool IsGroup { get; init; }
    public Guid? ParentId { get; init; }
    public double Opacity { get; init; } = 1;
    public string BlendMode { get; init; } = "Normal";
    public bool HasMask { get; init; }
    public bool MaskEnabled { get; init; }
    public Guid? MaskSourceId { get; init; }
}

public sealed record LayerTransformInfo(double X, double Y, double Width, double Height,
    double Rotation, bool FlipX, bool FlipY);

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
    public bool HasGroups => Layers.Any(layer => layer.IsGroup);
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
            IsGroup = layer["isGroup"]?.GetValue<bool>() ?? false,
            ParentId = layer["parentID"] is { } parent ? Guid.Parse(parent.GetValue<string>()) : null,
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
        .Where(layer => layer!["imageFile"] is not null)
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
            rasters.Count != Layers.Count(layer => !layer.IsGroup) || Layers.Where(layer => !layer.IsGroup).Any(layer => !rasters.ContainsKey(layer.Id)))
            throw new InvalidOperationException("Layer rasters can only be attached to a newly opened editable project.");
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
        int index = FindLayer(layerId);
        if (Current["layers"]![index]!["isGroup"]?.GetValue<bool>() == true)
            throw new InvalidOperationException("Group layers do not have a raster asset.");
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
        RequireMaskEditing();
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

    public bool CanMoveLayer(Guid layerId, int destinationIndex)
    {
        if (!CanEdit || Current["version"]!.GetValue<int>() != 8 || HasGroups ||
            snapshots[cursor].LayerRasters is null) return false;
        try { _ = ValidateLayerMove(layerId, destinationIndex); return true; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException or NotSupportedException)
        { return false; }
    }

    public void MoveLayer(Guid layerId, int destinationIndex)
    {
        RequireLayerStructureEditing();
        var plan = ValidateLayerMove(layerId, destinationIndex);
        if (plan.SourceIndex == destinationIndex) return;
        var next = (JsonObject)Current.DeepClone();
        var reordered = next["layers"]!.AsArray();
        var stackNodes = plan.StackIndexes.Select(index => reordered[index]!.DeepClone()).ToArray();
        foreach (int index in plan.StackIndexes.Reverse()) reordered.RemoveAt(index);
        int insertion = plan.Insertion;
        foreach (JsonNode? node in stackNodes) reordered.Insert(insertion++, node);
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public bool CanMoveLayerTo(Guid layerId, int destinationIndex)
    {
        if (!CanEdit || Current["version"]!.GetValue<int>() != 8 || HasGroups ||
            snapshots[cursor].LayerRasters is null) return false;
        try { _ = ValidateLayerMoveTo(layerId, destinationIndex); return true; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        { return false; }
    }

    public void MoveLayerTo(Guid layerId, int destinationIndex)
    {
        RequireLayerStructureEditing();
        var plan = ValidateLayerMoveTo(layerId, destinationIndex);
        if (destinationIndex >= plan.StackIndexes[0] && destinationIndex <= plan.StackIndexes[^1]) return;
        var next = (JsonObject)Current.DeepClone();
        var reordered = next["layers"]!.AsArray();
        var stackNodes = plan.StackIndexes.Select(index => reordered[index]!.DeepClone()).ToArray();
        foreach (int index in plan.StackIndexes.Reverse()) reordered.RemoveAt(index);
        int insertion = plan.Insertion;
        foreach (JsonNode? node in stackNodes) reordered.Insert(insertion++, node);
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    private (int SourceIndex, int[] StackIndexes, int Insertion) ValidateLayerMove(Guid layerId, int destinationIndex)
    {
        var layers = Current["layers"]!.AsArray();
        if ((uint)destinationIndex >= layers.Count) throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        int sourceIndex = FindLayer(layerId);
        if (sourceIndex == destinationIndex) return (sourceIndex, [sourceIndex], sourceIndex);
        var stackIds = new HashSet<Guid> { layerId };
        bool changed;
        do
        {
            changed = false;
            foreach (FlatLayerInfo layer in Layers)
            {
                if (layer.MaskSourceId is { } sourceId && stackIds.Contains(sourceId) && stackIds.Add(layer.Id))
                    changed = true;
                if (stackIds.Contains(layer.Id) && layer.MaskSourceId is { } parentId && stackIds.Add(parentId))
                    changed = true;
            }
        } while (changed);
        int[] stackIndexes = stackIds.Select(FindLayer).OrderBy(index => index).ToArray();
        if (stackIndexes[^1] - stackIndexes[0] + 1 != stackIndexes.Length)
            throw new NotSupportedException("剪贴栈必须保持连续才能移动。");
        int direction = Math.Sign(destinationIndex - sourceIndex);
        int insertion = direction > 0 ? stackIndexes[0] + 1 : stackIndexes[0] - 1;
        int remainingCount = layers.Count - stackIndexes.Length;
        if (insertion < 0 || insertion > remainingCount)
            throw new NotSupportedException("剪贴栈不能移出画布边界。");
        return (sourceIndex, stackIndexes, insertion);
    }

    private (int SourceIndex, int[] StackIndexes, int Insertion) ValidateLayerMoveTo(Guid layerId, int destinationIndex)
    {
        var layers = Current["layers"]!.AsArray();
        if ((uint)destinationIndex >= layers.Count) throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        int sourceIndex = FindLayer(layerId);
        var stackIds = new HashSet<Guid> { layerId };
        bool changed;
        do
        {
            changed = false;
            foreach (FlatLayerInfo layer in Layers)
            {
                if (layer.MaskSourceId is { } sourceId && stackIds.Contains(sourceId) && stackIds.Add(layer.Id))
                    changed = true;
                if (stackIds.Contains(layer.Id) && layer.MaskSourceId is { } parentId && stackIds.Add(parentId))
                    changed = true;
            }
        } while (changed);
        int[] stackIndexes = stackIds.Select(FindLayer).OrderBy(index => index).ToArray();
        if (stackIndexes[^1] - stackIndexes[0] + 1 != stackIndexes.Length)
            throw new NotSupportedException("剪贴栈必须保持连续才能移动。");
        if (destinationIndex >= stackIndexes[0] && destinationIndex <= stackIndexes[^1])
            return (sourceIndex, stackIndexes, stackIndexes[0]);
        int insertion = destinationIndex < stackIndexes[0]
            ? destinationIndex
            : destinationIndex - stackIndexes.Length + 1;
        int remainingCount = layers.Count - stackIndexes.Length;
        if (insertion < 0 || insertion > remainingCount)
            throw new NotSupportedException("剪贴栈不能移出画布边界。");
        return (sourceIndex, stackIndexes, insertion);
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
        return AddRasterLayer(name, new TileRaster(width, height), destinationIndex);
    }

    public Guid AddRasterLayer(string name, TileRaster raster, int destinationIndex)
    {
        RequireLayerStructureEditing();
        CheckRasterSize(raster);
        return InsertLayer(CreateBlankLayer(name, Width, Height), raster, destinationIndex);
    }

    public bool CanCopyLayerFrom(ProjectSession source, Guid sourceLayerId, int destinationIndex)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            ValidateLayerCopyFrom(source, sourceLayerId, destinationIndex);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            return false;
        }
    }

    public Guid CopyLayerFrom(ProjectSession source, Guid sourceLayerId, int destinationIndex)
    {
        ArgumentNullException.ThrowIfNull(source);
        RequireLayerStructureEditing();
        int[] sourceIndexes = ValidateLayerCopyFrom(source, sourceLayerId, destinationIndex);
        return InsertLayerStack(source, sourceIndexes, sourceLayerId, destinationIndex);
    }

    public Guid GroupLayer(Guid layerId, string name) => GroupLayers([layerId], name);

    public void FlipGroup(Guid groupId, bool horizontal)
    {
        RequireGroupStructureEditing();
        int index = FindLayer(groupId);
        var current = Current["layers"]![index]!.AsObject();
        if (current["isGroup"]?.GetValue<bool>() != true)
            throw new ArgumentException("Layer is not a group.", nameof(groupId));
        var next = (JsonObject)Current.DeepClone();
        var transform = next["layers"]![index]!["transform"]?.AsObject()
            ?? throw new InvalidDataException("Group transform data is missing.");
        string field = horizontal ? "flipX" : "flipY";
        transform[field] = !(transform[field]?.GetValue<bool>() ?? false);
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void ScaleGroup(Guid groupId, double factor)
    {
        RequireGroupStructureEditing();
        if (!double.IsFinite(factor) || factor is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(factor));
        int index = FindLayer(groupId);
        var layer = Current["layers"]![index]!.AsObject();
        if (layer["isGroup"]?.GetValue<bool>() != true)
            throw new ArgumentException("Layer is not a group.", nameof(groupId));
        var transform = layer["transform"]?.AsObject()
            ?? throw new InvalidDataException("Group transform data is missing.");
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        if (origin?.Count != 2 || size?.Count != 2)
            throw new InvalidDataException("Group transform data is invalid.");
        double x = origin[0]!.GetValue<double>(), y = origin[1]!.GetValue<double>();
        double width = size[0]!.GetValue<double>(), height = size[1]!.GetValue<double>();
        double nextWidth = width * factor, nextHeight = height * factor;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) ||
            !double.IsFinite(nextWidth) || !double.IsFinite(nextHeight) || width <= 0 || height <= 0 ||
            nextWidth <= 0 || nextHeight <= 0)
            throw new InvalidDataException("Group transform data is invalid.");
        SetGroupTransform(groupId, x + (width - nextWidth) / 2, y + (height - nextHeight) / 2,
            nextWidth, nextHeight, transform["rotation"]?.GetValue<double>() ?? 0);
    }

    public void MoveGroup(Guid groupId, double offsetX, double offsetY)
    {
        RequireGroupStructureEditing();
        if (!double.IsFinite(offsetX) || !double.IsFinite(offsetY))
            throw new ArgumentOutOfRangeException(nameof(offsetX));
        int index = FindLayer(groupId);
        var layer = Current["layers"]![index]!.AsObject();
        if (layer["isGroup"]?.GetValue<bool>() != true)
            throw new ArgumentException("Layer is not a group.", nameof(groupId));
        var transform = layer["transform"]?.AsObject()
            ?? throw new InvalidDataException("Group transform data is missing.");
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        if (origin?.Count != 2 || size?.Count != 2)
            throw new InvalidDataException("Group transform data is invalid.");
        SetGroupTransform(groupId,
            origin[0]!.GetValue<double>() + offsetX,
            origin[1]!.GetValue<double>() + offsetY,
            size[0]!.GetValue<double>(), size[1]!.GetValue<double>(),
            transform["rotation"]?.GetValue<double>() ?? 0);
    }

    public void FlipLayerTransform(Guid layerId, bool horizontal)
    {
        RequireLayerStructureEditing();
        int index = FindLayer(layerId);
        var current = Current["layers"]![index]!.AsObject();
        if (current["isGroup"]?.GetValue<bool>() == true)
            throw new ArgumentException("Use group transform for group layers.", nameof(layerId));
        var next = (JsonObject)Current.DeepClone();
        var transform = next["layers"]![index]!["transform"]?.AsObject()
            ?? throw new InvalidDataException("Layer transform data is missing.");
        string field = horizontal ? "flipX" : "flipY";
        transform[field] = !(transform[field]?.GetValue<bool>() ?? false);
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void ScaleLayerTransform(Guid layerId, double factor)
    {
        RequireLayerStructureEditing();
        if (!double.IsFinite(factor) || factor is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(factor));
        int index = FindLayer(layerId);
        var layer = Current["layers"]![index]!.AsObject();
        if (layer["isGroup"]?.GetValue<bool>() == true)
            throw new ArgumentException("Use group transform for group layers.", nameof(layerId));
        var transform = layer["transform"]?.AsObject()
            ?? throw new InvalidDataException("Layer transform data is missing.");
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        if (origin?.Count != 2 || size?.Count != 2)
            throw new InvalidDataException("Layer transform data is invalid.");
        double x = origin[0]!.GetValue<double>(), y = origin[1]!.GetValue<double>();
        double width = size[0]!.GetValue<double>(), height = size[1]!.GetValue<double>();
        double nextWidth = width * factor, nextHeight = height * factor;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) ||
            !double.IsFinite(nextWidth) || !double.IsFinite(nextHeight) || width <= 0 || height <= 0 ||
            nextWidth <= 0 || nextHeight <= 0)
            throw new InvalidDataException("Layer transform data is invalid.");
        SetLayerTransform(layerId, x + (width - nextWidth) / 2, y + (height - nextHeight) / 2,
            nextWidth, nextHeight, transform["rotation"]?.GetValue<double>() ?? 0);
    }

    public void MoveLayerTransform(Guid layerId, double offsetX, double offsetY)
    {
        RequireLayerStructureEditing();
        if (!double.IsFinite(offsetX) || !double.IsFinite(offsetY))
            throw new ArgumentOutOfRangeException(nameof(offsetX));
        int index = FindLayer(layerId);
        var layer = Current["layers"]![index]!.AsObject();
        if (layer["isGroup"]?.GetValue<bool>() == true)
            throw new ArgumentException("Use group transform for group layers.", nameof(layerId));
        var transform = layer["transform"]?.AsObject()
            ?? throw new InvalidDataException("Layer transform data is missing.");
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        if (origin?.Count != 2 || size?.Count != 2)
            throw new InvalidDataException("Layer transform data is invalid.");
        SetLayerTransform(layerId,
            origin[0]!.GetValue<double>() + offsetX,
            origin[1]!.GetValue<double>() + offsetY,
            size[0]!.GetValue<double>(), size[1]!.GetValue<double>(),
            transform["rotation"]?.GetValue<double>() ?? 0);
    }

    public void RotateLayerTransform90(Guid layerId, bool clockwise)
    {
        RequireLayerStructureEditing();
        int index = FindLayer(layerId);
        var layer = Current["layers"]![index]!.AsObject();
        if (layer["isGroup"]?.GetValue<bool>() == true)
            throw new ArgumentException("Use group transform for group layers.", nameof(layerId));
        var transform = layer["transform"]?.AsObject()
            ?? throw new InvalidDataException("Layer transform data is missing.");
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        if (origin?.Count != 2 || size?.Count != 2)
            throw new InvalidDataException("Layer transform data is invalid.");
        double rotation = transform["rotation"]?.GetValue<double>() ?? 0;
        if (!double.IsFinite(rotation)) throw new InvalidDataException("Layer transform data is invalid.");
        SetLayerTransform(layerId,
            origin[0]!.GetValue<double>() + (size[0]!.GetValue<double>() - size[1]!.GetValue<double>()) / 2,
            origin[1]!.GetValue<double>() + (size[1]!.GetValue<double>() - size[0]!.GetValue<double>()) / 2,
            size[1]!.GetValue<double>(), size[0]!.GetValue<double>(),
            rotation + (clockwise ? 90 : -90));
    }

    public void RotateLayerTransform(Guid layerId, double degrees)
    {
        RequireLayerStructureEditing();
        if (!double.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees));
        int index = FindLayer(layerId);
        var layer = Current["layers"]![index]!.AsObject();
        if (layer["isGroup"]?.GetValue<bool>() == true)
            throw new ArgumentException("Use group transform for group layers.", nameof(layerId));
        var transform = layer["transform"]?.AsObject()
            ?? throw new InvalidDataException("Layer transform data is missing.");
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        if (origin?.Count != 2 || size?.Count != 2)
            throw new InvalidDataException("Layer transform data is invalid.");
        double rotation = transform["rotation"]?.GetValue<double>() ?? 0;
        if (!double.IsFinite(rotation)) throw new InvalidDataException("Layer transform data is invalid.");
        SetLayerTransform(layerId,
            origin[0]!.GetValue<double>(), origin[1]!.GetValue<double>(),
            size[0]!.GetValue<double>(), size[1]!.GetValue<double>(), rotation + degrees);
    }

    public bool IsLayerTransformIdentity(Guid layerId)
    {
        int index = FindLayer(layerId);
        var layer = Current["layers"]![index]!.AsObject();
        var transform = layer["transform"]?.AsObject()
            ?? throw new InvalidDataException("Layer transform data is missing.");
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        return origin?.Count == 2 && size?.Count == 2 &&
            origin[0]!.GetValue<double>() == 0 && origin[1]!.GetValue<double>() == 0 &&
            size[0]!.GetValue<double>() == Width && size[1]!.GetValue<double>() == Height &&
            (transform["rotation"]?.GetValue<double>() ?? 0) == 0 &&
            (transform["flipX"]?.GetValue<bool>() ?? false) == false &&
            (transform["flipY"]?.GetValue<bool>() ?? false) == false;
    }

    public LayerTransformInfo GetLayerTransform(Guid layerId)
    {
        int index = FindLayer(layerId);
        var layer = Current["layers"]![index]!.AsObject();
        var transform = layer["transform"]?.AsObject()
            ?? throw new InvalidDataException("Layer transform data is missing.");
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        double x = origin?.Count == 2 ? origin[0]!.GetValue<double>() : double.NaN;
        double y = origin?.Count == 2 ? origin[1]!.GetValue<double>() : double.NaN;
        double width = size?.Count == 2 ? size[0]!.GetValue<double>() : double.NaN;
        double height = size?.Count == 2 ? size[1]!.GetValue<double>() : double.NaN;
        double rotation = transform["rotation"]?.GetValue<double>() ?? double.NaN;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) ||
            !double.IsFinite(rotation) || width <= 0 || height <= 0)
            throw new InvalidDataException("Layer transform data is invalid.");
        return new LayerTransformInfo(x, y, width, height, rotation,
            transform["flipX"]?.GetValue<bool>() ?? false,
            transform["flipY"]?.GetValue<bool>() ?? false);
    }

    public void SetLayerTransform(Guid layerId, double x, double y, double width, double height, double rotation)
    {
        RequireLayerStructureEditing();
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) ||
            !double.IsFinite(rotation) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        int index = FindLayer(layerId);
        var current = Current["layers"]![index]!.AsObject();
        if (current["isGroup"]?.GetValue<bool>() == true)
            throw new ArgumentException("Use group transform for group layers.", nameof(layerId));
        var currentTransform = current["transform"]?.AsObject()
            ?? throw new InvalidDataException("Layer transform data is missing.");
        var currentOrigin = currentTransform["origin"]?.AsArray();
        var currentSize = currentTransform["size"]?.AsArray();
        if (currentOrigin?.Count != 2 || currentSize?.Count != 2)
            throw new InvalidDataException("Layer transform data is invalid.");
        if (currentOrigin[0]!.GetValue<double>() == x && currentOrigin[1]!.GetValue<double>() == y &&
            currentSize[0]!.GetValue<double>() == width && currentSize[1]!.GetValue<double>() == height &&
            (currentTransform["rotation"]?.GetValue<double>() ?? 0) == rotation)
            return;
        var next = (JsonObject)Current.DeepClone();
        var transform = next["layers"]![index]!["transform"]?.AsObject()
            ?? throw new InvalidDataException("Layer transform data is missing.");
        transform["origin"] = new JsonArray(x, y);
        transform["size"] = new JsonArray(width, height);
        transform["rotation"] = rotation;
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    internal void ReplaceLayerTransformWithRaster(Guid layerId, TileRaster raster, GrayTileRaster? mask)
    {
        RequireLayerStructureEditing();
        int index = FindLayer(layerId);
        var currentLayer = Current["layers"]![index]!.AsObject();
        if (currentLayer["isGroup"]?.GetValue<bool>() == true)
            throw new ArgumentException("Group layers must use bake-ungroup.", nameof(layerId));
        CheckRasterSize(raster);
        bool hasMask = currentLayer["maskFile"] is not null;
        if (hasMask != (mask is not null))
            throw new ArgumentException("Baked layer mask does not match the layer manifest.", nameof(mask));
        if (mask is not null) CheckMaskSize(mask);
        var currentRasters = snapshots[cursor].LayerRasters
            ?? throw new InvalidOperationException("Open the editable project through ImageProjectWorkflow first.");
        var nextRasters = new Dictionary<Guid, TileRaster>(currentRasters) { [layerId] = raster };
        var nextMasks = snapshots[cursor].LayerMasks is { } currentMasks
            ? new Dictionary<Guid, GrayTileRaster>(currentMasks) : null;
        if (mask is not null)
        {
            nextMasks ??= new Dictionary<Guid, GrayTileRaster>();
            nextMasks[layerId] = mask;
        }
        var next = (JsonObject)Current.DeepClone();
        var transform = next["layers"]![index]!["transform"]?.AsObject()
            ?? throw new InvalidDataException("Layer transform data is missing.");
        transform["origin"] = new JsonArray(0d, 0d);
        transform["size"] = new JsonArray((double)Width, (double)Height);
        transform["rotation"] = 0d;
        transform["flipX"] = false;
        transform["flipY"] = false;
        Commit(new Snapshot(next, nextRasters, nextMasks is { Count: > 0 } ? nextMasks : null, ++nextRevision));
    }

    public void RotateGroup90(Guid groupId, bool clockwise)
    {
        RequireGroupStructureEditing();
        int index = FindLayer(groupId);
        var layer = Current["layers"]![index]!.AsObject();
        if (layer["isGroup"]?.GetValue<bool>() != true)
            throw new ArgumentException("Layer is not a group.", nameof(groupId));
        var transform = layer["transform"]?.AsObject()
            ?? throw new InvalidDataException("Group transform data is missing.");
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        if (origin?.Count != 2 || size?.Count != 2)
            throw new InvalidDataException("Group transform data is invalid.");
        double rotation = transform["rotation"]?.GetValue<double>() ?? 0;
        if (!double.IsFinite(rotation)) throw new InvalidDataException("Group transform data is invalid.");
        SetGroupTransform(groupId,
            origin[0]!.GetValue<double>() + (size[0]!.GetValue<double>() - size[1]!.GetValue<double>()) / 2,
            origin[1]!.GetValue<double>() + (size[1]!.GetValue<double>() - size[0]!.GetValue<double>()) / 2,
            size[1]!.GetValue<double>(),
            size[0]!.GetValue<double>(),
            rotation + (clockwise ? 90 : -90));
    }

    public void RotateGroupTransform(Guid groupId, double degrees)
    {
        RequireGroupStructureEditing();
        if (!double.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees));
        int index = FindLayer(groupId);
        var layer = Current["layers"]![index]!.AsObject();
        if (layer["isGroup"]?.GetValue<bool>() != true)
            throw new ArgumentException("Layer is not a group.", nameof(groupId));
        var transform = layer["transform"]?.AsObject()
            ?? throw new InvalidDataException("Group transform data is missing.");
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        if (origin?.Count != 2 || size?.Count != 2)
            throw new InvalidDataException("Group transform data is invalid.");
        double rotation = transform["rotation"]?.GetValue<double>() ?? 0;
        if (!double.IsFinite(rotation)) throw new InvalidDataException("Group transform data is invalid.");
        SetGroupTransform(groupId,
            origin[0]!.GetValue<double>(), origin[1]!.GetValue<double>(),
            size[0]!.GetValue<double>(), size[1]!.GetValue<double>(), rotation + degrees);
    }

    public bool IsGroupTransformIdentity(Guid groupId)
    {
        int index = FindLayer(groupId);
        var layer = Current["layers"]![index]!.AsObject();
        if (layer["isGroup"]?.GetValue<bool>() != true)
            throw new ArgumentException("Layer is not a group.", nameof(groupId));
        var transform = layer["transform"]?.AsObject()
            ?? throw new InvalidDataException("Group transform data is missing.");
        var origin = transform["origin"]?.AsArray();
        var size = transform["size"]?.AsArray();
        return origin?.Count == 2 && size?.Count == 2 &&
            origin[0]!.GetValue<double>() == 0 && origin[1]!.GetValue<double>() == 0 &&
            size[0]!.GetValue<double>() == Width && size[1]!.GetValue<double>() == Height &&
            (transform["rotation"]?.GetValue<double>() ?? 0) == 0 &&
            (transform["flipX"]?.GetValue<bool>() ?? false) == false &&
            (transform["flipY"]?.GetValue<bool>() ?? false) == false;
    }

    public void SetGroupTransform(Guid groupId, double x, double y, double width, double height, double rotation)
    {
        RequireGroupStructureEditing();
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) ||
            !double.IsFinite(rotation) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        int index = FindLayer(groupId);
        var current = Current["layers"]![index]!.AsObject();
        if (current["isGroup"]?.GetValue<bool>() != true)
            throw new ArgumentException("Layer is not a group.", nameof(groupId));
        var currentTransform = current["transform"]?.AsObject()
            ?? throw new InvalidDataException("Group transform data is missing.");
        var currentOrigin = currentTransform["origin"]?.AsArray();
        var currentSize = currentTransform["size"]?.AsArray();
        if (currentOrigin?.Count != 2 || currentSize?.Count != 2)
            throw new InvalidDataException("Group transform data is invalid.");
        if (currentOrigin[0]!.GetValue<double>() == x && currentOrigin[1]!.GetValue<double>() == y &&
            currentSize[0]!.GetValue<double>() == width && currentSize[1]!.GetValue<double>() == height &&
            (currentTransform["rotation"]?.GetValue<double>() ?? 0) == rotation)
            return;
        var next = (JsonObject)Current.DeepClone();
        var transform = next["layers"]![index]!["transform"]?.AsObject()
            ?? throw new InvalidDataException("Group transform data is missing.");
        transform["origin"] = new JsonArray(x, y);
        transform["size"] = new JsonArray(width, height);
        transform["rotation"] = rotation;
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public Guid GroupLayers(IReadOnlyList<Guid> layerIds, string name)
    {
        RequireGroupStructureEditing();
        if (layerIds.Count == 0) throw new ArgumentException("At least one layer is required.", nameof(layerIds));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 1000)
            throw new ArgumentException("Group name must contain 1 to 1000 characters.", nameof(name));
        var layers = Current["layers"]!.AsArray();
        var distinctIds = layerIds.Distinct().ToArray();
        var selectedSet = distinctIds.ToHashSet();
        var indexes = distinctIds.Select(FindLayer).OrderBy(index => index).ToArray();
        var parentIds = indexes.Select(index => layers[index]!["parentID"] is { } parent
            ? Guid.Parse(parent.GetValue<string>()) : (Guid?)null).Distinct().ToArray();
        if (indexes.Length != layerIds.Count || parentIds.Length != 1)
            throw new NotSupportedException("Only sibling layers can be grouped in this slice.");
        foreach (int index in indexes)
            if (layers[index]!["maskSourceID"] is { } sourceNode &&
                !selectedSet.Contains(Guid.Parse(sourceNode.GetValue<string>())))
                throw new NotSupportedException("A clipping source must be selected with its target layer.");
        var siblingIndexes = layers.Select((layer, index) => (layer, index))
            .Where(pair => pair.layer!["parentID"] is { } parent
                ? Guid.Parse(parent.GetValue<string>()) == parentIds[0]
                : parentIds[0] is null)
            .Select(pair => pair.index).ToArray();
        var selectedSiblingPositions = indexes.Select(index => Array.IndexOf(siblingIndexes, index)).OrderBy(index => index).ToArray();
        if (selectedSiblingPositions.Any(position => position < 0) ||
            selectedSiblingPositions[^1] - selectedSiblingPositions[0] + 1 != selectedSiblingPositions.Length)
            throw new NotSupportedException("Only contiguous sibling layers can be grouped in this slice.");
        int width = Width, height = Height;
        Guid groupId = Guid.NewGuid();
        var next = (JsonObject)Current.DeepClone();
        var nextLayers = next["layers"]!.AsArray();
        var group = new JsonObject
        {
            ["id"] = groupId.ToString("D"), ["isGroup"] = true, ["isVisible"] = true, ["name"] = name,
            ["transform"] = new JsonObject
            {
                ["origin"] = new JsonArray(0d, 0d), ["size"] = new JsonArray((double)width, (double)height),
                ["rotation"] = 0d, ["flipX"] = false, ["flipY"] = false, ["sampling"] = "High quality"
            }
        };
        if (parentIds[0] is { } parentId) group["parentID"] = parentId.ToString("D");
        foreach (int index in indexes)
            nextLayers[index]!["parentID"] = groupId.ToString("D");
        nextLayers.Insert(indexes[0], group);
        next["activeLayerID"] = layerIds.Contains(ActiveLayerId ?? Guid.Empty)
            ? (ActiveLayerId ?? layerIds[0]).ToString("D") : layerIds[0].ToString("D");
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
        return groupId;
    }

    public void UngroupLayer(Guid groupId)
    {
        RequireGroupStructureEditing();
        int index = FindLayer(groupId);
        var group = Current["layers"]![index]!;
        if (group["isGroup"]?.GetValue<bool>() != true)
            throw new ArgumentException("Layer is not a group.", nameof(groupId));
        if (!IsGroupTransformIdentity(groupId))
            throw new NotSupportedException("Transformed groups must be reset before ungrouping in this slice.");
        GrayTileRaster? groupMask = null;
        if (group["maskFile"] is not null)
        {
            if (group["maskEnabled"]?.GetValue<bool>() ?? true)
                groupMask = TryGetLoadedLayerMask(groupId, out var loadedGroupMask) ? loadedGroupMask
                    : throw new InvalidOperationException("Layer masks have not been loaded.");
        }
        Guid? parentId = group["parentID"] is { } parent ? Guid.Parse(parent.GetValue<string>()) : null;
        var directChildren = Current["layers"]!.AsArray()
            .Where(node => node!["parentID"] is { } parent && Guid.Parse(parent.GetValue<string>()) == groupId)
            .Select(node => Guid.Parse(node!["id"]!.GetValue<string>())).ToArray();
        var next = (JsonObject)Current.DeepClone();
        var nextLayers = next["layers"]!.AsArray();
        Dictionary<Guid, GrayTileRaster>? nextMasks = snapshots[cursor].LayerMasks is { } loadedMasks
            ? new Dictionary<Guid, GrayTileRaster>(loadedMasks) : null;
        if (groupMask is not null)
        {
            if (nextMasks is null) throw new InvalidOperationException("Layer masks have not been loaded.");
            foreach (Guid childId in directChildren)
            {
                var child = nextLayers.First(node => Guid.Parse(node!["id"]!.GetValue<string>()) == childId)!.AsObject();
                if (child["maskSourceID"] is not null) continue;
                GrayTileRaster? childMask = child["maskFile"] is not null &&
                    TryGetLoadedLayerMask(childId, out var loadedChildMask) ? loadedChildMask : null;
                nextMasks[childId] = childMask is null ? CloneMask(groupMask) : MultiplyMasks(childMask, groupMask);
                child["maskFile"] = childId.ToString("D") + ".mask.png";
                child["maskEnabled"] = true;
            }
        }
        foreach (JsonNode? node in nextLayers)
            if (node!["parentID"] is { } parentNode && Guid.Parse(parentNode.GetValue<string>()) == groupId)
            {
                if (parentId is { } outer) node["parentID"] = outer.ToString("D");
                else node.AsObject().Remove("parentID");
            }
        nextLayers.RemoveAt(index);
        if (ActiveLayerId == groupId)
        {
            Guid? ParentOf(JsonNode node) => node["parentID"] is { } value
                ? Guid.Parse(value.GetValue<string>()) : null;
            JsonNode? replacement = directChildren.Select(id => nextLayers.FirstOrDefault(node =>
                Guid.Parse(node!["id"]!.GetValue<string>()) == id)).FirstOrDefault(node => node is not null);
            replacement ??= nextLayers.Skip(Math.Min(index, nextLayers.Count)).FirstOrDefault(node => ParentOf(node!) == parentId);
            replacement ??= nextLayers.Take(Math.Min(index, nextLayers.Count)).LastOrDefault(node => ParentOf(node!) == parentId);
            if (replacement is null) next.Remove("activeLayerID");
            else next["activeLayerID"] = replacement!["id"]!.DeepClone();
        }
        if (nextMasks is not null) nextMasks.Remove(groupId);
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, nextMasks is { Count: > 0 } ? nextMasks : null, ++nextRevision));
    }

    internal void ReplaceGroupWithRaster(Guid groupId, TileRaster raster)
    {
        RequireGroupStructureEditing();
        if (raster.Width != Width || raster.Height != Height)
            throw new ArgumentException("Baked group raster dimensions do not match the canvas.", nameof(raster));
        int groupIndex = FindLayer(groupId);
        var layers = Current["layers"]!.AsArray();
        var group = layers[groupIndex]!.AsObject();
        if (group["isGroup"]?.GetValue<bool>() != true)
            throw new ArgumentException("Layer is not a group.", nameof(groupId));

        var byId = layers.ToDictionary(node => Guid.Parse(node!["id"]!.GetValue<string>()), node => node!.AsObject());
        var descendants = new HashSet<Guid>();
        foreach (Guid id in byId.Keys)
        {
            if (id == groupId) continue;
            Guid? parent = byId[id]["parentID"] is { } parentNode
                ? Guid.Parse(parentNode.GetValue<string>()) : null;
            var seen = new HashSet<Guid>();
            while (parent is { } parentId && seen.Add(parentId))
            {
                if (parentId == groupId)
                {
                    descendants.Add(id);
                    break;
                }
                parent = byId.TryGetValue(parentId, out var parentLayer) && parentLayer["parentID"] is { } nextParent
                    ? Guid.Parse(nextParent.GetValue<string>()) : null;
            }
        }

        var next = (JsonObject)Current.DeepClone();
        var nextLayers = next["layers"]!.AsArray();
        var baked = nextLayers[groupIndex]!.AsObject();
        baked["isGroup"] = false;
        baked["imageFile"] = groupId.ToString("D").ToUpperInvariant() + ".png";
        baked.Remove("maskFile");
        baked.Remove("maskEnabled");
        baked["transform"] = new JsonObject
        {
            ["origin"] = new JsonArray(0d, 0d),
            ["size"] = new JsonArray((double)Width, (double)Height),
            ["rotation"] = 0d,
            ["flipX"] = false,
            ["flipY"] = false,
            ["sampling"] = "High quality"
        };
        for (int index = nextLayers.Count - 1; index >= 0; index--)
        {
            Guid id = Guid.Parse(nextLayers[index]!["id"]!.GetValue<string>());
            if (descendants.Contains(id)) nextLayers.RemoveAt(index);
        }
        if (ActiveLayerId is { } active && descendants.Contains(active))
            next["activeLayerID"] = groupId.ToString("D");

        var currentRasters = snapshots[cursor].LayerRasters
            ?? throw new InvalidOperationException("Open the editable project through ImageProjectWorkflow first.");
        var nextRasters = new Dictionary<Guid, TileRaster>(currentRasters)
        {
            [groupId] = raster
        };
        foreach (Guid id in descendants) nextRasters.Remove(id);
        var nextMasks = snapshots[cursor].LayerMasks is { } currentMasks
            ? new Dictionary<Guid, GrayTileRaster>(currentMasks) : null;
        if (nextMasks is not null)
        {
            nextMasks.Remove(groupId);
            foreach (Guid id in descendants) nextMasks.Remove(id);
        }
        Commit(new Snapshot(next, nextRasters, nextMasks is { Count: > 0 } ? nextMasks : null, ++nextRevision));
    }

    private static GrayTileRaster CloneMask(GrayTileRaster source)
    {
        var result = new GrayTileRaster(source.Width, source.Height);
        for (int row = 0; row * TileRaster.TileSize < source.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < source.Width; column++)
            result = result.ReplaceTile(column, row, source.ReadTileCopy(column, row));
        return result;
    }

    private static GrayTileRaster MultiplyMasks(GrayTileRaster first, GrayTileRaster second)
    {
        if (first.Width != second.Width || first.Height != second.Height)
            throw new ArgumentException("Mask dimensions must match.");
        var result = new GrayTileRaster(first.Width, first.Height);
        for (int row = 0; row * TileRaster.TileSize < first.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < first.Width; column++)
        {
            byte[] pixels = first.ReadTileCopy(column, row);
            byte[] other = second.ReadTileCopy(column, row);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)((pixels[i] * other[i] + 127) / 255);
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
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

    public void MergeLayerDown(Guid upperLayerId, TileRaster mergedRaster)
    {
        int upperIndex = FindLayer(upperLayerId);
        if (upperIndex == 0) throw new InvalidOperationException("当前图层下方没有可合并的图层。");
        MergeLayers([Layers[upperIndex - 1].Id, upperLayerId], mergedRaster);
    }

    public void MergeLayers(IReadOnlyList<Guid> layerIds, TileRaster mergedRaster)
    {
        RequireLayerStructureEditing();
        Guid[] distinctIds = layerIds.Distinct().ToArray();
        if (distinctIds.Length < 2)
            throw new InvalidOperationException("请至少选择两个图层。");
        int[] indexes = distinctIds.Select(FindLayer).OrderBy(index => index).ToArray();
        if (indexes[^1] - indexes[0] + 1 != indexes.Length)
            throw new InvalidOperationException("只能合并连续图层。");
        if (mergedRaster.Width != Width || mergedRaster.Height != Height)
            throw new ArgumentException("Merged raster dimensions do not match the canvas.", nameof(mergedRaster));
        FlatLayerInfo[] selected = indexes.Select(index => Layers[index]).ToArray();
        if (selected.Any(layer => layer.IsGroup))
            throw new NotSupportedException("组图层暂不支持合并。");
        var selectedIds = selected.Select(layer => layer.Id).ToHashSet();
        foreach (FlatLayerInfo layer in Layers)
        {
            bool targetSelected = selectedIds.Contains(layer.Id);
            if (targetSelected && layer.MaskSourceId is { } targetSource && !selectedIds.Contains(targetSource))
                throw new NotSupportedException("剪贴目标必须与其源图层一起合并。");
            if (targetSelected && Layers.Any(candidate => candidate.MaskSourceId == layer.Id && !selectedIds.Contains(candidate.Id)))
                throw new NotSupportedException("剪贴源不能在目标图层之外被合并。");
        }
        FlatLayerInfo lower = selected[0];
        string mergedName = string.Join(" + ", selected.Select(layer => layer.Name));
        if (mergedName.Length > 1000) mergedName = lower.Name;
        var next = (JsonObject)Current.DeepClone();
        var nextLayers = next["layers"]!.AsArray();
        var lowerNode = nextLayers[indexes[0]]!.AsObject();
        lowerNode["name"] = mergedName;
        lowerNode["isVisible"] = selected.Any(layer => layer.IsVisible);
        lowerNode["opacity"] = 1d;
        lowerNode["blendMode"] = "Normal";
        var lowerTransform = lowerNode["transform"]?.AsObject()
            ?? throw new InvalidDataException("Layer transform data is missing.");
        lowerTransform["origin"] = new JsonArray(0d, 0d);
        lowerTransform["size"] = new JsonArray((double)Width, (double)Height);
        lowerTransform["rotation"] = 0d;
        lowerTransform["flipX"] = false;
        lowerTransform["flipY"] = false;
        lowerNode.Remove("maskFile");
        lowerNode.Remove("maskEnabled");
        lowerNode.Remove("maskSourceID");
        for (int index = indexes[^1]; index >= indexes[0]; index--)
            if (index != indexes[0]) nextLayers.RemoveAt(index);
        next["activeLayerID"] = lower.Id.ToString("D");
        var rasters = new Dictionary<Guid, TileRaster>(snapshots[cursor].LayerRasters!)
        {
            [lower.Id] = mergedRaster
        };
        foreach (FlatLayerInfo layer in selected.Skip(1)) rasters.Remove(layer.Id);
        Dictionary<Guid, GrayTileRaster>? masks = null;
        if (snapshots[cursor].LayerMasks is { } loadedMasks)
        {
            masks = new Dictionary<Guid, GrayTileRaster>(loadedMasks);
            foreach (FlatLayerInfo layer in selected) masks.Remove(layer.Id);
            if (masks.Count == 0) masks = null;
        }
        Commit(new Snapshot(next, rasters, masks, ++nextRevision));
    }

    private int[] ValidateLayerCopyFrom(ProjectSession source, Guid sourceLayerId, int destinationIndex)
    {
        if (ReferenceEquals(this, source)) throw new ArgumentException("Source and target projects must differ.", nameof(source));
        if (!CanEdit || Current["version"]!.GetValue<int>() != 8 || HasGroups)
            throw new NotSupportedException("Cross-project layer copy requires a flat editable v8 target project.");
        if (!source.CanEdit || source.Current["version"]!.GetValue<int>() != 8)
            throw new NotSupportedException("Cross-project layer copy requires an editable v8 source project.");
        if (Width != source.Width || Height != source.Height)
            throw new NotSupportedException("Cross-project layer copy requires matching canvas dimensions.");
        if (snapshots[cursor].LayerRasters is null || source.snapshots[source.cursor].LayerRasters is null)
            throw new InvalidOperationException("Both projects must be opened through the editable workflow.");
        if (destinationIndex < 0 || destinationIndex > Current["layers"]!.AsArray().Count)
            throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        int sourceIndex = source.FindLayer(sourceLayerId);
        FlatLayerInfo sourceLayer = source.Layers[sourceIndex];
        if (source.HasGroups)
        {
            if (!sourceLayer.IsGroup)
                throw new NotSupportedException("Raster layers inside a grouped project must be copied with their group.");
            return ValidateGroupCopy(source, sourceLayerId, sourceIndex);
        }
        var stackIds = new HashSet<Guid> { sourceLayerId };
        bool changed;
        do
        {
            changed = false;
            foreach (FlatLayerInfo layer in source.Layers)
            {
                if (layer.MaskSourceId is { } sourceId && stackIds.Contains(sourceId) && stackIds.Add(layer.Id))
                    changed = true;
                if (stackIds.Contains(layer.Id) && layer.MaskSourceId is { } parentId && stackIds.Add(parentId))
                    changed = true;
            }
        } while (changed);
        int[] sourceIndexes = stackIds.Select(source.FindLayer).OrderBy(index => index).ToArray();
        if (sourceIndexes[^1] - sourceIndexes[0] + 1 != sourceIndexes.Length)
            throw new NotSupportedException("Clipping stack must remain contiguous when copied.");
        foreach (int index in sourceIndexes)
        {
            FlatLayerInfo layer = source.Layers[index];
            if (layer.IsGroup) throw new NotSupportedException("Group layers cannot be copied across projects in this slice.");
            if (layer.HasMask && source.GetLayerMask(layer.Id) is null)
                throw new InvalidDataException("Source layer mask asset is missing.");
        }
        return sourceIndexes;
    }

    private static int[] ValidateGroupCopy(ProjectSession source, Guid groupId, int groupIndex)
    {
        var stackIds = new HashSet<Guid> { groupId };
        bool changed;
        do
        {
            changed = false;
            foreach (FlatLayerInfo layer in source.Layers)
                if (layer.ParentId is { } parentId && stackIds.Contains(parentId) && stackIds.Add(layer.Id))
                    changed = true;
        } while (changed);
        int[] sourceIndexes = stackIds.Select(source.FindLayer).OrderBy(index => index).ToArray();
        if (sourceIndexes.Length == 0 || sourceIndexes[0] != groupIndex ||
            sourceIndexes[^1] - sourceIndexes[0] + 1 != sourceIndexes.Length)
            throw new NotSupportedException("A copied group subtree must remain contiguous.");
        foreach (FlatLayerInfo layer in source.Layers)
        {
            bool included = stackIds.Contains(layer.Id);
            if (included && layer.ParentId is { } parentId && !stackIds.Contains(parentId))
                throw new NotSupportedException("A copied group cannot retain an external parent.");
            if (included && layer.MaskSourceId is { } sourceId && !stackIds.Contains(sourceId) ||
                !included && layer.MaskSourceId is { } externalSourceId && stackIds.Contains(externalSourceId))
                throw new NotSupportedException("A copied group cannot retain an external clipping relationship.");
        }
        foreach (int index in sourceIndexes)
        {
            FlatLayerInfo layer = source.Layers[index];
            if (layer.HasMask && source.GetLayerMask(layer.Id) is null)
                throw new InvalidDataException("Source group mask asset is missing.");
            if (!layer.IsGroup) _ = source.GetLayerRaster(layer.Id);
        }
        return sourceIndexes;
    }

    private Guid InsertLayerStack(ProjectSession source, IReadOnlyList<int> sourceIndexes, Guid sourceActiveLayerId,
        int destinationIndex)
    {
        if (sourceIndexes.Count == 0) throw new ArgumentException("At least one source layer is required.", nameof(sourceIndexes));
        var sourceLayers = source.Current["layers"]!.AsArray();
        var idMap = sourceIndexes.ToDictionary(
            index => Guid.Parse((sourceLayers[index]!.AsObject())["id"]!.GetValue<string>()),
            _ => Guid.NewGuid());
        var next = (JsonObject)Current.DeepClone();
        var nextLayers = next["layers"]!.AsArray();
        var rasters = new Dictionary<Guid, TileRaster>(snapshots[cursor].LayerRasters!);
        var masks = snapshots[cursor].LayerMasks is { } currentMasks
            ? new Dictionary<Guid, GrayTileRaster>(currentMasks)
            : null;
        for (int offset = 0; offset < sourceIndexes.Count; offset++)
        {
            int sourceIndex = sourceIndexes[offset];
            Guid sourceId = Guid.Parse((sourceLayers[sourceIndex]!.AsObject())["id"]!.GetValue<string>());
            Guid targetId = idMap[sourceId];
            var layer = sourceLayers[sourceIndex]!.DeepClone().AsObject();
            layer["id"] = targetId.ToString("D");
            if (layer["imageFile"] is not null)
                layer["imageFile"] = targetId.ToString("D").ToUpperInvariant() + ".png";
            if (layer["parentID"] is { } parentNode)
            {
                Guid sourceParentId = Guid.Parse(parentNode.GetValue<string>());
                if (!idMap.TryGetValue(sourceParentId, out Guid targetParentId))
                    throw new InvalidDataException("Copied group relationship points outside the copied subtree.");
                layer["parentID"] = targetParentId.ToString("D");
            }
            if (layer["maskSourceID"] is { } sourceNode)
            {
                Guid sourceMaskId = Guid.Parse(sourceNode.GetValue<string>());
                if (!idMap.TryGetValue(sourceMaskId, out Guid targetMaskId))
                    throw new InvalidDataException("Copied clipping relationship points outside the copied stack.");
                layer["maskSourceID"] = targetMaskId.ToString("D");
            }
            if (layer["maskFile"] is not null)
            {
                GrayTileRaster mask = source.GetLayerMask(sourceId)
                    ?? throw new InvalidDataException("Source layer mask asset is missing.");
                layer["maskFile"] = targetId.ToString("D").ToUpperInvariant() + ".mask.png";
                masks ??= new Dictionary<Guid, GrayTileRaster>();
                masks[targetId] = mask;
            }
            nextLayers.Insert(destinationIndex + offset, layer);
            if (layer["isGroup"]?.GetValue<bool>() != true)
                rasters[targetId] = source.GetLayerRaster(sourceId);
        }
        next["activeLayerID"] = idMap[sourceActiveLayerId].ToString("D");
        Commit(new Snapshot(next, rasters, masks, ++nextRevision));
        return idMap[sourceActiveLayerId];
    }

    private Guid InsertLayer(JsonObject layer, TileRaster raster, int destinationIndex, GrayTileRaster? mask = null)
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
        if (mask is not null)
        {
            CheckMaskSize(mask);
            layer["maskFile"] = id.ToString("D").ToUpperInvariant() + ".mask.png";
            layer["maskEnabled"] = layer["maskEnabled"]?.GetValue<bool>() ?? true;
        }
        var next = (JsonObject)Current.DeepClone();
        next["layers"]!.AsArray().Insert(destinationIndex, layer);
        next["activeLayerID"] = id.ToString("D");
        var rasters = new Dictionary<Guid, TileRaster>(snapshots[cursor].LayerRasters!) { [id] = raster };
        var masks = snapshots[cursor].LayerMasks is { } currentMasks
            ? new Dictionary<Guid, GrayTileRaster>(currentMasks)
            : null;
        if (mask is not null)
        {
            masks ??= new Dictionary<Guid, GrayTileRaster>();
            masks[id] = mask;
        }
        Commit(new Snapshot(next, rasters, masks, ++nextRevision));
        return id;
    }

    private void RequireLayerStructureEditing()
    {
        if (!CanEdit || Current["version"]!.GetValue<int>() != 8)
            throw new NotSupportedException("Layer structure changes require a flat editable v8 project.");
        if (snapshots[cursor].LayerRasters is null)
            throw new InvalidOperationException("Open the editable project through ImageProjectWorkflow first.");
        if (HasGroups)
            throw new NotSupportedException("Layer structure changes are not supported for grouped projects in this slice.");
    }

    private void RequireGroupStructureEditing()
    {
        if (!CanEdit || Current["version"]!.GetValue<int>() != 8)
            throw new NotSupportedException("Group structure changes require an editable v8 project.");
        if (snapshots[cursor].LayerRasters is null)
            throw new InvalidOperationException("Open the editable project through ImageProjectWorkflow first.");
    }

    private void RequireMaskEditing()
    {
        if (!CanEdit || Current["version"]!.GetValue<int>() != 8)
            throw new NotSupportedException("Mask edits require an editable v8 project.");
        if (snapshots[cursor].LayerMasks is null && Layers.Any(layer => layer.HasMask))
            throw new InvalidOperationException("Open the editable project through ImageProjectWorkflow first.");
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
