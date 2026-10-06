using Compositor.Core;
using Compositor.Imaging;
using Avalonia;

namespace Compositor.App;

internal enum ResizeFilter
{
    Bilinear,
    Lanczos3
}

public sealed class EditorWorkspace
{
    private SoftBrushStroke? brush;
    private Guid brushLayer;
    private SoftBrushStroke? maskBrush;
    private Guid maskBrushLayer;
    private bool maskBrushReveal;
    private TileRaster? clipboardRaster;
    private GrayTileRaster? clipboardMask;
    private Guid? clipboardLayerId;
    private LayerTransformInfo? clipboardLayerTransform;
    private Guid? floatingLayerId;
    private TileRaster? floatingRaster;
    private GrayTileRaster? floatingMask;
    private GrayTileRaster? floatingLayerMask;
    private GrayTileRaster? floatingPreviousSelection;
    private Rect? floatingPreviousBounds;
    private SelectionOutline? floatingPreviousOutline;
    private SelectionMoveHistory? selectionMoveHistory;
    private bool selectionMoveUndone;
    private bool readOnlyTextCache;
    private bool textAssetsLoaded;
    private sealed record SelectionMoveHistory(GrayTileRaster Before, GrayTileRaster After);
    private readonly List<GrayTileRaster?> selectionHistory = [null];
    private int selectionHistoryCursor;
    public bool HasActiveStroke => brush is not null || maskBrush is not null;
    public bool HasFloatingSelection => floatingRaster is not null;
    public ProjectSession? Session { get; private set; }
    public bool CanEdit => Session?.CanEdit == true && !readOnlyTextCache;
    public string ReadOnlyNotice => Session?.HasTextLayers == true
        ? "只读缓存预览：文字字体或文字语义未就绪，请先选择字体后再编辑。"
        : "只读缓存预览：当前工程包含暂不支持的语义，编辑功能已禁用。";
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
        readOnlyTextCache = false;
        textAssetsLoaded = false;
        Session = next;
        Preview = preview;
        ClearClipboard();
        ClearSelection();
        ResetSelectionHistory();
    }

    public void Open(string directory)
    {
        RequireIdle();
        var next = ProjectStore.Open(directory);
        readOnlyTextCache = next.HasTextLayers && TextLayerWorkflow.Inspect(next).Any(status => !status.FontAvailable);
        textAssetsLoaded = false;
        var preview = ImageProjectWorkflow.RenderFlatNormal(next);
        if (next.CanEdit && !readOnlyTextCache) next = ImageProjectWorkflow.OpenEditable(directory);
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
        readOnlyTextCache = false;
        textAssetsLoaded = false;
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
        RequireEditableSession();
        selectionMoveHistory = null;
        operation(session);
        Preview = null;
        Preview = ImageProjectWorkflow.RenderFlatNormal(session);
    }

    public void UpdateText(TextLayerMetadata metadata)
    {
        RequireIdle();
        RequireEditableSession();
        TextLayerWorkflow.Update(RequireSession(), metadata);
        Preview = ImageProjectWorkflow.RenderFlatNormal(RequireSession());
    }

    public void ResolveTextFont(Guid layerId, string font)
    {
        RequireIdle();
        if (string.IsNullOrWhiteSpace(font) || !TextLayerWorkflow.AvailableFonts.Contains(font, StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("请选择一个已安装或已导入的字体。");
        ProjectSession session;
        if (!textAssetsLoaded)
        {
            session = ImageProjectWorkflow.OpenEditable(RequireSession().SourceDirectory);
            Session = session;
            textAssetsLoaded = true;
        }
        else session = RequireSession();
        TextLayerMetadata current = session.TextLayers.SingleOrDefault(text => text.Id == layerId)
            ?? throw new ArgumentException("所选图层不是文字图层。", nameof(layerId));
        TextLayerWorkflow.Update(session, current with { FontPostScriptName = font });
        readOnlyTextCache = TextLayerWorkflow.Inspect(session).Any(status => !status.FontAvailable);
        Preview = ImageProjectWorkflow.RenderFlatNormal(session);
    }

    public void AddTextLayer(string content = "文字", bool box = false)
    {
        RequireIdle();
        ProjectSession session = RequireSession();
        RequireEditableSession();
        string font = TextLayerWorkflow.AvailableFonts.FirstOrDefault()
            ?? throw new NotSupportedException("No usable font is installed.");
        double? boxWidth = box ? Math.Min(360, session.Width) : null;
        var metadata = new TextLayerMetadata(Guid.Empty, "", content, font, 18,
            0, 0, 0, 1, "left", 0, 0, box ? "box" : "point", boxWidth);
        TileRaster raster = TextLayerWorkflow.RenderText(metadata, session.Width, session.Height, session.Resolution);
        int index = session.ActiveLayerId is { } active
            ? session.Layers.ToList().FindIndex(layer => layer.Id == active) + 1
            : session.Layers.Count;
        session.AddTextLayer("文字", metadata, raster, index);
        Preview = ImageProjectWorkflow.RenderFlatNormal(session);
    }

    public void BakeGroupTransform(Guid groupId)
    {
        RequireIdle();
        RequireEditableSession();
        selectionMoveHistory = null;
        ImageProjectWorkflow.BakeGroupTransform(RequireSession(), groupId);
        Preview = ImageProjectWorkflow.RenderFlatNormal(RequireSession());
    }

    public void BakeLayerTransform(Guid layerId)
    {
        RequireIdle();
        RequireEditableSession();
        selectionMoveHistory = null;
        ImageProjectWorkflow.BakeLayerTransform(RequireSession(), layerId);
        Preview = ImageProjectWorkflow.RenderFlatNormal(RequireSession());
    }

    public void MergeActiveLayerDown()
    {
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } upperId)
            throw new InvalidOperationException("当前工程没有活动图层。");
        if (session.PreviousSiblingId(upperId) is not { } previousId)
            throw new InvalidOperationException("当前图层下方没有可合并的同级图层。");
        MergeSelectedLayers([previousId, upperId]);
    }

    public bool CanMoveLayer(Guid layerId, int offset)
    {
        if (offset is not (-1 or 1) || Session is null || !CanEdit || HasFloatingSelection)
            return false;
        int index = Session.Layers.ToList().FindIndex(layer => layer.Id == layerId);
        return index >= 0 && Session.CanMoveLayer(layerId, index + offset);
    }

    public bool CanMoveLayerTo(Guid layerId, int destinationIndex)
    {
        return Session is not null && CanEdit && !HasActiveStroke && !HasFloatingSelection &&
            Session.CanMoveLayerTo(layerId, destinationIndex);
    }

    public void MoveLayerTo(Guid layerId, int destinationIndex)
    {
        RequireIdle();
        var session = RequireSession();
        if (!session.CanMoveLayerTo(layerId, destinationIndex))
            throw new NotSupportedException("当前工程不支持该图层拖放位置。");
        Edit(current => current.MoveLayerTo(layerId, destinationIndex));
    }

    public bool CanCopyLayerTo(EditorWorkspace target, Guid layerId)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(this, target) || !CanEdit || !target.CanEdit || HasActiveStroke || HasFloatingSelection ||
            target.HasActiveStroke || target.HasFloatingSelection || Session is not { } sourceSession ||
            target.Session is not { } targetSession)
            return false;
        (int destinationIndex, Guid? destinationParentId) = CopyDestination(targetSession);
        return targetSession.CanCopyLayerFrom(sourceSession, layerId, destinationIndex, destinationParentId);
    }

    public void CopyLayerTo(EditorWorkspace target, Guid layerId)
    {
        ArgumentNullException.ThrowIfNull(target);
        RequireIdle();
        target.RequireIdle();
        var sourceSession = RequireSession();
        var targetSession = target.RequireSession();
        if (!CanCopyLayerTo(target, layerId))
            throw new NotSupportedException("跨工程图层拖放目前只支持相同画布尺寸的可编辑 v8 工程；复杂组、变换和剪贴关系仍需整体复制或先烘焙。");
        (int destinationIndex, Guid? destinationParentId) = CopyDestination(targetSession);
        target.Edit(current => current.CopyLayerFrom(sourceSession, layerId, destinationIndex, destinationParentId));
    }

    private static (int Index, Guid? ParentId) CopyDestination(ProjectSession target)
    {
        FlatLayerInfo[] layers = target.Layers.ToArray();
        if (target.ActiveLayerId is not { } activeId) return (layers.Length, null);
        int activeIndex = Array.FindIndex(layers, layer => layer.Id == activeId);
        if (activeIndex < 0) return (layers.Length, null);
        FlatLayerInfo active = layers[activeIndex];
        int insertion = activeIndex + 1;
        if (active.IsGroup)
            while (insertion < layers.Length && IsDescendantOf(layers[insertion], active.Id, layers)) insertion++;
        return (insertion, active.ParentId);
    }

    private static bool IsDescendantOf(FlatLayerInfo layer, Guid ancestorId, IReadOnlyList<FlatLayerInfo> layers)
    {
        Guid? parentId = layer.ParentId;
        var seen = new HashSet<Guid>();
        while (parentId is { } current && seen.Add(current))
        {
            if (current == ancestorId) return true;
            parentId = layers.FirstOrDefault(candidate => candidate.Id == current)?.ParentId;
        }
        return false;
    }

    public void MergeSelectedLayers(IReadOnlyList<Guid> layerIds)
    {
        RequireIdle();
        var session = RequireSession();
        var selected = ValidateMergeSelection(session, layerIds);
        bool grouped = session.HasGroups;
        TileRaster merged = grouped
            ? ImageProjectWorkflow.RenderLayersForMerge(session, selected.Select(item => item.Layer.Id).ToArray())
            : ImageProjectWorkflow.RenderFlatNormalLayers(session, selected.Select(item => item.Layer.Id).ToHashSet());
        Edit(editSession =>
        {
            Guid[] selectedIds = selected.Select(item => item.Layer.Id).ToArray();
            if (grouped) editSession.MergeLayerRoots(selectedIds, merged);
            else editSession.MergeLayers(selectedIds, merged);
        });
    }

    public bool CanMergeSelectedLayers(IReadOnlyList<Guid> layerIds)
    {
        if (!CanEdit || HasActiveStroke || HasFloatingSelection || Session is null) return false;
        try { _ = ValidateMergeSelection(Session, layerIds); return true; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        { return false; }
    }

    private static (FlatLayerInfo Layer, int Index)[] ValidateMergeSelection(ProjectSession session,
        IReadOnlyList<Guid> layerIds)
    {
        Guid[] distinctIds = layerIds.Distinct().ToArray();
        if (distinctIds.Length < 2)
            throw new InvalidOperationException("请至少选择两个图层。");
        FlatLayerInfo[] layers = session.Layers.ToArray();
        var selected = distinctIds.Select(id =>
        {
            int index = Array.FindIndex(layers, layer => layer.Id == id);
            if (index < 0) throw new ArgumentException("图层不属于当前工程。", nameof(layerIds));
            return (Layer: layers[index], Index: index);
        }).OrderBy(item => item.Index).ToArray();
        if (session.HasGroups) return ValidateGroupedMergeSelection(selected, layers);
        if (selected[^1].Index - selected[0].Index + 1 != selected.Length)
            throw new InvalidOperationException("只能合并连续图层。");
        if (selected.Any(item => item.Layer.IsGroup))
            throw new NotSupportedException("组图层暂不支持合并。");
        var selectedIds = selected.Select(item => item.Layer.Id).ToHashSet();
        foreach (FlatLayerInfo layer in session.Layers)
        {
            bool targetSelected = selectedIds.Contains(layer.Id);
            if (targetSelected && layer.MaskSourceId is { } targetSource && !selectedIds.Contains(targetSource))
                throw new NotSupportedException("剪贴目标必须与其源图层一起合并。");
            if (targetSelected && session.Layers.Any(candidate => candidate.MaskSourceId == layer.Id && !selectedIds.Contains(candidate.Id)))
                throw new NotSupportedException("剪贴源不能在目标图层之外被合并。");
        }
        return selected;
    }

    private static (FlatLayerInfo Layer, int Index)[] ValidateGroupedMergeSelection(
        (FlatLayerInfo Layer, int Index)[] selected, IReadOnlyList<FlatLayerInfo> layers)
    {
        Guid? parentId = selected[0].Layer.ParentId;
        if (selected.Any(item => item.Layer.ParentId != parentId))
            throw new NotSupportedException("只能合并同级图层。");
        int[] siblingIndexes = layers.Select((layer, index) => (layer, index))
            .Where(item => item.layer.ParentId == parentId).Select(item => item.index).ToArray();
        int[] siblingPositions = selected.Select(item => Array.IndexOf(siblingIndexes, item.Index))
            .OrderBy(index => index).ToArray();
        if (siblingPositions.Any(index => index < 0) ||
            siblingPositions[^1] - siblingPositions[0] + 1 != siblingPositions.Length)
            throw new InvalidOperationException("只能合并连续同级图层。");

        var included = selected.Select(item => item.Layer.Id).ToHashSet();
        foreach (FlatLayerInfo layer in layers)
            if (selected.Any(root => layer.Id != root.Layer.Id && IsDescendantOf(layer, root.Layer.Id, layers)))
                included.Add(layer.Id);
        foreach (FlatLayerInfo layer in layers)
        {
            bool includedLayer = included.Contains(layer.Id);
            if (includedLayer && layer.MaskSourceId is { } sourceId && !included.Contains(sourceId) ||
                !includedLayer && layer.MaskSourceId is { } externalSourceId && included.Contains(externalSourceId))
                throw new NotSupportedException("剪贴关系必须与选中的图层一起合并。");
        }
        return selected;
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
        RequireEditableSession();
        ImageProjectWorkflow.Save(RequireSession(), ProjectDirectory ?? throw new InvalidOperationException("请先选择新工程的保存位置。"));
    }

    public void SaveAs(string directory)
    {
        RequireIdle();
        RequireEditableSession();
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
        brush = new SoftBrushStroke(session.GetLayerRaster(layerId), settings,
            Selection is { } selection ? SelectionForLayer(session, layerId, selection) : null);
        AppendStroke(point);
    }

    public void AppendStroke(BrushPoint point)
    {
        var active = brush ?? throw new InvalidOperationException("No active brush stroke.");
        active.Append(DocumentToLayerPoint(RequireSession(), brushLayer, point));
        Preview = ImageProjectWorkflow.RenderFlatNormal(RequireSession(), brushLayer, active.Snapshot());
    }

    public void BeginMaskStroke(Guid layerId, SoftBrushSettings settings, BrushPoint point, bool reveal)
    {
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId != layerId || !session.Layers.Single(layer => layer.Id == layerId).HasMask)
            throw new InvalidOperationException("当前活动图层没有蒙版。");
        if (!session.IsLayerMaskEnabled(layerId))
            throw new InvalidOperationException("请先启用当前图层蒙版。");
        maskBrushLayer = layerId;
        maskBrushReveal = reveal;
        TileRaster brushBounds = session.Layers.Single(layer => layer.Id == layerId).IsGroup
            ? new TileRaster(session.Width, session.Height)
            : session.GetLayerRaster(layerId);
        maskBrush = new SoftBrushStroke(brushBounds, settings with { Color = [1, 1, 1] },
            Selection is { } selection ? SelectionForLayer(session, layerId, selection) : null);
        AppendMaskStroke(point);
    }

    public void AppendMaskStroke(BrushPoint point)
    {
        var active = maskBrush ?? throw new InvalidOperationException("No active mask stroke.");
        active.Append(DocumentToLayerPoint(RequireSession(), maskBrushLayer, point));
        Preview = RenderMaskStrokePreview(active.CoverageSnapshot());
    }

    public void CommitMaskStroke(BrushPoint point)
    {
        AppendMaskStroke(point);
        var active = maskBrush!;
        GrayTileRaster coverage = active.CoverageSnapshot();
        Guid layerId = maskBrushLayer;
        bool reveal = maskBrushReveal;
        maskBrush = null;
        var session = RequireSession();
        GrayTileRaster current = session.GetLayerMask(layerId)!;
        GrayTileRaster next = current.Combine(coverage,
            reveal ? GraySelectionOperation.Add : GraySelectionOperation.Subtract);
        if (SameCoverage(current, next)) Preview = ImageProjectWorkflow.RenderFlatNormal(session);
        else Edit(editSession => editSession.ReplaceLayerMask(layerId, next));
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
        if (brush is { } currentBrush)
        {
            currentBrush.Cancel(); brush = null;
        }
        else if (maskBrush is { } currentMaskBrush)
        {
            currentMaskBrush.Cancel(); maskBrush = null;
        }
        else return;
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

    public void FeatherSelection(int radius)
    {
        RequireIdle();
        selectionMoveHistory = null;
        if (Selection is not { } selection) throw new InvalidOperationException("请先建立选区。");
        GrayTileRaster next = selection.Blur(radius);
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
    public TileRaster? ClipboardRaster => clipboardRaster;
    public bool CanLayerViaCopy
    {
        get
        {
            if (!CanEdit || HasActiveStroke || HasFloatingSelection || Selection is not { CoveredPixels: > 0 } ||
                Session is not { } session || session.HasGroups || session.ActiveLayerId is not { } layerId)
                return false;
            FlatLayerInfo layer = session.Layers.Single(layer => layer.Id == layerId);
            return !layer.IsGroup &&
                (!layer.HasMask || session.GetLayerMask(layerId) is not null);
        }
    }

    public void LayerViaCopy()
    {
        RequireIdle();
        if (!CanLayerViaCopy)
            throw new NotSupportedException("Layer via Copy 目前只支持平面图层，组图层不支持。");
        var session = RequireSession();
        Guid sourceId = session.ActiveLayerId!.Value;
        int destinationIndex = session.Layers.ToList().FindIndex(layer => layer.Id == sourceId) + 1;
        TileRaster copied = ApplySelection(ImageProjectWorkflow.RenderLayerForCopy(session, sourceId), Selection!, keepSelected: true);
        Edit(current => current.AddRasterLayer("Layer via Copy", copied, destinationIndex));
        ClearSelectionWithoutHistory();
        ResetSelectionHistory();
    }

    public void PasteBitmapAsLayer(TileRaster bitmap, Point? center = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        RequireIdle();
        RequireEditableSession();
        var session = RequireSession();
        TileRaster centered = PositionRaster(bitmap, session.Width, session.Height, center);
        int destinationIndex = session.ActiveLayerId is { } layerId
            ? session.Layers.ToList().FindIndex(layer => layer.Id == layerId) + 1
            : session.Layers.Count;
        Edit(current => current.AddRasterLayer("Clipboard Image", centered, destinationIndex));
    }

    public bool CanPasteSelection
    {
        get
        {
            if (!HasClipboard || Session?.ActiveLayerId is not { } layerId ||
                Session.Layers.Single(layer => layer.Id == layerId).IsGroup)
                return false;
            if (Session.IsLayerTransformIdentity(layerId)) return clipboardLayerTransform is null;
            return clipboardLayerId == layerId && clipboardLayerTransform == Session.GetLayerTransform(layerId);
        }
    }

    public bool CanPasteSelectionFrom(EditorWorkspace source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(this, source)) return CanPasteSelection;
        if (!CanEdit || HasActiveStroke || HasFloatingSelection || source.HasActiveStroke ||
            source.clipboardRaster is not { } raster || source.clipboardMask is null ||
            source.Session is not { } sourceSession || Session is not { } session || session.ActiveLayerId is not { } layerId ||
            session.Width != raster.Width || session.Height != raster.Height)
            return false;
        FlatLayerInfo layer = session.Layers.Single(layer => layer.Id == layerId);
        if (layer.IsGroup || !session.IsLayerTransformIdentity(layerId)) return false;
        if (source.clipboardLayerTransform is null) return true;
        if (source.clipboardLayerId is not { } sourceLayerId) return false;
        FlatLayerInfo sourceLayer = sourceSession.Layers.SingleOrDefault(item => item.Id == sourceLayerId)!;
        return sourceLayer is not null && !sourceSession.HasGroups && !sourceLayer.IsGroup &&
            sourceSession.GetLayerTransform(sourceLayerId) == source.clipboardLayerTransform;
    }

    public void PasteSelectionFrom(EditorWorkspace source)
    {
        ArgumentNullException.ThrowIfNull(source);
        RequireIdle();
        source.RequireIdle();
        if (!CanPasteSelectionFrom(source))
            throw new NotSupportedException("跨工程粘贴目前只支持相同画布尺寸的可编辑平面图层；变换源会先按文档坐标处理。");
        if (source.clipboardLayerTransform is not null)
        {
            Guid sourceLayerId = source.clipboardLayerId!.Value;
            TileRaster documentRaster = ImageProjectWorkflow.RenderLayerForCopy(source.Session!, sourceLayerId);
            clipboardRaster = ApplySelection(documentRaster, source.clipboardMask!, keepSelected: true);
            clipboardLayerId = null;
            clipboardLayerTransform = null;
        }
        else
        {
            clipboardRaster = source.clipboardRaster;
            clipboardLayerId = source.clipboardLayerId;
            clipboardLayerTransform = source.clipboardLayerTransform;
        }
        clipboardMask = source.clipboardMask;
        PasteSelection();
    }

    public void CopySelection()
    {
        RequireIdle();
        var session = RequireSession();
        if (Selection is not { } selection || session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("请先建立选区。");
        var layerSelection = SelectionForLayer(session, layerId, selection);
        clipboardRaster = ApplySelection(session.GetLayerRaster(layerId), layerSelection, keepSelected: true);
        clipboardMask = selection;
        clipboardLayerId = layerId;
        clipboardLayerTransform = session.IsLayerTransformIdentity(layerId) ? null : session.GetLayerTransform(layerId);
    }

    public void CopyMergedSelection()
    {
        RequireIdle();
        var session = RequireSession();
        if (Selection is not { } selection)
            throw new InvalidOperationException("请先建立选区。");
        clipboardRaster = ApplySelection(Preview ?? ImageProjectWorkflow.RenderFlatNormal(session), selection, keepSelected: true);
        clipboardMask = selection;
        clipboardLayerId = null;
        clipboardLayerTransform = null;
    }

    public void CutSelection()
    {
        CopySelection();
        var session = RequireSession();
        Guid layerId = session.ActiveLayerId!.Value;
        var layerSelection = SelectionForLayer(session, layerId, Selection!);
        var next = ApplySelection(session.GetLayerRaster(layerId), layerSelection, keepSelected: false);
        if (!SamePixels(session.GetLayerRaster(layerId), next)) Edit(current => current.ReplaceLayerRaster(layerId, next));
    }

    public void PasteSelection()
    {
        RequireIdle();
        var session = RequireSession();
        if (clipboardRaster is not { } source || clipboardMask is not { } mask || session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("没有可粘贴的选区。");
        if (!CanPasteSelection)
            throw new NotSupportedException("当前变换图层只能粘贴同一变换快照复制的选区，或先烘焙图层变换。");
        var layerMask = session.IsLayerTransformIdentity(layerId) ? mask : SelectionForLayer(session, layerId, mask);
        floatingLayerId = layerId;
        floatingRaster = ReplaceSelection(session.GetLayerRaster(layerId), source, layerMask);
        floatingMask = mask;
        floatingLayerMask = layerMask;
        floatingPreviousSelection = Selection;
        floatingPreviousBounds = SelectionBounds;
        floatingPreviousOutline = SelectionOutline;
        Selection = mask;
        SelectionBounds = SelectionBoundsFor(mask);
        UpdateSelectionOutline();
        Preview = ImageProjectWorkflow.RenderFlatNormal(session, layerId, floatingRaster);
    }

    public void CommitFloatingSelection()
    {
        if (HasActiveStroke) throw new InvalidOperationException("请先结束或取消当前笔划。");
        if (floatingLayerId is not { } layerId || floatingRaster is not { } raster)
            throw new InvalidOperationException("当前没有浮动选区。");
        var session = RequireSession();
        if (!SamePixels(session.GetLayerRaster(layerId), raster))
            session.ReplaceLayerRaster(layerId, raster);
        ClearFloatingSelection(restorePreviousSelection: false);
        Preview = ImageProjectWorkflow.RenderFlatNormal(session);
    }

    public void CancelFloatingSelection()
    {
        if (HasActiveStroke) throw new InvalidOperationException("请先结束或取消当前笔划。");
        if (!HasFloatingSelection) throw new InvalidOperationException("当前没有浮动选区。");
        ClearFloatingSelection(restorePreviousSelection: true);
        Preview = ImageProjectWorkflow.RenderFlatNormal(RequireSession());
    }

    public void MoveSelection(int offsetX, int offsetY)
    {
        RequireIdle(allowFloating: true);
        if (HasFloatingSelection)
        {
            MoveFloatingSelection(offsetX, offsetY);
            return;
        }
        var session = RequireSession();
        if (Selection is not { } selection || session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("请先建立选区。");
        if (offsetX == 0 && offsetY == 0) return;
        var selectionBefore = selection;
        TileRaster current = session.GetLayerRaster(layerId);
        var layerSelection = SelectionForLayer(session, layerId, selection);
        Point layerOffset = DocumentToLayerVector(session, layerId, new Vector(offsetX, offsetY));
        int layerOffsetX = (int)Math.Round(layerOffset.X, MidpointRounding.AwayFromZero);
        int layerOffsetY = (int)Math.Round(layerOffset.Y, MidpointRounding.AwayFromZero);
        byte[] source = ToRgba(current), mask = ToCoverage(layerSelection), moved = new byte[source.Length];
        source.CopyTo(moved, 0);
        for (int y = 0; y < session.Height; y++)
        for (int x = 0; x < session.Width; x++)
            if (mask[y * session.Width + x] != 0) moved.AsSpan((y * session.Width + x) * 4, 4).Clear();
        byte[] movedMask = new byte[mask.Length];
        for (int y = 0; y < session.Height; y++)
        for (int x = 0; x < session.Width; x++)
            if (mask[y * session.Width + x] != 0)
            {
                int targetX = x + layerOffsetX, targetY = y + layerOffsetY;
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
        var nextSelection = TranslateSelection(selection, offsetX, offsetY);
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

    public void FlipActiveLayer(bool horizontal)
    {
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("当前工程没有活动图层。");
        if (session.Layers.Single(layer => layer.Id == layerId).IsGroup)
        {
            Edit(editSession => editSession.FlipGroup(layerId, horizontal));
            return;
        }
        Edit(editSession => editSession.FlipLayerTransform(layerId, horizontal));
    }

    public void MoveActiveLayer(int offsetX, int offsetY)
    {
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("当前工程没有活动图层。");
        if (offsetX == 0 && offsetY == 0) return;
        if (session.Layers.Single(layer => layer.Id == layerId).IsGroup)
        {
            Edit(editSession => editSession.MoveGroup(layerId, offsetX, offsetY));
            return;
        }
        Edit(editSession => editSession.MoveLayerTransform(layerId, offsetX, offsetY));
    }

    public void ScaleActiveLayer(bool enlarge)
    {
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("当前工程没有活动图层。");
        if (session.Layers.Single(layer => layer.Id == layerId).IsGroup)
            Edit(editSession => editSession.ScaleGroup(layerId, enlarge ? 1.1 : 0.9));
        else
            Edit(editSession => editSession.ScaleLayerTransform(layerId, enlarge ? 1.1 : 0.9));
    }

    public void RotateActiveLayer90(bool clockwise)
    {
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("当前工程没有活动图层。");
        if (session.Layers.Single(layer => layer.Id == layerId).IsGroup)
            Edit(editSession => editSession.RotateGroup90(layerId, clockwise));
        else
            Edit(editSession => editSession.RotateLayerTransform90(layerId, clockwise));
    }

    public void RotateActiveLayer(double degrees)
    {
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("当前工程没有活动图层。");
        if (session.Layers.Single(layer => layer.Id == layerId).IsGroup)
            Edit(editSession => editSession.RotateGroupTransform(layerId, degrees));
        else
            Edit(editSession => editSession.RotateLayerTransform(layerId, degrees));
    }

    public void AddActiveLayerMask()
    {
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("当前工程没有活动图层。");
        Edit(editSession => editSession.EnsureLayerMask(layerId));
    }

    public void ToggleActiveLayerMask()
    {
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } layerId || !session.Layers.Single(layer => layer.Id == layerId).HasMask)
            throw new InvalidOperationException("当前图层没有蒙版。");
        bool enabled = session.IsLayerMaskEnabled(layerId);
        Edit(editSession => editSession.SetLayerMaskEnabled(layerId, !enabled));
    }

    public void InvertActiveLayerMask()
    {
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } layerId || session.GetLayerMask(layerId) is not { } current)
            throw new InvalidOperationException("当前图层没有蒙版。");
        Edit(editSession => editSession.ReplaceLayerMask(layerId, current.Invert()));
    }

    public void FillActiveLayerMask(bool reveal)
    {
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } layerId || session.GetLayerMask(layerId) is null)
            throw new InvalidOperationException("当前图层没有蒙版。");
        GrayTileRaster next = reveal
            ? GrayTileRaster.Rectangle(session.Width, session.Height, 0, 0, session.Width, session.Height)
            : new GrayTileRaster(session.Width, session.Height);
        Edit(editSession => editSession.ReplaceLayerMask(layerId, next));
    }

    public void BlurActiveLayerMask(int radius)
    {
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } layerId || session.GetLayerMask(layerId) is not { } current)
            throw new InvalidOperationException("当前图层没有蒙版。");
        Edit(editSession => editSession.ReplaceLayerMask(layerId, current.Blur(radius)));
    }

    public void SetActiveLayerClipping(bool enabled)
    {
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("当前工程没有活动图层。");
        int index = session.Layers.ToList().FindIndex(layer => layer.Id == layerId);
        if (enabled)
        {
            if (index <= 0) throw new InvalidOperationException("当前图层下方没有可用剪贴源。");
            Edit(editSession => editSession.SetLayerMaskSource(layerId, editSession.Layers[index - 1].Id));
        }
        else Edit(editSession => editSession.SetLayerMaskSource(layerId, null));
    }

    public void ApplySelectionToActiveLayerMask(bool reveal)
    {
        RequireIdle();
        var session = RequireSession();
        if (Selection is not { } selection || session.ActiveLayerId is not { } layerId)
            throw new InvalidOperationException("请先建立选区并选择图层。");
        if (session.GetLayerMask(layerId) is not { } current)
            throw new InvalidOperationException("当前图层没有蒙版，请先添加蒙版。");
        var layerSelection = SelectionForLayer(session, layerId, selection);
        var next = current.Combine(layerSelection, reveal ? GraySelectionOperation.Add : GraySelectionOperation.Subtract);
        Edit(editSession => editSession.ReplaceLayerMask(layerId, next));
    }

    public void ResizeCanvas(int width, int height) => ResizeDocument(width, height, scale: false, ResizeFilter.Bilinear);

    public void ResizeImage(int width, int height) => ResizeDocument(width, height, scale: true, ResizeFilter.Bilinear);

    // Internal verification entry point; the production window still uses the existing bilinear path.
    internal void ResizeImage(int width, int height, ResizeFilter filter) => ResizeDocument(width, height, scale: true, filter);

    public void RotateDocument90(bool clockwise) {
        RequireIdle();
        var session = RequireSession();
        int width = session.Width, height = session.Height;
        var rasters = session.Layers.ToDictionary(layer => layer.Id,
            layer => RotateRaster90(session.GetLayerRaster(layer.Id), clockwise));
        var masks = session.Layers.Where(layer => layer.HasMask).ToDictionary(layer => layer.Id,
            layer => RotateMask90(session.GetLayerMask(layer.Id)!, clockwise));
        Edit(current => current.ResizeDocument(height, width, rasters, masks.Count == 0 ? null : masks));
        ClearSelectionWithoutHistory();
        ResetSelectionHistory();
    }

    private void ResizeDocument(int width, int height, bool scale, ResizeFilter filter)
    {
        RequireIdle();
        var session = RequireSession();
        if (width == session.Width && height == session.Height)
            throw new ArgumentException("新尺寸必须与当前画布不同。");
        var rasters = session.Layers.ToDictionary(layer => layer.Id,
            layer => ResizeRaster(session.GetLayerRaster(layer.Id), width, height, scale, filter));
        var masks = session.Layers.Where(layer => layer.HasMask).ToDictionary(layer => layer.Id,
            layer => ResizeMask(session.GetLayerMask(layer.Id)!, width, height, scale, filter));
        Edit(current => current.ResizeDocument(width, height, rasters, masks.Count == 0 ? null : masks));
        ClearSelectionWithoutHistory();
        ResetSelectionHistory();
    }

    public void SelectLayerAlpha()
    {
        selectionMoveHistory = null;
        RequireIdle();
        var session = RequireSession();
        if (session.ActiveLayerId is not { } layerId) throw new InvalidOperationException("当前工程没有活动图层。");
        var source = GrayTileRaster.FromCoverage(session.Width, session.Height, ToAlpha(session.GetLayerRaster(layerId)));
        var next = LayerToDocumentCoverage(session, layerId, source);
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

    private static Point DocumentToLayerPoint(ProjectSession session, Guid layerId, Point document)
    {
        if (session.Layers.Single(layer => layer.Id == layerId).IsGroup || session.IsLayerTransformIdentity(layerId))
            return document;
        LayerTransformInfo transform = session.GetLayerTransform(layerId);
        double dx = document.X - (transform.X + transform.Width / 2), dy = document.Y - (transform.Y + transform.Height / 2);
        double radians = transform.Rotation * Math.PI / 180;
        double x = dx * Math.Cos(radians) + dy * Math.Sin(radians);
        double y = -dx * Math.Sin(radians) + dy * Math.Cos(radians);
        if (transform.FlipX) x = -x;
        if (transform.FlipY) y = -y;
        return new Point((x + transform.Width / 2) * session.Width / transform.Width,
            (y + transform.Height / 2) * session.Height / transform.Height);
    }

    private static BrushPoint DocumentToLayerPoint(ProjectSession session, Guid layerId, BrushPoint document)
    {
        Point mapped = DocumentToLayerPoint(session, layerId, new Point(document.X, document.Y));
        return new BrushPoint(mapped.X, mapped.Y, document.Pressure);
    }

    private static Point LayerToDocumentPoint(ProjectSession session, Guid layerId, Point layerPoint)
    {
        if (session.Layers.Single(layer => layer.Id == layerId).IsGroup || session.IsLayerTransformIdentity(layerId))
            return layerPoint;
        LayerTransformInfo transform = session.GetLayerTransform(layerId);
        double x = layerPoint.X * transform.Width / session.Width - transform.Width / 2;
        double y = layerPoint.Y * transform.Height / session.Height - transform.Height / 2;
        if (transform.FlipX) x = -x;
        if (transform.FlipY) y = -y;
        double radians = transform.Rotation * Math.PI / 180;
        return new Point(transform.X + transform.Width / 2 + x * Math.Cos(radians) - y * Math.Sin(radians),
            transform.Y + transform.Height / 2 + x * Math.Sin(radians) + y * Math.Cos(radians));
    }

    private static Point DocumentToLayerVector(ProjectSession session, Guid layerId, Vector document)
    {
        if (session.Layers.Single(layer => layer.Id == layerId).IsGroup || session.IsLayerTransformIdentity(layerId))
            return new Point(document.X, document.Y);
        LayerTransformInfo transform = session.GetLayerTransform(layerId);
        double radians = transform.Rotation * Math.PI / 180;
        double x = document.X * Math.Cos(radians) + document.Y * Math.Sin(radians);
        double y = -document.X * Math.Sin(radians) + document.Y * Math.Cos(radians);
        if (transform.FlipX) x = -x;
        if (transform.FlipY) y = -y;
        return new Point(x * session.Width / transform.Width, y * session.Height / transform.Height);
    }

    private static GrayTileRaster SelectionForLayer(ProjectSession session, Guid layerId, GrayTileRaster selection)
    {
        if (session.Layers.Single(layer => layer.Id == layerId).IsGroup || session.IsLayerTransformIdentity(layerId))
            return selection;
        byte[] documentCoverage = ToCoverage(selection), layerCoverage = new byte[documentCoverage.Length];
        for (int y = 0; y < session.Height; y++)
        for (int x = 0; x < session.Width; x++)
        {
            Point document = LayerToDocumentPoint(session, layerId, new Point(x + 0.5, y + 0.5));
            layerCoverage[y * session.Width + x] = SampleCoverage(documentCoverage, session.Width, session.Height, document);
        }
        return GrayTileRaster.FromCoverage(session.Width, session.Height, layerCoverage);
    }

    private static GrayTileRaster LayerToDocumentCoverage(ProjectSession session, Guid layerId, GrayTileRaster layerCoverage)
    {
        if (session.Layers.Single(layer => layer.Id == layerId).IsGroup || session.IsLayerTransformIdentity(layerId))
            return layerCoverage;
        byte[] sourceCoverage = ToCoverage(layerCoverage), documentCoverage = new byte[sourceCoverage.Length];
        for (int y = 0; y < session.Height; y++)
        for (int x = 0; x < session.Width; x++)
        {
            Point layer = DocumentToLayerPoint(session, layerId, new Point(x + 0.5, y + 0.5));
            documentCoverage[y * session.Width + x] = SampleCoverage(sourceCoverage, session.Width, session.Height, layer);
        }
        return GrayTileRaster.FromCoverage(session.Width, session.Height, documentCoverage);
    }

    private static byte SampleCoverage(byte[] coverage, int width, int height, Point point)
    {
        if (point.X < 0 || point.Y < 0 || point.X >= width || point.Y >= height)
            return 0;
        double sourceX = point.X - 0.5, sourceY = point.Y - 0.5;
        int x0 = Math.Clamp((int)Math.Floor(sourceX), 0, width - 1);
        int y0 = Math.Clamp((int)Math.Floor(sourceY), 0, height - 1);
        int x1 = Math.Min(width - 1, x0 + 1), y1 = Math.Min(height - 1, y0 + 1);
        double xWeight = Math.Clamp(sourceX - Math.Floor(sourceX), 0, 1);
        double yWeight = Math.Clamp(sourceY - Math.Floor(sourceY), 0, 1);
        double top = coverage[y0 * width + x0] * (1 - xWeight) + coverage[y0 * width + x1] * xWeight;
        double bottom = coverage[y1 * width + x0] * (1 - xWeight) + coverage[y1 * width + x1] * xWeight;
        return (byte)Math.Clamp(Math.Round(top * (1 - yWeight) + bottom * yWeight, MidpointRounding.AwayFromZero), 0, 255);
    }

    private static GrayTileRaster TranslateSelection(GrayTileRaster selection, int offsetX, int offsetY)
    {
        byte[] source = ToCoverage(selection), moved = new byte[source.Length];
        for (int y = 0; y < selection.Height; y++)
        for (int x = 0; x < selection.Width; x++)
        {
            int targetX = x + offsetX, targetY = y + offsetY;
            if ((uint)targetX < (uint)selection.Width && (uint)targetY < (uint)selection.Height)
                moved[targetY * selection.Width + targetX] = source[y * selection.Width + x];
        }
        return GrayTileRaster.FromCoverage(selection.Width, selection.Height, moved);
    }

    private void RequireIdle(bool allowFloating = false)
    {
        if (HasActiveStroke) throw new InvalidOperationException("请先结束或取消当前笔划。");
        if (!allowFloating && HasFloatingSelection) throw new InvalidOperationException("请先提交或取消浮动选区。");
    }

    private ProjectSession RequireSession() => Session ?? throw new InvalidOperationException("请先打开或导入工程。");

    private void RequireEditableSession()
    {
        if (!CanEdit) throw new NotSupportedException("当前工程包含未支持的语义，只读预览不可编辑。");
    }

    private static bool SamePixels(TileRaster first, TileRaster second)
    {
        if (first.Width != second.Width || first.Height != second.Height) return false;
        for (int row = 0; row * TileRaster.TileSize < first.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < first.Width; column++)
            if (!first.ReadTileCopy(column, row).SequenceEqual(second.ReadTileCopy(column, row))) return false;
        return true;
    }

    private static bool SameCoverage(GrayTileRaster first, GrayTileRaster second)
    {
        if (first.Width != second.Width || first.Height != second.Height) return false;
        for (int row = 0; row * TileRaster.TileSize < first.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < first.Width; column++)
            if (!first.ReadTileCopy(column, row).SequenceEqual(second.ReadTileCopy(column, row))) return false;
        return true;
    }

    private TileRaster RenderMaskStrokePreview(GrayTileRaster coverage)
    {
        var session = RequireSession();
        GrayTileRaster current = session.GetLayerMask(maskBrushLayer)!;
        GrayTileRaster next = current.Combine(coverage,
            maskBrushReveal ? GraySelectionOperation.Add : GraySelectionOperation.Subtract);
        TileRaster brushBounds = session.Layers.Single(layer => layer.Id == maskBrushLayer).IsGroup
            ? new TileRaster(session.Width, session.Height)
            : session.GetLayerRaster(maskBrushLayer);
        return ImageProjectWorkflow.RenderFlatNormal(session, maskBrushLayer, brushBounds, next);
    }

    private void ClearClipboard()
    {
        clipboardRaster = null;
        clipboardMask = null;
    }

    private void ClearFloatingSelection(bool restorePreviousSelection)
    {
        if (restorePreviousSelection)
        {
            Selection = floatingPreviousSelection;
            SelectionBounds = floatingPreviousBounds;
            SelectionOutline = floatingPreviousOutline;
        }
        floatingLayerId = null;
        floatingRaster = null;
        floatingMask = null;
        floatingLayerMask = null;
        floatingPreviousSelection = null;
        floatingPreviousBounds = null;
        floatingPreviousOutline = null;
    }

    private void MoveFloatingSelection(int offsetX, int offsetY)
    {
        if (floatingRaster is not { } current || floatingMask is not { } mask ||
            floatingLayerMask is not { } layerMask || floatingLayerId is not { } layerId)
            throw new InvalidOperationException("当前没有浮动选区。");
        if (offsetX == 0 && offsetY == 0) return;
        var session = RequireSession();
        byte[] source = ToRgba(current), selected = ToCoverage(layerMask), moved = source.ToArray(), movedMask = new byte[selected.Length];
        Point layerOffset = DocumentToLayerVector(session, layerId, new Vector(offsetX, offsetY));
        int layerOffsetX = (int)Math.Round(layerOffset.X, MidpointRounding.AwayFromZero);
        int layerOffsetY = (int)Math.Round(layerOffset.Y, MidpointRounding.AwayFromZero);
        for (int y = 0; y < session.Height; y++)
        for (int x = 0; x < session.Width; x++)
            if (selected[y * session.Width + x] != 0)
                moved.AsSpan((y * session.Width + x) * 4, 4).Clear();
        for (int y = 0; y < session.Height; y++)
        for (int x = 0; x < session.Width; x++)
            if (selected[y * session.Width + x] != 0)
            {
                int targetX = x + layerOffsetX, targetY = y + layerOffsetY;
                if ((uint)targetX < (uint)session.Width && (uint)targetY < (uint)session.Height)
                {
                    source.AsSpan((y * session.Width + x) * 4, 4)
                        .CopyTo(moved.AsSpan((targetY * session.Width + targetX) * 4, 4));
                    movedMask[targetY * session.Width + targetX] = selected[y * session.Width + x];
                }
            }
        floatingRaster = FromRgba(session.Width, session.Height, moved);
        floatingLayerMask = GrayTileRaster.FromCoverage(session.Width, session.Height, movedMask);
        floatingMask = TranslateSelection(mask, offsetX, offsetY);
        Selection = floatingMask;
        SelectionBounds = floatingMask.CoveredPixels == 0 ? null : SelectionBoundsFor(floatingMask);
        UpdateSelectionOutline();
        Preview = ImageProjectWorkflow.RenderFlatNormal(session, layerId, floatingRaster);
    }

    private static TileRaster ApplySelection(TileRaster source, GrayTileRaster selection, bool keepSelected)
    {
        var result = new TileRaster(source.Width, source.Height);
        for (int row = 0; row * TileRaster.TileSize < source.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < source.Width; column++)
        {
            byte[] pixels = source.ReadTileCopy(column, row), mask = selection.ReadTileCopy(column, row);
            for (int i = 0; i < mask.Length; i++)
            {
                int coverage = keepSelected ? mask[i] : 255 - mask[i];
                if (coverage == 255) continue;
                if (coverage == 0) { pixels.AsSpan(i * 4, 4).Clear(); continue; }
                for (int channel = 0; channel < 4; channel++)
                    pixels[i * 4 + channel] = (byte)((pixels[i * 4 + channel] * coverage + 127) / 255);
            }
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
            {
                int coverage = mask[i];
                if (coverage == 0) continue;
                if (coverage == 255)
                {
                    sourcePixels.AsSpan(i * 4, 4).CopyTo(pixels.AsSpan(i * 4, 4));
                    continue;
                }
                int inverse = 255 - coverage;
                for (int channel = 0; channel < 4; channel++)
                    pixels[i * 4 + channel] = (byte)((sourcePixels[i * 4 + channel] * coverage +
                        pixels[i * 4 + channel] * inverse + 127) / 255);
            }
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

    private static TileRaster PositionRaster(TileRaster source, int width, int height, Point? center)
    {
        byte[] input = ToRgba(source), output = new byte[checked(width * height * 4)];
        int offsetX = center is { } point
            ? (int)Math.Round(point.X - source.Width / 2d, MidpointRounding.AwayFromZero)
            : (width - source.Width) / 2;
        int offsetY = center is { } pointY
            ? (int)Math.Round(pointY.Y - source.Height / 2d, MidpointRounding.AwayFromZero)
            : (height - source.Height) / 2;
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            int targetX = x + offsetX, targetY = y + offsetY;
            if ((uint)targetX >= (uint)width || (uint)targetY >= (uint)height) continue;
            input.AsSpan((y * source.Width + x) * 4, 4)
                .CopyTo(output.AsSpan((targetY * width + targetX) * 4, 4));
        }
        return FromRgba(width, height, output);
    }

    private static TileRaster ResizeRaster(TileRaster source, int width, int height, bool scale, ResizeFilter filter)
    {
        byte[] input = ToRgba(source), output = new byte[checked(width * height * 4)];
        if (scale && width < source.Width && height < source.Height && filter == ResizeFilter.Lanczos3)
            return ResizeRasterLanczos3(source, width, height);
        if (scale && width < source.Width && height < source.Height)
            return ResizeRasterArea(source, width, height);
        if (scale)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double sourceX = (x + 0.5) * source.Width / width - 0.5;
                double sourceY = (y + 0.5) * source.Height / height - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sourceX), 0, source.Width - 1),
                    y0 = Math.Clamp((int)Math.Floor(sourceY), 0, source.Height - 1);
                int x1 = Math.Min(source.Width - 1, x0 + 1), y1 = Math.Min(source.Height - 1, y0 + 1);
                double xWeight = Math.Clamp(sourceX - Math.Floor(sourceX), 0, 1),
                    yWeight = Math.Clamp(sourceY - Math.Floor(sourceY), 0, 1);
                int destination = (y * width + x) * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    double top = input[(y0 * source.Width + x0) * 4 + channel] * (1 - xWeight) +
                        input[(y0 * source.Width + x1) * 4 + channel] * xWeight;
                    double bottom = input[(y1 * source.Width + x0) * 4 + channel] * (1 - xWeight) +
                        input[(y1 * source.Width + x1) * 4 + channel] * xWeight;
                    output[destination + channel] = (byte)Math.Clamp(
                        Math.Round(top * (1 - yWeight) + bottom * yWeight, MidpointRounding.AwayFromZero), 0, 255);
                }
            }
            return FromRgba(width, height, output);
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int sourceX = x, sourceY = y;
            if ((uint)sourceX >= (uint)source.Width || (uint)sourceY >= (uint)source.Height) continue;
            input.AsSpan((sourceY * source.Width + sourceX) * 4, 4)
                .CopyTo(output.AsSpan((y * width + x) * 4, 4));
        }
        return FromRgba(width, height, output);
    }

    private readonly record struct ResizeTap(int Index, double Weight);

    private static ResizeTap[][] BuildLanczosTaps(int sourceLength, int destinationLength)
    {
        var result = new ResizeTap[destinationLength][];
        for (int destination = 0; destination < destinationLength; destination++)
        {
            double center = (destination + 0.5) * sourceLength / destinationLength - 0.5;
            int first = (int)Math.Ceiling(center - 3), last = (int)Math.Floor(center + 3);
            var weights = new Dictionary<int, double>();
            for (int source = first; source <= last; source++)
            {
                double weight = Lanczos3(center - source);
                if (weight == 0) continue;
                int index = Math.Clamp(source, 0, sourceLength - 1);
                weights[index] = weights.GetValueOrDefault(index) + weight;
            }
            double total = weights.Values.Sum();
            if (!double.IsFinite(total) || Math.Abs(total) < 1e-12)
            {
                result[destination] = [new ResizeTap(Math.Clamp((int)Math.Round(center), 0, sourceLength - 1), 1)];
                continue;
            }
            result[destination] = weights.Select(pair => new ResizeTap(pair.Key, pair.Value / total)).ToArray();
        }
        return result;
    }

    private static double Lanczos3(double distance)
    {
        double absolute = Math.Abs(distance);
        if (absolute >= 3) return 0;
        if (absolute < 1e-12) return 1;
        double piDistance = Math.PI * distance;
        return Math.Sin(piDistance) / piDistance * Math.Sin(piDistance / 3) / (piDistance / 3);
    }

    private static TileRaster ResizeRasterLanczos3(TileRaster source, int width, int height)
    {
        byte[] input = ToRgba(source), horizontal = new byte[checked(width * source.Height * 4)], output = new byte[checked(width * height * 4)];
        ResizeTap[][] horizontalTaps = BuildLanczosTaps(source.Width, width);
        ResizeTap[][] verticalTaps = BuildLanczosTaps(source.Height, height);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < width; x++)
        {
            int destination = (y * width + x) * 4;
            for (int channel = 0; channel < 4; channel++)
            {
                double sum = 0;
                foreach (ResizeTap tap in horizontalTaps[x])
                    sum += input[(y * source.Width + tap.Index) * 4 + channel] * tap.Weight;
                horizontal[destination + channel] = (byte)Math.Clamp(
                    Math.Round(sum, MidpointRounding.AwayFromZero), 0, 255);
            }
            for (int channel = 0; channel < 3; channel++)
                horizontal[destination + channel] = Math.Min(horizontal[destination + channel], horizontal[destination + 3]);
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int destination = (y * width + x) * 4;
            for (int channel = 0; channel < 4; channel++)
            {
                double sum = 0;
                foreach (ResizeTap tap in verticalTaps[y])
                    sum += horizontal[(tap.Index * width + x) * 4 + channel] * tap.Weight;
                output[destination + channel] = (byte)Math.Clamp(
                    Math.Round(sum, MidpointRounding.AwayFromZero), 0, 255);
            }
            for (int channel = 0; channel < 3; channel++)
                output[destination + channel] = Math.Min(output[destination + channel], output[destination + 3]);
        }
        return FromRgba(width, height, output);
    }

    private static GrayTileRaster ResizeMaskLanczos3(GrayTileRaster source, int width, int height)
    {
        byte[] input = ToCoverage(source), horizontal = new byte[checked(width * source.Height)], output = new byte[checked(width * height)];
        ResizeTap[][] horizontalTaps = BuildLanczosTaps(source.Width, width);
        ResizeTap[][] verticalTaps = BuildLanczosTaps(source.Height, height);
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < width; x++)
        {
            double sum = 0;
            foreach (ResizeTap tap in horizontalTaps[x])
                sum += input[y * source.Width + tap.Index] * tap.Weight;
            horizontal[y * width + x] = (byte)Math.Clamp(
                Math.Round(sum, MidpointRounding.AwayFromZero), 0, 255);
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double sum = 0;
            foreach (ResizeTap tap in verticalTaps[y])
                sum += horizontal[tap.Index * width + x] * tap.Weight;
            output[y * width + x] = (byte)Math.Clamp(
                Math.Round(sum, MidpointRounding.AwayFromZero), 0, 255);
        }
        return GrayTileRaster.FromCoverage(width, height, output);
    }

    private static TileRaster ResizeRasterArea(TileRaster source, int width, int height)
    {
        byte[] input = ToRgba(source), horizontal = new byte[checked(width * source.Height * 4)], output = new byte[checked(width * height * 4)];
        double[] sums = new double[4];
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < width; x++)
        {
            double start = x * (double)source.Width / width, end = (x + 1) * (double)source.Width / width;
            int first = (int)Math.Floor(start), last = (int)Math.Ceiling(end);
            double weightSum = 0;
            Array.Clear(sums);
            for (int sourceX = first; sourceX < last; sourceX++)
            {
                double weight = Math.Min(end, sourceX + 1) - Math.Max(start, sourceX);
                if (weight <= 0) continue;
                weightSum += weight;
                for (int channel = 0; channel < 4; channel++)
                    sums[channel] += input[(y * source.Width + sourceX) * 4 + channel] * weight;
            }
            int destination = (y * width + x) * 4;
            for (int channel = 0; channel < 4; channel++)
                horizontal[destination + channel] = (byte)Math.Clamp(
                    Math.Round(sums[channel] / weightSum, MidpointRounding.AwayFromZero), 0, 255);
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double start = y * (double)source.Height / height, end = (y + 1) * (double)source.Height / height;
            int first = (int)Math.Floor(start), last = (int)Math.Ceiling(end);
            double weightSum = 0;
            Array.Clear(sums);
            for (int sourceY = first; sourceY < last; sourceY++)
            {
                double weight = Math.Min(end, sourceY + 1) - Math.Max(start, sourceY);
                if (weight <= 0) continue;
                weightSum += weight;
                for (int channel = 0; channel < 4; channel++)
                    sums[channel] += horizontal[(sourceY * width + x) * 4 + channel] * weight;
            }
            int destination = (y * width + x) * 4;
            for (int channel = 0; channel < 4; channel++)
                output[destination + channel] = (byte)Math.Clamp(
                    Math.Round(sums[channel] / weightSum, MidpointRounding.AwayFromZero), 0, 255);
        }
        return FromRgba(width, height, output);
    }

    private static TileRaster RotateRaster90(TileRaster source, bool clockwise)
    {
        int width = source.Width, height = source.Height;
        byte[] input = ToRgba(source), output = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int targetX = clockwise ? height - 1 - y : y;
            int targetY = clockwise ? x : width - 1 - x;
            input.AsSpan((y * width + x) * 4, 4)
                .CopyTo(output.AsSpan((targetY * height + targetX) * 4, 4));
        }
        return FromRgba(height, width, output);
    }

    private static GrayTileRaster ResizeMask(GrayTileRaster source, int width, int height, bool scale, ResizeFilter filter)
    {
        byte[] input = ToCoverage(source), output = new byte[checked(width * height)];
        if (scale && width < source.Width && height < source.Height && filter == ResizeFilter.Lanczos3)
            return ResizeMaskLanczos3(source, width, height);
        if (scale && width < source.Width && height < source.Height)
            return ResizeMaskArea(source, width, height);
        if (scale)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double sourceX = (x + 0.5) * source.Width / width - 0.5;
                double sourceY = (y + 0.5) * source.Height / height - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sourceX), 0, source.Width - 1),
                    y0 = Math.Clamp((int)Math.Floor(sourceY), 0, source.Height - 1);
                int x1 = Math.Min(source.Width - 1, x0 + 1), y1 = Math.Min(source.Height - 1, y0 + 1);
                double xWeight = Math.Clamp(sourceX - Math.Floor(sourceX), 0, 1),
                    yWeight = Math.Clamp(sourceY - Math.Floor(sourceY), 0, 1);
                double top = input[y0 * source.Width + x0] * (1 - xWeight) + input[y0 * source.Width + x1] * xWeight;
                double bottom = input[y1 * source.Width + x0] * (1 - xWeight) + input[y1 * source.Width + x1] * xWeight;
                output[y * width + x] = (byte)Math.Clamp(
                    Math.Round(top * (1 - yWeight) + bottom * yWeight, MidpointRounding.AwayFromZero), 0, 255);
            }
            return GrayTileRaster.FromCoverage(width, height, output);
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            if ((uint)x < (uint)source.Width && (uint)y < (uint)source.Height)
                output[y * width + x] = input[y * source.Width + x];
        return GrayTileRaster.FromCoverage(width, height, output);
    }

    private static GrayTileRaster ResizeMaskArea(GrayTileRaster source, int width, int height)
    {
        byte[] input = ToCoverage(source), horizontal = new byte[checked(width * source.Height)], output = new byte[checked(width * height)];
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < width; x++)
        {
            double start = x * (double)source.Width / width, end = (x + 1) * (double)source.Width / width;
            int first = (int)Math.Floor(start), last = (int)Math.Ceiling(end);
            double sum = 0, weightSum = 0;
            for (int sourceX = first; sourceX < last; sourceX++)
            {
                double weight = Math.Min(end, sourceX + 1) - Math.Max(start, sourceX);
                if (weight <= 0) continue;
                sum += input[y * source.Width + sourceX] * weight;
                weightSum += weight;
            }
            horizontal[y * width + x] = (byte)Math.Clamp(
                Math.Round(sum / weightSum, MidpointRounding.AwayFromZero), 0, 255);
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double start = y * (double)source.Height / height, end = (y + 1) * (double)source.Height / height;
            int first = (int)Math.Floor(start), last = (int)Math.Ceiling(end);
            double sum = 0, weightSum = 0;
            for (int sourceY = first; sourceY < last; sourceY++)
            {
                double weight = Math.Min(end, sourceY + 1) - Math.Max(start, sourceY);
                if (weight <= 0) continue;
                sum += horizontal[sourceY * width + x] * weight;
                weightSum += weight;
            }
            output[y * width + x] = (byte)Math.Clamp(
                Math.Round(sum / weightSum, MidpointRounding.AwayFromZero), 0, 255);
        }
        return GrayTileRaster.FromCoverage(width, height, output);
    }

    private static GrayTileRaster RotateMask90(GrayTileRaster source, bool clockwise)
    {
        int width = source.Width, height = source.Height;
        byte[] input = ToCoverage(source), output = new byte[checked(width * height)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int targetX = clockwise ? height - 1 - y : y;
            int targetY = clockwise ? x : width - 1 - x;
            output[targetY * height + targetX] = input[y * width + x];
        }
        return GrayTileRaster.FromCoverage(height, width, output);
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
