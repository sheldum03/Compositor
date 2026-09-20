#include "scene.hpp"
#include "bridge.h"
#include <QPainter>
#include <algorithm>
#include <array>
#include <cmath>

namespace {
using Color = std::array<double, 3>;
double luminance(const Color &c) { return .3 * c[0] + .59 * c[1] + .11 * c[2]; }
double saturation(const Color &c) { return *std::max_element(c.begin(), c.end()) - *std::min_element(c.begin(), c.end()); }
Color withLuminance(Color c, double lightness) {
    double delta = lightness - luminance(c);
    for (auto &v : c) v += delta;
    double low = *std::min_element(c.begin(), c.end()), high = *std::max_element(c.begin(), c.end());
    if (low < 0) for (auto &v : c) v = lightness + (v - lightness) * lightness / (lightness - low);
    if (high > 1) for (auto &v : c) v = lightness + (v - lightness) * (1 - lightness) / (high - lightness);
    return c;
}
Color withSaturation(Color c, double amount) {
    std::array<int, 3> order{0, 1, 2};
    std::sort(order.begin(), order.end(), [&](int a, int b) { return c[a] < c[b]; });
    int low = order[0], mid = order[1], high = order[2];
    if (c[high] > c[low]) {
        c[mid] = (c[mid] - c[low]) * amount / (c[high] - c[low]); c[high] = amount;
    } else c[mid] = c[high] = 0;
    c[low] = 0; return c;
}
uchar byte(double value) { return static_cast<uchar>(std::clamp(std::lround(value), 0L, 255L)); }

void applyMask(QImage &target, const Mask &mask) {
    // Gray coverage is not an RGB color. Convert raw coverage into premultiplied white.
    auto source = rgbaImage(mask.coverage.size());
    for (int y = 0; y < source.height(); ++y) for (int x = 0; x < source.width(); ++x) {
        auto value = mask.coverage.constScanLine(y)[x]; auto *p = source.scanLine(y) + x * 4;
        std::fill(p, p + 4, value);
    }
    auto coverage = rgbaImage(target.size());
    { QPainter painter(&coverage); painter.setRenderHint(QPainter::SmoothPixmapTransform, mask.smooth); painter.drawImage(mask.destination, source); }
    for (int y = 0; y < target.height(); ++y) for (int x = 0; x < target.width(); ++x) {
        auto *p = target.scanLine(y) + x * 4; int alpha = coverage.constScanLine(y)[x * 4 + 3];
        for (int c = 0; c < 4; ++c) p[c] = uchar((p[c] * alpha + 127) / 255);
    }
}
QImage ownPixels(const Layer &layer, QSize size) {
    auto result = rgbaImage(size);
    { QPainter painter(&result); painter.setRenderHint(QPainter::SmoothPixmapTransform, layer.smooth); painter.drawImage(layer.destination, layer.image); }
    // Quantize layer opacity to the same 8-bit coverage used by the Skia probe.
    int opacity = int(std::lround(layer.opacity * 255));
    if (opacity != 255) for (int y = 0; y < result.height(); ++y) for (int x = 0; x < result.width() * 4; ++x)
        result.scanLine(y)[x] = uchar((result.constScanLine(y)[x] * opacity + 127) / 255);
    if (layer.mask) applyMask(result, *layer.mask);
    return result;
}
void desaturate(QImage &image, double amount, double opacity) {
    // Same limited 33^3 master-desaturation cube interpolation as the C# probe.
    double factor = 1 + amount / 100;
    for (int y = 0; y < image.height(); ++y) for (int x = 0; x < image.width(); ++x) {
        auto *p = image.scanLine(y) + x * 4;
        double r = p[0] * 32.0 / 255, g = p[1] * 32.0 / 255, b = p[2] * 32.0 / 255;
        int ri = std::min(31, int(r)), gi = std::min(31, int(g)), bi = std::min(31, int(b));
        double lightness = 0;
        for (int dz = 0; dz < 2; ++dz) for (int dy = 0; dy < 2; ++dy) for (int dx = 0; dx < 2; ++dx) {
            double weight = (dx ? r - ri : 1 - (r - ri)) * (dy ? g - gi : 1 - (g - gi)) * (dz ? b - bi : 1 - (b - bi));
            lightness += weight * (std::max({ri + dx, gi + dy, bi + dz}) + std::min({ri + dx, gi + dy, bi + dz})) / 64;
        }
        for (int c = 0; c < 3; ++c) {
            double adjusted = std::floor(lightness * 255 + (p[c] - lightness * 255) * factor + .5);
            p[c] = byte(p[c] * (1 - opacity) + adjusted * opacity);
        }
    }
}
}

void blendOnto(QImage &target, const QImage &source, const QString &mode) {
    static const QMap<QString, QPainter::CompositionMode> nativeModes = {
        {"Normal", QPainter::CompositionMode_SourceOver}, {"Multiply", QPainter::CompositionMode_Multiply},
        {"Screen", QPainter::CompositionMode_Screen}, {"Overlay", QPainter::CompositionMode_Overlay},
        {"Darken", QPainter::CompositionMode_Darken}, {"Lighten", QPainter::CompositionMode_Lighten},
        {"Difference", QPainter::CompositionMode_Difference}, {"Color Dodge", QPainter::CompositionMode_ColorDodge},
        {"Color Burn", QPainter::CompositionMode_ColorBurn}};
    require(source.size() == target.size(), "Blend dimensions");
    if (nativeModes.contains(mode)) {
        QPainter painter(&target); painter.setCompositionMode(nativeModes[mode]); painter.drawImage(0, 0, source); return;
    }
    require(mode == "Hue" || mode == "Saturation" || mode == "Color" || mode == "Luminosity", "Unknown blend mode");
    // QPainter has no nonseparable modes. W3C Compositing Level 1, sections 10.2 and 6:
    // work in unassociated sRGB for the blend, then source-over premultiplied channels.
    for (int y = 0; y < target.height(); ++y) for (int x = 0; x < target.width(); ++x) {
        auto *d = target.scanLine(y) + x * 4; const auto *s = source.constScanLine(y) + x * 4;
        if (s[3] == 0) continue;
        if (d[3] == 0) { std::copy(s, s + 4, d); continue; }
        double sa = s[3] / 255.0, da = d[3] / 255.0;
        Color src{s[0] / double(s[3]), s[1] / double(s[3]), s[2] / double(s[3])};
        Color dst{d[0] / double(d[3]), d[1] / double(d[3]), d[2] / double(d[3])}, blended;
        if (mode == "Hue") blended = withLuminance(withSaturation(src, saturation(dst)), luminance(dst));
        else if (mode == "Saturation") blended = withLuminance(withSaturation(dst, saturation(src)), luminance(dst));
        else if (mode == "Color") blended = withLuminance(src, luminance(dst));
        else blended = withLuminance(dst, luminance(src));
        for (int c = 0; c < 3; ++c) d[c] = byte((1 - sa) * d[c] + (1 - da) * s[c] + sa * da * blended[c] * 255);
        d[3] = byte((sa + da * (1 - sa)) * 255);
    }
}

void composite(QImage &target, const std::vector<Layer> &layers) {
    std::vector<const Layer *> visible;
    for (auto &layer : layers) if (layer.visible) visible.push_back(&layer);
    for (size_t i = 0; i < visible.size(); ++i) {
        auto &base = *visible[i]; size_t end = i + 1;
        while (end < visible.size() && visible[end]->clipSource == base.id) ++end;
        auto pixels = ownPixels(base, target.size());
        if (end > i + 1) {
            std::vector<uint8_t> alpha(size_t(target.width()) * size_t(target.height()));
            auto width = size_t(target.width()), height = size_t(target.height());
            layer_extract_alpha(pixels.constBits(), size_t(pixels.bytesPerLine()), alpha.data(), width, width, height);
            layer_unpremultiply_opaque(pixels.bits(), size_t(pixels.bytesPerLine()), width, height);
            for (size_t child = i + 1; child < end; ++child) {
                auto &layer = *visible[child];
                if (layer.saturation) desaturate(pixels, *layer.saturation, layer.opacity);
                else blendOnto(pixels, ownPixels(layer, target.size()), layer.blend);
            }
            layer_restore_alpha(pixels.bits(), size_t(pixels.bytesPerLine()), alpha.data(), width, width, height);
        }
        for (auto &mask : base.folderMasks) applyMask(pixels, mask);
        blendOnto(target, pixels, base.blend);
        i = end - 1;
    }
}
