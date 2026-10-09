using System.Globalization;

namespace Compositor.App;

public readonly record struct PaletteColor(byte Red, byte Green, byte Blue)
{
    public static PaletteColor Black => new(0, 0, 0);
    public static PaletteColor White => new(255, 255, 255);
    public string Hex => $"{Red:X2}{Green:X2}{Blue:X2}";
    public (double Red, double Green, double Blue) Rgb => (Red / 255d, Green / 255d, Blue / 255d);

    public static PaletteColor FromRgb(double red, double green, double blue) => new(
        Channel(red), Channel(green), Channel(blue));

    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255,
        MidpointRounding.AwayFromZero);

    public static bool TryParse(string? text, out PaletteColor color)
    {
        color = Black;
        string hex = (text ?? "").Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length == 3) hex = string.Concat(hex.Select(character => new string(character, 2)));
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture, out uint value)) return false;
        color = new((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }
}
