using System.Text.Json.Nodes;

namespace Compositor.Core;

public sealed record FlatLayerInfo(Guid Id, string Name, bool IsVisible)
{
    public bool IsGroup { get; init; }
    public bool IsText { get; init; }
    public bool IsAdjustment { get; init; }
    public string? AdjustmentKind { get; init; }
    public bool IsShape { get; init; }
    public string? ShapeKind { get; init; }
    public bool IsGradient { get; init; }
    public Guid? ParentId { get; init; }
    public double Opacity { get; init; } = 1;
    public string BlendMode { get; init; } = "Normal";
    public bool HasMask { get; init; }
    public bool MaskEnabled { get; init; }
    public bool MaskLinked { get; init; } = true;
    public Guid? MaskSourceId { get; init; }
}

public sealed record LayerTransformInfo(double X, double Y, double Width, double Height,
    double Rotation, bool FlipX, bool FlipY)
{
    public static LayerTransformInfo Read(JsonObject transform)
    {
        var origin = transform["origin"]!.AsArray();
        var size = transform["size"]!.AsArray();
        return new(origin[0]!.GetValue<double>(), origin[1]!.GetValue<double>(),
            size[0]!.GetValue<double>(), size[1]!.GetValue<double>(),
            transform["rotation"]?.GetValue<double>() ?? 0,
            transform["flipX"]?.GetValue<bool>() ?? false,
            transform["flipY"]?.GetValue<bool>() ?? false);
    }

    public (double X, double Y) ToDocument(double unitX, double unitY)
    {
        double x = (unitX - 0.5) * Width * (FlipX ? -1 : 1);
        double y = (unitY - 0.5) * Height * (FlipY ? -1 : 1);
        double angle = Rotation % 360 * Math.PI / 180;
        return (X + Width / 2 + x * Math.Cos(angle) - y * Math.Sin(angle),
            Y + Height / 2 + x * Math.Sin(angle) + y * Math.Cos(angle));
    }

    public (double X, double Y) FromDocument(double documentX, double documentY)
    {
        double x = documentX - X - Width / 2, y = documentY - Y - Height / 2;
        double angle = Rotation % 360 * Math.PI / 180;
        return ((x * Math.Cos(angle) + y * Math.Sin(angle)) * (FlipX ? -1 : 1) / Width + 0.5,
            (-x * Math.Sin(angle) + y * Math.Cos(angle)) * (FlipY ? -1 : 1) / Height + 0.5);
    }

    public LayerTransformInfo Following(LayerTransformInfo oldLayer, LayerTransformInfo newLayer)
    {
        if (oldLayer.Width == newLayer.Width && oldLayer.Height == newLayer.Height &&
            oldLayer.Rotation == newLayer.Rotation && oldLayer.FlipX == newLayer.FlipX && oldLayer.FlipY == newLayer.FlipY)
            return this with { X = X + newLayer.X - oldLayer.X, Y = Y + newLayer.Y - oldLayer.Y };
        (double X, double Y) Map(double x, double y)
        {
            var document = ToDocument(x, y);
            var unit = oldLayer.FromDocument(document.X, document.Y);
            return newLayer.ToDocument(unit.X, unit.Y);
        }
        var start = Map(0, 0);
        var horizontal = Map(1, 0);
        var vertical = Map(0, 1);
        var center = Map(0.5, 0.5);
        double a = horizontal.X - start.X, b = horizontal.Y - start.Y;
        double c = vertical.X - start.X, d = vertical.Y - start.Y;
        double angle = Math.Atan2(b * (FlipX ? -1 : 1), a * (FlipX ? -1 : 1));
        double along = -c * Math.Sin(angle) + d * Math.Cos(angle);
        double width = Math.Sqrt(a * a + b * b), height = Math.Abs(along);
        double degrees = angle * 180 / Math.PI;
        return this with { X = center.X - width / 2, Y = center.Y - height / 2, Width = width, Height = height,
            Rotation = degrees + Math.Round((Rotation - degrees) / 360, MidpointRounding.AwayFromZero) * 360,
            FlipY = along < 0 };
    }
}

public sealed record TextLayerMetadata(Guid Id, string ImageFile, string Content,
    string FontPostScriptName, double FontSizePoints, double Red, double Green, double Blue,
    double Alpha, string Alignment, double LineSpacingPoints, double TrackingPoints,
    string Layout, double? BoxWidth);

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
        ReadOnlyMemory<byte> imageHash, IReadOnlyDictionary<string, byte[]>? assetHashes = null,
        int sourceFormatVersion = 8)
    {
        SavedDirectory = sourceDirectory;
        var hashes = assetHashes is null
            ? new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, byte[]>(assetHashes, StringComparer.OrdinalIgnoreCase);
        if (canEdit && imageName.Length != 0 && !hashes.ContainsKey(imageName))
            hashes.Add(imageName, imageHash.ToArray());
        AssetHashes = hashes;
        CanEdit = canEdit;
        SourceFormatVersion = sourceFormatVersion;
        snapshots = [new Snapshot(manifest, null, null, 0)];
    }

    public string? SavedDirectory { get; private set; }
    public bool HasBeenSaved => SavedDirectory is not null;
    public bool HasTextLayers => Current["layers"]!.AsArray().Any(layer => layer?["text"] is not null);
    public IReadOnlyList<TextLayerMetadata> TextLayers => Current["layers"]!.AsArray()
        .Where(layer => layer?["text"] is JsonObject)
        .Select(layer =>
        {
            var record = layer!.AsObject();
            var text = record["text"]!.AsObject();
            var layout = text["layout"]!.AsObject();
            if (layout["point"] is not null)
                return new TextLayerMetadata(Guid.Parse(record["id"]!.GetValue<string>()),
                    record["imageFile"]?.GetValue<string>() ?? "", text["content"]!.GetValue<string>(),
                    text["fontPostScriptName"]!.GetValue<string>(), text["fontSizePoints"]!.GetValue<double>(),
                    text["red"]!.GetValue<double>(), text["green"]!.GetValue<double>(), text["blue"]!.GetValue<double>(),
                    text["alpha"]!.GetValue<double>(), text["alignment"]!.GetValue<string>(),
                    text["lineSpacingPoints"]!.GetValue<double>(), text["trackingPoints"]!.GetValue<double>(),
                    "point", null);
            var box = layout["box"]?.AsObject()
                ?? throw new InvalidDataException("Text layout data is invalid.");
            return new TextLayerMetadata(Guid.Parse(record["id"]!.GetValue<string>()),
                record["imageFile"]?.GetValue<string>() ?? "", text["content"]!.GetValue<string>(),
                text["fontPostScriptName"]!.GetValue<string>(), text["fontSizePoints"]!.GetValue<double>(),
                text["red"]!.GetValue<double>(), text["green"]!.GetValue<double>(), text["blue"]!.GetValue<double>(),
                text["alpha"]!.GetValue<double>(), text["alignment"]!.GetValue<string>(),
                text["lineSpacingPoints"]!.GetValue<double>(), text["trackingPoints"]!.GetValue<double>(),
                "box", box["width"]!.GetValue<double>());
        }).ToArray();
    public bool HasGroups => Layers.Any(layer => layer.IsGroup);
    public string SourceDirectory => SavedDirectory ?? throw new InvalidOperationException("This document has not been saved yet.");
    public int Width => Current["width"]!.GetValue<int>();
    public int Height => Current["height"]!.GetValue<int>();
    public double Resolution => Current["resolution"]?.GetValue<double>() ?? 72;
    public string ImageName => Current["layers"]!.AsArray().Count == 1
        ? Current["layers"]![0]?["imageFile"]?.GetValue<string>() ?? "" : "";
    internal IReadOnlyDictionary<string, byte[]> AssetHashes { get; private set; }
    public bool CanEdit { get; }
    public int SourceFormatVersion { get; private set; }
    public bool LegacyUpgradePending => CanEdit && SourceFormatVersion is >= 1 and < 8;
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
            IsText = layer["text"] is not null,
            IsAdjustment = layer["adjustment"] is not null,
            AdjustmentKind = layer["adjustment"]?["kind"]?.GetValue<string>(),
            IsShape = layer["shape"] is not null,
            ShapeKind = layer["shape"]?["kind"]?.GetValue<string>(),
            IsGradient = layer["gradient"] is not null,
            ParentId = layer["parentID"] is { } parent ? Guid.Parse(parent.GetValue<string>()) : null,
            Opacity = layer["opacity"]?.GetValue<double>() ?? 1,
            BlendMode = layer["blendMode"]?.GetValue<string>() ?? "Normal",
            HasMask = layer["maskFile"] is not null,
            MaskEnabled = layer["maskFile"] is not null && (layer["maskEnabled"]?.GetValue<bool>() ?? true),
            MaskLinked = layer["maskLinked"]?.GetValue<bool>() ?? true,
            MaskSourceId = layer["maskSourceID"] is { } source ? Guid.Parse(source.GetValue<string>()) : null
        }).ToArray();
    public Guid? PreviousSiblingId(Guid layerId)
    {
        int index = FindLayer(layerId);
        Guid? parentId = Layers[index].ParentId;
        for (int candidate = index - 1; candidate >= 0; candidate--)
            if (Layers[candidate].ParentId == parentId)
                return Layers[candidate].Id;
        return null;
    }
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
            rasters.Count != Layers.Count(layer => !layer.IsGroup && !layer.IsAdjustment) ||
            Layers.Where(layer => !layer.IsGroup && !layer.IsAdjustment).Any(layer => !rasters.ContainsKey(layer.Id)))
            throw new InvalidOperationException("Layer rasters can only be attached to a newly opened editable project.");
        foreach (TileRaster raster in rasters.Values) CheckRasterSize(raster);
        if (masks is not null)
        {
            if (Layers.Any(layer => layer.HasMask != masks.ContainsKey(layer.Id)))
                throw new InvalidOperationException("Loaded layer masks do not match the project manifest.");
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
        if (Current["layers"]![index]!["adjustment"] is not null)
            throw new InvalidOperationException("Adjustment layers do not have a raster asset.");
        return TryGetLoadedLayerRaster(layerId, out var raster) ? raster
            : throw new InvalidOperationException("Layer rasters have not been loaded.");
    }

    public ShapeSettings GetShape(Guid layerId)
    {
        int index = FindLayer(layerId);
        if (!ShapeSettings.TryRead(Current["layers"]![index]!["shape"], out var settings))
            throw new InvalidOperationException("Layer is not a valid shape layer.");
        return settings;
    }

    public GradientSettings GetGradient(Guid layerId)
    {
        int index = FindLayer(layerId);
        if (!GradientSettings.TryRead(Current["layers"]![index]!["gradient"], out var settings))
            throw new InvalidOperationException("Layer is not a gradient layer.");
        return settings;
    }

    public void UpdateTextLayer(TextLayerMetadata metadata, TileRaster raster)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only.");
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(raster);
        int index = FindLayer(metadata.Id);
        var layer = Current["layers"]![index]!.AsObject();
        if (layer["text"] is not JsonObject)
            throw new InvalidOperationException("Layer is not a text layer.");
        if (metadata.ImageFile != (layer["imageFile"]?.GetValue<string>() ?? ""))
            throw new ArgumentException("Text image asset cannot be changed by a metadata edit.", nameof(metadata));
        ValidateTextMetadata(metadata);
        CheckRasterSize(raster);
        var next = (JsonObject)Current.DeepClone();
        var text = next["layers"]![index]!["text"]!.AsObject();
        text["alignment"] = metadata.Alignment;
        text["alpha"] = metadata.Alpha;
        text["blue"] = metadata.Blue;
        text["content"] = metadata.Content;
        text["fontPostScriptName"] = metadata.FontPostScriptName;
        text["fontSizePoints"] = metadata.FontSizePoints;
        text["green"] = metadata.Green;
        text["layout"] = metadata.Layout == "point"
            ? new JsonObject { ["point"] = new JsonObject() }
            : new JsonObject { ["box"] = new JsonObject { ["width"] = metadata.BoxWidth!.Value } };
        text["lineSpacingPoints"] = metadata.LineSpacingPoints;
        text["red"] = metadata.Red;
        text["trackingPoints"] = metadata.TrackingPoints;
        var rasters = new Dictionary<Guid, TileRaster>(
            snapshots[cursor].LayerRasters ?? throw new InvalidOperationException("Layer rasters have not been loaded."))
        {
            [metadata.Id] = raster
        };
        Commit(new Snapshot(next, rasters, snapshots[cursor].LayerMasks, ++nextRevision));
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
        if (Layers[index].IsAdjustment) throw new NotSupportedException("调整层不支持图层蒙版。");
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
        if (Layers[index].IsAdjustment) throw new NotSupportedException("调整层不支持图层蒙版。");
        if (Current["layers"]![index]!["maskFile"] is null)
            throw new InvalidOperationException("Layer does not have a raster mask.");
        var current = snapshots[cursor].LayerMasks
            ?? throw new InvalidOperationException("Layer masks have not been loaded.");
        if (mask.Width != current[layerId].Width || mask.Height != current[layerId].Height)
            throw new ArgumentException("Mask dimensions do not match the existing mask.", nameof(mask));
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

    public bool IsLayerMaskLinked(Guid layerId)
    {
        int index = FindLayer(layerId);
        if (Current["layers"]![index]!["maskFile"] is null)
            throw new InvalidOperationException("Layer does not have a raster mask.");
        return Current["layers"]![index]!["maskLinked"]?.GetValue<bool>() ?? true;
    }

    public LayerTransformInfo GetLayerMaskTransform(Guid layerId)
    {
        int index = FindLayer(layerId);
        var layer = Current["layers"]![index]!.AsObject();
        if (layer["maskFile"] is null) throw new InvalidOperationException("Layer does not have a raster mask.");
        return layer["maskPlacement"] is JsonObject placement
            ? LayerTransformInfo.Read(placement) : GetLayerTransform(layerId);
    }

    public void SetLayerMaskLinked(Guid layerId, bool linked)
    {
        RequireMaskEditing();
        int index = FindLayer(layerId);
        var layer = Current["layers"]![index]!.AsObject();
        if (layer["maskFile"] is null) throw new InvalidOperationException("Layer does not have a raster mask.");
        bool current = layer["maskLinked"]?.GetValue<bool>() ?? true;
        if (current == linked) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["maskLinked"] = linked;
        if (!linked && next["layers"]![index]!["maskPlacement"] is null)
            next["layers"]![index]!["maskPlacement"] = layer["transform"]!.DeepClone();
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void SetLayerMaskTransform(Guid layerId, double x, double y, double width, double height, double rotation)
    {
        RequireMaskEditing();
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) ||
            !double.IsFinite(rotation) || width is < 1 or > 300000 || height is < 1 or > 300000 ||
            Math.Abs(x) > 1000000 || Math.Abs(y) > 1000000)
            throw new ArgumentOutOfRangeException(nameof(width));
        int index = FindLayer(layerId);
        var current = Current["layers"]![index]!.AsObject();
        if (current["maskFile"] is null) throw new InvalidOperationException("Layer does not have a raster mask.");
        var currentPlacement = current["maskPlacement"]?.AsObject();
        if (currentPlacement is not null &&
            currentPlacement["origin"]?.AsArray() is { Count: 2 } origin &&
            currentPlacement["size"]?.AsArray() is { Count: 2 } size &&
            origin[0]!.GetValue<double>() == x && origin[1]!.GetValue<double>() == y &&
            size[0]!.GetValue<double>() == width && size[1]!.GetValue<double>() == height &&
            (currentPlacement["rotation"]?.GetValue<double>() ?? 0) == rotation) return;
        var next = (JsonObject)Current.DeepClone();
        var layer = next["layers"]![index]!.AsObject();
        var placement = currentPlacement is null
            ? (JsonObject)current["transform"]!.DeepClone() : (JsonObject)currentPlacement.DeepClone();
        placement["origin"] = new JsonArray(x, y);
        placement["size"] = new JsonArray(width, height);
        placement["rotation"] = rotation;
        layer["maskPlacement"] = placement;
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void ReplaceLayerRaster(Guid layerId, TileRaster raster)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        int index = FindLayer(layerId);
        if (Layers[index].IsAdjustment) throw new NotSupportedException("调整层没有可直接编辑的像素。");
        if (Current["layers"]![index]!["text"] is not null)
            throw new NotSupportedException("Text layers require the text renderer to keep metadata and pixels in sync.");
        CheckRasterSize(raster);
        var current = snapshots[cursor].LayerRasters
            ?? throw new InvalidOperationException("Layer rasters have not been loaded.");
        if (ReferenceEquals(current[layerId], raster)) return;
        var next = new Dictionary<Guid, TileRaster>(current) { [layerId] = raster };
        var manifest = (JsonObject)Current.DeepClone();
        manifest["layers"]![index]!.AsObject().Remove("shape");
        manifest["layers"]![index]!.AsObject().Remove("gradient");
        Commit(new Snapshot(manifest, next, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void ReplaceLayerRasterAndMask(Guid layerId, TileRaster raster, GrayTileRaster mask)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        int index = FindLayer(layerId);
        if (Current["layers"]![index]!["text"] is not null)
            throw new NotSupportedException("Text layers require the text renderer to keep metadata and pixels in sync.");
        if (Current["layers"]![index]!["maskFile"] is null)
            throw new InvalidOperationException("Layer does not have a raster mask.");
        CheckRasterSize(raster);
        var currentRasters = snapshots[cursor].LayerRasters
            ?? throw new InvalidOperationException("Layer rasters have not been loaded.");
        var currentMasks = snapshots[cursor].LayerMasks
            ?? throw new InvalidOperationException("Layer masks have not been loaded.");
        if (mask.Width != currentMasks[layerId].Width || mask.Height != currentMasks[layerId].Height)
            throw new ArgumentException("Mask dimensions do not match the existing mask.", nameof(mask));
        if (ReferenceEquals(currentRasters[layerId], raster) && ReferenceEquals(currentMasks[layerId], mask)) return;
        var nextRasters = new Dictionary<Guid, TileRaster>(currentRasters) { [layerId] = raster };
        var nextMasks = new Dictionary<Guid, GrayTileRaster>(currentMasks) { [layerId] = mask };
        var manifest = (JsonObject)Current.DeepClone();
        if (!ReferenceEquals(currentRasters[layerId], raster))
        {
            manifest["layers"]![index]!.AsObject().Remove("shape");
            manifest["layers"]![index]!.AsObject().Remove("gradient");
        }
        Commit(new Snapshot(manifest, nextRasters, nextMasks, ++nextRevision));
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
        if (Layers[index].IsAdjustment && mode != "Normal")
            throw new NotSupportedException("Adjustment layers only support Normal blending.");
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
                layers[sourceIndex]!["isGroup"]?.GetValue<bool>() == true ||
                Layers[targetIndex].IsAdjustment || Layers[sourceIndex].IsAdjustment)
                throw new NotSupportedException("组图层和调整层不能作为当前剪贴关系。");
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
        if (rasters.Count != Layers.Count(layer => !layer.IsGroup && !layer.IsAdjustment) ||
            rasters.Any(pair => !Layers.Any(layer => layer.Id == pair.Key && !layer.IsGroup && !layer.IsAdjustment) ||
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

    public Guid AddShapeLayer(string name, ShapeSettings settings, int destinationIndex)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var layer = CreateBlankLayer(name, Width, Height);
        layer["shape"] = settings.ToJson();
        return InsertLayer(layer, RasterCompositor.CreateShape(Width, Height, settings), destinationIndex);
    }

    public void SetShape(Guid layerId, ShapeSettings settings)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int index = FindLayer(layerId);
        FlatLayerInfo layer = Layers[index];
        if (layer.IsGroup || layer.IsAdjustment || layer.IsText)
            throw new InvalidOperationException("Only raster layers can be shape layers.");
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["shape"] = settings.ToJson();
        next["layers"]![index]!.AsObject().Remove("gradient");
        LayerTransformInfo transform = GetLayerTransform(layerId);
        var rasters = new Dictionary<Guid, TileRaster>(snapshots[cursor].LayerRasters!)
        {
            [layerId] = RasterCompositor.CreateShape(Width, Height, settings, transform.Width, transform.Height)
        };
        Commit(new Snapshot(next, rasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public Guid AddGradientLayer(string name, GradientSettings settings, int destinationIndex)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var layer = CreateBlankLayer(name, Width, Height);
        layer["gradient"] = settings.ToJson();
        return InsertLayer(layer, RasterCompositor.CreateGradient(Width, Height, settings), destinationIndex);
    }

    public void SetGradient(Guid layerId, GradientSettings settings)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int index = FindLayer(layerId);
        FlatLayerInfo layer = Layers[index];
        if (layer.IsGroup || layer.IsAdjustment || layer.IsText)
            throw new InvalidOperationException("Only raster layers can be gradient layers.");
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["gradient"] = settings.ToJson();
        next["layers"]![index]!.AsObject().Remove("shape");
        var rasters = new Dictionary<Guid, TileRaster>(snapshots[cursor].LayerRasters!)
        {
            [layerId] = RasterCompositor.CreateGradient(Width, Height, settings)
        };
        Commit(new Snapshot(next, rasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public Guid AddRootRasterLayer(string name, TileRaster raster, int destinationIndex)
    {
        RequireGroupStructureEditing();
        if (!HasGroups) throw new NotSupportedException("Root raster insertion requires a grouped project.");
        CheckRasterSize(raster);
        if (destinationIndex < Layers.Count && Layers[destinationIndex].ParentId is not null)
            throw new NotSupportedException("Root raster insertion must stay outside group subtrees.");
        return InsertLayer(CreateBlankLayer(name, Width, Height), raster, destinationIndex);
    }

    public Guid AddRasterLayerToGroup(string name, TileRaster raster, int destinationIndex, Guid parentId)
    {
        RequireGroupStructureEditing();
        CheckRasterSize(raster);
        int parentIndex = FindLayer(parentId);
        if (!Layers[parentIndex].IsGroup)
            throw new ArgumentException("The destination parent must be a group.", nameof(parentId));
        if (destinationIndex <= parentIndex || destinationIndex > Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        int lastDescendant = parentIndex;
        for (int index = parentIndex + 1; index < Layers.Count; index++)
        {
            if (!IsDescendantOf(Layers[index], parentId)) break;
            lastDescendant = index;
        }
        if (destinationIndex > lastDescendant + 1)
            throw new NotSupportedException("The destination layer must stay inside its parent group.");
        var layer = CreateBlankLayer(name, Width, Height);
        layer["parentID"] = parentId.ToString("D");
        return InsertLayer(layer, raster, destinationIndex);
    }

    public Guid AddTextLayer(string name, TextLayerMetadata metadata, TileRaster raster, int destinationIndex)
    {
        RequireLayerStructureEditing();
        CheckRasterSize(raster);
        Guid id = Guid.NewGuid();
        string imageFile = id.ToString("D").ToUpperInvariant() + ".png";
        TextLayerMetadata assigned = metadata with { Id = id, ImageFile = imageFile };
        ValidateTextMetadata(assigned);
        var layer = CreateBlankLayer(name, Width, Height);
        layer["id"] = id.ToString("D");
        layer["imageFile"] = imageFile;
        layer["opacity"] = 1d;
        layer["blendMode"] = "Normal";
        layer["text"] = CreateTextNode(assigned);
        return InsertLayer(layer, raster, destinationIndex);
    }

    public Guid AddExposureAdjustment(string name, ExposureSettings settings, int destinationIndex)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var layer = CreateBlankLayer(name, Width, Height);
        layer["adjustment"] = new JsonObject
        {
            ["kind"] = "Exposure",
            ["exposureSettings"] = settings.ToJson()
        };
        return InsertAdjustmentLayer(layer, destinationIndex);
    }

    public Guid AddLevelsAdjustment(string name, LevelsSettings settings, int destinationIndex)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var layer = CreateBlankLayer(name, Width, Height);
        layer["adjustment"] = new JsonObject
        {
            ["kind"] = "Levels",
            ["levelsSettings"] = settings.ToJson()
        };
        return InsertAdjustmentLayer(layer, destinationIndex);
    }

    public Guid AddHueSaturationAdjustment(string name, HueSaturationSettings settings, int destinationIndex)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var layer = CreateBlankLayer(name, Width, Height);
        layer["adjustment"] = new JsonObject
        {
            ["kind"] = "Hue/Saturation",
            ["hueSaturationSettings"] = settings.ToJson()
        };
        return InsertAdjustmentLayer(layer, destinationIndex);
    }

    public Guid AddCurvesAdjustment(string name, CurvesSettings settings, int destinationIndex)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var layer = CreateBlankLayer(name, Width, Height);
        layer["adjustment"] = new JsonObject
        {
            ["kind"] = "Curves",
            ["curvesSettings"] = settings.ToJson()
        };
        return InsertAdjustmentLayer(layer, destinationIndex);
    }

    public ExposureSettings GetExposureAdjustment(Guid layerId)
    {
        int index = FindLayer(layerId);
        var adjustment = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (adjustment["kind"]?.GetValue<string>() != "Exposure" ||
            !ExposureSettings.TryRead(adjustment["exposureSettings"], out var settings))
            throw new NotSupportedException("Only Exposure adjustment layers are supported in this slice.");
        return settings;
    }

    public void SetExposureAdjustment(Guid layerId, ExposureSettings settings)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int index = FindLayer(layerId);
        var current = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (current["kind"]?.GetValue<string>() != "Exposure")
            throw new NotSupportedException("Only Exposure adjustment layers are supported in this slice.");
        if (GetExposureAdjustment(layerId) == settings) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["adjustment"]!["exposureSettings"] = settings.ToJson();
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public LevelsSettings GetLevelsAdjustment(Guid layerId)
    {
        int index = FindLayer(layerId);
        var adjustment = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (adjustment["kind"]?.GetValue<string>() != "Levels" ||
            !LevelsSettings.TryRead(adjustment["levelsSettings"], out var settings))
            throw new NotSupportedException("Only Levels adjustment layers are supported in this slice.");
        return settings;
    }

    public void SetLevelsAdjustment(Guid layerId, LevelsSettings settings)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int index = FindLayer(layerId);
        var current = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (current["kind"]?.GetValue<string>() != "Levels")
            throw new NotSupportedException("Only Levels adjustment layers are supported in this slice.");
        if (GetLevelsAdjustment(layerId) == settings) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["adjustment"]!["levelsSettings"] = settings.ToJson();
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public HueSaturationSettings GetHueSaturationAdjustment(Guid layerId)
    {
        int index = FindLayer(layerId);
        var adjustment = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (adjustment["kind"]?.GetValue<string>() != "Hue/Saturation" ||
            !HueSaturationSettings.TryRead(adjustment["hueSaturationSettings"], out var settings))
            throw new NotSupportedException("Only Hue/Saturation adjustment layers are supported in this slice.");
        return settings;
    }

    public void SetHueSaturationAdjustment(Guid layerId, HueSaturationSettings settings)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int index = FindLayer(layerId);
        var current = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (current["kind"]?.GetValue<string>() != "Hue/Saturation")
            throw new NotSupportedException("Only Hue/Saturation adjustment layers are supported in this slice.");
        if (GetHueSaturationAdjustment(layerId) == settings) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["adjustment"]!["hueSaturationSettings"] = settings.ToJson();
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public CurvesSettings GetCurvesAdjustment(Guid layerId)
    {
        int index = FindLayer(layerId);
        var adjustment = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (adjustment["kind"]?.GetValue<string>() != "Curves" ||
            !CurvesSettings.TryRead(adjustment["curvesSettings"], out var settings))
            throw new NotSupportedException("Only Curves adjustment layers are supported in this slice.");
        return settings;
    }

    public void SetCurvesAdjustment(Guid layerId, CurvesSettings settings)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int index = FindLayer(layerId);
        var current = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (current["kind"]?.GetValue<string>() != "Curves")
            throw new NotSupportedException("Only Curves adjustment layers are supported in this slice.");
        if (GetCurvesAdjustment(layerId) == settings) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["adjustment"]!["curvesSettings"] = settings.ToJson();
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public Guid AddGradientMapAdjustment(string name, GradientMapSettings settings, int destinationIndex)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var layer = CreateBlankLayer(name, Width, Height);
        layer["adjustment"] = new JsonObject
        {
            ["kind"] = "Gradient Map",
            ["gradientMapSettings"] = settings.ToJson()
        };
        return InsertAdjustmentLayer(layer, destinationIndex);
    }

    public GradientMapSettings GetGradientMapAdjustment(Guid layerId)
    {
        int index = FindLayer(layerId);
        var adjustment = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (adjustment["kind"]?.GetValue<string>() != "Gradient Map" ||
            !GradientMapSettings.TryRead(adjustment["gradientMapSettings"], out var settings))
            throw new NotSupportedException("Only Gradient Map adjustment layers are supported in this slice.");
        return settings;
    }

    public void SetGradientMapAdjustment(Guid layerId, GradientMapSettings settings)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int index = FindLayer(layerId);
        var current = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (current["kind"]?.GetValue<string>() != "Gradient Map")
            throw new NotSupportedException("Only Gradient Map adjustment layers are supported in this slice.");
        if (GetGradientMapAdjustment(layerId) == settings) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["adjustment"]!["gradientMapSettings"] = settings.ToJson();
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public Guid AddGaussianBlurAdjustment(string name, GaussianBlurSettings settings, int destinationIndex)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var layer = CreateBlankLayer(name, Width, Height);
        layer["adjustment"] = new JsonObject
        {
            ["kind"] = "Gaussian Blur",
            ["gaussianBlurSettings"] = settings.ToJson()
        };
        return InsertAdjustmentLayer(layer, destinationIndex);
    }

    public GaussianBlurSettings GetGaussianBlurAdjustment(Guid layerId)
    {
        int index = FindLayer(layerId);
        var adjustment = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (adjustment["kind"]?.GetValue<string>() != "Gaussian Blur" ||
            !GaussianBlurSettings.TryRead(adjustment["gaussianBlurSettings"], out var settings))
            throw new NotSupportedException("Only Gaussian Blur adjustment layers are supported in this slice.");
        return settings;
    }

    public void SetGaussianBlurAdjustment(Guid layerId, GaussianBlurSettings settings)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int index = FindLayer(layerId);
        var current = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (current["kind"]?.GetValue<string>() != "Gaussian Blur")
            throw new NotSupportedException("Only Gaussian Blur adjustment layers are supported in this slice.");
        if (GetGaussianBlurAdjustment(layerId) == settings) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["adjustment"]!["gaussianBlurSettings"] = settings.ToJson();
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public Guid AddMotionBlurAdjustment(string name, MotionBlurSettings settings, int destinationIndex)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var layer = CreateBlankLayer(name, Width, Height);
        layer["adjustment"] = new JsonObject
        {
            ["kind"] = "Motion Blur",
            ["motionBlurSettings"] = settings.ToJson()
        };
        return InsertAdjustmentLayer(layer, destinationIndex);
    }

    public MotionBlurSettings GetMotionBlurAdjustment(Guid layerId)
    {
        int index = FindLayer(layerId);
        var adjustment = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (adjustment["kind"]?.GetValue<string>() != "Motion Blur" ||
            !MotionBlurSettings.TryRead(adjustment["motionBlurSettings"], out var settings))
            throw new NotSupportedException("Only Motion Blur adjustment layers are supported in this slice.");
        return settings;
    }

    public void SetMotionBlurAdjustment(Guid layerId, MotionBlurSettings settings)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int index = FindLayer(layerId);
        var current = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (current["kind"]?.GetValue<string>() != "Motion Blur")
            throw new NotSupportedException("Only Motion Blur adjustment layers are supported in this slice.");
        if (GetMotionBlurAdjustment(layerId) == settings) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["adjustment"]!["motionBlurSettings"] = settings.ToJson();
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public Guid AddNoiseAdjustment(string name, NoiseSettings settings, int destinationIndex)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var layer = CreateBlankLayer(name, Width, Height);
        layer["adjustment"] = new JsonObject
        {
            ["kind"] = "Add Noise",
            ["noiseSettings"] = settings.ToJson()
        };
        return InsertAdjustmentLayer(layer, destinationIndex);
    }

    public NoiseSettings GetNoiseAdjustment(Guid layerId)
    {
        int index = FindLayer(layerId);
        var adjustment = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (adjustment["kind"]?.GetValue<string>() != "Add Noise" ||
            !NoiseSettings.TryRead(adjustment["noiseSettings"], out var settings))
            throw new NotSupportedException("Only Add Noise adjustment layers are supported in this slice.");
        return settings;
    }

    public void SetNoiseAdjustment(Guid layerId, NoiseSettings settings)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int index = FindLayer(layerId);
        var current = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (current["kind"]?.GetValue<string>() != "Add Noise")
            throw new NotSupportedException("Only Add Noise adjustment layers are supported in this slice.");
        if (GetNoiseAdjustment(layerId) == settings) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["adjustment"]!["noiseSettings"] = settings.ToJson();
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public Guid AddLensCorrectionAdjustment(string name, LensCorrectionSettings settings, int destinationIndex)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var layer = CreateBlankLayer(name, Width, Height);
        layer["adjustment"] = new JsonObject
        {
            ["kind"] = "Lens Correction",
            ["lensCorrectionSettings"] = settings.ToJson()
        };
        return InsertAdjustmentLayer(layer, destinationIndex);
    }

    public LensCorrectionSettings GetLensCorrectionAdjustment(Guid layerId)
    {
        int index = FindLayer(layerId);
        var adjustment = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (adjustment["kind"]?.GetValue<string>() != "Lens Correction" ||
            !LensCorrectionSettings.TryRead(adjustment["lensCorrectionSettings"], out var settings))
            throw new NotSupportedException("Only Lens Correction adjustment layers are supported in this slice.");
        return settings;
    }

    public void SetLensCorrectionAdjustment(Guid layerId, LensCorrectionSettings settings)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int index = FindLayer(layerId);
        var current = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (current["kind"]?.GetValue<string>() != "Lens Correction")
            throw new NotSupportedException("Only Lens Correction adjustment layers are supported in this slice.");
        if (GetLensCorrectionAdjustment(layerId) == settings) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["adjustment"]!["lensCorrectionSettings"] = settings.ToJson();
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public Guid AddGrainAdjustment(string name, GrainSettings settings, int destinationIndex)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var layer = CreateBlankLayer(name, Width, Height);
        layer["adjustment"] = new JsonObject
        {
            ["kind"] = "Grain",
            ["grainSettings"] = settings.ToJson()
        };
        return InsertAdjustmentLayer(layer, destinationIndex);
    }

    public GrainSettings GetGrainAdjustment(Guid layerId)
    {
        int index = FindLayer(layerId);
        var adjustment = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (adjustment["kind"]?.GetValue<string>() != "Grain" ||
            !GrainSettings.TryRead(adjustment["grainSettings"], out var settings))
            throw new NotSupportedException("Only Grain adjustment layers are supported in this slice.");
        return settings;
    }

    public void SetGrainAdjustment(Guid layerId, GrainSettings settings)
    {
        RequireLayerStructureEditing();
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int index = FindLayer(layerId);
        var current = Current["layers"]![index]!["adjustment"]?.AsObject()
            ?? throw new InvalidOperationException("Layer is not an adjustment layer.");
        if (current["kind"]?.GetValue<string>() != "Grain")
            throw new NotSupportedException("Only Grain adjustment layers are supported in this slice.");
        if (GetGrainAdjustment(layerId) == settings) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![index]!["adjustment"]!["grainSettings"] = settings.ToJson();
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public bool CanCopyLayerFrom(ProjectSession source, Guid sourceLayerId, int destinationIndex,
        Guid? destinationParentId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            ValidateLayerCopyFrom(source, sourceLayerId, destinationIndex, destinationParentId);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            return false;
        }
    }

    public Guid CopyLayerFrom(ProjectSession source, Guid sourceLayerId, int destinationIndex,
        Guid? destinationParentId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        RequireCrossProjectCopyEditing();
        int[] sourceIndexes = ValidateLayerCopyFrom(source, sourceLayerId, destinationIndex, destinationParentId);
        return InsertLayerStack(source, sourceIndexes, sourceLayerId, destinationIndex, destinationParentId);
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
        UpdateMaskPlacement(next["layers"]![index]!.AsObject(), current["transform"]!.AsObject(), transform);
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

    public void MoveGroup(Guid groupId, double offsetX, double offsetY, bool snap = false)
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
        if (snap) (offsetX, offsetY) = SnapMoveOffset([groupId], offsetX, offsetY);
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
        if (current["adjustment"] is not null)
            throw new NotSupportedException("调整层不支持图层变换。");
        var next = (JsonObject)Current.DeepClone();
        var transform = next["layers"]![index]!["transform"]?.AsObject()
            ?? throw new InvalidDataException("Layer transform data is missing.");
        string field = horizontal ? "flipX" : "flipY";
        transform[field] = !(transform[field]?.GetValue<bool>() ?? false);
        UpdateMaskPlacement(next["layers"]![index]!.AsObject(), current["transform"]!.AsObject(), transform);
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

    public void MoveLayerTransform(Guid layerId, double offsetX, double offsetY, bool snap = false)
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
        if (snap) (offsetX, offsetY) = SnapMoveOffset([layerId], offsetX, offsetY);
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
        if (current["adjustment"] is not null)
            throw new NotSupportedException("调整层不支持图层变换。");
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
        UpdateMaskPlacement(next["layers"]![index]!.AsObject(), currentTransform, transform);
        Commit(new Snapshot(next, ShapeRastersAfterResize(next, [index]), snapshots[cursor].LayerMasks, ++nextRevision));
    }

    public void TransformLayers(IReadOnlyList<Guid> layerIds, double offsetX, double offsetY,
        double scale, double rotation, bool snap = false)
    {
        RequireLayerStructureEditing();
        if (layerIds.Count == 0)
            throw new ArgumentException("At least one layer is required.", nameof(layerIds));
        if (!double.IsFinite(offsetX) || !double.IsFinite(offsetY) ||
            !double.IsFinite(scale) || scale <= 0 || scale > 100 || !double.IsFinite(rotation))
            throw new ArgumentOutOfRangeException(nameof(scale));

        Guid[] ids = layerIds.Distinct().ToArray();
        if (snap && scale == 1 && rotation == 0)
            (offsetX, offsetY) = SnapMoveOffset(ids, offsetX, offsetY);
        var transforms = ids.Select(id =>
        {
            int index = FindLayer(id);
            FlatLayerInfo layer = Layers[index];
            if (layer.IsGroup || layer.IsAdjustment || layer.ParentId is not null)
                throw new NotSupportedException("多层变换目前只支持无组根图层。");
            return (Index: index, Transform: GetLayerTransform(id));
        }).ToArray();
        if (offsetX == 0 && offsetY == 0 && scale == 1 && rotation == 0) return;
        var bounds = transforms.Select(item =>
        {
            LayerTransformInfo transform = item.Transform;
            double angle = transform.Rotation * Math.PI / 180;
            double halfWidth = (Math.Abs(Math.Cos(angle)) * transform.Width + Math.Abs(Math.Sin(angle)) * transform.Height) / 2;
            double halfHeight = (Math.Abs(Math.Sin(angle)) * transform.Width + Math.Abs(Math.Cos(angle)) * transform.Height) / 2;
            double centerX = transform.X + transform.Width / 2, centerY = transform.Y + transform.Height / 2;
            return (Left: centerX - halfWidth, Top: centerY - halfHeight,
                Right: centerX + halfWidth, Bottom: centerY + halfHeight);
        }).ToArray();
        double left = bounds.Min(item => item.Left);
        double top = bounds.Min(item => item.Top);
        double right = bounds.Max(item => item.Right);
        double bottom = bounds.Max(item => item.Bottom);
        double pivotX = (left + right) / 2;
        double pivotY = (top + bottom) / 2;
        double radians = rotation * Math.PI / 180;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        var next = (JsonObject)Current.DeepClone();
        bool changed = false;
        foreach (var item in transforms)
        {
            LayerTransformInfo current = item.Transform;
            double centerX = current.X + current.Width / 2;
            double centerY = current.Y + current.Height / 2;
            double relativeX = (centerX - pivotX) * scale;
            double relativeY = (centerY - pivotY) * scale;
            double nextCenterX = pivotX + relativeX * cos - relativeY * sin + offsetX;
            double nextCenterY = pivotY + relativeX * sin + relativeY * cos + offsetY;
            double nextWidth = current.Width * scale;
            double nextHeight = current.Height * scale;
            double nextX = nextCenterX - nextWidth / 2;
            double nextY = nextCenterY - nextHeight / 2;
            double nextRotation = current.Rotation + rotation;
            if (!double.IsFinite(nextX) || !double.IsFinite(nextY) || !double.IsFinite(nextWidth) ||
                !double.IsFinite(nextHeight) || nextWidth <= 0 || nextHeight <= 0 || !double.IsFinite(nextRotation))
                throw new ArgumentOutOfRangeException(nameof(scale));
            if (nextX == current.X && nextY == current.Y && nextWidth == current.Width &&
                nextHeight == current.Height && rotation == 0)
                continue;
            var transform = next["layers"]![item.Index]!["transform"]?.AsObject()
                ?? throw new InvalidDataException("Layer transform data is missing.");
            transform["origin"] = new JsonArray(nextX, nextY);
            transform["size"] = new JsonArray(nextWidth, nextHeight);
            transform["rotation"] = nextRotation;
            UpdateMaskPlacement(next["layers"]![item.Index]!.AsObject(),
                Current["layers"]![item.Index]!["transform"]!.AsObject(), transform);
            changed = true;
        }
        if (changed)
            Commit(new Snapshot(next, ShapeRastersAfterResize(next, transforms.Select(item => item.Index)), snapshots[cursor].LayerMasks, ++nextRevision));
    }

    private IReadOnlyDictionary<Guid, TileRaster>? ShapeRastersAfterResize(JsonObject next, IEnumerable<int> indexes)
    {
        var current = snapshots[cursor].LayerRasters;
        if (current is null) return null;
        Dictionary<Guid, TileRaster>? changed = null;
        foreach (int index in indexes)
        {
            JsonNode layer = next["layers"]![index]!;
            if (!ShapeSettings.TryRead(layer["shape"], out var shape) || shape.Kind != "Rectangle" || shape.CornerRadius <= 0)
                continue;
            LayerTransformInfo before = LayerTransformInfo.Read(Current["layers"]![index]!["transform"]!.AsObject());
            LayerTransformInfo after = LayerTransformInfo.Read(layer["transform"]!.AsObject());
            if (before.Width == after.Width && before.Height == after.Height) continue;
            changed ??= new Dictionary<Guid, TileRaster>(current);
            Guid id = Guid.Parse(layer["id"]!.GetValue<string>());
            changed[id] = RasterCompositor.CreateShape(Width, Height, shape, after.Width, after.Height);
        }
        return changed ?? current;
    }

    internal void ReplaceLayerTransformWithRaster(Guid layerId, TileRaster raster, GrayTileRaster? mask)
    {
        RequireLayerStructureEditing();
        int index = FindLayer(layerId);
        var currentLayer = Current["layers"]![index]!.AsObject();
        if (currentLayer["isGroup"]?.GetValue<bool>() == true)
            throw new ArgumentException("Group layers must use bake-ungroup.", nameof(layerId));
        if (currentLayer["adjustment"] is not null)
            throw new NotSupportedException("调整层不支持图层变换烘焙。");
        if (currentLayer["text"] is not null)
            throw new NotSupportedException("Text layers require the text renderer to keep metadata and pixels in sync.");
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
        next["layers"]![index]!.AsObject().Remove("maskPlacement");
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
        UpdateMaskPlacement(next["layers"]![index]!.AsObject(), currentTransform, transform);
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
        if (indexes.Any(index => Layers[index].IsAdjustment))
            throw new NotSupportedException("调整层不能加入组。");
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
        baked.Remove("maskLinked");
        baked.Remove("maskPlacement");
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

    private static JsonObject CreateTextNode(TextLayerMetadata metadata) =>
        new JsonObject
        {
            ["alignment"] = metadata.Alignment,
            ["alpha"] = metadata.Alpha,
            ["blue"] = metadata.Blue,
            ["content"] = metadata.Content,
            ["fontPostScriptName"] = metadata.FontPostScriptName,
            ["fontSizePoints"] = metadata.FontSizePoints,
            ["green"] = metadata.Green,
            ["layout"] = metadata.Layout == "point"
                ? new JsonObject { ["point"] = new JsonObject() }
                : new JsonObject { ["box"] = new JsonObject { ["width"] = metadata.BoxWidth!.Value } },
            ["lineSpacingPoints"] = metadata.LineSpacingPoints,
            ["red"] = metadata.Red,
            ["trackingPoints"] = metadata.TrackingPoints
        };

    public Guid DuplicateLayer(Guid layerId, string name)
    {
        RequireLayerStructureEditing();
        int index = FindLayer(layerId);
        var layer = (JsonObject)Current["layers"]![index]!.DeepClone();
        layer["name"] = name;
        if (Layers[index].IsAdjustment)
            return InsertAdjustmentLayer(layer, index + 1);
        GrayTileRaster? mask = Current["layers"]![index]!["maskFile"] is not null
            ? CloneMask(GetLayerMask(layerId) ?? throw new InvalidDataException("Layer mask asset is missing."))
            : null;
        return InsertLayer(layer, GetLayerRaster(layerId), index + 1, mask);
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
        lowerNode.Remove("maskLinked");
        lowerNode.Remove("maskPlacement");
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

    public void MergeLayerRoots(IReadOnlyList<Guid> layerIds, TileRaster mergedRaster)
    {
        RequireGroupStructureEditing();
        Guid[] distinctIds = layerIds.Distinct().ToArray();
        if (distinctIds.Length < 2)
            throw new InvalidOperationException("请至少选择两个图层。");
        if (mergedRaster.Width != Width || mergedRaster.Height != Height)
            throw new ArgumentException("Merged raster dimensions do not match the canvas.", nameof(mergedRaster));
        FlatLayerInfo[] layers = Layers.ToArray();
        (FlatLayerInfo Layer, int Index)[] selected = distinctIds.Select(id =>
        {
            int index = FindLayer(id);
            return (Layer: layers[index], Index: index);
        }).OrderBy(item => item.Index).ToArray();
        Guid? parentId = selected[0].Layer.ParentId;
        if (selected.Any(item => item.Layer.ParentId != parentId))
            throw new NotSupportedException("只能合并同级图层。");
        int[] siblingIndexes = layers.Select((layer, index) => (layer, index))
            .Where(item => item.layer.ParentId == parentId)
            .Select(item => item.index).ToArray();
        int[] selectedSiblingPositions = selected.Select(item => Array.IndexOf(siblingIndexes, item.Index)).OrderBy(index => index).ToArray();
        if (selectedSiblingPositions.Any(index => index < 0) ||
            selectedSiblingPositions[^1] - selectedSiblingPositions[0] + 1 != selectedSiblingPositions.Length)
            throw new InvalidOperationException("只能合并连续同级图层。");

        var included = selected.Select(item => item.Layer.Id).ToHashSet();
        foreach (FlatLayerInfo layer in layers)
        {
            if (layer.ParentId is not { } layerParent) continue;
            var seen = new HashSet<Guid>();
            while (seen.Add(layerParent))
            {
                if (included.Contains(layerParent))
                {
                    included.Add(layer.Id);
                    break;
                }
                FlatLayerInfo? parent = layers.SingleOrDefault(candidate => candidate.Id == layerParent);
                if (parent?.ParentId is not { } nextParent) break;
                layerParent = nextParent;
            }
        }
        foreach (FlatLayerInfo layer in layers)
        {
            bool includedLayer = included.Contains(layer.Id);
            if (includedLayer && layer.MaskSourceId is { } sourceId && !included.Contains(sourceId) ||
                !includedLayer && layer.MaskSourceId is { } externalSourceId && included.Contains(externalSourceId))
                throw new NotSupportedException("剪贴关系必须与选中的图层一起合并。");
        }

        FlatLayerInfo lower = selected[0].Layer;
        string mergedName = string.Join(" + ", selected.Select(item => item.Layer.Name));
        if (mergedName.Length > 1000) mergedName = lower.Name;
        var next = (JsonObject)Current.DeepClone();
        var nextLayers = next["layers"]!.AsArray();
        var lowerNode = nextLayers[selected[0].Index]!.AsObject();
        lowerNode["name"] = mergedName;
        lowerNode["isGroup"] = false;
        lowerNode["isVisible"] = selected.Any(item => item.Layer.IsVisible);
        lowerNode["opacity"] = 1d;
        lowerNode["blendMode"] = "Normal";
        lowerNode["imageFile"] = lower.Id.ToString("D").ToUpperInvariant() + ".png";
        lowerNode.Remove("maskFile");
        lowerNode.Remove("maskEnabled");
        lowerNode.Remove("maskLinked");
        lowerNode.Remove("maskPlacement");
        lowerNode.Remove("maskSourceID");
        lowerNode["transform"] = new JsonObject
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
            if (included.Contains(id) && id != lower.Id) nextLayers.RemoveAt(index);
        }
        next["activeLayerID"] = lower.Id.ToString("D");
        var currentRasters = snapshots[cursor].LayerRasters
            ?? throw new InvalidOperationException("Open the editable project through ImageProjectWorkflow first.");
        var nextRasters = new Dictionary<Guid, TileRaster>(currentRasters)
        {
            [lower.Id] = mergedRaster
        };
        foreach (Guid id in included)
            if (id != lower.Id) nextRasters.Remove(id);
        Dictionary<Guid, GrayTileRaster>? nextMasks = snapshots[cursor].LayerMasks is { } currentMasks
            ? new Dictionary<Guid, GrayTileRaster>(currentMasks) : null;
        if (nextMasks is not null)
        {
            foreach (Guid id in included) nextMasks.Remove(id);
            if (nextMasks.Count == 0) nextMasks = null;
        }
        Commit(new Snapshot(next, nextRasters, nextMasks, ++nextRevision));
    }

    private int[] ValidateLayerCopyFrom(ProjectSession source, Guid sourceLayerId, int destinationIndex,
        Guid? destinationParentId)
    {
        if (ReferenceEquals(this, source)) throw new ArgumentException("Source and target projects must differ.", nameof(source));
        if (!CanEdit || Current["version"]!.GetValue<int>() != 8)
            throw new NotSupportedException("Cross-project layer copy requires an editable v8 target project.");
        if (!source.CanEdit || source.Current["version"]!.GetValue<int>() != 8)
            throw new NotSupportedException("Cross-project layer copy requires an editable v8 source project.");
        if (Width != source.Width || Height != source.Height)
            throw new NotSupportedException("Cross-project layer copy requires matching canvas dimensions.");
        if (snapshots[cursor].LayerRasters is null || source.snapshots[source.cursor].LayerRasters is null)
            throw new InvalidOperationException("Both projects must be opened through the editable workflow.");
        if (destinationIndex < 0 || destinationIndex > Current["layers"]!.AsArray().Count)
            throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        ValidateCopyDestination(destinationIndex, destinationParentId);
        int sourceIndex = source.FindLayer(sourceLayerId);
        FlatLayerInfo sourceLayer = source.Layers[sourceIndex];
        if (source.HasGroups)
        {
            return sourceLayer.IsGroup
                ? ValidateGroupCopy(source, sourceLayerId, sourceIndex)
                : ValidateGroupedStackCopy(source, sourceLayer);
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
        foreach (int index in sourceIndexes)
        {
            FlatLayerInfo layer = source.Layers[index];
            if (layer.IsGroup) throw new NotSupportedException("Group layers cannot be copied across projects in this slice.");
            if (layer.HasMask && source.GetLayerMask(layer.Id) is null)
                throw new InvalidDataException("Source layer mask asset is missing.");
        }
        return sourceIndexes;
    }

    private static int[] ValidateGroupedStackCopy(ProjectSession source, FlatLayerInfo sourceLayer)
    {
        var stackIds = new HashSet<Guid> { sourceLayer.Id };
        bool changed;
        do
        {
            changed = false;
            foreach (FlatLayerInfo layer in source.Layers)
            {
                if (layer.MaskSourceId is { } sourceId && stackIds.Contains(sourceId) && stackIds.Add(layer.Id))
                    changed = true;
                if (stackIds.Contains(layer.Id) && layer.MaskSourceId is { } groupedSourceParentId && stackIds.Add(groupedSourceParentId))
                    changed = true;
            }
        } while (changed);
        int[] sourceIndexes = stackIds.Select(source.FindLayer).OrderBy(index => index).ToArray();
        Guid? commonParentId = source.Layers[sourceIndexes[0]].ParentId;
        if (sourceIndexes.Any(index => source.Layers[index].ParentId != commonParentId))
            throw new NotSupportedException("A grouped clipping stack must remain in one parent group.");
        foreach (FlatLayerInfo layer in source.Layers)
        {
            bool included = stackIds.Contains(layer.Id);
            if (included && layer.IsGroup)
                throw new NotSupportedException("Group layers cannot be copied as part of a clipping stack.");
            if (included && layer.HasMask && source.GetLayerMask(layer.Id) is null)
                throw new InvalidDataException("Source layer mask asset is missing.");
            if (included && layer.MaskSourceId is { } sourceId && !stackIds.Contains(sourceId) ||
                !included && layer.MaskSourceId is { } externalSourceId && stackIds.Contains(externalSourceId))
                throw new NotSupportedException("A grouped clipping stack cannot retain an external relationship.");
        }
        Guid? parentId = sourceLayer.ParentId;
        while (parentId is { } current)
        {
            FlatLayerInfo parent = source.Layers.Single(layer => layer.Id == current);
            if (!parent.IsGroup)
                throw new InvalidDataException("Grouped layer parent is not a group.");
            if (!source.IsGroupTransformIdentity(parent.Id))
                throw new NotSupportedException("A layer inside a transformed group must be copied with its group.");
            if (parent.HasMask && parent.MaskEnabled)
                throw new NotSupportedException("A layer inside an enabled group mask must be copied with its group.");
            parentId = parent.ParentId;
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
            if (included && layer.ParentId is { } parentId && !stackIds.Contains(parentId) && layer.Id != groupId)
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
        int destinationIndex, Guid? destinationParentId)
    {
        if (sourceIndexes.Count == 0) throw new ArgumentException("At least one source layer is required.", nameof(sourceIndexes));
        var sourceLayers = source.Current["layers"]!.AsArray();
        var idMap = sourceIndexes.ToDictionary(
            index => Guid.Parse((sourceLayers[index]!.AsObject())["id"]!.GetValue<string>()),
            _ => Guid.NewGuid());
        bool groupedStack = source.HasGroups && sourceIndexes.Count > 0 &&
            source.Layers[sourceIndexes[0]].ParentId is not null &&
            sourceIndexes.All(index => source.Layers[index].IsGroup is false);
        var next = (JsonObject)Current.DeepClone();
        var nextLayers = next["layers"]!.AsArray();
        var rasters = new Dictionary<Guid, TileRaster>(snapshots[cursor].LayerRasters!);
        var masks = snapshots[cursor].LayerMasks is { } currentMasks
            ? new Dictionary<Guid, GrayTileRaster>(currentMasks)
            : null;
        Guid sourceRootId = Guid.Parse((sourceLayers[sourceIndexes[0]]!.AsObject())["id"]!.GetValue<string>());
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
                {
                    if (groupedStack)
                    {
                        if (destinationParentId is { } groupedStackParent)
                            layer["parentID"] = groupedStackParent.ToString("D");
                        else
                            layer.Remove("parentID");
                    }
                    else if (sourceId != sourceRootId)
                        throw new InvalidDataException("Copied group relationship points outside the copied subtree.");
                    else if (destinationParentId is { } rootParentId)
                        layer["parentID"] = rootParentId.ToString("D");
                    else
                        layer.Remove("parentID");
                }
                else layer["parentID"] = targetParentId.ToString("D");
            }
            else if (sourceId == sourceRootId && destinationParentId is { } rootParent)
                layer["parentID"] = rootParent.ToString("D");
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

    private void ValidateCopyDestination(int destinationIndex, Guid? destinationParentId)
    {
        if (destinationParentId is not { } parentId) return;
        int parentIndex = FindLayer(parentId);
        if (Layers[parentIndex].IsGroup is false)
            throw new NotSupportedException("Cross-project copies can only target a group parent.");
        if (destinationIndex <= parentIndex || destinationIndex > Current["layers"]!.AsArray().Count)
            throw new NotSupportedException("Cross-project copy destination is outside the target group.");
        int lastDescendant = parentIndex;
        for (int index = parentIndex + 1; index < Layers.Count; index++)
        {
            if (!IsDescendantOf(Layers[index], parentId)) break;
            lastDescendant = index;
        }
        if (destinationIndex > lastDescendant + 1)
            throw new NotSupportedException("Cross-project copy destination is outside the target group.");
    }

    private bool IsDescendantOf(FlatLayerInfo layer, Guid ancestorId)
    {
        Guid? parentId = layer.ParentId;
        var seen = new HashSet<Guid>();
        while (parentId is { } current && seen.Add(current))
        {
            if (current == ancestorId) return true;
            FlatLayerInfo? parent = Layers.FirstOrDefault(candidate => candidate.Id == current);
            parentId = parent?.ParentId;
        }
        return false;
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

    private Guid InsertAdjustmentLayer(JsonObject layer, int destinationIndex)
    {
        string name = layer["name"]!.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 1000)
            throw new ArgumentException("Layer name must contain 1 to 1000 characters.", nameof(layer));
        int count = Current["layers"]!.AsArray().Count;
        if (destinationIndex < 0 || destinationIndex > count)
            throw new ArgumentOutOfRangeException(nameof(destinationIndex));
        if (count >= 10000) throw new NotSupportedException("Adding a layer exceeds the layer limit.");
        Guid id = Guid.NewGuid();
        layer["id"] = id.ToString("D");
        var next = (JsonObject)Current.DeepClone();
        next["layers"]!.AsArray().Insert(destinationIndex, layer);
        next["activeLayerID"] = id.ToString("D");
        Commit(new Snapshot(next, snapshots[cursor].LayerRasters, snapshots[cursor].LayerMasks, ++nextRevision));
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

    private void RequireCrossProjectCopyEditing()
    {
        if (!CanEdit || Current["version"]!.GetValue<int>() != 8)
            throw new NotSupportedException("Cross-project layer copy requires an editable v8 project.");
        if (snapshots[cursor].LayerRasters is null)
            throw new InvalidOperationException("Open the editable project through ImageProjectWorkflow first.");
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

    private (double X, double Y) SnapMoveOffset(IEnumerable<Guid> movingIds, double offsetX, double offsetY)
    {
        HashSet<Guid> moving = movingIds.ToHashSet();
        bool added;
        do
        {
            added = false;
            foreach (FlatLayerInfo layer in Layers)
                if (layer.ParentId is { } parent && moving.Contains(parent) && moving.Add(layer.Id)) added = true;
        } while (added);

        var movingBounds = Layers
            .Where(layer => moving.Contains(layer.Id) && (layer.ParentId is not { } parent || !moving.Contains(parent)))
            .Select(layer => Bounds(GetLayerTransform(layer.Id)))
            .ToArray();
        if (movingBounds.Length == 0) return (offsetX, offsetY);
        double left = movingBounds.Min(item => item.Left) + offsetX;
        double top = movingBounds.Min(item => item.Top) + offsetY;
        double right = movingBounds.Max(item => item.Right) + offsetX;
        double bottom = movingBounds.Max(item => item.Bottom) + offsetY;
        double[] xs = [0, Width / 2d, Width], ys = [0, Height / 2d, Height];
        foreach (FlatLayerInfo layer in Layers.Where(layer => layer.IsVisible && !moving.Contains(layer.Id)))
        {
            var bounds = Bounds(GetLayerTransform(layer.Id));
            xs = [.. xs, bounds.Left, (bounds.Left + bounds.Right) / 2, bounds.Right];
            ys = [.. ys, bounds.Top, (bounds.Top + bounds.Bottom) / 2, bounds.Bottom];
        }
        return (offsetX + Nearest([left, (left + right) / 2, right], xs),
            offsetY + Nearest([top, (top + bottom) / 2, bottom], ys));

        static (double Left, double Top, double Right, double Bottom) Bounds(LayerTransformInfo transform)
        {
            double radians = transform.Rotation * Math.PI / 180;
            double halfWidth = (Math.Abs(Math.Cos(radians)) * transform.Width + Math.Abs(Math.Sin(radians)) * transform.Height) / 2;
            double halfHeight = (Math.Abs(Math.Sin(radians)) * transform.Width + Math.Abs(Math.Cos(radians)) * transform.Height) / 2;
            double centerX = transform.X + transform.Width / 2, centerY = transform.Y + transform.Height / 2;
            return (centerX - halfWidth, centerY - halfHeight, centerX + halfWidth, centerY + halfHeight);
        }

        static double Nearest(double[] guides, double[] targets)
        {
            double best = 0;
            double distance = double.PositiveInfinity;
            foreach (double guide in guides)
            foreach (double target in targets)
            {
                double shift = target - guide;
                if (Math.Abs(shift) <= 10 && Math.Abs(shift) < distance)
                {
                    best = shift;
                    distance = Math.Abs(shift);
                }
            }
            return best;
        }
    }

    private static void UpdateMaskPlacement(JsonObject layer, JsonObject oldTransform, JsonObject newTransform)
    {
        if (layer["maskFile"] is null) return;
        bool linked = layer["maskLinked"]?.GetValue<bool>() ?? true;
        var placement = layer["maskPlacement"] as JsonObject;
        if (placement is null)
        {
            if (!linked) layer["maskPlacement"] = oldTransform.DeepClone();
            return;
        }
        if (!linked) return;
        LayerTransformInfo next = LayerTransformInfo.Read(placement).Following(
            LayerTransformInfo.Read(oldTransform), LayerTransformInfo.Read(newTransform));
        if (next == LayerTransformInfo.Read(newTransform))
        {
            layer.Remove("maskPlacement");
            return;
        }
        placement["origin"] = new JsonArray(next.X, next.Y);
        placement["size"] = new JsonArray(next.Width, next.Height);
        placement["rotation"] = next.Rotation;
        placement["flipX"] = next.FlipX;
        placement["flipY"] = next.FlipY;
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

    private static void ValidateTextMetadata(TextLayerMetadata metadata)
    {
        if (metadata.Content.Length is < 1 or > 1_000_000 ||
            metadata.FontPostScriptName.Length is < 1 or > 1024 ||
            metadata.Alignment is not ("left" or "center" or "right") ||
            metadata.Layout is not ("point" or "box") ||
            !double.IsFinite(metadata.FontSizePoints) || metadata.FontSizePoints is < 1 or > 2000 ||
            !double.IsFinite(metadata.Red) || metadata.Red is < 0 or > 1 ||
            !double.IsFinite(metadata.Green) || metadata.Green is < 0 or > 1 ||
            !double.IsFinite(metadata.Blue) || metadata.Blue is < 0 or > 1 ||
            !double.IsFinite(metadata.Alpha) || metadata.Alpha is < 0 or > 1 ||
            !double.IsFinite(metadata.LineSpacingPoints) || metadata.LineSpacingPoints is < -2000 or > 2000 ||
            !double.IsFinite(metadata.TrackingPoints) || metadata.TrackingPoints is < -2000 or > 2000 ||
            metadata.Layout == "box" && (!metadata.BoxWidth.HasValue ||
                !double.IsFinite(metadata.BoxWidth.Value) || metadata.BoxWidth.Value is < 1 or > 30000) ||
            metadata.Layout == "point" && metadata.BoxWidth is not null)
            throw new ArgumentException("Text metadata is invalid.", nameof(metadata));
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
        SourceFormatVersion = 8;
        AssetHashes = new Dictionary<string, byte[]>(assetHashes, StringComparer.OrdinalIgnoreCase);
        sourceLayerRasters = snapshots[cursor].LayerRasters;
        sourceLayerMasks = snapshots[cursor].LayerMasks;
        savedRevision = snapshots[cursor].Revision;
    }
}
