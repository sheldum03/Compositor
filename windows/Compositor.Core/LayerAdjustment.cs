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

public sealed record HueSaturationSettings(double Hue = 0, double Saturation = 0,
    double Lightness = 0, bool Colorize = false)
{
    public bool IsValid => double.IsFinite(Hue) && Hue is >= -360 and <= 360 &&
        double.IsFinite(Saturation) && Saturation is >= -100 and <= 100 &&
        double.IsFinite(Lightness) && Lightness is >= -100 and <= 100;

    public bool IsIdentity => !Colorize && Hue == 0 && Saturation == 0 && Lightness == 0;

    public static bool TryRead(JsonNode? node, out HueSaturationSettings settings)
    {
        settings = new HueSaturationSettings();
        if (node is null) return true;
        try
        {
            var value = node.AsObject();
            settings = new HueSaturationSettings(
                value["hue"]?.GetValue<double>() ?? 0,
                value["saturation"]?.GetValue<double>() ?? 0,
                value["lightness"]?.GetValue<double>() ?? 0,
                value["colorize"]?.GetValue<bool>() ?? false);
            return settings.IsValid;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    public JsonObject ToJson() => new()
    {
        ["hue"] = Hue,
        ["saturation"] = Saturation,
        ["lightness"] = Lightness,
        ["colorize"] = Colorize
    };
}

public sealed record LevelRange(double InputBlack = 0, double InputWhite = 255, double Gamma = 1,
    double OutputBlack = 0, double OutputWhite = 255)
{
    public bool IsValid => double.IsFinite(InputBlack) && InputBlack is >= 0 and <= 254 &&
        double.IsFinite(InputWhite) && InputWhite >= InputBlack + 1 && InputWhite <= 255 &&
        double.IsFinite(Gamma) && Gamma is >= 0.1 and <= 9.99 &&
        double.IsFinite(OutputBlack) && OutputBlack is >= 0 and <= 255 &&
        double.IsFinite(OutputWhite) && OutputWhite is >= 0 and <= 255;

    public double Apply(double value)
    {
        if (!IsValid) throw new InvalidOperationException("Invalid levels range.");
        double input = Math.Clamp((value * 255 - InputBlack) / (InputWhite - InputBlack), 0, 1);
        return Math.Clamp((OutputBlack + Math.Pow(input, 1 / Gamma) * (OutputWhite - OutputBlack)) / 255, 0, 1);
    }

    public static bool TryRead(JsonNode? node, out LevelRange range)
    {
        range = new LevelRange();
        if (node is null) return true;
        try
        {
            var value = node.AsObject();
            range = new LevelRange(
                value["inputBlack"]?.GetValue<double>() ?? 0,
                value["inputWhite"]?.GetValue<double>() ?? 255,
                value["gamma"]?.GetValue<double>() ?? 1,
                value["outputBlack"]?.GetValue<double>() ?? 0,
                value["outputWhite"]?.GetValue<double>() ?? 255);
            return range.IsValid;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    public JsonObject ToJson() => new()
    {
        ["inputBlack"] = InputBlack,
        ["inputWhite"] = InputWhite,
        ["gamma"] = Gamma,
        ["outputBlack"] = OutputBlack,
        ["outputWhite"] = OutputWhite
    };
}

public enum LevelsAutoMode
{
    Contrast,
    Color,
    Neutral
}

public sealed record LevelsSettings(LevelRange Rgb, LevelRange Red, LevelRange Green, LevelRange Blue)
{
    public LevelsSettings() : this(new(), new(), new(), new()) { }

    public bool IsValid => Rgb.IsValid && Red.IsValid && Green.IsValid && Blue.IsValid;
    public bool IsIdentity => Rgb == new LevelRange() && Red == new LevelRange() &&
        Green == new LevelRange() && Blue == new LevelRange();

    public double Apply(double value, int channel)
    {
        LevelRange range = channel switch
        {
            0 => Red,
            1 => Green,
            2 => Blue,
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
        return Rgb.Apply(range.Apply(value));
    }

    public static LevelsSettings FromHistogram(IReadOnlyList<double[]> histogram, LevelsAutoMode mode)
    {
        if (histogram.Count < 4 || histogram.Any(bins => bins.Length != 256))
            throw new ArgumentException("Levels histogram must contain four 256-bin channels.", nameof(histogram));
        var result = new LevelRange[4];

        static (double Low, double High)? Endpoints(double[] bins)
        {
            double total = bins.Sum(value => value > 0 && double.IsFinite(value) ? value : 0);
            if (total <= 0) return null;
            double sum = 0;
            int low = 0, high = 255;
            for (int index = 0; index < 256; index++)
            {
                sum += Math.Max(0, bins[index]);
                if (sum > total * 0.001) { low = index; break; }
            }
            sum = 0;
            for (int index = 255; index >= 0; index--)
            {
                sum += Math.Max(0, bins[index]);
                if (sum > total * 0.001) { high = index; break; }
            }
            return low < high ? (low, high) : null;
        }

        if (mode == LevelsAutoMode.Contrast)
        {
            var limits = Enumerable.Range(1, 3).Select(channel => Endpoints(histogram[channel])).
                Where(value => value is not null).Select(value => value!.Value).ToArray();
            if (limits.Length > 0)
                result[0] = new LevelRange(limits.Min(value => value.Low), limits.Max(value => value.High));
        }
        else
        {
            for (int channel = 1; channel <= 3; channel++)
            {
                var limits = Endpoints(histogram[channel]);
                if (limits is null) continue;
                var range = new LevelRange(limits.Value.Low, limits.Value.High);
                if (mode == LevelsAutoMode.Neutral)
                {
                    double total = histogram[channel].Sum(value => value > 0 && double.IsFinite(value) ? value : 0);
                    if (total > 0)
                    {
                        double mean = histogram[channel].Select((value, index) =>
                            range.Apply(index / 255d) * Math.Max(0, value)).Sum() / total;
                        if (mean > 0 && mean < 1)
                            range = range with { Gamma = Math.Clamp(Math.Log(mean) / Math.Log(0.5), 0.1, 9.99) };
                    }
                }
                result[channel] = range;
            }
        }
        return new LevelsSettings(result[0] ?? new(), result[1] ?? new(), result[2] ?? new(), result[3] ?? new());
    }

    public static bool TryRead(JsonNode? node, out LevelsSettings settings)
    {
        settings = new LevelsSettings();
        if (node is null) return true;
        try
        {
            var value = node.AsObject();
            if (!LevelRange.TryRead(value["rgb"], out var rgb) ||
                !LevelRange.TryRead(value["red"], out var red) ||
                !LevelRange.TryRead(value["green"], out var green) ||
                !LevelRange.TryRead(value["blue"], out var blue)) return false;
            settings = new LevelsSettings(rgb, red, green, blue);
            return settings.IsValid;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    public JsonObject ToJson() => new()
    {
        ["rgb"] = Rgb.ToJson(),
        ["red"] = Red.ToJson(),
        ["green"] = Green.ToJson(),
        ["blue"] = Blue.ToJson()
    };
}

public sealed record CurveChannelSettings(double Shadow = 0, double Mid = 128, double Highlight = 255)
{
    public bool IsValid => double.IsFinite(Shadow) && Shadow is >= 0 and <= 255 &&
        double.IsFinite(Mid) && Mid is >= 0 and <= 255 &&
        double.IsFinite(Highlight) && Highlight is >= 0 and <= 255;

    public bool IsIdentity => Shadow == 0 && Mid == 128 && Highlight == 255;

    public double Apply(double value)
    {
        if (!IsValid) throw new InvalidOperationException("Invalid curves settings.");
        return ApplyValue(value, Shadow, Mid, Highlight);
    }

    internal static double ApplyValue(double value, double shadow, double mid, double highlight)
    {
        double x = Math.Clamp(value, 0, 1) * 255;
        double y;
        if (x <= 128)
        {
            y = Hermite(x, 0, shadow, 128, mid, (mid - shadow) / 128,
                Slope((mid - shadow) / 128, (highlight - mid) / 127));
        }
        else
        {
            y = Hermite(x, 128, mid, 255, highlight,
                Slope((mid - shadow) / 128, (highlight - mid) / 127), (highlight - mid) / 127);
        }
        return Math.Clamp(y / 255, 0, 1);

        static double Slope(double left, double right)
        {
            if (left * right <= 0) return 0;
            return 2 / (1 / left + 1 / right);
        }

        static double Hermite(double x, double x0, double y0, double x1, double y1,
            double slope0, double slope1)
        {
            double span = x1 - x0, t = Math.Clamp((x - x0) / span, 0, 1);
            return (2 * t * t * t - 3 * t * t + 1) * y0 +
                (t * t * t - 2 * t * t + t) * span * slope0 +
                (-2 * t * t * t + 3 * t * t) * y1 +
                (t * t * t - t * t) * span * slope1;
        }
    }

    public JsonObject ToJson() => new()
    {
        ["shadow"] = Shadow,
        ["mid"] = Mid,
        ["highlight"] = Highlight
    };
}

public sealed record CurvesSettings(double Shadow = 0, double Mid = 128, double Highlight = 255,
    CurveChannelSettings? Red = null, CurveChannelSettings? Green = null, CurveChannelSettings? Blue = null)
{
    public bool IsValid => double.IsFinite(Shadow) && Shadow is >= 0 and <= 255 &&
        double.IsFinite(Mid) && Mid is >= 0 and <= 255 &&
        double.IsFinite(Highlight) && Highlight is >= 0 and <= 255 &&
        (Red?.IsValid ?? true) && (Green?.IsValid ?? true) && (Blue?.IsValid ?? true);

    public bool IsIdentity => Shadow == 0 && Mid == 128 && Highlight == 255 &&
        (Red?.IsIdentity ?? true) && (Green?.IsIdentity ?? true) && (Blue?.IsIdentity ?? true);

    public double Apply(double value) => CurveChannelSettings.ApplyValue(value, Shadow, Mid, Highlight);

    public double Apply(double value, int channel)
    {
        CurveChannelSettings? selected = channel switch
        {
            0 => Red,
            1 => Green,
            2 => Blue,
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
        return selected is null ? Apply(value) : selected.Apply(value);
    }

    public static bool TryRead(JsonNode? node, out CurvesSettings settings)
    {
        settings = new CurvesSettings();
        if (node is null) return true;
        try
        {
            var value = node.AsObject();
            CurveChannelSettings? ReadChannel(string name)
            {
                if (value[name] is null) return null;
                var channel = value[name]!.AsObject();
                return new CurveChannelSettings(
                    channel["shadow"]?.GetValue<double>() ?? 0,
                    channel["mid"]?.GetValue<double>() ?? 128,
                    channel["highlight"]?.GetValue<double>() ?? 255);
            }
            settings = new CurvesSettings(
                value["shadow"]?.GetValue<double>() ?? 0,
                value["mid"]?.GetValue<double>() ?? 128,
                value["highlight"]?.GetValue<double>() ?? 255,
                ReadChannel("red"), ReadChannel("green"), ReadChannel("blue"));
            return settings.IsValid;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    public JsonObject ToJson()
    {
        var value = new JsonObject
        {
            ["shadow"] = Shadow,
            ["mid"] = Mid,
            ["highlight"] = Highlight
        };
        if (Red is not null) value["red"] = Red.ToJson();
        if (Green is not null) value["green"] = Green.ToJson();
        if (Blue is not null) value["blue"] = Blue.ToJson();
        return value;
    }
}

public sealed record GradientMapStop(int Red = 0, int Green = 0, int Blue = 0)
{
    public bool IsValid => Red is >= 0 and <= 255 && Green is >= 0 and <= 255 && Blue is >= 0 and <= 255;

    public JsonObject ToJson() => new()
    {
        ["red"] = Red,
        ["green"] = Green,
        ["blue"] = Blue
    };

    public static bool TryRead(JsonNode? node, out GradientMapStop stop)
    {
        stop = new GradientMapStop();
        if (node is null) return false;
        try
        {
            var value = node.AsObject();
            stop = new GradientMapStop(
                value["red"]?.GetValue<int>() ?? 0,
                value["green"]?.GetValue<int>() ?? 0,
                value["blue"]?.GetValue<int>() ?? 0);
            return stop.IsValid;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }
}

public sealed record GradientMapSettings(GradientMapStop Shadow, GradientMapStop Highlight)
{
    public GradientMapSettings() : this(new(), new(255, 255, 255)) { }

    public bool IsValid => Shadow.IsValid && Highlight.IsValid;

    public bool IsIdentity => Shadow == new GradientMapStop() && Highlight == new GradientMapStop(255, 255, 255);

    public (double Red, double Green, double Blue) Apply(double value)
    {
        if (!IsValid) throw new InvalidOperationException("Invalid gradient map settings.");
        double amount = Math.Clamp(value, 0, 1);
        return (
            (Shadow.Red + (Highlight.Red - Shadow.Red) * amount) / 255d,
            (Shadow.Green + (Highlight.Green - Shadow.Green) * amount) / 255d,
            (Shadow.Blue + (Highlight.Blue - Shadow.Blue) * amount) / 255d);
    }

    public static bool TryRead(JsonNode? node, out GradientMapSettings settings)
    {
        settings = new GradientMapSettings();
        if (node is null) return true;
        try
        {
            var value = node.AsObject();
            if (!GradientMapStop.TryRead(value["shadow"], out var shadow) ||
                !GradientMapStop.TryRead(value["highlight"], out var highlight)) return false;
            settings = new GradientMapSettings(shadow, highlight);
            return settings.IsValid;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    public JsonObject ToJson() => new()
    {
        ["shadow"] = Shadow.ToJson(),
        ["highlight"] = Highlight.ToJson()
    };
}

public sealed record GaussianBlurSettings(int Radius = 1)
{
    public bool IsValid => Radius is >= 1 and <= 32;

    public bool IsIdentity => false;

    public static bool TryRead(JsonNode? node, out GaussianBlurSettings settings)
    {
        settings = new GaussianBlurSettings();
        if (node is null) return true;
        try
        {
            settings = new GaussianBlurSettings(node.AsObject()["radius"]?.GetValue<int>() ?? 1);
            return settings.IsValid;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    public JsonObject ToJson() => new() { ["radius"] = Radius };
}

public sealed record MotionBlurSettings(double Angle = 0, int Distance = 1)
{
    public bool IsValid => double.IsFinite(Angle) && Angle is >= -90 and <= 90 && Distance is >= 1 and <= 32;

    public bool IsIdentity => false;

    public static bool TryRead(JsonNode? node, out MotionBlurSettings settings)
    {
        settings = new MotionBlurSettings();
        if (node is null) return true;
        try
        {
            var value = node.AsObject();
            settings = new MotionBlurSettings(
                value["angle"]?.GetValue<double>() ?? 0,
                value["distance"]?.GetValue<int>() ?? 1);
            return settings.IsValid;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    public JsonObject ToJson() => new()
    {
        ["angle"] = Angle,
        ["distance"] = Distance
    };
}

public sealed record NoiseSettings(double Amount = 10, bool Gaussian = false, bool Monochromatic = false, uint Seed = 0)
{
    public bool IsValid => double.IsFinite(Amount) && Amount is >= 0.1 and <= 400;

    public bool IsIdentity => false;

    public static bool TryRead(JsonNode? node, out NoiseSettings settings)
    {
        settings = new NoiseSettings();
        if (node is null) return true;
        try
        {
            var value = node.AsObject();
            settings = new NoiseSettings(
                value["amount"]?.GetValue<double>() ?? 10,
                value["gaussian"]?.GetValue<bool>() ?? false,
                value["monochromatic"]?.GetValue<bool>() ?? false,
                value["seed"]?.GetValue<uint>() ?? 0);
            return settings.IsValid;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    public JsonObject ToJson() => new()
    {
        ["amount"] = Amount,
        ["gaussian"] = Gaussian,
        ["monochromatic"] = Monochromatic,
        ["seed"] = Seed
    };
}

public sealed record LensCorrectionSettings(double Distortion = 0)
{
    public bool IsValid => double.IsFinite(Distortion) && Distortion is >= -100 and <= 100;

    public bool IsIdentity => Distortion == 0;

    public static bool TryRead(JsonNode? node, out LensCorrectionSettings settings)
    {
        settings = new LensCorrectionSettings();
        if (node is null) return true;
        try
        {
            settings = new LensCorrectionSettings(node.AsObject()["distortion"]?.GetValue<double>() ?? 0);
            return settings.IsValid;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    public JsonObject ToJson() => new() { ["distortion"] = Distortion };
}

public sealed record GrainSettings(double Amount = 25, double Size = 1.5, double Roughness = 50, uint Seed = 0)
{
    public bool IsValid => double.IsFinite(Amount) && Amount is >= 0 and <= 100 &&
        double.IsFinite(Size) && Size is >= 0.5 and <= 20 &&
        double.IsFinite(Roughness) && Roughness is >= 0 and <= 100;

    public bool IsIdentity => Amount == 0;

    public static bool TryRead(JsonNode? node, out GrainSettings settings)
    {
        settings = new GrainSettings();
        if (node is null) return true;
        try
        {
            var value = node.AsObject();
            settings = new GrainSettings(
                value["amount"]?.GetValue<double>() ?? 25,
                value["size"]?.GetValue<double>() ?? 1.5,
                value["roughness"]?.GetValue<double>() ?? 50,
                value["seed"]?.GetValue<uint>() ?? 0);
            return settings.IsValid;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    public JsonObject ToJson() => new()
    {
        ["amount"] = Amount,
        ["size"] = Size,
        ["roughness"] = Roughness,
        ["seed"] = Seed
    };
}
