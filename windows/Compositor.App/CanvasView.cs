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
    private bool panning, selecting, movingSelection, spaceHeld, autoFit = true;
    private Point previous;
    private Point selectionMoveStart;
    private Rect? selectionRect;
    private List<Point>? selectionPath;
    private SelectionOutline? selectionOutline;
    public CanvasViewport Viewport { get; } = new();
    public WriteableBitmap? Bitmap { get; private set; }
    public bool PaintEnabled { get; set; }
    public bool PixelGridEnabled { get; set; }
    public bool SelectionEnabled { get; set; }
    public bool LassoEnabled { get; set; }
    public bool SelectionMoveEnabled { get; set; }
    public bool IsDrawing => captured is not null && !panning && !selecting && !movingSelection;
    public bool IsSelecting => captured is not null && selecting;
    public Rect? SelectionRect => selectionRect;
    public event Action<BrushPoint>? StrokeStarted;
    public event Action<BrushPoint>? StrokeMoved;
    public event Action<BrushPoint>? StrokeFinished;
    public event Action? StrokeCanceled;
    public event Action<Rect>? SelectionFinished;
    public event Action<IReadOnlyList<Point>>? LassoFinished;
    public event Action<Point, Point>? SelectionMoveFinished;
    public event Action? SelectionCanceled;

    public CanvasView()
    {
        Name = "Canvas"; Focusable = true; ClipToBounds = true;
        PointerPressed += (_, e) =>
        {
            if (Bitmap is null || captured is not null) return;
            var properties = e.GetCurrentPoint(this).Properties;
            bool pan = properties.IsMiddleButtonPressed || spaceHeld && properties.IsLeftButtonPressed;
            Point view = e.GetPosition(this), document = Viewport.ToDocument(view);
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
            else if (!panning) StrokeStarted?.Invoke(StrokePoint(e.GetCurrentPoint(this), document));
            e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (captured != e.Pointer) return;
            Point view = e.GetPosition(this);
            if (panning) { Viewport.Pan(view - previous); previous = view; autoFit = false; InvalidateVisual(); }
            else if (selecting || movingSelection)
            {
                Point document = Viewport.ToDocument(view);
                selectionRect = Normalize(selectionRect!.Value.Position, document);
                if (selecting && LassoEnabled && (selectionPath is null || Math.Abs(document.X - selectionPath[^1].X) + Math.Abs(document.Y - selectionPath[^1].Y) > 0.5)) selectionPath?.Add(document);
                InvalidateVisual();
            }
            else StrokeMoved?.Invoke(StrokePoint(e.GetCurrentPoint(this), Viewport.ToDocument(view)));
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (captured != e.Pointer) return;
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
                StrokeFinished?.Invoke(StrokePoint(e.GetCurrentPoint(this), Viewport.ToDocument(e.GetPosition(this))));
            e.Handled = true;
        };
        PointerCaptureLost += (_, _) => Cancel();
        PointerWheelChanged += (_, e) =>
        {
            if (Bitmap is null || captured is not null) return;
            Viewport.Zoom(e.GetPosition(this), Math.Pow(1.15, e.Delta.Y));
            autoFit = false; InvalidateVisual(); e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Space) { spaceHeld = true; e.Handled = true; }
            if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        };
        KeyUp += (_, e) => { if (e.Key == Key.Space) { spaceHeld = false; e.Handled = true; } };
        LostFocus += (_, _) => { spaceHeld = false; Cancel(); };
    }

    public void SetBitmap(WriteableBitmap? bitmap)
    {
        bool changedSize = Bitmap?.PixelSize != bitmap?.PixelSize;
        Bitmap = bitmap;
        if (changedSize) Fit();
        InvalidateVisual();
    }

    public void Fit()
    {
        autoFit = true;
        if (Bitmap is { } bitmap)
            Viewport.Fit(new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height), Bounds.Size);
        InvalidateVisual();
    }

    public void ActualSize()
    {
        if (Bitmap is { } bitmap) Viewport.ActualSize(new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height), Bounds.Size);
        autoFit = false;
        InvalidateVisual();
    }

    public void Cancel()
    {
        if (captured is null) return;
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
    }

    private static Rect Normalize(Point start, Point end) => new(
        Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
        Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
}
