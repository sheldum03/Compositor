using System.Text.Json.Nodes;

namespace Compositor.Core;

public sealed class ProjectSession
{
    private const int MaxUndoSteps = 100;
    private const long MaxHistoryImageBytes = 256L * 1024 * 1024;
    private sealed record Snapshot(JsonObject Manifest, TileRaster? Raster, long Revision);
    private readonly List<Snapshot> snapshots;
    private int cursor;
    private long nextRevision;
    private long savedRevision;
    private TileRaster? sourceRaster;

    internal ProjectSession(string sourceDirectory, JsonObject manifest, string imageName, bool canEdit, ReadOnlyMemory<byte> imageHash)
    {
        SourceDirectory = sourceDirectory;
        ImageName = imageName;
        ImageHash = imageHash;
        CanEdit = canEdit;
        snapshots = [new Snapshot(manifest, null, 0)];
    }

    public string SourceDirectory { get; private set; }
    public string ImageName { get; }
    internal ReadOnlyMemory<byte> ImageHash { get; private set; }
    public bool CanEdit { get; }
    public bool IsDirty => snapshots[cursor].Revision != savedRevision;
    public string LayerName => Current["layers"]![0]!["name"]!.GetValue<string>();
    public TileRaster? Raster => snapshots[cursor].Raster;
    internal bool RequiresRasterEncoding => Raster is not null && !ReferenceEquals(Raster, sourceRaster);
    internal JsonObject Current => snapshots[cursor].Manifest;
    internal long HistoryExclusiveBytes
    {
        get
        {
            var currentBuffers = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
            if (Raster is { } current)
                foreach (byte[] buffer in current.Buffers) currentBuffers.Add(buffer);
            var historyBuffers = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
            long total = 0;
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (i == cursor || snapshots[i].Raster is not { } history) continue;
                foreach (byte[] buffer in history.Buffers)
                    if (!currentBuffers.Contains(buffer) && historyBuffers.Add(buffer)) total += buffer.Length;
            }
            return total;
        }
    }

    internal void AttachRaster(TileRaster raster)
    {
        if (!CanEdit || snapshots.Count != 1 || Raster is not null)
            throw new InvalidOperationException("Raster can only be attached to a newly opened editable project.");
        CheckRasterSize(raster);
        snapshots[0] = new Snapshot(Current, raster, 0);
        sourceRaster = raster;
    }

    public void ReplaceRaster(TileRaster raster)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        CheckRasterSize(raster);
        if (ReferenceEquals(Raster, raster)) return;
        Commit(new Snapshot(Current, raster, ++nextRevision));
    }

    public void RenameLayer(string name)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Layer name cannot be blank.", nameof(name));
        if (name.Length > 1000) throw new ArgumentOutOfRangeException(nameof(name));
        if (LayerName == name) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![0]!["name"] = name;
        Commit(new Snapshot(next, Raster, ++nextRevision));
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
        if (sourceRaster is not null && !snapshots.Any(snapshot => ReferenceEquals(snapshot.Raster, sourceRaster)))
            sourceRaster = null;
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
        savedRevision = snapshots[cursor].Revision;
    }
}
