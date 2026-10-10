using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Threading;

internal static class ImeCancelRegression
{
    public static int Run(Assembly assembly, MethodInfo factory)
    {
        const string original = "中文 English 🙂\n请切换微软拼音，在这里输入、取消和确认。";
        int failed = 0, cases = 0;
        foreach (bool reattach in new[] { false, true })
        foreach (var range in new[] { (0, original.Length), (3, 10), (10, 3), (2, 2) })
        foreach (string ending in new[] { "cancel-null", "cancel-empty", "focus-loss", "commit-before-clear", "commit-after-clear" })
        {
            var presenter = (TextPresenter)Activator.CreateInstance(assembly.GetType("SpacedTextPresenter")!, new object[] { 420d, 3d })!;
            var family = new FontFamily("avares://Compositor.AvaloniaProbe/Fonts/SourceHanSansSC-Regular.otf#Source Han Sans SC");
            var editor = (TextBox)factory.Invoke(null, new object[] { presenter, family, original, 24d, 1.25d, TextAlignment.Left, Brushes.DarkBlue })!;
            editor.IsUndoEnabled = true;
            var window = new Window { Width = 760, Height = 520, Content = editor };
            window.Show(); editor.Focus(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            if (reattach)
            {
                window.Content = null; window.Content = editor;
                editor.Focus(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            }
            editor.CaretIndex = range.Item2;
            editor.SelectionStart = range.Item1; editor.SelectionEnd = range.Item2;
            var request = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
            editor.RaiseEvent(request);
            var client = request.Client ?? throw new Exception("No input method client");
            bool valid = ReferenceEquals(client.TextViewVisual, presenter);
            foreach (string preedit in new[] { "c", "ce", "ceshi" })
            {
                client.SetPreeditText(preedit, preedit.Length);
                _ = presenter.TextLayout;
                var cursor = client.CursorRectangle;
                valid &= editor.Text == original && presenter.PreeditText == preedit && double.IsFinite(cursor.X);
            }
            bool commits = ending.StartsWith("commit");
            if (ending == "commit-before-clear") window.KeyTextInput("测试");
            if (ending == "focus-loss")
            {
                var other = new TextBox(); window.Content = other;
                other.Focus(); Dispatcher.UIThread.RunJobs();
            }
            else client.SetPreeditText(ending == "cancel-empty" ? "" : null);
            if (ending == "commit-after-clear") window.KeyTextInput("测试");
            Dispatcher.UIThread.RunJobs();
            int start = Math.Min(range.Item1, range.Item2), length = Math.Abs(range.Item2 - range.Item1);
            string expected = commits ? original.Remove(start, length).Insert(start, "测试") : original;
            valid &= editor.Text == expected && string.IsNullOrEmpty(presenter.PreeditText);
            if (commits)
            {
                editor.Undo(); valid &= editor.Text == original;
                editor.Redo(); valid &= editor.Text == expected;
            }
            else if (ending != "focus-loss")
            {
                valid &= editor.SelectionStart == range.Item1 && editor.SelectionEnd == range.Item2;
                // Cancel must not leave a deletion or restoration in the undo history.
                window.KeyTextInput("1");
                editor.Undo(); valid &= editor.Text == original;
                editor.Redo(); valid &= editor.Text == original.Remove(start, length).Insert(start, "1");
            }
            Console.WriteLine(JsonSerializer.Serialize(new { reattach, start = range.Item1, end = range.Item2, ending, passed = valid }));
            cases++; if (!valid) failed++;
            window.Close();
        }
        Console.WriteLine(JsonSerializer.Serialize(new { boundary = "TextBox IME client; native Windows retest still required", cases, failed }));
        return failed == 0 ? 0 : 1;
    }
}
