#pragma once
#include <QImage>
#include <QJsonObject>
#include <QMap>
#include <QRectF>
#include <QString>
#include <optional>
#include <vector>

void require(bool condition, const QString &message);
QByteArray readFile(const QString &path);
void writeFile(const QString &path, const QByteArray &bytes);
QImage rgbaImage(QSize size);
QImage decodedImage(const QByteArray &png, bool mask = false);
QJsonObject compareImages(const QImage &a, const QImage &b, const QString &heatmap = {});

struct Mask {
    QImage coverage;
    QRectF destination;
    bool smooth;
};
struct Layer {
    QString id, parent, clipSource, blend;
    QImage image;
    QRectF destination;
    bool visible, smooth;
    double opacity;
    std::optional<Mask> mask;
    std::optional<double> saturation;
    std::vector<Mask> folderMasks;
};
// Restricted fixed-corpus reader and CPU compositor, not production ProjectIO.
struct Scene {
    QJsonObject manifest;
    QSize size;
    std::vector<Layer> layers;
    QMap<QString, QByteArray> assets;
    static Scene read(const QString &directory);
    QImage render() const;
    void saveRenamedCopy(const QString &destination, const QString &name) const;
};
void composite(QImage &target, const std::vector<Layer> &layers);
void blendOnto(QImage &target, const QImage &source, const QString &mode);
