using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;

// The control and export call the same compositor. The caller owns the scene lifetime.
internal sealed class SceneControl(FixtureScene scene) : Control
{
    private readonly FixtureScene scene = scene;
    public int DrawCalls { get; private set; }
    public override void Render(DrawingContext context) => context.Custom(new Draw(this));

    private sealed class Draw(SceneControl owner) : ICustomDrawOperation
    {
        public Rect Bounds => new(0, 0, owner.scene.Width, owner.scene.Height);
        public bool HitTest(Point point) => Bounds.Contains(point);
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature
                ?? throw new InvalidOperationException("A real Skia drawing context is required");
            using var lease = feature.Lease();
            owner.scene.Paint(lease.SkCanvas);
            owner.DrawCalls++;
        }
    }
}
