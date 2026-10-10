using Avalonia;
using Compositor.Core;

namespace Compositor.App;

public sealed record ShapeDraft(ShapeSettings Settings, Point Anchor, Rect Bounds)
{
    public ShapeDraft DragTo(Point point, bool square, bool fromCenter)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentException("Invalid shape pointer.", nameof(point));
        double dx = Math.Round(point.X, MidpointRounding.AwayFromZero) - Anchor.X;
        double dy = Math.Round(point.Y, MidpointRounding.AwayFromZero) - Anchor.Y;
        if (square)
        {
            double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            dx = dx < 0 ? -side : side;
            dy = dy < 0 ? -side : side;
        }
        Rect bounds = fromCenter
            ? new Rect(Anchor.X - Math.Abs(dx), Anchor.Y - Math.Abs(dy), Math.Abs(dx) * 2, Math.Abs(dy) * 2)
            : new Rect(Math.Min(Anchor.X, Anchor.X + dx), Math.Min(Anchor.Y, Anchor.Y + dy), Math.Abs(dx), Math.Abs(dy));
        return this with { Bounds = bounds };
    }
}
