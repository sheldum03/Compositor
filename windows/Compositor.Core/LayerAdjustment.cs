using System.Text.Json;
using System.Text.Json.Nodes;

namespace Compositor.Core;

public sealed record ExposureSettings(double Exposure = 0, double Offset = 0, double Gamma = 1)
{
    public bool IsValid => double.IsFinite(Exposure) && Exposure is >= -20 and <= 20 &&
        double.IsFinite(Offset) && Offset is >= -0.5 and <= 0.5 &&
        double.IsFinite(Gamma) && Gamma is >= 0.01 and <= 9.99;

    public static bool TryRead(JsonNode? node, out ExposureSettings settings)
    {
        settings = new ExposureSettings();
        if (node is null) return true;
        try
        {
            var value = node.AsObject();
            settings = new ExposureSettings(
                value["exposure"]?.GetValue<double>() ?? 0,
                value["offset"]?.GetValue<double>() ?? 0,
                value["gamma"]?.GetValue<double>() ?? 1);
            return settings.IsValid;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    public JsonObject ToJson() => new()
    {
        ["exposure"] = Exposure,
        ["offset"] = Offset,
        ["gamma"] = Gamma
    };
}
