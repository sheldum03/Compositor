using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Compositor.App;

public sealed class CanvasView : Control
{
    private IPointer? captured;
    private bool panning, selecting, spaceHeld, autoFit = true;
    private Point previous;
    private Rect? selectionRect;
    public CanvasViewport Viewport { get; } = new();
    public WriteableBitmap? Bitmap { get; private set; }
    public bool PaintEnabled { get; set; }
    public bool PixelGridEnabled { get; set; }
    public bool SelectionEnabled { get; set; }
    public bool IsDrawing => captured is not null && !panning && !selecting;
    public bool IsSelecting => captured is not null && selecting;
    public Rect? SelectionRect => selectionRect;
    public event Action<Point>? StrokeStarted;
    public event Action<Point>? StrokeMoved;
    public event Action<Point>? StrokeFinished;
    public event Action? StrokeCanceled;
    public event Action<Rect>? SelectionFinished;
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
            if (!pan && !select && (!PaintEnabled || !properties.IsLeftButtonPressed || document.X < 0 || document.Y < 0 ||
                         document.X >= Bitmap.PixelSize.Width || document.Y >= Bitmap.PixelSize.Height)) return;
            if (select && (document.X < 0 || document.Y < 0 || document.X >= Bitmap.PixelSize.Width || document.Y >= Bitmap.PixelSize.Height)) return;
            Focus(); captured = e.Pointer; panning = pan; selecting = select; previous = view; captured.Capture(this);
            if (select) selectionRect = new Rect(document, new Size(0, 0));
            else if (!panning) StrokeStarted?.Invoke(document);
            e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (captured != e.Pointer) return;
            Point view = e.GetPosition(this);
            if (panning) { Viewport.Pan(view - previous); previous = view; autoFit = false; InvalidateVisual(); }
            else if (selecting) { selectionRect = Normalize(selectionRect!.Value.Position, Viewport.ToDocument(view)); InvalidateVisual(); }
            else StrokeMoved?.Invoke(Viewport.ToDocument(view));
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (captured != e.Pointer) return;
            bool paint = !panning;
            bool select = selecting;
            captured = null; e.Pointer.Capture(null);
            selecting = false;
            if (select) SelectionFinished?.Invoke(selectionRect!.Value);
            else if (paint) StrokeFinished?.Invoke(Viewport.ToDocument(e.GetPosition(this)));
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
        if (select) { selectionRect = null; SelectionCanceled?.Invoke(); }
        else if (paint) StrokeCanceled?.Invoke();
    }

    public void SetSelectionRect(Rect? rectangle)
    {
        selectionRect = rectangle;
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
        if (selectionRect is { } selection)
        {
            var topLeft = Viewport.ToView(new Point(selection.Left, selection.Top));
            var bottomRight = Viewport.ToView(new Point(selection.Right, selection.Bottom));
            context.DrawRectangle(null, new Pen(Brushes.Black, 1), new Rect(topLeft, bottomRight));
        }
    }

    private static Rect Normalize(Point start, Point end) => new(
        Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
        Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
}
