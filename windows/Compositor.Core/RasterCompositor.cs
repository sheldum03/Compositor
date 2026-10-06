namespace Compositor.Core;

public static class RasterCompositor
{
    public static TileRaster ApplyExposure(TileRaster image, ExposureSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        double scale = Math.Pow(2, settings.Exposure);
        var table = new byte[256];
        for (int index = 0; index < table.Length; index++)
        {
            double encoded = index / 255d;
            double linear = encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);
            linear = Math.Pow(Math.Max(0, linear * scale + settings.Offset), 1 / settings.Gamma);
            double output = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
            table[index] = (byte)Math.Round(Math.Clamp(output, 0, 1) * 255, MidpointRounding.AwayFromZero);
        }
        var result = new TileRaster(image.Width, image.Height);
        for (int row = 0; row * TileRaster.TileSize < image.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < image.Width; column++)
        {
            byte[] pixels = image.ReadTileCopy(column, row);
            for (int pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                int alpha = pixels[pixel + 3];
                if (alpha == 0) continue;
                for (int channel = 0; channel < 3; channel++)
                {
                    double straight = pixels[pixel + channel] * 255d / alpha;
                    int low = Math.Clamp((int)straight, 0, 255);
                    int high = Math.Min(255, low + 1);
                    double mapped = table[low] + (table[high] - table[low]) * (straight - low);
                    pixels[pixel + channel] = (byte)Math.Clamp(
                        Math.Round(mapped * alpha / 255d, MidpointRounding.AwayFromZero), 0, alpha);
                }
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    public static TileRaster ApplyLevels(TileRaster image, LevelsSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var tables = new byte[3][];
        for (int channel = 0; channel < tables.Length; channel++)
        {
            tables[channel] = new byte[256];
            for (int index = 0; index < 256; index++)
                tables[channel][index] = (byte)Math.Round(settings.Apply(index / 255d, channel) * 255,
                    MidpointRounding.AwayFromZero);
        }
        var result = new TileRaster(image.Width, image.Height);
        for (int row = 0; row * TileRaster.TileSize < image.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < image.Width; column++)
        {
            byte[] pixels = image.ReadTileCopy(column, row);
            for (int pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                int alpha = pixels[pixel + 3];
                if (alpha == 0) continue;
                for (int channel = 0; channel < 3; channel++)
                {
                    double straight = pixels[pixel + channel] * 255d / alpha;
                    int low = Math.Clamp((int)straight, 0, 255);
                    int high = Math.Min(255, low + 1);
                    double mapped = tables[channel][low] +
                        (tables[channel][high] - tables[channel][low]) * (straight - low);
                    pixels[pixel + channel] = (byte)Math.Clamp(
                        Math.Round(mapped * alpha / 255d, MidpointRounding.AwayFromZero), 0, alpha);
                }
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    public static TileRaster ApplyHueSaturation(TileRaster image, HueSaturationSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var result = new TileRaster(image.Width, image.Height);
        for (int row = 0; row * TileRaster.TileSize < image.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < image.Width; column++)
        {
            byte[] pixels = image.ReadTileCopy(column, row);
            for (int pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                int alpha = pixels[pixel + 3];
                if (alpha == 0) continue;
                double red = pixels[pixel] / (double)alpha;
                double green = pixels[pixel + 1] / (double)alpha;
                double blue = pixels[pixel + 2] / (double)alpha;
                (double hue, double saturation, double lightness) = ToHsl(red, green, blue);
                double lightnessAmount;
                if (settings.Colorize)
                {
                    hue = WrapHue(settings.Hue);
                    saturation = Math.Clamp(settings.Saturation / 100, 0, 1);
                    lightnessAmount = settings.Lightness / 100;
                }
                else
                {
                    hue = WrapHue(hue + settings.Hue);
                    saturation = Math.Clamp(saturation * (1 + settings.Saturation / 100), 0, 1);
                    lightnessAmount = settings.Lightness / 100;
                }
                double amount = Math.Clamp(lightnessAmount, -1, 1);
                lightness = amount >= 0
                    ? lightness + (1 - lightness) * amount
                    : lightness * (1 + amount);
                (red, green, blue) = ToRgb(hue, saturation, Math.Clamp(lightness, 0, 1));
                pixels[pixel] = (byte)Math.Clamp(Math.Round(red * alpha, MidpointRounding.AwayFromZero), 0, alpha);
                pixels[pixel + 1] = (byte)Math.Clamp(Math.Round(green * alpha, MidpointRounding.AwayFromZero), 0, alpha);
                pixels[pixel + 2] = (byte)Math.Clamp(Math.Round(blue * alpha, MidpointRounding.AwayFromZero), 0, alpha);
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;

        static double WrapHue(double hue)
        {
            double wrapped = hue % 360;
            return wrapped < 0 ? wrapped + 360 : wrapped;
        }

        static (double Hue, double Saturation, double Lightness) ToHsl(double red, double green, double blue)
        {
            double high = Math.Max(red, Math.Max(green, blue));
            double low = Math.Min(red, Math.Min(green, blue));
            double lightness = (high + low) / 2;
            double delta = high - low;
            if (delta <= 0) return (0, 0, lightness);
            double saturation = delta / (1 - Math.Abs(2 * lightness - 1));
            double hue = high == red ? (green - blue) / delta
                : high == green ? (blue - red) / delta + 2
                : (red - green) / delta + 4;
            hue *= 60;
            if (hue < 0) hue += 360;
            return (hue, Math.Clamp(saturation, 0, 1), lightness);
        }

        static (double Red, double Green, double Blue) ToRgb(double hue, double saturation, double lightness)
        {
            if (saturation <= 0) return (lightness, lightness, lightness);
            double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            double sector = hue / 60;
            double second = chroma * (1 - Math.Abs(sector % 2 - 1));
            double baseValue = lightness - chroma / 2;
            var rgb = (Red: chroma, Green: 0d, Blue: 0d);
            switch ((int)sector)
            {
                case 1: rgb = (second, chroma, 0); break;
                case 2: rgb = (0, chroma, second); break;
                case 3: rgb = (0, second, chroma); break;
                case 4: rgb = (second, 0, chroma); break;
                case 5: rgb = (chroma, 0, second); break;
            }
            return (Math.Clamp(rgb.Red + baseValue, 0, 1), Math.Clamp(rgb.Green + baseValue, 0, 1),
                Math.Clamp(rgb.Blue + baseValue, 0, 1));
        }
    }

    public static TileRaster ApplyCurves(TileRaster image, CurvesSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var tables = new byte[3][];
        for (int channel = 0; channel < tables.Length; channel++)
        {
            tables[channel] = new byte[256];
            for (int index = 0; index < tables[channel].Length; index++)
                tables[channel][index] = (byte)Math.Round(settings.Apply(index / 255d, channel) * 255,
                    MidpointRounding.AwayFromZero);
        }
        var result = new TileRaster(image.Width, image.Height);
        for (int row = 0; row * TileRaster.TileSize < image.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < image.Width; column++)
        {
            byte[] pixels = image.ReadTileCopy(column, row);
            for (int pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                int alpha = pixels[pixel + 3];
                if (alpha == 0) continue;
                for (int channel = 0; channel < 3; channel++)
                {
                    double straight = pixels[pixel + channel] * 255d / alpha;
                    int low = Math.Clamp((int)straight, 0, 255);
                    int high = Math.Min(255, low + 1);
                    double mapped = tables[channel][low] +
                        (tables[channel][high] - tables[channel][low]) * (straight - low);
                    pixels[pixel + channel] = (byte)Math.Clamp(
                        Math.Round(mapped * alpha / 255d, MidpointRounding.AwayFromZero), 0, alpha);
                }
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    public static TileRaster ApplyGradientMap(TileRaster image, GradientMapSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        var table = new (byte Red, byte Green, byte Blue)[256];
        for (int index = 0; index < table.Length; index++)
        {
            var mapped = settings.Apply(index / 255d);
            table[index] = (
                (byte)Math.Round(mapped.Red * 255, MidpointRounding.AwayFromZero),
                (byte)Math.Round(mapped.Green * 255, MidpointRounding.AwayFromZero),
                (byte)Math.Round(mapped.Blue * 255, MidpointRounding.AwayFromZero));
        }
        var result = new TileRaster(image.Width, image.Height);
        for (int row = 0; row * TileRaster.TileSize < image.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < image.Width; column++)
        {
            byte[] pixels = image.ReadTileCopy(column, row);
            for (int pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                int alpha = pixels[pixel + 3];
                if (alpha == 0) continue;
                double red = pixels[pixel] * 255d / alpha;
                double green = pixels[pixel + 1] * 255d / alpha;
                double blue = pixels[pixel + 2] * 255d / alpha;
                int low = Math.Clamp((int)Math.Round(0.2126 * red + 0.7152 * green + 0.0722 * blue,
                    MidpointRounding.AwayFromZero), 0, 255);
                (byte mappedRed, byte mappedGreen, byte mappedBlue) = table[low];
                pixels[pixel] = (byte)Math.Clamp(Math.Round(mappedRed * alpha / 255d, MidpointRounding.AwayFromZero), 0, alpha);
                pixels[pixel + 1] = (byte)Math.Clamp(Math.Round(mappedGreen * alpha / 255d, MidpointRounding.AwayFromZero), 0, alpha);
                pixels[pixel + 2] = (byte)Math.Clamp(Math.Round(mappedBlue * alpha / 255d, MidpointRounding.AwayFromZero), 0, alpha);
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    public static TileRaster ApplyGaussianBlur(TileRaster image, GaussianBlurSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int width = image.Width, height = image.Height, radius = settings.Radius;
        byte[] input = new byte[checked(width * height * 4)];
        for (int row = 0; row * TileRaster.TileSize < height; row++)
        for (int column = 0; column * TileRaster.TileSize < width; column++)
        {
            var size = image.TileDimensions(column, row);
            byte[] tile = image.ReadTileCopy(column, row);
            for (int y = 0; y < size.Height; y++)
                tile.AsSpan(y * size.Width * 4, size.Width * 4).CopyTo(
                    input.AsSpan(((row * TileRaster.TileSize + y) * width + column * TileRaster.TileSize) * 4, size.Width * 4));
        }
        double sigma = Math.Max(0.5, radius / 2d);
        double[] kernel = new double[radius * 2 + 1];
        double total = 0;
        for (int offset = -radius; offset <= radius; offset++)
        {
            double weight = Math.Exp(-(offset * offset) / (2 * sigma * sigma));
            kernel[offset + radius] = weight;
            total += weight;
        }
        for (int index = 0; index < kernel.Length; index++) kernel[index] /= total;
        double[] horizontal = new double[input.Length], blurred = new double[input.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        for (int channel = 0; channel < 4; channel++)
        {
            double value = 0;
            for (int offset = -radius; offset <= radius; offset++)
            {
                int sampleX = Math.Clamp(x + offset, 0, width - 1);
                value += input[(y * width + sampleX) * 4 + channel] * kernel[offset + radius];
            }
            horizontal[(y * width + x) * 4 + channel] = value;
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        for (int channel = 0; channel < 4; channel++)
        {
            double value = 0;
            for (int offset = -radius; offset <= radius; offset++)
            {
                int sampleY = Math.Clamp(y + offset, 0, height - 1);
                value += horizontal[(sampleY * width + x) * 4 + channel] * kernel[offset + radius];
            }
            blurred[(y * width + x) * 4 + channel] = value;
        }
        var result = new TileRaster(width, height);
        for (int row = 0; row * TileRaster.TileSize < height; row++)
        for (int column = 0; column * TileRaster.TileSize < width; column++)
        {
            var size = result.TileDimensions(column, row);
            byte[] tile = new byte[size.Width * size.Height * 4];
            for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
            for (int channel = 0; channel < 4; channel++)
                tile[(y * size.Width + x) * 4 + channel] = (byte)Math.Clamp(
                    Math.Round(blurred[((row * TileRaster.TileSize + y) * width + column * TileRaster.TileSize + x) * 4 + channel], MidpointRounding.AwayFromZero), 0, 255);
            result = result.ReplaceTile(column, row, tile);
        }
        return result;
    }

    public static TileRaster ApplyMotionBlur(TileRaster image, MotionBlurSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        int width = image.Width, height = image.Height, distance = settings.Distance;
        byte[] input = new byte[checked(width * height * 4)];
        for (int row = 0; row * TileRaster.TileSize < height; row++)
        for (int column = 0; column * TileRaster.TileSize < width; column++)
        {
            var size = image.TileDimensions(column, row);
            byte[] tile = image.ReadTileCopy(column, row);
            for (int y = 0; y < size.Height; y++)
                tile.AsSpan(y * size.Width * 4, size.Width * 4).CopyTo(
                    input.AsSpan(((row * TileRaster.TileSize + y) * width + column * TileRaster.TileSize) * 4, size.Width * 4));
        }
        double angle = settings.Angle * Math.PI / 180;
        double directionX = Math.Cos(angle), directionY = Math.Sin(angle);
        double[] blurred = new double[input.Length];
        int sampleCount = distance * 2 + 1;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        for (int channel = 0; channel < 4; channel++)
        {
            double value = 0;
            for (int offset = -distance; offset <= distance; offset++)
            {
                double sampleX = x + directionX * offset;
                double sampleY = y + directionY * offset;
                value += Sample(sampleX, sampleY, channel);
            }
            blurred[(y * width + x) * 4 + channel] = value / sampleCount;
        }
        var result = new TileRaster(width, height);
        for (int row = 0; row * TileRaster.TileSize < height; row++)
        for (int column = 0; column * TileRaster.TileSize < width; column++)
        {
            var size = result.TileDimensions(column, row);
            byte[] tile = new byte[size.Width * size.Height * 4];
            for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
            for (int channel = 0; channel < 4; channel++)
                tile[(y * size.Width + x) * 4 + channel] = (byte)Math.Clamp(
                    Math.Round(blurred[((row * TileRaster.TileSize + y) * width + column * TileRaster.TileSize + x) * 4 + channel], MidpointRounding.AwayFromZero), 0, 255);
            result = result.ReplaceTile(column, row, tile);
        }
        return result;

        double Sample(double x, double y, int channel)
        {
            x = Math.Clamp(x, 0, width - 1);
            y = Math.Clamp(y, 0, height - 1);
            int left = (int)Math.Floor(x), top = (int)Math.Floor(y);
            int right = Math.Min(width - 1, left + 1), bottom = Math.Min(height - 1, top + 1);
            double horizontal = x - left, vertical = y - top;
            double upper = input[(top * width + left) * 4 + channel] * (1 - horizontal) +
                input[(top * width + right) * 4 + channel] * horizontal;
            double lower = input[(bottom * width + left) * 4 + channel] * (1 - horizontal) +
                input[(bottom * width + right) * 4 + channel] * horizontal;
            return upper * (1 - vertical) + lower * vertical;
        }
    }

    public static TileRaster ApplyNoise(TileRaster image, NoiseSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        double spread = settings.Amount / 100d * 127.5d;
        var result = new TileRaster(image.Width, image.Height);
        for (int row = 0; row * TileRaster.TileSize < image.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < image.Width; column++)
        {
            byte[] pixels = image.ReadTileCopy(column, row);
            var size = image.TileDimensions(column, row);
            for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
            {
                int pixel = (y * size.Width + x) * 4;
                int alpha = pixels[pixel + 3];
                if (alpha == 0) continue;
                int documentX = column * TileRaster.TileSize + x;
                int documentY = row * TileRaster.TileSize + y;
                uint baseKey = Mix32(settings.Seed ^ Mix32(unchecked((uint)(documentY * (long)image.Width + documentX))));
                for (int channel = 0; channel < 3; channel++)
                {
                    uint key = settings.Monochromatic
                        ? baseKey
                        : unchecked(baseKey + (uint)channel * 0x9e3779b9U);
                    double noise = settings.Gaussian
                        ? Math.Sqrt(-2 * Math.Log(1 - Unit(key))) * Math.Cos(2 * Math.PI * Unit(key ^ 0x68e31da4U)) * spread * (2d / 3d)
                        : (Unit(key) * 2 - 1) * spread;
                    double straight = pixels[pixel + channel] * 255d / alpha;
                    straight = Math.Clamp(straight + noise, 0, 255);
                    pixels[pixel + channel] = (byte)Math.Clamp(
                        Math.Round(straight * alpha / 255d, MidpointRounding.AwayFromZero), 0, alpha);
                }
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;

        static double Unit(uint key) => (Mix32(key) >> 8) * (1d / 16777216d);
        static uint Mix32(uint value)
        {
            value ^= value >> 16;
            value = unchecked(value * 0x7feb352dU);
            value ^= value >> 15;
            value = unchecked(value * 0x846ca68bU);
            return value ^ (value >> 16);
        }
    }

    public static TileRaster ApplyGrain(TileRaster image, GrainSettings settings)
    {
        if (!settings.IsValid) throw new ArgumentOutOfRangeException(nameof(settings));
        if (settings.Amount == 0) return image;
        float strength = (float)(settings.Amount / 100d * 0.35d * 255d);
        float roughness = (float)(settings.Roughness / 100d);
        uint fineSeed = Mix32(settings.Seed ^ 0xA511E9B3U);
        var result = new TileRaster(image.Width, image.Height);
        for (int row = 0; row * TileRaster.TileSize < image.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < image.Width; column++)
        {
            byte[] pixels = image.ReadTileCopy(column, row);
            var size = image.TileDimensions(column, row);
            for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
            {
                int pixel = (y * size.Width + x) * 4;
                int alpha = pixels[pixel + 3];
                if (alpha == 0) continue;
                int documentX = column * TileRaster.TileSize + x;
                int documentY = row * TileRaster.TileSize + y;
                double cellY = Math.Floor((documentY + 0.5) / settings.Size);
                float ty = SmoothStep((float)((documentY + 0.5) / settings.Size - cellY));
                long iy = (long)cellY, fineY = documentY;
                double cellX = Math.Floor((documentX + 0.5) / settings.Size);
                float tx = SmoothStep((float)((documentX + 0.5) / settings.Size - cellX));
                long ix = (long)cellX;
                float n00 = Lattice(ix, iy, settings.Seed), n10 = Lattice(ix + 1, iy, settings.Seed);
                float n01 = Lattice(ix, iy + 1, settings.Seed), n11 = Lattice(ix + 1, iy + 1, settings.Seed);
                float top = n00 + (n10 - n00) * tx, bottom = n01 + (n11 - n01) * tx;
                float smooth = (top + (bottom - top) * ty) * 1.6f;
                float fine = Lattice(documentX, fineY, fineSeed);
                float noise = smooth + (fine - smooth) * roughness;
                double red = pixels[pixel] * 255d / alpha;
                double green = pixels[pixel + 1] * 255d / alpha;
                double blue = pixels[pixel + 2] * 255d / alpha;
                float level = (float)Math.Clamp((0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255d, 0, 1);
                float delta = noise * strength * (0.4f + 2.4f * level * (1 - level));
                double coverage = alpha / 255d;
                pixels[pixel] = (byte)Math.Clamp(Math.Round(Clamp255(red + delta) * coverage, MidpointRounding.AwayFromZero), 0, alpha);
                pixels[pixel + 1] = (byte)Math.Clamp(Math.Round(Clamp255(green + delta) * coverage, MidpointRounding.AwayFromZero), 0, alpha);
                pixels[pixel + 2] = (byte)Math.Clamp(Math.Round(Clamp255(blue + delta) * coverage, MidpointRounding.AwayFromZero), 0, alpha);
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;

        static float SmoothStep(float value) => value * value * (3 - 2 * value);
        static double Clamp255(double value) => Math.Clamp(value, 0, 255);
        static uint Mix32(uint value)
        {
            value ^= value >> 16;
            value = unchecked(value * 0x7feb352dU);
            value ^= value >> 15;
            value = unchecked(value * 0x846ca68bU);
            return value ^ (value >> 16);
        }
        static float Lattice(long x, long y, uint seed)
        {
            uint h = Mix32(unchecked((uint)x * 0x9E3779B1U) ^
                Mix32(unchecked((uint)y * 0x85EBCA77U) ^ seed));
            return (h & 0xFFFFU) / 65535f + (h >> 16) / 65535f - 1f;
        }
    }

    public static TileRaster ApplyMask(TileRaster image, GrayTileRaster mask)
    {
        if (image.Width != mask.Width || image.Height != mask.Height)
            throw new ArgumentException("Image and mask dimensions must match.");
        var result = new TileRaster(image.Width, image.Height);
        for (int row = 0; row * TileRaster.TileSize < image.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < image.Width; column++)
        {
            byte[] pixels = image.ReadTileCopy(column, row);
            byte[] coverage = mask.ReadTileCopy(column, row);
            for (int pixel = 0; pixel < coverage.Length; pixel++)
            {
                int i = pixel * 4, alpha = pixels[i + 3];
                if (pixels[i] > alpha || pixels[i + 1] > alpha || pixels[i + 2] > alpha)
                    throw new InvalidDataException("Layer contains invalid premultiplied RGBA.");
                for (int channel = 0; channel < 4; channel++)
                    pixels[i + channel] = (byte)((pixels[i + channel] * coverage[pixel] + 127) / 255);
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    public static TileRaster ApplyAlphaMask(TileRaster image, TileRaster source, double sourceOpacity = 1)
    {
        if (image.Width != source.Width || image.Height != source.Height)
            throw new ArgumentException("Image and source dimensions must match.");
        if (!double.IsFinite(sourceOpacity) || sourceOpacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(sourceOpacity));
        var result = new TileRaster(image.Width, image.Height);
        for (int row = 0; row * TileRaster.TileSize < image.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < image.Width; column++)
        {
            byte[] pixels = image.ReadTileCopy(column, row);
            byte[] sourcePixels = source.ReadTileCopy(column, row);
            for (int pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                int alpha = (int)Math.Round(sourcePixels[pixel + 3] * sourceOpacity, MidpointRounding.AwayFromZero);
                if (pixels[pixel] > pixels[pixel + 3] || pixels[pixel + 1] > pixels[pixel + 3] || pixels[pixel + 2] > pixels[pixel + 3] ||
                    sourcePixels[pixel] > sourcePixels[pixel + 3] || sourcePixels[pixel + 1] > sourcePixels[pixel + 3] || sourcePixels[pixel + 2] > sourcePixels[pixel + 3])
                    throw new InvalidDataException("Layer contains invalid premultiplied RGBA.");
                for (int channel = 0; channel < 4; channel++)
                    pixels[pixel + channel] = (byte)((pixels[pixel + channel] * alpha + 127) / 255);
            }
            result = result.ReplaceTile(column, row, pixels);
        }
        return result;
    }

    public static TileRaster SourceOver(TileRaster bottom, TileRaster top)
    {
        if (bottom.Width != top.Width || bottom.Height != top.Height)
            throw new ArgumentException("Layer dimensions must match.");
        var result = new TileRaster(bottom.Width, bottom.Height);
        for (int row = 0; row * TileRaster.TileSize < bottom.Height; row++)
        for (int column = 0; column * TileRaster.TileSize < bottom.Width; column++)
        {
            byte[] lower = bottom.ReadTileCopy(column, row);
            byte[] upper = top.ReadTileCopy(column, row);
            for (int i = 0; i < lower.Length; i += 4)
            {
                int bottomAlpha = lower[i + 3], topAlpha = upper[i + 3];
                if (lower[i] > bottomAlpha || lower[i + 1] > bottomAlpha || lower[i + 2] > bottomAlpha ||
                    upper[i] > topAlpha || upper[i + 1] > topAlpha || upper[i + 2] > topAlpha)
                    throw new InvalidDataException("Layer contains invalid premultiplied RGBA.");
                int inverse = 255 - topAlpha;
                for (int channel = 0; channel < 3; channel++)
                    lower[i + channel] = (byte)(upper[i + channel] + (lower[i + channel] * inverse + 127) / 255);
                lower[i + 3] = (byte)(topAlpha + (bottomAlpha * inverse + 127) / 255);
            }
            result = result.ReplaceTile(column, row, lower);
        }
        return result;
    }
}
