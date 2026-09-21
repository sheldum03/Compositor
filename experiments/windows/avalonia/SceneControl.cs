using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

// The control and export call the same compositor. The caller owns the scene lifetime.
internal sealed class SceneControl(int width, int height, Action<SKCanvas> paint, Action? afterRender = null, Action? beforeRender = null) : Control
{
    public SceneControl(FixtureScene scene) : this(scene.Width, scene.Height, scene.Paint) { }
    private readonly int width = width, height = height;
    private readonly Action<SKCanvas> paint = paint;
    private readonly Action? afterRender = afterRender;
    private readonly Action? beforeRender = beforeRender;
    public int DrawCalls { get; private set; }
    public override void Render(DrawingContext context) => context.Custom(new Draw(this));

    private sealed class Draw(SceneControl owner) : ICustomDrawOperation
    {
        public Rect Bounds => new(0, 0, owner.width, owner.height);
        public bool HitTest(Point point) => Bounds.Contains(point);
        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context)
        {
            owner.beforeRender?.Invoke();
            var feature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature
                ?? throw new InvalidOperationException("A real Skia drawing context is required");
            using (var lease = feature.Lease()) owner.paint(lease.SkCanvas);
            owner.DrawCalls++;
            owner.afterRender?.Invoke();
        }
    }
}
