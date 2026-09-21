using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

// S02 workload only. Timings end after releasing the Skia canvas lease, not at physical presentation.
internal static class BrushPerformanceProbe
{
    internal static void Window(string fixtures, string output) => AppBuilder.Configure(() => new ProbeApp(fixtures, output))
        .UsePlatformDetect()
        .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software], CompositionMode = [Win32CompositionMode.RedirectionSurface] })
        .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
        .StartWithClassicDesktopLifetime([]);

    private sealed class ProbeApp(string fixtures, string output) : Application
    {
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var run = new Run(fixtures, output, true);
                var window = new Window { Title = "Compositor S02 — running", Width = 1024, Height = 1040, Content = run.View, CanResize = false };
                window.Closed += (_, _) => run.Stop.Cancel();
                window.Opened += async (_, _) =>
                {
                    await run.Execute(30, () => run.View.InvalidateVisual());
                    desktop.Shutdown(run.Passed ? 0 : 1);
                };
                desktop.MainWindow = window;
            }
            base.OnFrameworkInitializationCompleted();
        }
    }

    internal static void Check(string fixtures, string output)
    {
        var run = new Run(fixtures, output, false);
        run.View.Measure(new Size(1000, 1000)); run.View.Arrange(new Rect(0, 0, 1000, 1000));
        using var target = new RenderTargetBitmap(new PixelSize(1000, 1000), new Vector(96, 96));
        run.Execute(1, () => target.Render(run.View)).GetAwaiter().GetResult();
        if (!run.Passed) throw new InvalidOperationException("S02 harness check failed; see report.json");
    }

    private sealed class Run
    {
        private readonly string fixtures, output;
        private readonly bool nativeWindow;
        private readonly object gate = new();
        private readonly TileImageCache images = new();
        private bool closed;
        private readonly List<object> trials = [];
        private readonly SoftBrushSettings settings = new(800, 1, [1, 0.3, 0.1]);
        private BrushSession session = new(new TiledRaster(4000, 4000));
        private TaskCompletionSource<Frame>? pending;
        private long started, requestedAt, renderEnteredAt, paintedAt, paintEndedAt, copied;
        private double appendMs, paintMs;
        private int sequence, paintedSequence, renderThread;
        public SceneControl View { get; }
        public CancellationTokenSource Stop { get; } = new();
        public bool Passed { get; private set; }
        private sealed record Frame(int Sequence, double AppendMilliseconds, double PaintMilliseconds,
            double UpdateToCanvasLeaseReleasedMilliseconds, long NativePixelCopyBytes, int RenderThread,
            double RequestToRenderMilliseconds, double CanvasAcquireMilliseconds, double CanvasReleaseMilliseconds);

        internal Run(string fixtures, string output, bool nativeWindow)
        {
            this.fixtures = fixtures; this.output = output; this.nativeWindow = nativeWindow;
            View = new SceneControl(1000, 1000, Paint, Painted, () => { lock (gate) renderEnteredAt = Stopwatch.GetTimestamp(); }) { Width = 1000, Height = 1000 };
        }
        private void Paint(SKCanvas canvas)
        {
            lock (gate)
            {
                if (closed) return;
                long t = Stopwatch.GetTimestamp();
                canvas.SaveLayer(); canvas.Scale(0.25f);
                copied = session.Active is { } stroke ? stroke.Paint(canvas, images) : session.Current.Paint(canvas, images: images);
                canvas.Restore();
                paintEndedAt = Stopwatch.GetTimestamp();
                paintMs = Stopwatch.GetElapsedTime(t, paintEndedAt).TotalMilliseconds;
                paintedSequence = sequence; paintedAt = t; renderThread = Environment.CurrentManagedThreadId;
            }
        }
        private void Painted()
        {
            lock (gate)
            {
                if (pending is not { } completion || paintedSequence != sequence || paintedAt < started) return;
                var frame = new Frame(sequence, appendMs, paintMs, Stopwatch.GetElapsedTime(started).TotalMilliseconds, copied, renderThread,
                    Stopwatch.GetElapsedTime(requestedAt, renderEnteredAt).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(renderEnteredAt, paintedAt).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(paintEndedAt).TotalMilliseconds);
                pending = null; completion.SetResult(frame);
            }
        }
        private async Task<Frame> Update(BrushPoint point, Action render)
        {
            if (nativeWindow) Dispatcher.UIThread.VerifyAccess();
            Task<Frame> frame;
            lock (gate)
            {
                if (pending is not null) throw new InvalidOperationException("Previous frame was not consumed");
                started = Stopwatch.GetTimestamp();
                session.Active!.Append(point);
                appendMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                sequence++;
                pending = new(TaskCreationOptions.RunContinuationsAsynchronously); frame = pending.Task;
                requestedAt = Stopwatch.GetTimestamp();
            }
            render();
            return await frame.WaitAsync(TimeSpan.FromSeconds(10), Stop.Token);
        }
        internal async Task Execute(int measuredCount, Action render)
        {
            string? error = null; long? sampledPrivatePeak = null;
            using var process = Process.GetCurrentProcess();
            try
            {
                if (nativeWindow && (TopLevel.GetTopLevel(View) is not { } top ||
                    top.ClientSize.Width < 1000 || top.ClientSize.Height < 1000 || !View.IsEffectivelyVisible))
                    throw new InvalidOperationException("S02 needs a fully visible 1000x1000 logical viewport; current screen/DPI cannot provide it");
                var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(fixtures, "checksums.json")))!.AsArray();
                foreach (var item in manifest)
                {
                    string path = Path.Combine(fixtures, item!["path"]!.GetValue<string>());
                    if (new FileInfo(path).Length != item["bytes"]!.GetValue<long>() ||
                        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() != item["sha256"]!.GetValue<string>())
                        throw new InvalidDataException("Frozen brush corpus identity changed");
                }
                var input = JsonNode.Parse(File.ReadAllText(Path.Combine(fixtures, "soft-crossing-4k.json")))!;
                if (input["width"]!.GetValue<int>() != 4000 || input["height"]!.GetValue<int>() != 4000 ||
                    input["diameter"]!.GetValue<int>() != 800 || input["hardness"]!.GetValue<double>() != 0)
                    throw new InvalidDataException("S02 source workload mismatch");
                var paths = input["strokes"]!.AsArray().Select(path => path!.AsArray().Select(p =>
                    new BrushPoint(p![0]!.GetValue<double>(), p[1]!.GetValue<double>())).ToArray()).ToArray();
                if (paths.Length != 2 || paths.Any(p => p.Length != 121)) throw new InvalidDataException("Expected pointer-down plus 120 updates");
                var empty = new TiledRaster(4000, 4000);
                var existing = SoftBrushStroke.ReplaySettled(empty, settings, paths[0]);
                var expected = new[] { SoftBrushStroke.ReplaySettled(empty, settings, paths[1]), SoftBrushStroke.ReplaySettled(existing, settings, paths[1]) };
                for (int scenario = 0; scenario < 2; scenario++)
                for (int trial = 0; trial <= measuredCount; trial++)
                {
                    Stop.Token.ThrowIfCancellationRequested();
                    var source = scenario == 0 ? empty : existing;
                    string sourceDigest = source.Digest();
                    lock (gate) { images.Clear(); session = new BrushSession(source); session.Begin(settings); }
                    var frames = new List<Frame>(); var privateBytes = new List<long?>();
                    var gcBefore = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                    process.Refresh(); var cpuBefore = process.TotalProcessorTime;
                    foreach (var point in paths[1])
                    {
                        frames.Add(await Update(point, render));
                        process.Refresh();
                        long? currentPrivate = process.PrivateMemorySize64 > 0 ? process.PrivateMemorySize64 : null;
                        privateBytes.Add(currentPrivate);
                        if (currentPrivate is { } bytes) sampledPrivatePeak = Math.Max(sampledPrivatePeak ?? 0, bytes);
                    }
                    Stop.Token.ThrowIfCancellationRequested();
                    double commitMs;
                    lock (gate)
                    {
                        long start = Stopwatch.GetTimestamp(); session.Commit();
                        commitMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    }
                    process.Refresh();
                    if (process.PrivateMemorySize64 > 0) sampledPrivatePeak = Math.Max(sampledPrivatePeak ?? 0, process.PrivateMemorySize64);
                    double cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
                    var gcCollections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gcBefore[i]).ToArray();
                    lock (gate)
                    {
                        if (session.UndoCount != 1 || !session.Current.HasSamePixels(expected[scenario]) || source.Digest() != sourceDigest)
                            throw new InvalidDataException("Commit/oracle/source immutability failed");
                        session.Undo(); if (!session.Current.HasSamePixels(source)) throw new InvalidDataException("Undo pixels changed");
                        session.Redo(); if (!session.Current.HasSamePixels(expected[scenario])) throw new InvalidDataException("Redo pixels changed");
                        if (trial == measuredCount) session.Current.Export(Path.Combine(output, scenario == 0 ? "empty-final.png" : "existing-final.png"));
                    }
                    trials.Add(new { scenario = scenario == 0 ? "empty" : "existing", trial, warmup = trial == 0,
                        pointerDown = frames[0], updates = frames.Skip(1).ToArray(), commitMilliseconds = commitMs,
                        cpuMilliseconds = cpuMs, privateBytes, gcCollections,
                        correctness = "settled replay/immutable source/undo/redo exact", digest = session.Current.Digest() });
                    Save(null, sampledPrivatePeak, measuredCount);
                }
                Stop.Token.ThrowIfCancellationRequested();
                Passed = true;
            }
            catch (Exception e) { error = e.ToString(); }
            finally
            {
                lock (gate) { closed = true; images.Dispose(); }
                Save(error, sampledPrivatePeak, measuredCount);
            }
        }
        private void Save(string? error, long? sampledPrivatePeak, int measuredCount) => File.WriteAllText(Path.Combine(output, "report.json"),
            JsonSerializer.Serialize(new { completed = Passed, error, nativeWindow, windowsExecuted = OperatingSystem.IsWindows(),
                platform = Environment.OSVersion.ToString(), processorCount = Environment.ProcessorCount,
                runtime = Environment.Version.ToString(), renderScaling = TopLevel.GetTopLevel(View)?.RenderScaling,
                clientWidth = TopLevel.GetTopLevel(View)?.ClientSize.Width, clientHeight = TopLevel.GetTopLevel(View)?.ClientSize.Height, viewport = "1000x1000 logical; 4000x4000 document; scale .25", opacity = 1,
                diameter = 800, hardness = 0, measuredCountPerScenario = measuredCount, sampledPrivatePeak,
                memoryNotes = "PrivateMemorySize64 sampled after each frame and commit; null if unavailable; not a continuous peak measurement. Fresh one-stroke history per trial; S05 is separate.",
                timingNotes = "Synthetic sequential updates wait for native canvas lease release; includes scheduling and append, not physical presentation, input-device latency, GPU timing, or VRAM. No S02 acceptance inferred automatically.",
                performanceAccepted = false, trials }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
