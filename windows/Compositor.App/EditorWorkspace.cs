using Compositor.Core;
using Compositor.Imaging;
using Avalonia;

namespace Compositor.App;

public sealed class EditorWorkspace
{
    private SoftBrushStroke? brush;
    private Guid brushLayer;
    public bool HasActiveStroke => brush is not null;
    public ProjectSession? Session { get; private set; }
    public TileRaster? Preview { get; private set; }
    public GrayTileRaster? Selection { get; private set; }
    public Rect? SelectionBounds { get; private set; }
    public bool HasSelection => Selection is not null;
    public long SelectedPixels => Selection?.CoveredPixels ?? 0;
    public bool IsDirty => Session?.IsDirty ?? false;
    public string? ProjectDirectory => Session?.SavedDirectory;

    public void New(int width, int height, double resolution)
    {
        RequireIdle();
        var next = ProjectSession.CreateBlank(width, height, resolution);
        var preview = ImageProjectWorkflow.RenderFlatNormal(next);
        Session = next;
        Preview = preview;
        ClearSelection();
    }

    public void Open(string directory)
    {
        RequireIdle();
        var next = ImageProjectWorkflow.OpenEditable(directory);
        var preview = ImageProjectWorkflow.RenderFlatNormal(next);
        Session = next;
        Preview = preview;
        ClearSelection();
    }

    public void Import(string image, string directory)
    {
        RequireIdle();
        var next = ImageProjectWorkflow.Import(image, directory);
        var preview = ImageProjectWorkflow.RenderFlatNormal(next);
        Session = next;
        Preview = preview;
        ClearSelection();
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
        ImageProjectWorkflow.Save(RequireSession(), ProjectDirectory ?? throw new InvalidOperationException("请先选择新工程的保存位置。"));
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
        brush = new SoftBrushStroke(session.GetLayerRaster(layerId), settings, Selection);
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
        var session = RequireSession();
        if (SamePixels(session.GetLayerRaster(brushLayer), pixels))
            Preview = ImageProjectWorkflow.RenderFlatNormal(session);
        else
            Edit(current => current.ReplaceLayerRaster(brushLayer, pixels));
    }

    public void CancelStroke()
    {
        if (brush is null) return;
        brush.Cancel(); brush = null;
        Preview = ImageProjectWorkflow.RenderFlatNormal(RequireSession());
    }

    public void SelectRectangle(Rect rectangle, GraySelectionOperation operation = GraySelectionOperation.Replace) =>
        SelectShape(rectangle, operation, false);

    public void SelectEllipse(Rect rectangle, GraySelectionOperation operation = GraySelectionOperation.Replace) =>
        SelectShape(rectangle, operation, true);

    public void SelectMagicWand(Point point, int tolerance, int radius, bool contiguous,
        GraySelectionOperation operation = GraySelectionOperation.Replace)
    {
        var session = RequireSession();
        var raster = Preview ?? ImageProjectWorkflow.RenderFlatNormal(session);
        int x = (int)Math.Floor(point.X), y = (int)Math.Floor(point.Y);
        var wand = NativeSelections.Select(ToRgba(raster), session.Width, session.Height, session.Width * 4,
            x, y, radius, tolerance, contiguous);
        var next = GrayTileRaster.FromCoverage(session.Width, session.Height, wand.Mask);
        Selection = operation == GraySelectionOperation.Replace
            ? next
            : Selection is { } current ? current.Combine(next, operation)
            : operation == GraySelectionOperation.Add ? next : null;
        if (Selection is null || Selection.CoveredPixels == 0) { ClearSelection(); return; }
        SelectionBounds = SelectionBoundsFor(Selection);
    }

    private void SelectShape(Rect rectangle, GraySelectionOperation operation, bool ellipse)
    {
        var session = RequireSession();
        double left = Math.Max(0, Math.Min(rectangle.Left, rectangle.Right));
        double top = Math.Max(0, Math.Min(rectangle.Top, rectangle.Bottom));
        double right = Math.Min(session.Width, Math.Max(rectangle.Left, rectangle.Right));
        double bottom = Math.Min(session.Height, Math.Max(rectangle.Top, rectangle.Bottom));
        const double epsilon = 1e-9;
        int x0 = (int)Math.Floor(left + epsilon), y0 = (int)Math.Floor(top + epsilon);
        int x1 = (int)Math.Ceiling(right - epsilon), y1 = (int)Math.Ceiling(bottom - epsilon);
        if (x1 <= x0 || y1 <= y0) { ClearSelection(); return; }
        var next = ellipse
            ? GrayTileRaster.Ellipse(session.Width, session.Height, x0, y0, x1, y1)
            : GrayTileRaster.Rectangle(session.Width, session.Height, x0, y0, x1, y1);
        Selection = operation == GraySelectionOperation.Replace
            ? next
            : Selection is { } current ? current.Combine(next, operation)
            : operation == GraySelectionOperation.Add ? next : null;
        if (Selection is null || Selection.CoveredPixels == 0) { ClearSelection(); return; }
        SelectionBounds = new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    public void ClearSelection()
    {
        Selection = null;
        SelectionBounds = null;
    }

    private void RequireIdle()
    {
        if (brush is not null) throw new InvalidOperationException("请先结束或取消当前笔划。");
    }

    private ProjectSession RequireSession() => Session ?? throw new InvalidOperationException("请先打开或导入工程。");

    private static bool SamePixels(TileRaster first, TileRaster second)
    {
        if (first.Width != second.Width || first.Height != second.Height) return false;
        for (int row = 0; row * TileRaster.TileSize < first.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < first.Width; column++)
            if (!first.ReadTileCopy(column, row).SequenceEqual(second.ReadTileCopy(column, row))) return false;
        return true;
    }

    private static byte[] ToRgba(TileRaster raster)
    {
        byte[] rgba = new byte[checked(raster.Width * raster.Height * 4)];
        for (int row = 0; row * TileRaster.TileSize < raster.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < raster.Width; column++)
        {
            var size = raster.TileDimensions(column, row);
            byte[] tile = raster.ReadTileCopy(column, row);
            for (int y = 0; y < size.Height; y++)
                tile.AsSpan(y * size.Width * 4, size.Width * 4).CopyTo(
                    rgba.AsSpan(((row * TileRaster.TileSize + y) * raster.Width + column * TileRaster.TileSize) * 4,
                        size.Width * 4));
        }
        return rgba;
    }

    private static Rect SelectionBoundsFor(GrayTileRaster raster)
    {
        int left = raster.Width, top = raster.Height, right = -1, bottom = -1;
        for (int row = 0; row * TileRaster.TileSize < raster.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < raster.Width; column++)
        {
            var size = raster.TileDimensions(column, row);
            byte[] tile = raster.ReadTileCopy(column, row);
            for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
                if (tile[y * size.Width + x] != 0)
                {
                    int documentX = column * TileRaster.TileSize + x, documentY = row * TileRaster.TileSize + y;
                    left = Math.Min(left, documentX); top = Math.Min(top, documentY);
                    right = Math.Max(right, documentX); bottom = Math.Max(bottom, documentY);
                }
        }
        return new Rect(left, top, right - left + 1, bottom - top + 1);
    }
}
