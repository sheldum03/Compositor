using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;

public sealed class EditorWorkspace
{
    public ProjectSession? Session { get; private set; }
    public TileRaster? Preview { get; private set; }
    public bool IsDirty => Session?.IsDirty ?? false;
    public string? ProjectDirectory => Session?.SourceDirectory;

    public void Open(string directory)
    {
        var next = ImageProjectWorkflow.OpenEditable(directory);
        var preview = ImageProjectWorkflow.RenderFlatNormal(next);
        Session = next;
        Preview = preview;
    }

    public void Import(string image, string directory)
    {
        var next = ImageProjectWorkflow.Import(image, directory);
        var preview = ImageProjectWorkflow.RenderFlatNormal(next);
        Session = next;
        Preview = preview;
    }

    public void Edit(Action<ProjectSession> operation)
    {
        var session = RequireSession();
        operation(session);
        Preview = null;
        Preview = ImageProjectWorkflow.RenderFlatNormal(session);
    }

    public void Save() => ImageProjectWorkflow.Save(RequireSession(), ProjectDirectory!);

    public void SaveAs(string directory)
    {
        if (Path.Exists(directory)) throw new IOException("请选择尚不存在的新工程文件夹。");
        ImageProjectWorkflow.Save(RequireSession(), directory);
    }

    public void Export(string path, bool jpeg)
    {
        if (jpeg) ImageProjectWorkflow.ExportJpeg(RequireSession(), path, 95, (255, 255, 255));
        else ImageProjectWorkflow.ExportPng(RequireSession(), path);
    }

    private ProjectSession RequireSession() => Session ?? throw new InvalidOperationException("请先打开或导入工程。");
}
