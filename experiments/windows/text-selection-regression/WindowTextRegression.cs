using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class WindowTextRegression
{
    internal static int Run(Assembly assembly, string fixtures, string output, string native)
    {
        if (Path.Exists(output)) throw new ArgumentException("Output must be new");
        Directory.CreateDirectory(output);
        var library = NativeLibrary.Load(Path.GetFullPath(native));
        NativeLibrary.SetDllImportResolver(assembly, (name, _, _) => name == "compositor_native" ? library : 0);
        var type = assembly.GetType("WindowProbe+ProbeWindow")!;
        var window = (Window)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
            null, [Path.GetFullPath(fixtures), Path.GetFullPath(output), Path.GetFullPath(native), false], null)!;
        var editor = (TextBox)type.GetField("editor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var presenter = (TextPresenter)type.GetField("presenter", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        void Click(string label)
        {
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label));
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            Check(point.X >= 0 && point.X < window.Bounds.Width && point.Y >= 0 && point.Y < window.Bounds.Height, "Button outside viewport: " + label);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.None); Layout();
        }
        void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
        window.Show(); Layout();
        string original = editor.Text!;
        Click("垂直翻转"); Click("非等比缩放"); Click("水平翻转");
        foreach (bool reverse in new[] { false, true })
        {
            editor.Focus(); Layout();
            var a = presenter.TextLayout.HitTestTextPosition(0);
            var b = presenter.TextLayout.HitTestTextPosition(2);
            var from = presenter.TranslatePoint(new Point(a.X + .1, a.Y + a.Height / 2), window)!.Value;
            var to = presenter.TranslatePoint(new Point(b.X - .1, b.Y + b.Height / 2), window)!.Value;
            if (reverse) (from, to) = (to, from);
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
            for (int i = 1; i <= 10; i++) window.MouseMove(from + (to - from) * (i / 10d), RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left, RawInputModifiers.None); Layout();
            Check(editor.SelectedText == "中文", "Transformed native-probe selection changed");
            int start = editor.SelectionStart, end = editor.SelectionEnd, caret = editor.CaretIndex;
            Click("导出并校验");
            string save = Path.Combine(output, reverse ? "002-text" : "001-text");
            using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(save, "text-state.json")));
            var row = state.RootElement;
            Check(row.GetProperty("text").GetString() == original && row.GetProperty("selectedText").GetString() == "中文", "Export text state differs");
            Check(row.GetProperty("selectionStart").GetInt32() == start && row.GetProperty("selectionEnd").GetInt32() == end && row.GetProperty("caretIndex").GetInt32() == caret, "Export selection state differs");
            Check(row.GetProperty("flipX").GetBoolean() && row.GetProperty("flipY").GetBoolean() && row.GetProperty("stretchX").GetDouble() == .75, "Export transform differs");
            Check(editor.SelectionStart == start && editor.SelectionEnd == end && editor.CaretIndex == caret, "Export changed selection");
            Check(File.ReadAllBytes(Path.Combine(save, "preview.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(save, "export.png"))), "Shared-layout export pixels differ");
            editor.Focus(); window.KeyTextInput("1"); Check(editor.Text == "1" + original[2..], "Selection replacement changed");
            editor.Undo(); Check(editor.Text == original, "Export damaged undo");
            editor.Redo(); Check(editor.Text == "1" + original[2..], "Export damaged redo");
            editor.Undo(); Check(editor.Text == original, "History failed to restore original");
        }
        window.Close(); Layout();
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "window-report.json")));
        Check(report.RootElement.GetProperty("processId").GetInt32() == Environment.ProcessId && report.RootElement.GetProperty("title").GetString()!.Contains(Path.GetFileName(output)), "Window identity differs");
        Check(!report.RootElement.GetProperty("nativeWindow").GetBoolean(), "Headless check mislabeled as native window");
        Check(!report.RootElement.GetProperty("events").EnumerateArray().Any(e => e.GetProperty("name").GetString() == "action-error"), "Window action failed");
        Console.WriteLine(JsonSerializer.Serialize(new { boundary = "actual probe window in Headless; not native IME or system DPI acceptance", selections = 2, exports = 2, replacementUndoRedo = "passed", exportPreservedSelection = true }));
        return 0;
    }
}
