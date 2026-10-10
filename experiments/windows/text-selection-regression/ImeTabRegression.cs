using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Threading;

internal static class ImeTabRegression
{
    public static int Run(Assembly assembly, MethodInfo factory)
    {
        const string original = "中文 English 🙂\n请切换微软拼音，在这里输入、取消和确认。";
        var presenter = (TextPresenter)Activator.CreateInstance(assembly.GetType("SpacedTextPresenter")!, new object[] { 420d, 3d })!;
        var family = new FontFamily("avares://Compositor.AvaloniaProbe/Fonts/SourceHanSansSC-Regular.otf#Source Han Sans SC");
        var editor = (TextBox)factory.Invoke(null, new object[] { presenter, family, original, 24d, 1.25d, TextAlignment.Left, Brushes.DarkBlue })!;
        editor.Width = 420; editor.Height = 260;
        var canvas = new Canvas { Width = 760, Height = 520, Children = { editor } };
        var tabs = new TabControl { ItemsSource = new[] {
            new TabItem { Header = "文字 / 输入法", Content = new ScrollViewer { Content = canvas } },
            new TabItem { Header = "4K 笔刷", Content = new Canvas { Width = 760, Height = 520 } }
        }};
        var window = new ImeWindow { Width = 1000, Height = 880, Content = tabs };
        void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        TextInputMethodClient Client()
        {
            return window.Recorder.Client ?? throw new Exception("No platform input method client");
        }
        window.Show(); Layout(); editor.Focus(); Layout();
        var client = Client();
        editor.SelectAll(); client.SetPreeditText("ceshi", 5); client.SetPreeditText(null);
        bool firstCancel = editor.Text == original;
        editor.SelectAll(); client.SetPreeditText("ceshi", 5); client.SetPreeditText(null); window.KeyTextInput("测试");
        bool committed = editor.Text == "测试";
        editor.Undo(); editor.Redo(); editor.Undo();
        bool restored = editor.Text == original;
        int cases = 0, failed = firstCancel && committed && restored ? 0 : 1;
        foreach (bool switchTabs in new[] { false, true })
        foreach (var range in new[] { (0, original.Length), (3, 10), (10, 3), (2, 2) })
        foreach (string ending in new[] { "cancel", "commit-before-clear", "commit-after-clear" })
        {
            if (switchTabs)
            {
                tabs.SelectedIndex = 1; Layout(); tabs.SelectedIndex = 0; Layout(); editor.Focus(); Layout();
            }
            var after = Client();
            editor.CaretIndex = range.Item2;
            editor.SelectionStart = range.Item1; editor.SelectionEnd = range.Item2;
            // Replay Avalonia.Win32 11.3.22 Imm32InputMethod.HandleCompositionStart:
            // clear preedit, then synthesize Delete when the client exposes a selection.
            after.SetPreeditText(null);
            if (after.SupportsSurroundingText && after.Selection.Start != after.Selection.End)
            {
                window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
                window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            }
            foreach (string preedit in new[] { "c", "ce", "ceshi" }) after.SetPreeditText(preedit, preedit.Length);
            bool preservedDuring = editor.Text == original;
            if (ending == "commit-before-clear") window.KeyTextInput("测试");
            after.SetPreeditText(null);
            if (ending == "commit-after-clear") window.KeyTextInput("测试");
            Layout();
            int start = Math.Min(range.Item1, range.Item2), length = Math.Abs(range.Item2 - range.Item1);
            string expected = ending == "cancel" ? original : original.Remove(start, length).Insert(start, "测试");
            bool passed = preservedDuring && editor.Text == expected;
            if (ending != "cancel")
            {
                editor.Undo(); passed &= editor.Text == original;
                editor.Redo(); passed &= editor.Text == expected;
                editor.Undo(); passed &= editor.Text == original;
            }
            Console.WriteLine(JsonSerializer.Serialize(new { switchTabs, start = range.Item1, end = range.Item2, ending, sameClient = ReferenceEquals(client, after), passed, actual = editor.Text }));
            cases++; if (!passed) { failed++; break; }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { boundary = "framework-managed client and IMM32 start replay; native retest required", cases, failed }));
        window.Close();
        return failed == 0 ? 0 : 1;
    }

    private sealed class ImeWindow : Window, ITextInputMethodRoot
    {
        public ITextInputMethodImpl Ime { get; } = DispatchProxy.Create<ITextInputMethodImpl, RecordingIme>();
        public RecordingIme Recorder => (RecordingIme)(object)Ime;
        ITextInputMethodImpl ITextInputMethodRoot.InputMethod => Ime;
    }

    public class RecordingIme : DispatchProxy
    {
        public TextInputMethodClient Client { get; private set; }
        protected override object Invoke(MethodInfo method, object[] args)
        {
            if (method.Name == "SetClient")
            {
                Client?.SetPreeditText(null);
                Client = (TextInputMethodClient)args[0];
            }
            if (method.Name == "Reset") Client?.SetPreeditText(null);
            return null;
        }
    }
}
