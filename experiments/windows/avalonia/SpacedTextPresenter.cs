using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

// Plain-text experiment only. TextBox owns input; TextBlock's public LineSpacing
// supplies paragraph spacing which the default TextPresenter does not forward.
internal sealed class SpacedTextPresenter(double? boxWidth, double lineSpacing) : TextPresenter
{
    protected override TextLayout CreateTextLayout()
    {
        string preedit = PreeditText ?? "";
        string text = Text ?? "";
        // TextBox can request layout between the text and selection/caret updates.
        int caret = Math.Clamp(CaretIndex, 0, text.Length);
        string combined = text.Insert(caret, preedit);
        var builder = new LayoutBuilder
        {
            FontFamily = FontFamily, FontSize = FontSize, FontStyle = FontStyle,
            FontWeight = FontWeight, FontStretch = FontStretch, FontFeatures = FontFeatures,
            Foreground = Foreground, FlowDirection = FlowDirection, TextAlignment = TextAlignment,
            TextWrapping = boxWidth.HasValue ? TextWrapping.Wrap : TextWrapping.NoWrap,
            LetterSpacing = LetterSpacing, LineSpacing = lineSpacing, UseLayoutRounding = false
        };
        int start = preedit.Length > 0 ? caret : Math.Min(SelectionStart, SelectionEnd);
        int end = preedit.Length > 0 ? start + preedit.Length : Math.Max(SelectionStart, SelectionEnd);
        if (preedit.Length == 0 && (start == end || !ShowSelectionHighlight || SelectionForegroundBrush == null))
            builder.Text = combined;
        else
        {
            builder.Inlines!.Add(new Run(combined[..start]));
            var marked = new Run(combined[start..end]);
            if (preedit.Length > 0) marked.TextDecorations = TextDecorations.Underline;
            else marked.Foreground = SelectionForegroundBrush;
            builder.Inlines.Add(marked);
            builder.Inlines.Add(new Run(combined[end..]));
        }
        return builder.Build(boxWidth);
    }

    private sealed class LayoutBuilder : TextBlock
    {
        public TextLayout Build(double? boxWidth)
        {
            Measure(new Size(boxWidth ?? double.PositiveInfinity, double.PositiveInfinity));
            double width = boxWidth ?? Math.Max(1, Math.Ceiling(DesiredSize.Width));
            Arrange(new Rect(0, 0, width, DesiredSize.Height));
            // Create a detached layout owned by the presenter, then dispose the
            // factory's temporary cached layout before discarding the factory.
            var result = CreateTextLayout(Text);
            InvalidateTextLayout();
            return result;
        }
    }
}
