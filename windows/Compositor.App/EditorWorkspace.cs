using Compositor.Core;
using Compositor.Imaging;
using Avalonia;

namespace Compositor.App;

public sealed class EditorWorkspace
{
    private SoftBrushStroke? brush;
    private Guid brushLayer;
    private TileRaster? clipboardRaster;
    private GrayTileRaster? clipboardMask;
    private SelectionMoveHistory? selectionMoveHistory;
    private bool selectionMoveUndone;
    private sealed record SelectionMoveHistory(GrayTileRaster Before, GrayTileRaster After);
    private readonly List<GrayTileRaster?> selectionHistory = [null];
    private int selectionHistoryCursor;
    public bool HasActiveStroke => brush is not null;
    public ProjectSession? Session { get; private set; }
    public TileRaster? Preview { get; private set; }
    public GrayTileRaster? Selection { get; private set; }
    public Rect? SelectionBounds { get; private set; }
    public SelectionOutline? SelectionOutline { get; private set; }
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
        ClearClipboard();
        ClearSelection();
        ResetSelectionHistory();
    }

    public void Open(string directory)
    {
        RequireIdle();
        var next = ImageProjectWorkflow.OpenEditable(directory);
        var preview = ImageProjectWorkflow.RenderFlatNormal(next);
        Session = next;
        Preview = preview;
        ClearClipboard();
        ClearSelection();
        ResetSelectionHistory();
    }

    public void Import(string image, string directory)
    {
        RequireIdle();
        var next = ImageProjectWorkflow.Import(image, directory);
        var preview = ImageProjectWorkflow.RenderFlatNormal(next);
        Session = next;
        Preview = preview;
        ClearClipboard();
        ClearSelection();
        ResetSelectionHistory();
    }

    public void Edit(Action<ProjectSession> operation)
    {
        RequireIdle();
        var session = RequireSession();
        selectionMoveHistory = null;
        operation(session);
        Preview = null;
        Preview = ImageProjectWorkflow.RenderFlatNormal(session);
    }

    public bool Undo()
    {
        RequireIdle();
        var session = RequireSession();
        if (!session.Undo())
        {
            if (selectionHistoryCursor == 0) return false;
            ApplySelectionState(selectionHistory[--selectionHistoryCursor]);
            return true;
        }
        if (selectionMoveHistory is { } move && !selectionMoveUndone)
        {
            RestoreSelection(move.Before);
            selectionMoveUndone = true;
        }
        Preview = ImageProjectWorkflow.RenderFlatNormal(session);
        return true;
    }

    public bool Redo()
    {
        RequireIdle();
        var session = RequireSession();
        if (!session.Redo())
        {
            if (selectionHistoryCursor >= selectionHistory.Count - 1) return false;
            ApplySelectionState(selectionHistory[++selectionHistoryCursor]);
            return true;
        }
        if (selectionMoveHistory is { } move && selectionMoveUndone)
        {
            RestoreSelection(move.After);
            selectionMoveUndone = false;
        }
        Preview = ImageProjectWorkflow.RenderFlatNormal(session);
        return true;
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

    public void SelectLasso(IReadOnlyList<Point> points, GraySelectionOperation operation = GraySelectionOperation.Replace)
    {
        selectionMoveHistory = null;
        var session = RequireSession();
        if (points.Count < 3) { ClearSelection(); return; }
        var polygon = points.Select(point => (point.X, point.Y)).ToArray();
        var next = GrayTileRaster.Polygon(session.Width, session.Height, polygon);
        Selection = operation == GraySelectionOperation.Replace
            ? next
            : Selection is { } current ? current.Combine(next, operation)
            : operation == GraySelectionOperation.Add ? next : null;
        if (Selection is null || Selection.CoveredPixels == 0) { ClearSelection(); return; }
        SelectionBounds = SelectionBoundsFor(Selection);
        RecordSelectionState();
        UpdateSelectionOutline();
    }

    public void SelectAll()
    {
        selectionMoveHistory = null;
        var session = RequireSession();
        Selection = GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width, session.Height);
        SelectionBounds = new Rect(0, 0, session.Width, session.Height);
        RecordSelectionState();
        UpdateSelectionOutline();
    }

    public void InvertSelection()
    {
        RequireIdle();
        selectionMoveHistory = null;
        if (Selection is not { } selection) throw new InvalidOperationException("请先建立选区。");
        var next = selection.Invert();
        if (next.CoveredPixels == 0)
        {
            ClearSelection();
            return;
        }
        Selection = next;
        SelectionBounds = SelectionBoundsFor(next);
        RecordSelectionState();
        UpdateSelectionOutline();
    }

    public void SelectMagicWand(Point point, int tolerance, int radius, bool contiguous,
        GraySelectionOperation operation = GraySelectionOperation.Replace)
    {
        selectionMoveHistory = null;
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
        RecordSelectionState();
        UpdateSelectionOutline();
    }

    private void SelectShape(Rect rectangle, GraySelectionOperation operation, bool ellipse)
    {
        selectionMoveHistory = null;
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
        RecordSelectionState();
        UpdateSelectionOutline();
    }

    public void ClearSelection()
    {
        selectionMoveHistory = null;
        Selection = null;
        SelectionBounds = null;
        SelectionOutline = null;
        RecordSelectionState();
    }

    public bool HasClipboard => clipboardRaster is not null && clipboardMask is not null;

    public void CopySelection()
    {
        RequireIdle();
        var session = RequireSession();
        if (Selection is not { } selection || session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("请先建立选区。");
        clipboardRaster = ApplySelection(session.GetLayerRaster(layerId), selection, keepSelected: true);
        clipboardMask = selection;
    }

    public void CopyMergedSelection()
    {
        RequireIdle();
        var session = RequireSession();
        if (Selection is not { } selection)
            throw new InvalidOperationException("请先建立选区。");
        clipboardRaster = ApplySelection(Preview ?? ImageProjectWorkflow.RenderFlatNormal(session), selection, keepSelected: true);
        clipboardMask = selection;
    }

    public void CutSelection()
    {
        CopySelection();
        var session = RequireSession();
        Guid layerId = session.ActiveLayerId!.Value;
        var next = ApplySelection(session.GetLayerRaster(layerId), Selection!, keepSelected: false);
        if (!SamePixels(session.GetLayerRaster(layerId), next)) Edit(current => current.ReplaceLayerRaster(layerId, next));
    }

    public void PasteSelection()
    {
        RequireIdle();
        var session = RequireSession();
        if (clipboardRaster is not { } source || clipboardMask is not { } mask || session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("没有可粘贴的选区。");
        var next = ReplaceSelection(session.GetLayerRaster(layerId), source, mask);
        if (!SamePixels(session.GetLayerRaster(layerId), next)) Edit(current => current.ReplaceLayerRaster(layerId, next));
    }

    public void MoveSelection(int offsetX, int offsetY)
    {
        RequireIdle();
        var session = RequireSession();
        if (Selection is not { } selection || session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("请先建立选区。");
        if (offsetX == 0 && offsetY == 0) return;
        var selectionBefore = selection;
        TileRaster current = session.GetLayerRaster(layerId);
        byte[] source = ToRgba(current), mask = ToCoverage(selection), moved = new byte[source.Length];
        source.CopyTo(moved, 0);
        for (int y = 0; y < session.Height; y++)
        for (int x = 0; x < session.Width; x++)
            if (mask[y * session.Width + x] != 0) moved.AsSpan((y * session.Width + x) * 4, 4).Clear();
        byte[] movedMask = new byte[mask.Length];
        for (int y = 0; y < session.Height; y++)
        for (int x = 0; x < session.Width; x++)
            if (mask[y * session.Width + x] != 0)
            {
                int targetX = x + offsetX, targetY = y + offsetY;
                if ((uint)targetX < (uint)session.Width && (uint)targetY < (uint)session.Height)
                {
                    source.AsSpan((y * session.Width + x) * 4, 4)
                        .CopyTo(moved.AsSpan((targetY * session.Width + targetX) * 4, 4));
                    movedMask[targetY * session.Width + targetX] = mask[y * session.Width + x];
                }
        }
        var next = FromRgba(session.Width, session.Height, moved);
        bool changed = !SamePixels(current, next);
        if (changed) Edit(currentSession => currentSession.ReplaceLayerRaster(layerId, next));
        var nextSelection = GrayTileRaster.FromCoverage(session.Width, session.Height, movedMask);
        if (nextSelection.CoveredPixels == 0) ClearSelection();
        else
        {
            Selection = nextSelection;
            SelectionBounds = SelectionBoundsFor(nextSelection);
        }
        selectionMoveHistory = changed ? new SelectionMoveHistory(selectionBefore, nextSelection) : null;
        selectionMoveUndone = false;
        UpdateSelectionOutline();
    }

    public void SelectLayerAlpha()
    {
        selectionMoveHistory = null;
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } layerId) throw new InvalidOperationException("当前工程没有活动图层。");
        var next = GrayTileRaster.FromCoverage(session.Width, session.Height, ToAlpha(session.GetLayerRaster(layerId)));
        if (next.CoveredPixels == 0) { ClearSelection(); return; }
        Selection = next;
        SelectionBounds = SelectionBoundsFor(next);
        RecordSelectionState();
        UpdateSelectionOutline();
    }

    private void RestoreSelection(GrayTileRaster selection)
    {
        Selection = selection;
        SelectionBounds = SelectionBoundsFor(selection);
        UpdateSelectionOutline();
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

    private void ClearClipboard()
    {
        clipboardRaster = null;
        clipboardMask = null;
    }

    private static TileRaster ApplySelection(TileRaster source, GrayTileRaster selection, bool keepSelected)
    {
        var result = new TileRaster(source.Width, source.Height);
        for (int row = 0; row * TileRaster.TileSize < source.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < source.Width; column++)
        {
            byte[] pixels = source.ReadTileCopy(column, row), mask = selection.ReadTileCopy(column, row);
            for (int i = 0; i < mask.Length; i++)
                if ((mask[i] != 0) != keepSelected) pixels.AsSpan(i * 4, 4).Clear();
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    private static TileRaster ReplaceSelection(TileRaster target, TileRaster source, GrayTileRaster selection)
    {
        var result = new TileRaster(target.Width, target.Height);
        for (int row = 0; row * TileRaster.TileSize < target.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < target.Width; column++)
        {
            byte[] pixels = target.ReadTileCopy(column, row), sourcePixels = source.ReadTileCopy(column, row), mask = selection.ReadTileCopy(column, row);
            for (int i = 0; i < mask.Length; i++)
                if (mask[i] != 0) sourcePixels.AsSpan(i * 4, 4).CopyTo(pixels.AsSpan(i * 4, 4));
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
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

    private static byte[] ToCoverage(GrayTileRaster raster)
    {
        byte[] coverage = new byte[checked(raster.Width * raster.Height)];
        for (int row = 0; row * TileRaster.TileSize < raster.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < raster.Width; column++)
        {
            var size = raster.TileDimensions(column, row);
            byte[] tile = raster.ReadTileCopy(column, row);
            for (int y = 0; y < size.Height; y++)
                tile.AsSpan(y * size.Width, size.Width).CopyTo(
                    coverage.AsSpan((row * TileRaster.TileSize + y) * raster.Width + column * TileRaster.TileSize, size.Width));
        }
        return coverage;
    }

    private static byte[] ToAlpha(TileRaster raster)
    {
        byte[] alpha = new byte[checked(raster.Width * raster.Height)];
        for (int row = 0; row * TileRaster.TileSize < raster.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < raster.Width; column++)
        {
            var size = raster.TileDimensions(column, row);
            byte[] tile = raster.ReadTileCopy(column, row);
            for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
                alpha[(row * TileRaster.TileSize + y) * raster.Width + column * TileRaster.TileSize + x] = tile[(y * size.Width + x) * 4 + 3];
        }
        return alpha;
    }

    private static TileRaster FromRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != (long)width * height * 4) throw new ArgumentException("RGBA data length does not match its dimensions.", nameof(rgba));
        var result = new TileRaster(width, height);
        for (int row = 0; row * TileRaster.TileSize < height; row++)
        for (int column = 0; column * TileRaster.TileSize < width; column++)
        {
            var size = result.TileDimensions(column, row);
            byte[] tile = new byte[size.Width * size.Height * 4];
            for (int y = 0; y < size.Height; y++)
                rgba.Slice(((row * TileRaster.TileSize + y) * width + column * TileRaster.TileSize) * 4, size.Width * 4)
                    .CopyTo(tile.AsSpan(y * size.Width * 4, size.Width * 4));
            result = result.ReplaceTile(column, row, tile);
        }
        return result;
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

    private void ApplySelectionState(GrayTileRaster? selection)
    {
        if (selection is null) ClearSelectionWithoutHistory();
        else RestoreSelection(selection);
    }

    private void RecordSelectionState()
    {
        selectionMoveHistory = null;
        if (ReferenceEquals(selectionHistory[selectionHistoryCursor], Selection)) return;
        if (selectionHistoryCursor < selectionHistory.Count - 1)
            selectionHistory.RemoveRange(selectionHistoryCursor + 1, selectionHistory.Count - selectionHistoryCursor - 1);
        selectionHistory.Add(Selection);
        selectionHistoryCursor++;
    }

    private void ResetSelectionHistory()
    {
        selectionHistory.Clear();
        selectionHistory.Add(Selection);
        selectionHistoryCursor = 0;
        selectionMoveHistory = null;
    }

    private void ClearSelectionWithoutHistory()
    {
        Selection = null;
        SelectionBounds = null;
        SelectionOutline = null;
    }

    private void UpdateSelectionOutline()
    {
        SelectionOutline = null;
        if (Selection is not { } selection) return;
        try
        {
            SelectionOutline = NativeSelections.Trace(ToCoverage(selection), selection.Width, selection.Height);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        catch (BadImageFormatException) { }
        catch (InvalidOperationException) { }
    }
}
