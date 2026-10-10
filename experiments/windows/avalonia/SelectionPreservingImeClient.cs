using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;

// Avalonia 11.3.22 deletes the TextBox selection when preedit begins. Keep that
// range in the document until TextInput commits; the presenter replaces it visually.
internal sealed class SelectionPreservingImeClient : TextInputMethodClient
{
    private readonly TextInputMethodClient inner;
    private readonly TextBox editor;
    private readonly SpacedTextPresenter presenter;
    private (int Start, int End, int Caret)? selection;

    internal static void Attach(TextBox editor, SpacedTextPresenter presenter)
    {
        SelectionPreservingImeClient? client = null;
        editor.AddHandler(InputElement.TextInputMethodClientRequestedEvent, (_, e) =>
        {
            if (e.Client == null) return;
            client ??= new SelectionPreservingImeClient(e.Client, editor, presenter);
            e.Client = client;
        });
        editor.AddHandler(InputElement.TextInputEvent, (_, e) =>
        {
            if (!e.Handled && !string.IsNullOrEmpty(e.Text)) client?.Finish();
        }, RoutingStrategies.Tunnel);
    }

    private SelectionPreservingImeClient(TextInputMethodClient inner, TextBox editor, SpacedTextPresenter presenter)
    {
        this.inner = inner; this.editor = editor; this.presenter = presenter;
        inner.TextViewVisualChanged += (_, _) => RaiseTextViewVisualChanged();
        inner.CursorRectangleChanged += (_, _) => RaiseCursorRectangleChanged();
        inner.SurroundingTextChanged += (_, _) => RaiseSurroundingTextChanged();
        inner.SelectionChanged += (_, _) => RaiseSelectionChanged();
        inner.ResetRequested += (_, _) => RequestReset();
        inner.InputPaneActivationRequested += (_, _) => RaiseInputPaneActivationRequested();
        presenter.PropertyChanged += (_, e) =>
        {
            // Losing focus can clear preedit directly, without calling this client.
            if (e.Property == TextPresenter.PreeditTextProperty && string.IsNullOrEmpty(presenter.PreeditText))
                Finish();
        };
    }

    public override Visual TextViewVisual => inner.TextViewVisual;
    public override bool SupportsPreedit => inner.SupportsPreedit;
    // IMM32 otherwise sends Delete for the exposed selection BEFORE SetPreeditText.
    // This adapter owns pending selection replacement; the backend must not edit it.
    public override bool SupportsSurroundingText => false;
    public override string SurroundingText => inner.SurroundingText;
    public override Rect CursorRectangle => inner.CursorRectangle;
    public override TextSelection Selection { get => inner.Selection; set => inner.Selection = value; }
    public override void ExecuteContextMenuAction(ContextMenuAction action) => inner.ExecuteContextMenuAction(action);
    public override void SetPreeditText(string? text) => SetPreeditText(text, null);

    public override void SetPreeditText(string? text, int? cursorPos)
    {
        if (!string.IsNullOrEmpty(text) && selection == null && editor.SelectionStart != editor.SelectionEnd)
        {
            selection = (editor.SelectionStart, editor.SelectionEnd, editor.CaretIndex);
            presenter.PreeditSelectionLength = Math.Abs(editor.SelectionEnd - editor.SelectionStart);
            int anchor = Math.Min(editor.SelectionStart, editor.SelectionEnd);
            editor.CaretIndex = anchor;
            editor.SelectionStart = anchor;
            editor.SelectionEnd = anchor;
        }
        inner.SetPreeditText(text, cursorPos);
        if (string.IsNullOrEmpty(text)) Finish();
    }

    private void Finish()
    {
        if (selection is not { } saved) return;
        selection = null;
        presenter.PreeditSelectionLength = 0;
        inner.SetPreeditText(null);
        editor.CaretIndex = saved.Caret;
        editor.SelectionStart = saved.Start;
        editor.SelectionEnd = saved.End;
    }
}
