using System.Text.Json.Nodes;

namespace Compositor.Core;

public sealed class ProjectSession
{
    private sealed record Snapshot(JsonObject Manifest, TileRaster? Raster);
    private readonly List<Snapshot> snapshots;
    private int cursor;
    private Snapshot savedSnapshot;
    private TileRaster? sourceRaster;

    internal ProjectSession(string sourceDirectory, JsonObject manifest, string imageName, bool canEdit, ReadOnlyMemory<byte> imageHash)
    {
        SourceDirectory = sourceDirectory;
        ImageName = imageName;
        ImageHash = imageHash;
        CanEdit = canEdit;
        snapshots = [new Snapshot(manifest, null)];
        savedSnapshot = snapshots[0];
    }

    public string SourceDirectory { get; private set; }
    public string ImageName { get; }
    internal ReadOnlyMemory<byte> ImageHash { get; private set; }
    public bool CanEdit { get; }
    public bool IsDirty => !ReferenceEquals(snapshots[cursor], savedSnapshot);
    public string LayerName => Current["layers"]![0]!["name"]!.GetValue<string>();
    public TileRaster? Raster => snapshots[cursor].Raster;
    internal bool RequiresRasterEncoding => Raster is not null && !ReferenceEquals(Raster, sourceRaster);
    internal JsonObject Current => snapshots[cursor].Manifest;

    internal void AttachRaster(TileRaster raster)
    {
        if (!CanEdit || snapshots.Count != 1 || Raster is not null)
            throw new InvalidOperationException("Raster can only be attached to a newly opened editable project.");
        CheckRasterSize(raster);
        snapshots[0] = new Snapshot(Current, raster);
        savedSnapshot = snapshots[0];
        sourceRaster = raster;
    }

    public void ReplaceRaster(TileRaster raster)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        CheckRasterSize(raster);
        if (ReferenceEquals(Raster, raster)) return;
        Commit(new Snapshot(Current, raster));
    }

    public void RenameLayer(string name)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Layer name cannot be blank.", nameof(name));
        if (name.Length > 1000) throw new ArgumentOutOfRangeException(nameof(name));
        if (LayerName == name) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![0]!["name"] = name;
        Commit(new Snapshot(next, Raster));
    }

    private void Commit(Snapshot next)
    {
        snapshots.RemoveRange(cursor + 1, snapshots.Count - cursor - 1);
        snapshots.Add(next);
        cursor++;
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

    internal void MarkSaved(string directory, ReadOnlyMemory<byte> imageHash)
    {
        SourceDirectory = directory;
        ImageHash = imageHash;
        sourceRaster = Raster;
        savedSnapshot = snapshots[cursor];
    }
}
