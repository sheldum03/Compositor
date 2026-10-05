using Avalonia;

namespace Compositor.App;

public sealed class CanvasViewport
{
    public double Scale { get; private set; } = 1;
    public Vector Offset { get; private set; }
    public Point ToDocument(Point view) => new((view.X - Offset.X) / Scale, (view.Y - Offset.Y) / Scale);
    public Point ToView(Point document) => new(document.X * Scale + Offset.X, document.Y * Scale + Offset.Y);

    public void Fit(Size document, Size view)
    {
        if (document.Width <= 0 || document.Height <= 0 || view.Width <= 0 || view.Height <= 0) return;
        Scale = Math.Clamp(Math.Min(Math.Max(1, view.Width - 48) / document.Width,
            Math.Max(1, view.Height - 48) / document.Height), 0.001, 32);
        Offset = new Vector((view.Width - document.Width * Scale) / 2, (view.Height - document.Height * Scale) / 2);
    }

    public void Zoom(Point anchor, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        Point pixel = ToDocument(anchor);
        Scale = Math.Clamp(Scale * factor, 0.001, 32);
        Offset = new Vector(anchor.X - pixel.X * Scale, anchor.Y - pixel.Y * Scale);
    }

    public void Pan(Vector delta) => Offset += delta;
}
