using System.Text.Json.Nodes;

namespace Compositor.Core;

public sealed record FlatLayerInfo(Guid Id, string Name, bool IsVisible);

public sealed class ProjectSession
{
    private const int MaxUndoSteps = 100;
    private const long MaxHistoryImageBytes = 256L * 1024 * 1024;
    private sealed record Snapshot(JsonObject Manifest, IReadOnlyDictionary<Guid, TileRaster>? LayerRasters, long Revision);
    private readonly List<Snapshot> snapshots;
    private int cursor;
    private long nextRevision;
    private long savedRevision;
    private IReadOnlyDictionary<Guid, TileRaster>? sourceLayerRasters;

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
        snapshots = [new Snapshot(manifest, null, 0)];
    }

    public string? SavedDirectory { get; private set; }
    public bool HasBeenSaved => SavedDirectory is not null;
    public string SourceDirectory => SavedDirectory ?? throw new InvalidOperationException("This document has not been saved yet.");
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
            layer["name"]!.GetValue<string>(), layer["isVisible"]?.GetValue<bool>() ?? true)).ToArray();
    public TileRaster? Raster => ImageName.Length != 0 &&
        TryGetLoadedLayerRaster(Guid.Parse(Current["layers"]![0]!["id"]!.GetValue<string>()), out var raster) ? raster : null;
    internal bool RequiresRasterEncoding => ImageName.Length != 0 && TryGetRasterForEncoding(ImageName, out _);
    internal JsonObject Current => snapshots[cursor].Manifest;
    internal IEnumerable<string> CurrentImageNames => Current["layers"]!.AsArray()
        .Select(layer => layer!["imageFile"]!.GetValue<string>());
    internal bool TryGetLoadedLayerRaster(Guid layerId, out TileRaster raster)
    {
        if (snapshots[cursor].LayerRasters is { } layers && layers.TryGetValue(layerId, out raster!)) return true;
        raster = null!;
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
    }

    internal void AttachRaster(TileRaster raster)
    {
        if (!CanEdit || ImageName.Length == 0 || snapshots.Count != 1 || Raster is not null)
            throw new InvalidOperationException("Raster can only be attached to a newly opened editable project.");
        CheckRasterSize(raster);
        var attached = new Dictionary<Guid, TileRaster>
        {
            [Guid.Parse(Current["layers"]![0]!["id"]!.GetValue<string>())] = raster
        };
        snapshots[0] = new Snapshot(Current, attached, 0);
        sourceLayerRasters = attached;
    }

    internal void AttachLayerRasters(IReadOnlyDictionary<Guid, TileRaster> rasters)
    {
        if (!CanEdit || ImageName.Length != 0 || snapshots.Count != 1 || snapshots[0].LayerRasters is not null ||
            rasters.Count != Layers.Count || Layers.Any(layer => !rasters.ContainsKey(layer.Id)))
            throw new InvalidOperationException("Layer rasters can only be attached to a newly opened flat project.");
        foreach (TileRaster raster in rasters.Values) CheckRasterSize(raster);
        var attached = new Dictionary<Guid, TileRaster>(rasters);
        snapshots[0] = new Snapshot(Current, attached, 0);
        sourceLayerRasters = attached;
    }

    public TileRaster GetLayerRaster(Guid layerId)
    {
        FindLayer(layerId);
        return TryGetLoadedLayerRaster(layerId, out var raster) ? raster
            : throw new InvalidOperationException("Layer rasters have not been loaded.");
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
        Commit(new Snapshot(Current, next, ++nextRevision));
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
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, ++nextRevision));
    }

    public void SetLayerVisible(Guid layerId, bool visible)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        int index = FindLayer(layerId);
        if (Current["layers"]![index]!["isVisible"]!.GetValue<bool>() == visible) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["isVisible"] = visible;
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, ++nextRevision));
    }

    public void MoveLayer(Guid layerId, int destinationIndex)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        var layers = Current["layers"]!.AsArray();
        if ((uint)destinationIndex >= layers.Count) throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        int sourceIndex = FindLayer(layerId);
        if (sourceIndex == destinationIndex) return;
        var next = (JsonObject)Current.DeepClone();
        var reordered = next["layers"]!.AsArray();
        JsonNode layer = reordered[sourceIndex]!;
        reordered.RemoveAt(sourceIndex);
        reordered.Insert(destinationIndex, layer);
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, ++nextRevision));
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
        var layer = (JsonObject)Current["layers"]![index]!.DeepClone();
        layer["name"] = name;
        return InsertLayer(layer, GetLayerRaster(layerId), index + 1);
    }

    public void DeleteLayer(Guid layerId)
    {
        RequireLayerStructureEditing();
        int index = FindLayer(layerId);
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
        Commit(new Snapshot(next, rasters, ++nextRevision));
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
        Commit(new Snapshot(next, rasters, ++nextRevision));
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
    }

    private void CheckRasterSize(TileRaster raster)
    {
        if (raster.Width != Current["width"]!.GetValue<int>() ||
            raster.Height != Current["height"]!.GetValue<int>())
            throw new ArgumentException("Raster dimensions do not match the canvas.", nameof(raster));
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
        savedRevision = snapshots[cursor].Revision;
    }
}
