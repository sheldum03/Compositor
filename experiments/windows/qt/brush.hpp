#pragma once
#include "scene.hpp"
#include <QPainter>
#include <array>
#include <map>
#include <memory>

class TiledRaster {
public:
    static constexpr int tileSize = 256;
    explicit TiledRaster(QSize size) : size_(size) {}
    QSize size() const { return size_; }
    int columns() const { return (size_.width() + tileSize - 1) / tileSize; }
    QRect bounds(int key) const;
    QImage copyTile(int key) const;
    std::shared_ptr<const TiledRaster> replacing(const std::map<int, QImage> &replacements) const;
    int sharedTiles(const TiledRaster &other) const;
    int tileCount() const { return int(tiles.size()); }
    bool samePixels(const TiledRaster &other) const;
    QByteArray digest() const;
    void paint(QPainter &painter, const std::map<int, QImage> *replacements = nullptr) const;
    void exportPng(const QString &path) const;
    mutable int fullRasterExports = 0;
private:
    QSize size_;
    std::map<int, QImage> tiles;
};
struct BrushSettings { int diameter; double opacity; std::array<double, 3> color; };
class SoftBrushStroke {
public:
    SoftBrushStroke(std::shared_ptr<const TiledRaster> source, BrushSettings settings);
    void append(QPointF point);
    void flush();
    std::shared_ptr<const TiledRaster> commit();
    void cancel();
    void paint(QPainter &painter) const { source->paint(painter, &pixels); }
    int touchedTiles() const { return int(pixels.size()); }
    qint64 publishedPixelBytes = 0, tailBackupBytes = 0, commitCopiedBytes = 0;
    static std::shared_ptr<const TiledRaster> replaySettled(std::shared_ptr<const TiledRaster> source,
        BrushSettings settings, const std::vector<QPointF> &points);
private:
    std::shared_ptr<const TiledRaster> source;
    BrushSettings settings;
    std::vector<uchar> tip;
    std::map<int, std::vector<uchar>> coverage;
    std::map<int, QImage> original, pixels;
    std::map<int, QRect> dirty;
    std::map<int, std::optional<std::vector<uchar>>> tail;
    std::vector<QPointF> samples;
    std::optional<QPointF> previous;
    double distanceToNext = 0;
    bool finished = false;
    void ensureActive() const;
    std::vector<int> keys(QRect bounds) const;
    void drawTail(QPointF start, QPointF end);
    void removeTail();
    void curve(QPointF start, QPointF end, QPointF before, QPointF after);
    void walk(QPointF point);
    void dab(QPointF point);
    void publish();
};
class BrushSession {
public:
    explicit BrushSession(std::shared_ptr<const TiledRaster> initial) : history{std::move(initial)} {}
    std::shared_ptr<const TiledRaster> current() const { return history[position]; }
    int undoCount() const { return int(position); }
    std::shared_ptr<SoftBrushStroke> begin(BrushSettings settings);
    void commit();
    void cancel();
    void undo();
    void redo();
    std::shared_ptr<SoftBrushStroke> active;
private:
    std::vector<std::shared_ptr<const TiledRaster>> history;
    size_t position = 0;
};
void runBrushProbe(const QString &fixtures, const QString &output);
int runBrushPerformanceProbe(const QString &fixtures, const QString &output, bool nativeWindow);
