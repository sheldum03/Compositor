using System.Text.Json;
using System.Text.Json.Nodes;

namespace Compositor.Core;

public sealed record ShapeSettings(string Kind = "Rectangle", double Red = 0, double Green = 0,
    double Blue = 0, double CornerRadius = 0)
{
    public bool IsValid => Kind is "Rectangle" or "Ellipse" &&
        double.IsFinite(Red) && Red is >= 0 and <= 1 &&
        double.IsFinite(Green) && Green is >= 0 and <= 1 &&
        double.IsFinite(Blue) && Blue is >= 0 and <= 1 &&
        double.IsFinite(CornerRadius) && CornerRadius is >= 0 and <= 30000;

    public JsonObject ToJson() => new()
    {
        ["blue"] = Blue,
        ["cornerRadius"] = CornerRadius,
        ["green"] = Green,
        ["kind"] = Kind,
        ["red"] = Red
    };

    public static bool TryRead(JsonNode? node, out ShapeSettings settings)
    {
        settings = new ShapeSettings();
        if (node is null) return false;
        try
        {
            var value = node.AsObject();
            if (value.Count != 5 || !value.All(pair => new[] { "blue", "cornerRadius", "green", "kind", "red" }.Contains(pair.Key)))
                return false;
            settings = new ShapeSettings(
                value["kind"]?.GetValue<string>() ?? "",
                value["red"]?.GetValue<double>() ?? 0,
                value["green"]?.GetValue<double>() ?? 0,
                value["blue"]?.GetValue<double>() ?? 0,
                value["cornerRadius"]?.GetValue<double>() ?? 0);
            return settings.IsValid && (settings.Kind != "Ellipse" || settings.CornerRadius == 0);
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }
}
