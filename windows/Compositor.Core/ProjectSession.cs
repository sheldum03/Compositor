using System.Text.Json.Nodes;

namespace Compositor.Core;

public sealed class ProjectSession
{
    private readonly List<JsonObject> snapshots;
    private int cursor;
    private JsonObject savedSnapshot;

    internal ProjectSession(string sourceDirectory, JsonObject manifest, string imageName, bool canEdit)
    {
        SourceDirectory = sourceDirectory;
        ImageName = imageName;
        CanEdit = canEdit;
        snapshots = [manifest];
        savedSnapshot = manifest;
    }

    public string SourceDirectory { get; private set; }
    public string ImageName { get; }
    public bool CanEdit { get; }
    public bool IsDirty => !ReferenceEquals(Current, savedSnapshot);
    public string LayerName => Current["layers"]![0]!["name"]!.GetValue<string>();
    internal JsonObject Current => snapshots[cursor];

    public void RenameLayer(string name)
    {
        if (!CanEdit) throw new NotSupportedException("This project is read-only in the first production slice.");
        if (name.Length > 1000) throw new ArgumentOutOfRangeException(nameof(name));
        if (LayerName == name) return;
        var next = (JsonObject)Current.DeepClone();
        next["layers"]![0]!["name"] = name;
        snapshots.RemoveRange(cursor + 1, snapshots.Count - cursor - 1);
        snapshots.Add(next);
        cursor++;
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

    internal void MarkSaved(string directory)
    {
        SourceDirectory = directory;
        savedSnapshot = Current;
    }
}
