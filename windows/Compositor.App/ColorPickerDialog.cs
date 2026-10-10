using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.App;

internal sealed class ColorPickerDialog : Window
{
    private readonly Slider hue = new() { Name = "PickerHue", Minimum = 0, Maximum = 360, Width = 280 };
    private readonly Slider saturation = new() { Name = "PickerSaturation", Minimum = 0, Maximum = 100, Width = 280 };
    private readonly Slider brightness = new() { Name = "PickerBrightness", Minimum = 0, Maximum = 100, Width = 280 };
    private readonly NumericUpDown red = Channel("PickerRed");
    private readonly NumericUpDown green = Channel("PickerGreen");
    private readonly NumericUpDown blue = Channel("PickerBlue");
    private readonly TextBox hex = new() { Name = "PickerHex", Width = 110, MaxLength = 7 };
    private readonly Border preview = new() { Name = "PickerPreview", Width = 140, Height = 48 };
    private readonly TextBlock error = new() { Name = "PickerError", Foreground = Brushes.Firebrick };
    private readonly Button apply = new() { Name = "AcceptColor", Content = "确定" };
    private PaletteColor working;
    private bool updating;

    public ColorPickerDialog(string title, PaletteColor original)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var cancel = new Button { Name = "CancelColor", Content = "取消" };
        apply.Click += (_, _) => Close((PaletteColor?)working);
        cancel.Click += (_, _) => Close((PaletteColor?)null);
        Content = new StackPanel { Margin = new Thickness(20), Spacing = 10, Children =
        {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                new TextBlock { Text = "原颜色", Width = 140 }, new TextBlock { Text = "新颜色", Width = 140 }
            } },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                new Border { Width = 140, Height = 48, Background = Brush(original) }, preview
            } },
            Row("色相", hue), Row("饱和度", saturation), Row("亮度", brightness),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                new TextBlock { Text = "R" }, red, new TextBlock { Text = "G" }, green,
                new TextBlock { Text = "B" }, blue
            } },
            Row("HEX", hex), error,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, cancel } }
        } };
        foreach (Slider field in new[] { hue, saturation, brightness })
            field.PropertyChanged += (_, change) =>
            {
                if (!updating && change.Property == Slider.ValueProperty) SetColor(FromHsb(), updateHsb: false);
            };
        foreach (NumericUpDown field in new[] { red, green, blue })
            field.PropertyChanged += (_, change) =>
            {
                if (!updating && change.Property == NumericUpDown.ValueProperty)
                    SetColor(PaletteColor.FromRgb((double)(red.Value ?? 0) / 255,
                        (double)(green.Value ?? 0) / 255, (double)(blue.Value ?? 0) / 255));
            };
        hex.PropertyChanged += (_, change) =>
        {
            if (updating || change.Property != TextBox.TextProperty) return;
            if (PaletteColor.TryParse(hex.Text, out var color)) SetColor(color, updateHex: false);
            else { error.Text = "请输入 RGB 或 RRGGBB 十六进制颜色。"; apply.IsEnabled = false; }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) { Close((PaletteColor?)null); e.Handled = true; }
        };
        SetColor(original);
    }

    private void SetColor(PaletteColor color, bool updateHsb = true, bool updateHex = true)
    {
        updating = true;
        try
        {
            working = color;
            red.Value = color.Red; green.Value = color.Green; blue.Value = color.Blue;
            if (updateHex) hex.Text = color.Hex;
            if (updateHsb)
            {
                var rgb = color.Rgb;
                double high = Math.Max(rgb.Red, Math.Max(rgb.Green, rgb.Blue));
                double low = Math.Min(rgb.Red, Math.Min(rgb.Green, rgb.Blue));
                double delta = high - low;
                brightness.Value = high * 100;
                if (high > 0) saturation.Value = delta / high * 100;
                if (delta > 0)
                {
                    double value = high == rgb.Red ? (rgb.Green - rgb.Blue) / delta
                        : high == rgb.Green ? (rgb.Blue - rgb.Red) / delta + 2 : (rgb.Red - rgb.Green) / delta + 4;
                    hue.Value = (value * 60 + 360) % 360;
                }
            }
            preview.Background = Brush(color);
            error.Text = "";
            apply.IsEnabled = true;
        }
        finally { updating = false; }
    }

    private PaletteColor FromHsb()
    {
        double h = hue.Value % 360 / 60, s = saturation.Value / 100, v = brightness.Value / 100;
        double c = v * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;
        (double r, double g, double b) = (int)h switch
        {
            0 => (c, x, 0d), 1 => (x, c, 0d), 2 => (0d, c, x),
            3 => (0d, x, c), 4 => (x, 0d, c), _ => (c, 0d, x)
        };
        return PaletteColor.FromRgb(r + m, g + m, b + m);
    }

    private static NumericUpDown Channel(string name) => new()
        { Name = name, Minimum = 0, Maximum = 255, Increment = 1, FormatString = "0", Width = 70 };
    private static SolidColorBrush Brush(PaletteColor color) => new(Color.FromRgb(color.Red, color.Green, color.Blue));
    private static StackPanel Row(string label, Control field) => new()
    {
        Orientation = Orientation.Horizontal, Spacing = 8, Children =
        { new TextBlock { Text = label, Width = 56, VerticalAlignment = VerticalAlignment.Center }, field }
    };
}
