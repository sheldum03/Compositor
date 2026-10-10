using System.Text.Json;
using System.Text.Json.Nodes;

namespace Compositor.Core;

public sealed record GradientSettings(double StartRed = 0, double StartGreen = 0, double StartBlue = 0,
    double EndRed = 1, double EndGreen = 1, double EndBlue = 1, double Angle = 0)
{
    public bool IsValid =>
        double.IsFinite(StartRed) && StartRed is >= 0 and <= 1 &&
        double.IsFinite(StartGreen) && StartGreen is >= 0 and <= 1 &&
        double.IsFinite(StartBlue) && StartBlue is >= 0 and <= 1 &&
        double.IsFinite(EndRed) && EndRed is >= 0 and <= 1 &&
        double.IsFinite(EndGreen) && EndGreen is >= 0 and <= 1 &&
        double.IsFinite(EndBlue) && EndBlue is >= 0 and <= 1 &&
        double.IsFinite(Angle) && Angle is >= -3600 and <= 3600;

    public JsonObject ToJson() => new()
    {
        ["angle"] = Angle,
        ["endBlue"] = EndBlue,
        ["endGreen"] = EndGreen,
        ["endRed"] = EndRed,
        ["startBlue"] = StartBlue,
        ["startGreen"] = StartGreen,
        ["startRed"] = StartRed
    };

    public static bool TryRead(JsonNode? node, out GradientSettings settings)
    {
        settings = new GradientSettings();
        if (node is null) return false;
        try
        {
            var value = node.AsObject();
            string[] keys = ["angle", "endBlue", "endGreen", "endRed", "startBlue", "startGreen", "startRed"];
            if (value.Count != keys.Length || !value.All(pair => keys.Contains(pair.Key))) return false;
            settings = new GradientSettings(
                value["startRed"]?.GetValue<double>() ?? 0,
                value["startGreen"]?.GetValue<double>() ?? 0,
                value["startBlue"]?.GetValue<double>() ?? 0,
                value["endRed"]?.GetValue<double>() ?? 1,
                value["endGreen"]?.GetValue<double>() ?? 1,
                value["endBlue"]?.GetValue<double>() ?? 1,
                value["angle"]?.GetValue<double>() ?? 0);
            return settings.IsValid;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }
}
