using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
if (args.Length != 1 || Path.Exists(args[0])) throw new ArgumentException("Supply a new report path");
var samples = new List<object>(); var refs = new List<WeakReference>(); var timer = Stopwatch.StartNew();
var baseline = Sample();
for (int round = 0; round < 27; round++)
{
    refs.Add(Edit());
    Thread.Sleep(1000);
    samples.Add(new { round, afterClose = Sample(), retainedDocuments = refs.Count(r => r.IsAlive) });
}
var idle = new List<object> { new { seconds = 0, resources = Sample() } };
for (int i = 1; i <= 6; i++) { Thread.Sleep(10000); idle.Add(new { seconds = i * 10, resources = Sample() }); }
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
var afterDiagnosticCollection = Sample();
if (refs.Any(r => r.IsAlive)) throw new Exception("Core document retained");
File.WriteAllText(args[0], JsonSerializer.Serialize(new { scenario = "S05-core-only-control", windowsExecuted = OperatingSystem.IsWindows(), elapsedSeconds = timer.Elapsed.TotalSeconds, baseline, samples, idle, afterDiagnosticCollection, retainedDocuments = 0, resourceAccepted = false, scope = "Same actual brush/history code and 27x100 edits. No window, rendering, PNG export/readback, or retained frame records. Separate diagnostic process; not S05 acceptance." }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("CORE CONTROL COMPLETE: 2700 edits, undo and redo checks; zero retained documents");
static object Sample()
{
    using var p = Process.GetCurrentProcess(); p.Refresh(); var gc = GC.GetGCMemoryInfo();
    return new { p.PrivateMemorySize64, p.WorkingSet64, ManagedBytes = GC.GetTotalMemory(false), gc.TotalCommittedBytes, gc.HeapSizeBytes, gc.FragmentedBytes, Gen2Collections = GC.CollectionCount(2), TotalAllocatedBytes = GC.GetTotalAllocatedBytes(false), Handles = OperatingSystem.IsWindows() ? p.HandleCount : (int?)null };
}
[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference Edit()
{
    var session = new BrushSession(new TiledRaster(4000,4000));
    var digests = new List<string> { session.Current.Digest() };
    for (int edit = 0; edit < 100; edit++)
    {
        var stroke = session.Begin(new(160,.4,[1,.3,.1]));
        for (int point = 0; point <= 20; point++) stroke.Append(new(180 + edit % 10 * 360 + point * 6, 180 + edit / 10 * 360 + 50 * Math.Sin(point * Math.PI / 10)));
        session.Commit(); digests.Add(session.Current.Digest());
    }
    if (digests[^1] != "9f82875d4ea2fef45bfb642e257fdab9683cd21124fb52e92ce99bb3b5b73af7") throw new Exception("S05 pixels changed");
    for (int i=0;i<100;i++) { session.Undo(); if (session.Current.Digest()!=digests[session.UndoCount]) throw new Exception("Undo changed"); }
    for (int i=0;i<100;i++) { session.Redo(); if (session.Current.Digest()!=digests[session.UndoCount]) throw new Exception("Redo changed"); }
    return new WeakReference(session);
}
