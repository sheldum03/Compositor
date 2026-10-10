using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;

public sealed class CanvasView : Control
{
    private IPointer? captured;
    private bool panning, selecting, movingSelection, spaceHeld, shiftHeld, autoFit = true;
    private bool freeDistorting;
    private bool gradientDragging;
    private bool shaping;
    private (Point Start, bool Moved)? layerMoveStart;
    private (Point Start, double Scale, bool Moved)? zoomDrag;
    private ShapeDraft? shapeDraft;
    private int gradientHandle = -1;
    private Point? gradientStart, gradientEnd;
    private int freeDistortHandle = -1;
    private Point previous;
    private Point strokeStart;
    private Point selectionMoveStart;
    private Point[] freeDistortCorners = [];
    private Point[] freeDistortBefore = [];
    private Rect? selectionRect;
    private List<Point>? selectionPath;
    private SelectionOutline? selectionOutline;
    private IReadOnlyList<IReadOnlyList<Point>> textSelectionPolygons = [];
    private Point? textCaretStart, textCaretEnd;
    public CanvasViewport Viewport { get; } = new();
    public WriteableBitmap? Bitmap { get; private set; }
    public bool PaintEnabled { get; set; }
    public bool PixelGridEnabled { get; set; }
    public bool SelectionEnabled { get; set; }
    public bool LassoEnabled { get; set; }
    public bool SelectionMoveEnabled { get; set; }
    public bool TextEditEnabled { get; set; }
    public bool EyedropperEnabled { get; set; }
    public bool CloneEnabled { get; set; }
    public bool FreeDistortEnabled { get; set; }
    public bool GradientEnabled { get; set; }
    public bool ShapeEnabled { get; set; }
    public bool HandEnabled { get; set; }
    public bool ZoomEnabled { get; set; }
    public bool LayerMoveEnabled { get; set; }
    public Func<Point, int?>? TextHitTest { get; set; }
    public Func<Point, Guid?>? LayerHitTest { get; set; }
    public Point? LastDocumentPointer { get; private set; }
    public bool IsDrawing => captured is not null && !panning && !selecting && !movingSelection && !freeDistorting && !gradientDragging && !shaping && zoomDrag is null && layerMoveStart is null;
    public bool IsSelecting => captured is not null && selecting;
    public Rect? SelectionRect => selectionRect;
    public int TextSelectionOverlayCount => textSelectionPolygons.Count;
    public bool TextCaretOverlayVisible => textCaretStart is not null && textCaretEnd is not null;
    public event Action<BrushPoint>? StrokeStarted;
    public event Action<BrushPoint>? StrokeMoved;
    public event Action<BrushPoint>? StrokeFinished;
    public event Action? StrokeCanceled;
    public event Action<Rect>? SelectionFinished;
    public event Action<IReadOnlyList<Point>>? LassoFinished;
    public event Action<Point, Point>? SelectionMoveFinished;
    public event Action? SelectionCanceled;
    public event Action<int, bool>? TextCaretPressed;
    public event Action<Point>? TextPlaceRequested;
    public event Action<Point>? ColorSampled;
    public event Action<Point>? CloneSourceSelected;
    public event Action<IReadOnlyList<Point>>? FreeDistortChanged;
    public event Action<Point, Point>? GradientChanged;
    public event Action? GradientFinished;
    public event Action? GradientCanceled;
    public event Action? GradientApplied;
    public event Action<Point>? ShapeStarted;
    public event Action<Point, bool, bool>? ShapeMoved;
    public event Action? ShapeFinished;
    public event Action? ShapeCanceled;
    public event Action<double>? ZoomChanged;
    public event Action<KeyModifiers>? LayerMoveStarted;
    public event Action<Guid>? LayerPicked;
    public event Action<Vector, bool>? LayerMoveChanged;
    public event Action? LayerMoveFinished;
    public event Action? LayerMoveCanceled;

    public CanvasView()
    {
        Name = "Canvas"; Focusable = true; ClipToBounds = true;
        PointerPressed += (_, e) =>
        {
            if (Bitmap is null || captured is not null) return;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) shiftHeld = true;
            var properties = e.GetCurrentPoint(this).Properties;
            bool pan = properties.IsMiddleButtonPressed || (spaceHeld || HandEnabled) && properties.IsLeftButtonPressed;
            Point view = e.GetPosition(this), document = Viewport.ToDocument(view);
            LastDocumentPointer = document;
            if (!pan && LayerMoveEnabled && properties.IsLeftButtonPressed)
            {
                if (LayerHitTest?.Invoke(document) is { } hit) LayerPicked?.Invoke(hit);
                Focus();
                layerMoveStart = (document, false);
                captured = e.Pointer;
                captured.Capture(this);
                LayerMoveStarted?.Invoke(e.KeyModifiers);
                e.Handled = true;
                return;
            }
            if (!pan && ZoomEnabled && properties.IsLeftButtonPressed)
            {
                Focus();
                zoomDrag = (view, Viewport.Scale, false);
                captured = e.Pointer;
                captured.Capture(this);
                e.Handled = true;
                return;
            }
            if (!pan && ShapeEnabled && properties.IsLeftButtonPressed)
            {
                Focus();
                shaping = true;
                captured = e.Pointer;
                captured.Capture(this);
                ShapeStarted?.Invoke(document);
                e.Handled = true;
                return;
            }
            if (!pan && GradientEnabled && properties.IsLeftButtonPressed)
            {
                gradientHandle = FindGradientHandle(view);
                if (gradientHandle < 0)
                {
                    if (document.X < 0 || document.Y < 0 || document.X >= Bitmap.PixelSize.Width || document.Y >= Bitmap.PixelSize.Height) return;
                    gradientStart = gradientEnd = document;
                    gradientHandle = 1;
                }
                Focus();
                gradientDragging = true;
                captured = e.Pointer;
                captured.Capture(this);
                MoveGradientLine(document, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                return;
            }
            if (!pan && FreeDistortEnabled && properties.IsLeftButtonPressed)
            {
                freeDistortHandle = FindFreeDistortHandle(view);
                if (freeDistortHandle >= 0)
                {
                    Focus();
                    freeDistorting = true;
                    freeDistortBefore = (Point[])freeDistortCorners.Clone();
                    captured = e.Pointer;
                    captured.Capture(this);
                    e.Handled = true;
                }
                return;
            }
            if (!pan && TextEditEnabled && properties.IsLeftButtonPressed)
            {
                if (TextHitTest?.Invoke(document) is { } characterIndex)
                    TextCaretPressed?.Invoke(characterIndex, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                else if (document.X >= 0 && document.Y >= 0 && document.X < Bitmap.PixelSize.Width && document.Y < Bitmap.PixelSize.Height)
                    TextPlaceRequested?.Invoke(document);
                e.Handled = true;
                return;
            }
            if (!pan && EyedropperEnabled && properties.IsLeftButtonPressed)
            {
                ColorSampled?.Invoke(document);
                e.Handled = true;
                return;
            }
            if (!pan && CloneEnabled && properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                CloneSourceSelected?.Invoke(document);
                e.Handled = true;
                return;
            }
            bool select = !pan && SelectionEnabled && properties.IsLeftButtonPressed;
            bool moveSelection = !pan && SelectionMoveEnabled && properties.IsLeftButtonPressed;
            if (!pan && !select && !moveSelection && (!PaintEnabled || !properties.IsLeftButtonPressed || document.X < 0 || document.Y < 0 ||
                         document.X >= Bitmap.PixelSize.Width || document.Y >= Bitmap.PixelSize.Height)) return;
            if ((select || moveSelection) && (document.X < 0 || document.Y < 0 || document.X >= Bitmap.PixelSize.Width || document.Y >= Bitmap.PixelSize.Height)) return;
            Focus(); captured = e.Pointer; panning = pan; selecting = select; movingSelection = moveSelection; previous = view; captured.Capture(this);
            if (select)
            {
                selectionRect = new Rect(document, new Size(0, 0));
                selectionPath = LassoEnabled ? [document] : null;
                selectionOutline = null;
            }
            else if (moveSelection) { selectionMoveStart = document; selectionRect = new Rect(document, new Size(0, 0)); }
            else if (!panning)
            {
                strokeStart = document;
                StrokeStarted?.Invoke(StrokePoint(e.GetCurrentPoint(this), ConstrainStroke(document)));
            }
            e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            Point view = e.GetPosition(this);
            LastDocumentPointer = Viewport.ToDocument(view);
            if (captured != e.Pointer) return;
            if (layerMoveStart is not null)
            {
                MoveLayers(Viewport.ToDocument(view), e.KeyModifiers);
                e.Handled = true;
                return;
            }
            if (zoomDrag is not null)
            {
                MoveZoom(e.GetPosition(this));
                e.Handled = true;
                return;
            }
            if (shaping)
            {
                ShapeMoved?.Invoke(Viewport.ToDocument(view), e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Alt));
                e.Handled = true;
                return;
            }
            if (gradientDragging)
            {
                MoveGradientLine(Viewport.ToDocument(view), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                return;
            }
            if (freeDistorting)
            {
                MoveFreeDistortHandle(Viewport.ToDocument(view), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                return;
            }
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) shiftHeld = true;
            if (panning) { Viewport.Pan(view - previous); previous = view; autoFit = false; InvalidateVisual(); }
            else if (selecting || movingSelection)
            {
                Point document = Viewport.ToDocument(view);
                selectionRect = Normalize(selectionRect!.Value.Position, document);
                if (selecting && LassoEnabled && (selectionPath is null || Math.Abs(document.X - selectionPath[^1].X) + Math.Abs(document.Y - selectionPath[^1].Y) > 0.5)) selectionPath?.Add(document);
                InvalidateVisual();
            }
            else StrokeMoved?.Invoke(StrokePoint(e.GetCurrentPoint(this), ConstrainStroke(Viewport.ToDocument(view))));
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (captured != e.Pointer) return;
            if (layerMoveStart is not null)
            {
                MoveLayers(Viewport.ToDocument(e.GetPosition(this)), e.KeyModifiers);
                bool moved = layerMoveStart.Value.Moved;
                layerMoveStart = null;
                captured = null;
                e.Pointer.Capture(null);
                if (moved) LayerMoveFinished?.Invoke(); else LayerMoveCanceled?.Invoke();
                e.Handled = true;
                return;
            }
            if (zoomDrag is not null)
            {
                MoveZoom(e.GetPosition(this));
                if (zoomDrag is { Moved: false } drag)
                    ZoomTo(drag.Scale * (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 0.5 : 2), drag.Start);
                zoomDrag = null;
                captured = null;
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }
            LastDocumentPointer = Viewport.ToDocument(e.GetPosition(this));
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) shiftHeld = true;
            if (shaping)
            {
                ShapeMoved?.Invoke(Viewport.ToDocument(e.GetPosition(this)), e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Alt));
                shaping = false;
                captured = null;
                e.Pointer.Capture(null);
                ShapeFinished?.Invoke();
                e.Handled = true;
                return;
            }
            if (gradientDragging)
            {
                MoveGradientLine(Viewport.ToDocument(e.GetPosition(this)), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                gradientDragging = false;
                gradientHandle = -1;
                captured = null;
                e.Pointer.Capture(null);
                GradientFinished?.Invoke();
                e.Handled = true;
                return;
            }
            if (freeDistorting)
            {
                MoveFreeDistortHandle(Viewport.ToDocument(e.GetPosition(this)), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                freeDistorting = false;
                freeDistortHandle = -1;
                captured = null;
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }
            bool paint = !panning && !movingSelection;
            bool select = selecting;
            bool moveSelection = movingSelection;
            captured = null; e.Pointer.Capture(null);
            selecting = false;
            movingSelection = false;
            if (select)
            {
                if (LassoEnabled) LassoFinished?.Invoke(selectionPath?.ToArray() ?? []);
                else SelectionFinished?.Invoke(selectionRect!.Value);
                selectionPath = null;
            }
            else if (moveSelection) SelectionMoveFinished?.Invoke(selectionMoveStart, Viewport.ToDocument(e.GetPosition(this)));
            else if (paint)
                StrokeFinished?.Invoke(StrokePoint(e.GetCurrentPoint(this), ConstrainStroke(Viewport.ToDocument(e.GetPosition(this)))));
            e.Handled = true;
        };
        PointerCaptureLost += (_, _) => Cancel();
        PointerWheelChanged += (_, e) =>
        {
            if (Bitmap is null || captured is not null) return;
            Viewport.Zoom(e.GetPosition(this), Math.Pow(1.15, e.Delta.Y));
            ZoomChanged?.Invoke(Viewport.Scale);
            autoFit = false; InvalidateVisual(); e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Space) { spaceHeld = true; e.Handled = true; }
            if (e.Key is Key.LeftShift or Key.RightShift) { shiftHeld = true; e.Handled = true; }
            if (e.Key == Key.Escape)
            {
                if (captured is null && GradientEnabled && gradientStart is not null) GradientCanceled?.Invoke();
                else Cancel();
                e.Handled = true;
            }
            if (e.Key == Key.Enter && captured is null && GradientEnabled && gradientStart is not null)
            { GradientApplied?.Invoke(); e.Handled = true; }
        };
        KeyUp += (_, e) =>
        {
            if (e.Key == Key.Space) { spaceHeld = false; e.Handled = true; }
            if (e.Key is Key.LeftShift or Key.RightShift) { shiftHeld = false; e.Handled = true; }
        };
        LostFocus += (_, _) => { spaceHeld = shiftHeld = false; Cancel(); };
    }

    public void SetBitmap(WriteableBitmap? bitmap)
    {
        bool changedSize = Bitmap?.PixelSize != bitmap?.PixelSize;
        Bitmap = bitmap;
        if (changedSize) Fit();
        InvalidateVisual();
    }

    public void SetGradientLine((Point Start, Point End)? line)
    {
        gradientStart = line?.Start;
        gradientEnd = line?.End;
        InvalidateVisual();
    }

    public void SetShapeDraft(ShapeDraft? draft)
    {
        shapeDraft = draft;
        InvalidateVisual();
    }

    public void Fit()
    {
        autoFit = true;
        if (Bitmap is { } bitmap)
            Viewport.Fit(new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height), Bounds.Size);
        ZoomChanged?.Invoke(Viewport.Scale);
        InvalidateVisual();
    }

    public void ActualSize()
    {
        if (Bitmap is { } bitmap) Viewport.ActualSize(new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height), Bounds.Size);
        autoFit = false;
        ZoomChanged?.Invoke(Viewport.Scale);
        InvalidateVisual();
    }

    public void ZoomTo(double scale, Point? anchor = null)
    {
        if (Bitmap is null) return;
        Viewport.Zoom(anchor ?? new Point(Bounds.Width / 2, Bounds.Height / 2), scale / Viewport.Scale);
        autoFit = false;
        ZoomChanged?.Invoke(Viewport.Scale);
        InvalidateVisual();
    }

    private void MoveZoom(Point point)
    {
        if (zoomDrag is not { } drag) return;
        double dx = point.X - drag.Start.X;
        if (Math.Abs(dx) >= 3) drag.Moved = true;
        zoomDrag = drag;
        if (drag.Moved) ZoomTo(Math.Clamp(drag.Scale * Math.Pow(2, dx / 100), 0.001, 32), drag.Start);
    }

    private void MoveLayers(Point document, KeyModifiers modifiers)
    {
        if (layerMoveStart is not { } drag) return;
        Vector offset = document - drag.Start;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            offset = Math.Abs(offset.X) >= Math.Abs(offset.Y) ? new Vector(offset.X, 0) : new Vector(0, offset.Y);
        offset = new Vector(Math.Round(offset.X, MidpointRounding.AwayFromZero), Math.Round(offset.Y, MidpointRounding.AwayFromZero));
        layerMoveStart = drag with { Moved = Math.Abs(offset.X) >= 1 || Math.Abs(offset.Y) >= 1 };
        LayerMoveChanged?.Invoke(offset, !modifiers.HasFlag(KeyModifiers.Control));
    }

    public void Cancel()
    {
        if (captured is null) return;
        if (layerMoveStart is not null)
        {
            layerMoveStart = null;
            var movePointer = captured;
            captured = null;
            movePointer.Capture(null);
            LayerMoveCanceled?.Invoke();
            return;
        }
        if (zoomDrag is not null)
        {
            zoomDrag = null;
            var zoomPointer = captured;
            captured = null;
            zoomPointer.Capture(null);
            return;
        }
        if (shaping)
        {
            var shapePointer = captured;
            captured = null;
            shaping = false;
            shapePointer.Capture(null);
            ShapeCanceled?.Invoke();
            return;
        }
        if (gradientDragging)
        {
            var gradientPointer = captured;
            captured = null;
            gradientDragging = false;
            gradientHandle = -1;
            gradientPointer.Capture(null);
            GradientCanceled?.Invoke();
            return;
        }
        if (freeDistorting)
        {
            var distortPointer = captured;
            captured = null;
            freeDistorting = false;
            freeDistortHandle = -1;
            distortPointer.Capture(null);
            freeDistortCorners = (Point[])freeDistortBefore.Clone();
            FreeDistortChanged?.Invoke((Point[])freeDistortCorners.Clone());
            InvalidateVisual();
            return;
        }
        bool paint = !panning;
        bool select = selecting;
        var pointer = captured; captured = null; pointer.Capture(null);
        selecting = false;
        if (select || movingSelection) { selectionRect = null; selectionPath = null; movingSelection = false; SelectionCanceled?.Invoke(); }
        else if (paint) StrokeCanceled?.Invoke();
    }

    private static BrushPoint StrokePoint(PointerPoint point, Point document)
    {
        double rawPressure = point.Properties.Pressure;
        double pressure = point.Pointer.Type == PointerType.Pen && double.IsFinite(rawPressure)
            ? Math.Clamp(rawPressure, 0, 1) : 1;
        return new BrushPoint(document.X, document.Y, pressure);
    }

    private Point ConstrainStroke(Point point)
    {
        if (!shiftHeld) return point;
        Vector delta = point - strokeStart;
        return Math.Abs(delta.X) >= Math.Abs(delta.Y)
            ? new Point(point.X, strokeStart.Y)
            : new Point(strokeStart.X, point.Y);
    }

    public void SetSelectionRect(Rect? rectangle)
    {
        selectionRect = rectangle;
        InvalidateVisual();
    }

    public void SetSelectionOutline(SelectionOutline? outline)
    {
        selectionOutline = outline;
        InvalidateVisual();
    }

    public void SetTextOverlay(IReadOnlyList<IReadOnlyList<Point>>? selectionPolygons, Point? caretStart, Point? caretEnd)
    {
        textSelectionPolygons = selectionPolygons ?? [];
        textCaretStart = caretStart;
        textCaretEnd = caretEnd;
        InvalidateVisual();
    }

    public void SetFreeDistortCorners(IReadOnlyList<Point>? corners)
    {
        freeDistortCorners = corners is { Count: 4 } ? corners.ToArray() : [];
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty && autoFit) Fit();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(Color.Parse("#D4D4D4")), new Rect(Bounds.Size));
        if (Bitmap is not { } bitmap) return;
        var source = new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        var destination = new Rect(Viewport.Offset.X, Viewport.Offset.Y,
            bitmap.PixelSize.Width * Viewport.Scale, bitmap.PixelSize.Height * Viewport.Scale);
        var visible = destination.Intersect(new Rect(Bounds.Size));
        using (context.PushClip(visible))
        {
            int left = (int)Math.Floor((visible.Left - destination.Left) / 12);
            int top = (int)Math.Floor((visible.Top - destination.Top) / 12);
            for (int y = top; destination.Top + y * 12 < visible.Bottom; y++)
            for (int x = left; destination.Left + x * 12 < visible.Right; x++)
                context.DrawRectangle((x + y) % 2 == 0 ? Brushes.White : Brushes.LightGray, null,
                    new Rect(destination.Left + x * 12, destination.Top + y * 12, 12, 12));
        }
        context.DrawImage(bitmap, source, destination);
        if (textSelectionPolygons.Count > 0)
        {
            var fill = new SolidColorBrush(Color.FromArgb(92, 35, 120, 220));
            foreach (IReadOnlyList<Point> polygon in textSelectionPolygons)
            {
                if (polygon.Count < 3) continue;
                var geometry = new StreamGeometry();
                using (StreamGeometryContext geometryContext = geometry.Open())
                {
                    geometryContext.BeginFigure(Viewport.ToView(polygon[0]), true);
                    for (int index = 1; index < polygon.Count; index++)
                        geometryContext.LineTo(Viewport.ToView(polygon[index]));
                    geometryContext.EndFigure(true);
                }
                context.DrawGeometry(fill, null, geometry);
            }
        }
        if (textCaretStart is { } caretStart && textCaretEnd is { } caretEnd)
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(230, 30, 90, 220)), Math.Max(1, 1 / Viewport.Scale)),
                Viewport.ToView(caretStart), Viewport.ToView(caretEnd));
        if (PixelGridEnabled && Viewport.Scale >= 4)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(160, 80, 80, 80)), 1);
            int left = Math.Max(0, (int)Math.Floor((visible.Left - destination.Left) / Viewport.Scale));
            int top = Math.Max(0, (int)Math.Floor((visible.Top - destination.Top) / Viewport.Scale));
            int right = Math.Min(bitmap.PixelSize.Width, (int)Math.Ceiling((visible.Right - destination.Left) / Viewport.Scale));
            int bottom = Math.Min(bitmap.PixelSize.Height, (int)Math.Ceiling((visible.Bottom - destination.Top) / Viewport.Scale));
            for (int x = left; x <= right; x++)
            {
                double viewX = destination.Left + x * Viewport.Scale;
                context.DrawLine(pen, new Point(viewX, visible.Top), new Point(viewX, visible.Bottom));
            }
            for (int y = top; y <= bottom; y++)
            {
                double viewY = destination.Top + y * Viewport.Scale;
                context.DrawLine(pen, new Point(visible.Left, viewY), new Point(visible.Right, viewY));
            }
        }
        if (selectionOutline is { } outline && outline.LoopLengths.Length > 0)
        {
            var pen = new Pen(Brushes.Black, Math.Max(1, 1 / Viewport.Scale));
            int start = 0;
            foreach (int length in outline.LoopLengths)
            {
                for (int i = 0; i < length; i++)
                {
                    int first = (start + i) * 2;
                    int second = (start + (i + 1) % length) * 2;
                    context.DrawLine(pen,
                        Viewport.ToView(new Point(outline.Coordinates[first], outline.Coordinates[first + 1])),
                        Viewport.ToView(new Point(outline.Coordinates[second], outline.Coordinates[second + 1])));
                }
                start += length;
            }
        }
        else if (selectionRect is { } selection)
        {
            var topLeft = Viewport.ToView(new Point(selection.Left, selection.Top));
            var bottomRight = Viewport.ToView(new Point(selection.Right, selection.Bottom));
            context.DrawRectangle(null, new Pen(Brushes.Black, 1), new Rect(topLeft, bottomRight));
        }
        if (selectionPath is { Count: > 1 } path)
        {
            var pen = new Pen(Brushes.Black, 1);
            for (int i = 1; i < path.Count; i++) context.DrawLine(pen, Viewport.ToView(path[i - 1]), Viewport.ToView(path[i]));
        }
        if (shapeDraft is { } draft && draft.Bounds.Width >= 1 && draft.Bounds.Height >= 1)
        {
            Rect bounds = new(Viewport.ToView(draft.Bounds.TopLeft), Viewport.ToView(draft.Bounds.BottomRight));
            var rgb = draft.Settings;
            var fill = new SolidColorBrush(Color.FromRgb((byte)Math.Round(rgb.Red * 255),
                (byte)Math.Round(rgb.Green * 255), (byte)Math.Round(rgb.Blue * 255)));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(153, 0, 0, 0)), 1);
            if (draft.Settings.Kind == "Ellipse") context.DrawEllipse(fill, pen, bounds.Center, bounds.Width / 2, bounds.Height / 2);
            else
            {
                double radius = Math.Min(draft.Settings.CornerRadius, Math.Min(draft.Bounds.Width, draft.Bounds.Height) / 2) * Viewport.Scale;
                context.DrawRectangle(fill, pen, bounds, radius, radius);
            }
        }
        if (GradientEnabled && gradientStart is { } startPoint && gradientEnd is { } endPoint)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(230, 28, 104, 190)), 2);
            Point start = Viewport.ToView(startPoint), end = Viewport.ToView(endPoint);
            context.DrawLine(pen, start, end);
            context.DrawEllipse(Brushes.White, pen, start, 6, 6);
            context.DrawEllipse(Brushes.White, pen, end, 6, 6);
        }
        if (FreeDistortEnabled && freeDistortCorners.Length == 4)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(230, 28, 104, 190)), Math.Max(1, 1 / Viewport.Scale));
            for (int index = 0; index < 4; index++)
                context.DrawLine(pen, Viewport.ToView(freeDistortCorners[index]), Viewport.ToView(freeDistortCorners[(index + 1) % 4]));
            for (int index = 0; index < 4; index++)
            {
                Point handle = Viewport.ToView(freeDistortCorners[index]);
                context.DrawEllipse(Brushes.White, pen, handle, 6, 6);
            }
        }
    }

    private int FindGradientHandle(Point view)
    {
        if (gradientStart is { } start && (Viewport.ToView(start) - view).Length <= 10) return 0;
        if (gradientEnd is { } end && (Viewport.ToView(end) - view).Length <= 10) return 1;
        return -1;
    }

    private void MoveGradientLine(Point document, bool constrain)
    {
        Point anchor = gradientHandle == 0 ? gradientEnd!.Value : gradientStart!.Value;
        if (constrain)
        {
            Vector delta = document - anchor;
            document = Math.Abs(delta.X) >= Math.Abs(delta.Y)
                ? new Point(document.X, anchor.Y) : new Point(anchor.X, document.Y);
        }
        document = new Point(Math.Clamp(document.X, -30000, 30000), Math.Clamp(document.Y, -30000, 30000));
        if (gradientHandle == 0) gradientStart = document;
        else gradientEnd = document;
        GradientChanged?.Invoke(gradientStart!.Value, gradientEnd!.Value);
        InvalidateVisual();
    }

    private int FindFreeDistortHandle(Point view)
    {
        if (freeDistortCorners.Length != 4) return -1;
        const double radius = 12;
        int nearest = -1;
        double nearestDistance = radius * radius;
        for (int index = 0; index < freeDistortCorners.Length; index++)
        {
            Point handle = Viewport.ToView(freeDistortCorners[index]);
            double distance = Math.Pow(handle.X - view.X, 2) + Math.Pow(handle.Y - view.Y, 2);
            if (distance <= nearestDistance)
            {
                nearest = index;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    private void MoveFreeDistortHandle(Point document, bool constrain)
    {
        if (constrain)
        {
            Point start = freeDistortBefore[freeDistortHandle];
            Vector delta = document - start;
            document = Math.Abs(delta.X) >= Math.Abs(delta.Y)
                ? new Point(document.X, start.Y) : new Point(start.X, document.Y);
        }
        freeDistortCorners[freeDistortHandle] = new Point(
            Math.Clamp(document.X, -30000, 30000), Math.Clamp(document.Y, -30000, 30000));
        FreeDistortChanged?.Invoke((Point[])freeDistortCorners.Clone());
        InvalidateVisual();
    }

    private static Rect Normalize(Point start, Point end) => new(
        Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
        Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
}
