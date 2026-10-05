using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;

public sealed class EditorWorkspace
{
    private SoftBrushStroke? brush;
    private Guid brushLayer;
    public bool HasActiveStroke => brush is not null;
    public ProjectSession? Session { get; private set; }
    public TileRaster? Preview { get; private set; }
    public bool IsDirty => Session?.IsDirty ?? false;
    public string? ProjectDirectory => Session?.SourceDirectory;

    public void Open(string directory)
    {
        RequireIdle();
        var next = ImageProjectWorkflow.OpenEditable(directory);
        var preview = ImageProjectWorkflow.RenderFlatNormal(next);
        Session = next;
        Preview = preview;
    }

    public void Import(string image, string directory)
    {
        RequireIdle();
        var next = ImageProjectWorkflow.Import(image, directory);
        var preview = ImageProjectWorkflow.RenderFlatNormal(next);
        Session = next;
        Preview = preview;
    }

    public void Edit(Action<ProjectSession> operation)
    {
        RequireIdle();
        var session = RequireSession();
        operation(session);
        Preview = null;
        Preview = ImageProjectWorkflow.RenderFlatNormal(session);
    }

    public void Save()
    {
        RequireIdle();
        ImageProjectWorkflow.Save(RequireSession(), ProjectDirectory!);
    }

    public void SaveAs(string directory)
    {
        RequireIdle();
        if (Path.Exists(directory)) throw new IOException("请选择尚不存在的新工程文件夹。");
        ImageProjectWorkflow.Save(RequireSession(), directory);
    }

    public void Export(string path, bool jpeg)
    {
        RequireIdle();
        if (jpeg) ImageProjectWorkflow.ExportJpeg(RequireSession(), path, 95, (255, 255, 255));
        else ImageProjectWorkflow.ExportPng(RequireSession(), path);
    }

    public void BeginStroke(Guid layerId, SoftBrushSettings settings, BrushPoint point)
    {
        RequireIdle();
        var session = RequireSession();
        brushLayer = layerId;
        brush = new SoftBrushStroke(session.GetLayerRaster(layerId), settings);
        AppendStroke(point);
    }

    public void AppendStroke(BrushPoint point)
    {
        var active = brush ?? throw new InvalidOperationException("No active brush stroke.");
        active.Append(point);
        Preview = ImageProjectWorkflow.RenderFlatNormal(RequireSession(), brushLayer, active.Snapshot());
    }

    public void CommitStroke(BrushPoint point)
    {
        AppendStroke(point);
        var active = brush!;
        TileRaster pixels = active.Commit();
        brush = null;
        Edit(session => session.ReplaceLayerRaster(brushLayer, pixels));
    }

    public void CancelStroke()
    {
        if (brush is null) return;
        brush.Cancel(); brush = null;
        Preview = ImageProjectWorkflow.RenderFlatNormal(RequireSession());
    }

    private void RequireIdle()
    {
        if (brush is not null) throw new InvalidOperationException("请先结束或取消当前笔划。");
    }

    private ProjectSession RequireSession() => Session ?? throw new InvalidOperationException("请先打开或导入工程。");
}
