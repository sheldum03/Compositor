using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;

internal static partial class BrushPerformanceProbe
{
    private sealed partial class Run
    {
        private sealed record ResourceSample(long? PrivateBytes, long WorkingSetBytes, long ManagedBytes, long LastGcCommittedBytes, long LastGcFragmentedBytes, int? Handles);
        private static ResourceSample Resources()
        {
            using var process = Process.GetCurrentProcess(); process.Refresh();
            var gc = GC.GetGCMemoryInfo();
            return new(process.PrivateMemorySize64 > 0 ? process.PrivateMemorySize64 : null, process.WorkingSet64, GC.GetTotalMemory(false), gc.TotalCommittedBytes, gc.FragmentedBytes,
                OperatingSystem.IsWindows() ? process.HandleCount : null);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private WeakReference ReleaseDocument()
        {
            lock (gate)
            {
                var released = new WeakReference(session);
                session.Cancel(); images.Clear(); session = new BrushSession(new TiledRaster(4000, 4000));
                return released;
            }
        }
        private void HistoryCheck(List<string> digests, bool redo)
        {
            lock (gate)
            {
                if (redo) session.Redo(); else session.Undo();
                if (session.Current.Digest() != digests[session.UndoCount]) throw new InvalidDataException("S05 history pixels changed");
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private async Task<List<string>> EditDocument(int round, Action render, Action checkpoint)
        {
            var digests = new List<string> { session.Current.Digest() };
            for (int edit = 0; edit < 100; edit++)
            {
                Stop.Token.ThrowIfCancellationRequested();
                lock (gate) session.Begin(new(160, .4, [1, .3, .1]));
                var frames = new List<Frame>();
                for (int point = 0; point <= 20; point++)
                {
                    Stop.Token.ThrowIfCancellationRequested();
                    frames.Add(await Update(new(180 + edit % 10 * 360 + point * 6,
                        180 + edit / 10 * 360 + 50 * Math.Sin(point * Math.PI / 10)), render));
                }
                double commitMs;
                lock (gate)
                {
                    long t = Stopwatch.GetTimestamp(); session.Commit(); commitMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                    if (session.UndoCount != edit + 1) throw new InvalidDataException("S05 edit history count changed");
                    digests.Add(session.Current.Digest());
                }
                trials.Add(new { round, edit, frames, commitMilliseconds = commitMs, resources = Resources() });
                if (edit % 10 == 9) checkpoint();
            }
            for (int edit = 0; edit < 100; edit++) { Stop.Token.ThrowIfCancellationRequested(); HistoryCheck(digests, false); }
            for (int edit = 0; edit < 100; edit++) { Stop.Token.ThrowIfCancellationRequested(); HistoryCheck(digests, true); }
            var directory = Path.Combine(output, "round-" + round); Directory.CreateDirectory(directory);
            lock (gate)
            {
                session.Current.Export(Path.Combine(directory, "final.png"));
                using var reopened = FixtureScene.Read(BrushProbe.WriteProject(fixtures, directory, 4000, 4000));
                reopened.Export(Path.Combine(directory, "reopened.png"));
                if (Program.Compare(Path.Combine(directory, "final.png"), Path.Combine(directory, "reopened.png")).DifferentPixels != 0)
                    throw new InvalidDataException("S05 saved project pixels changed");
            }
            return digests;
        }
        internal async Task ExecuteLong(Action render, int roundCount)
        {
            string? error = null;
            var rounds = new List<object>(); var releasedDocuments = new List<WeakReference>();
            var baseline = Resources(); var elapsed = Stopwatch.StartNew();
            void SaveLong() => File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
            {
                scenario = roundCount == 3 ? "S05" : "S05-soak-diagnostic", expectedRounds = roundCount, completed = Passed, error, windowsExecuted = OperatingSystem.IsWindows(), nativeWindow,
                windowClosed = WindowClosed, renderScaling = TopLevel.GetTopLevel(View)?.RenderScaling,
                workload = $"4000x4000; 100 local 160px soft strokes with 21 points; undo all/redo all/save/reopen/close; {roundCount} rounds",
                baseline, rounds, elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds, trials,
                resourceAccepted = false,
                notes = "Native software canvas callbacks; synthetic input. All natural post-close samples precede diagnostic full GC at the end. No forced GC between rounds or during edits. Private memory is sampled, not continuous peak; VRAM is not measured. Stable resource tolerance and other tools require separate review."
            }, new JsonSerializerOptions { WriteIndented = true }));
            try
            {
                if (nativeWindow && (TopLevel.GetTopLevel(View) is not { } top || top.ClientSize.Width < 1000 || top.ClientSize.Height < 1000))
                    throw new InvalidOperationException("S05 needs a 1000x1000 logical viewport");
                for (int round = 0; round < roundCount; round++)
                {
                    Stop.Token.ThrowIfCancellationRequested();
                    var digests = await EditDocument(round, render, SaveLong);
                    var beforeClose = Resources(); releasedDocuments.Add(ReleaseDocument()); render();
                    if (nativeWindow) await Task.Delay(1000, Stop.Token); else Thread.Sleep(1000);
                    var afterClose = Resources();
                    // Only diagnose reachability after recording natural post-close memory.
                    ResourceSample? afterDiagnosticCollection = null;
                    if (round == roundCount - 1)
                    {
                        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                        afterDiagnosticCollection = Resources();
                    }
                    int retainedDocuments = releasedDocuments.Count(w => w.IsAlive);
                    if (round == roundCount - 1 && retainedDocuments != 0) throw new InvalidDataException("Closed S05 document is still referenced");
                    rounds.Add(new { round, edits = 100, undoChecks = 100, redoChecks = 100, beforeClose, afterClose,
                        afterDiagnosticCollection, retainedDocuments, finalDigest = digests[^1], saveReopenPassed = true });
                    SaveLong();
                }
                Stop.Token.ThrowIfCancellationRequested(); Passed = true;
            }
            catch (Exception e) { error = e.ToString(); }
            finally { lock (gate) { closed = true; images.Dispose(); } SaveLong(); }
        }
    }
}
