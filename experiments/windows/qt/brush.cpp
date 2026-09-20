#include "brush.hpp"
#include <QCryptographicHash>
#include <QtEndian>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <set>

QRect TiledRaster::bounds(int key) const {
    int x = key % columns() * tileSize, y = key / columns() * tileSize;
    return {x, y, std::min(tileSize, size_.width() - x), std::min(tileSize, size_.height() - y)};
}
QImage TiledRaster::copyTile(int key) const {
    auto i = tiles.find(key); return i == tiles.end() ? rgbaImage(bounds(key).size()) : i->second.copy();
}
std::shared_ptr<const TiledRaster> TiledRaster::replacing(const std::map<int, QImage> &replacements) const {
    auto result = std::make_shared<TiledRaster>(size_); result->tiles = tiles;
    for (auto &[key, image] : replacements) result->tiles[key] = image.copy();
    return result;
}
int TiledRaster::sharedTiles(const TiledRaster &other) const {
    int count = 0;
    for (auto &[key, image] : tiles) {
        auto i = other.tiles.find(key); if (i != other.tiles.end() && image.constBits() == i->second.constBits()) ++count;
    }
    return count;
}
bool TiledRaster::samePixels(const TiledRaster &other) const {
    if (size_ != other.size_) return false;
    std::set<int> all;
    for (auto &p : tiles) all.insert(p.first);
    for (auto &p : other.tiles) all.insert(p.first);
    for (int key : all) {
        auto a = copyTile(key), b = other.copyTile(key);
        for (int y = 0; y < a.height(); ++y) if (std::memcmp(a.constScanLine(y), b.constScanLine(y), size_t(a.width()) * 4)) return false;
    }
    return true;
}
QByteArray TiledRaster::digest() const {
    QCryptographicHash hash(QCryptographicHash::Sha256);
    for (auto &[key, image] : tiles) {
        auto little = qToLittleEndian(qint32(key)); hash.addData(QByteArrayView(reinterpret_cast<const char *>(&little), sizeof(little)));
        for (int y = 0; y < image.height(); ++y) hash.addData(QByteArrayView(reinterpret_cast<const char *>(image.constScanLine(y)), image.width() * 4));
    }
    return hash.result().toHex();
}
void TiledRaster::paint(QPainter &painter, const std::map<int, QImage> *replacements) const {
    painter.save(); painter.setCompositionMode(QPainter::CompositionMode_Source);
    painter.setRenderHint(QPainter::SmoothPixmapTransform, false);
    for (auto &[key, image] : tiles) if (!replacements || !replacements->count(key)) painter.drawImage(bounds(key).topLeft(), image);
    if (replacements) for (auto &[key, image] : *replacements) painter.drawImage(bounds(key).topLeft(), image);
    painter.restore();
}
void TiledRaster::exportPng(const QString &path) const {
    ++fullRasterExports; auto result = rgbaImage(size_);
    { QPainter painter(&result); paint(painter); }
    require(result.save(path, "PNG"), "Cannot export brush image");
}
namespace {
uchar rounded(double value) { return uchar(std::clamp(std::floor(value + .5), 0.0, 255.0)); }
double distance(QPointF a, QPointF b) { auto d = b - a; return std::sqrt(d.x() * d.x() + d.y() * d.y()); }
}
SoftBrushStroke::SoftBrushStroke(std::shared_ptr<const TiledRaster> source, BrushSettings settings)
    : source(std::move(source)), settings(settings) {
    require(settings.diameter >= 1 && settings.diameter <= 2000 && std::isfinite(settings.opacity) &&
        settings.opacity >= .01 && settings.opacity <= 1, "Brush settings");
    for (auto c : settings.color) require(std::isfinite(c) && c >= 0 && c <= 1, "Brush color");
    tip.resize(size_t(settings.diameter) * settings.diameter);
    std::array<double, 25> stops;
    for (int i = 0; i <= 24; ++i) stops[i] = std::max(0.0, (std::exp(-2.5 * i * i / (24.0 * 24)) - std::exp(-2.5)) / (1 - std::exp(-2.5)));
    double radius = settings.diameter / 2.0;
    for (int y = 0; y < settings.diameter; ++y) for (int x = 0; x < settings.diameter; ++x) {
        double dx = x + .5 - radius, dy = y + .5 - radius, u = std::sqrt(dx * dx + dy * dy) / radius;
        if (u >= 1) continue;
        int stop = std::min(23, int(u * 24));
        tip[y * settings.diameter + x] = rounded((stops[stop] + (stops[stop + 1] - stops[stop]) * (u * 24 - stop)) * 255);
    }
}
void SoftBrushStroke::ensureActive() const { require(!finished, "Stroke already committed or canceled"); }
void SoftBrushStroke::append(QPointF point) {
    ensureActive(); require(std::isfinite(point.x()) && std::isfinite(point.y()) && std::abs(point.x()) <= 10'000'000 && std::abs(point.y()) <= 10'000'000, "Pointer coordinate");
    if (!samples.empty() && samples.back() == point) return;
    removeTail(); samples.push_back(point); if (samples.size() > 4) samples.erase(samples.begin());
    int n = int(samples.size());
    if (n == 1) walk(point);
    else if (n >= 3) curve(samples[n - 3], samples[n - 2], samples[std::max(0, n - 4)], point);
    if (n >= 2) drawTail(samples[n - 2], point);
    publish();
}
void SoftBrushStroke::flush() {
    ensureActive(); removeTail(); int n = int(samples.size());
    if (n >= 2) {
        curve(samples[n - 2], samples.back(), samples[std::max(0, n - 3)], samples.back());
        auto last = samples.back(); samples = {last};
    }
    publish();
}
std::shared_ptr<const TiledRaster> SoftBrushStroke::commit() {
    flush(); auto result = source->replacing(pixels);
    for (auto &p : pixels) commitCopiedBytes += p.second.sizeInBytes();
    finished = true; return result;
}
void SoftBrushStroke::cancel() { ensureActive(); finished = true; }
std::shared_ptr<const TiledRaster> SoftBrushStroke::replaySettled(std::shared_ptr<const TiledRaster> source,
    BrushSettings settings, const std::vector<QPointF> &points) {
    SoftBrushStroke stroke(source, settings);
    if (!points.empty()) stroke.walk(points.front());
    for (size_t i = 0; i + 1 < points.size(); ++i) stroke.curve(points[i], points[i + 1], points[i ? i - 1 : 0], points[std::min(points.size() - 1, i + 2)]);
    stroke.publish(); return source->replacing(stroke.pixels);
}
std::vector<int> SoftBrushStroke::keys(QRect bounds) const {
    auto clipped = bounds.intersected(QRect(QPoint(), source->size())); std::vector<int> result;
    if (clipped.isEmpty()) return result;
    for (int y = clipped.top() / TiledRaster::tileSize; y <= clipped.bottom() / TiledRaster::tileSize; ++y)
        for (int x = clipped.left() / TiledRaster::tileSize; x <= clipped.right() / TiledRaster::tileSize; ++x) result.push_back(y * source->columns() + x);
    return result;
}
void SoftBrushStroke::drawTail(QPointF start, QPointF end) {
    double reach = settings.diameter / 2.0 + 2;
    int left = int(std::floor(std::min(start.x(), end.x()) - reach)), top = int(std::floor(std::min(start.y(), end.y()) - reach));
    int right = int(std::ceil(std::max(start.x(), end.x()) + reach)), bottom = int(std::ceil(std::max(start.y(), end.y()) + reach));
    for (int key : keys(QRect(left, top, right - left, bottom - top))) {
        auto i = coverage.find(key);
        if (i == coverage.end()) tail[key] = std::nullopt;
        else { tail[key] = i->second; tailBackupBytes += qint64(i->second.size()); }
    }
    auto saved = previous; double spacing = distanceToNext;
    walk(end); previous = saved; distanceToNext = spacing;
}
void SoftBrushStroke::removeTail() {
    for (auto &[key, backup] : tail) {
        auto i = coverage.find(key); if (i == coverage.end()) continue;
        if (backup) i->second = *backup; else std::fill(i->second.begin(), i->second.end(), 0);
        dirty[key] = source->bounds(key);
    }
    tail.clear();
}
void SoftBrushStroke::curve(QPointF start, QPointF end, QPointF before, QPointF after) {
    auto knot = [](double t, QPointF a, QPointF b) { return t + std::max(.0001, std::sqrt(distance(a, b))); };
    auto mix = [](QPointF a, QPointF b, double ta, double tb, double t) { return a * ((tb - t) / (tb - ta)) + b * ((t - ta) / (tb - ta)); };
    double t0 = 0, t1 = knot(t0, before, start), t2 = knot(t1, start, end), t3 = knot(t2, end, after);
    int pieces = std::max(1, int(std::ceil(distance(start, end) / 2)));
    for (int i = 1; i <= pieces; ++i) {
        double t = t1 + (t2 - t1) * i / pieces;
        auto a = mix(before, start, t0, t1, t), b = mix(start, end, t1, t2, t), c = mix(end, after, t2, t3, t);
        walk(i == pieces ? end : mix(mix(a, b, t0, t2, t), mix(b, c, t1, t3, t), t1, t2, t));
    }
}
void SoftBrushStroke::walk(QPointF point) {
    double spacing = std::max(.25, settings.diameter * .025);
    if (previous) {
        double length = distance(*previous, point);
        if (length > 0) {
            double next = distanceToNext;
            while (next <= length) { dab(*previous + (point - *previous) * (next / length)); next += spacing; }
            distanceToNext = next - length;
        }
    } else { dab(point); distanceToNext = spacing; }
    previous = point;
}
void SoftBrushStroke::dab(QPointF point) {
    int left = int(std::round(point.x() - settings.diameter / 2.0)), top = int(std::round(point.y() - settings.diameter / 2.0));
    QRect stamp(left, top, settings.diameter, settings.diameter);
    for (int key : keys(stamp)) {
        auto tile = source->bounds(key), touched = stamp.intersected(tile);
        auto &mask = coverage[key];
        if (mask.empty()) { mask.resize(size_t(tile.width()) * tile.height()); original[key] = source->copyTile(key); pixels[key] = original[key].copy(); }
        dirty[key] = dirty.count(key) ? dirty[key].united(touched) : touched;
        for (int y = touched.top(); y <= touched.bottom(); ++y) for (int x = touched.left(); x <= touched.right(); ++x) {
            int offset = (y - tile.top()) * tile.width() + x - tile.left();
            int deposited = tip[(y - top) * settings.diameter + x - left];
            mask[offset] = uchar(mask[offset] + (deposited * (255 - mask[offset]) + 127) / 255);
        }
    }
}
void SoftBrushStroke::publish() {
    for (auto &[key, area] : dirty) {
        auto tile = source->bounds(key); auto &mask = coverage[key]; auto &baseline = original[key]; auto &result = pixels[key];
        for (int y = area.top(); y <= area.bottom(); ++y) for (int x = area.left(); x <= area.right(); ++x) {
            int offset = (y - tile.top()) * tile.width() + x - tile.left(), alpha = rounded(mask[offset] * settings.opacity);
            const auto *b = baseline.constScanLine(y - tile.top()) + (x - tile.left()) * 4;
            auto *p = result.scanLine(y - tile.top()) + (x - tile.left()) * 4;
            for (int c = 0; c < 3; ++c) p[c] = uchar(rounded(settings.color[c] * alpha) + (b[c] * (255 - alpha) + 127) / 255);
            p[3] = uchar(alpha + (b[3] * (255 - alpha) + 127) / 255);
        }
        publishedPixelBytes += qint64(area.width()) * area.height() * 4;
    }
    dirty.clear();
}
std::shared_ptr<SoftBrushStroke> BrushSession::begin(BrushSettings settings) {
    require(!active, "Stroke already active"); active = std::make_shared<SoftBrushStroke>(current(), settings); return active;
}
void BrushSession::commit() {
    require(bool(active), "No active stroke"); auto stroke = active; auto next = stroke->commit(); active.reset();
    if (!stroke->touchedTiles()) return;
    history.resize(position + 1); history.push_back(std::move(next)); ++position;
}
void BrushSession::cancel() { if (active) active->cancel(); active.reset(); }
void BrushSession::undo() { require(!active, "Finish stroke first"); if (position) --position; }
void BrushSession::redo() { require(!active, "Finish stroke first"); if (position + 1 < history.size()) ++position; }
