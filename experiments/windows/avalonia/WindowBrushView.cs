using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

// The render callback can run outside the UI thread. Both painting and input
// mutations take this lock; TiledRaster.Paint copies bytes into owned SKImages.
internal sealed class WindowBrushView(Action<string, object?> record) : Decorator
{
    private readonly object gate = new();
    private readonly BrushSession session = new(new TiledRaster(4000, 4000));
    private readonly SoftBrushSettings settings = new(800, 0.4, [1, 0.3, 0.1]);
    private SceneControl? drawing;
    private bool closed;
    private bool renderThreadRecorded;
    private IPointer? captured;

    internal void Initialize()
    {
        Focusable = true;
        Width = Height = 700;
        drawing = new SceneControl(700, 700, canvas =>
        {
            lock (gate)
            {
                if (closed) return;
                if (!renderThreadRecorded) { record("brush-render-thread", Environment.CurrentManagedThreadId); renderThreadRecorded = true; }
                // Src tile replacement must stay inside a transparent layer,
                // otherwise transparent pixels erase the native window background.
                canvas.SaveLayer(); canvas.Scale(0.175f);
                if (session.Active is { } active) active.Paint(canvas);
                else session.Current.Paint(canvas);
                canvas.Restore();
            }
        }) { Width = 700, Height = 700 };
        Child = drawing;
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Focus();
            lock (gate) { if (session.Active is not null) return; session.Begin(settings); }
            captured = e.Pointer; captured.Capture(this);
            Append(e); e.Handled = true;
        };
        PointerMoved += (_, e) => { if (captured == e.Pointer) Append(e); };
        PointerReleased += (_, e) =>
        {
            if (captured != e.Pointer) return;
            Append(e);
            lock (gate)
            {
                long start = Stopwatch.GetTimestamp(); session.Commit();
                record("brush-commit", new { milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds, undoCount = session.UndoCount });
            }
            captured = null; e.Pointer.Capture(null); drawing.InvalidateVisual(); e.Handled = true;
        };
        PointerCaptureLost += (_, _) => { if (captured is not null) Cancel(); };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
        };
    }

    private void Append(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        lock (gate)
        {
            if (session.Active is not { } active) return;
            long start = Stopwatch.GetTimestamp();
            active.Append(new BrushPoint(point.X / 0.175, point.Y / 0.175));
            record("brush-pointer-update", new { x = point.X, y = point.Y, milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds });
        }
        drawing!.InvalidateVisual();
    }

    internal void Cancel()
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (gate) session.Cancel();
        var pointer = captured; captured = null; pointer?.Capture(null);
        drawing!.InvalidateVisual(); record("brush-cancel", null);
    }

    internal void History(bool redo)
    {
        Cancel();
        lock (gate) { if (redo) session.Redo(); else session.Undo(); }
        drawing!.InvalidateVisual(); record(redo ? "brush-redo" : "brush-undo", null);
    }

    internal void Save(string fixtures, string output)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (gate)
        {
            if (session.Active is not null) throw new InvalidOperationException("请先结束或取消当前笔划。");
            session.Current.Export(Path.Combine(output, "final.png"));
            string project = BrushProbe.WriteProject(Path.Combine(fixtures, "brush"), output, 4000, 4000);
            using var reopened = FixtureScene.Read(project);
            reopened.Export(Path.Combine(output, "reopened.png"));
            if (Program.Compare(Path.Combine(output, "final.png"), Path.Combine(output, "reopened.png")).DifferentPixels != 0)
                throw new InvalidDataException("Brush project changed pixels on reopen");
            record("brush-save-reopen", new { passed = true, undoCount = session.UndoCount });
        }
    }

    internal void Close()
    {
        Cancel();
        lock (gate) closed = true;
    }
}
