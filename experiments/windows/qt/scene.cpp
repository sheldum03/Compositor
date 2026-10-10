#include "scene.hpp"
#include <QColorSpace>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QImageReader>
#include <QBuffer>
#include <QJsonArray>
#include <QJsonDocument>
#include <QSet>
#include <QTemporaryDir>
#include <QUuid>
#include <algorithm>
#include <cmath>
#include <stdexcept>

void require(bool condition, const QString &message) {
    if (!condition) throw std::runtime_error(message.toStdString());
}
QByteArray readFile(const QString &path) {
    require(!QFileInfo(path).isSymLink(), "Linked paths unsupported");
    QFile file(path);
    require(file.open(QIODevice::ReadOnly), "Cannot read " + path);
    return file.readAll();
}
void writeFile(const QString &path, const QByteArray &bytes) {
    QFile file(path);
    require(file.open(QIODevice::WriteOnly | QIODevice::NewOnly), "Destination exists or cannot be created: " + path);
    require(file.write(bytes) == bytes.size() && file.flush(), "Incomplete write: " + path);
}
QImage rgbaImage(QSize size) {
    QImage result(size, QImage::Format_RGBA8888_Premultiplied);
    require(!result.isNull(), "Image allocation failed");
    result.setColorSpace(QColorSpace::SRgb);
    result.fill(Qt::transparent);
    return result;
}
QImage decodedImage(const QByteArray &png, bool mask) {
    require(png.size() >= 26 && png.startsWith(QByteArray::fromHex("89504e470d0a1a0a")), "Expected PNG");
    require(!mask || (static_cast<unsigned char>(png[24]) == 8 && png[25] == 0), "Mask must be 8-bit Gray without alpha");
    QBuffer buffer;
    buffer.setData(png); buffer.open(QIODevice::ReadOnly);
    QImageReader reader(&buffer, "PNG");
    QSize size = reader.size();
    require(size.width() > 0 && size.height() > 0 && qint64(size.width()) * size.height() <= 100'000'000, "Image budget");
    QImage image = reader.read();
    require(!image.isNull(), "PNG decode failed: " + reader.errorString());
    if (mask) return image.convertToFormat(QImage::Format_Grayscale8);
    if (image.colorSpace().isValid()) image.convertToColorSpace(QColorSpace::SRgb);
    image = image.convertToFormat(QImage::Format_RGBA8888_Premultiplied);
    image.setColorSpace(QColorSpace::SRgb);
    return image;
}
static void fields(const QJsonObject &object, const QString &allowed) {
    auto names = allowed.split(' ');
    for (auto i = object.begin(); i != object.end(); ++i)
        require(names.contains(i.key()), "Unsupported field: " + i.key());
}
static double number(const QJsonObject &object, const QString &name, double fallback) {
    if (!object.contains(name)) return fallback;
    require(object[name].isDouble() && std::isfinite(object[name].toDouble()), "Invalid number: " + name);
    return object[name].toDouble();
}
static QPointF pair(const QJsonObject &object, const QString &name) {
    auto a = object[name].toArray();
    require(a.size() == 2 && a[0].isDouble() && a[1].isDouble() && std::isfinite(a[0].toDouble()) && std::isfinite(a[1].toDouble()), "Invalid pair");
    return {a[0].toDouble(), a[1].toDouble()};
}
Scene Scene::read(const QString &directory) {
    require(!QFileInfo(directory).isSymLink() && !QFileInfo(directory + "/images").isSymLink(), "Linked package unsupported");
    QByteArray data = readFile(directory + "/manifest.json");
    require(data.size() <= 4 * 1024 * 1024, "Manifest budget");
    auto document = QJsonDocument::fromJson(data);
    require(document.isObject(), "Invalid JSON manifest");
    Scene scene; scene.manifest = document.object();
    auto &root = scene.manifest;
    fields(root, "activeLayerID colorSpace documentID format height layers resolution version width");
    require(root["format"] == "com.compositor.project" && root["colorSpace"] == "sRGB", "Unsupported format/color space");
    require(!QUuid(root["documentID"].toString()).isNull(), "Invalid document ID");
    int version = root["version"].toInt();
    require(version >= 1 && version <= 8 && number(root, "resolution", 72) > 0, "Version/resolution unsupported");
    scene.size = QSize(root["width"].toInt(), root["height"].toInt());
    require(scene.size.width() > 0 && scene.size.height() > 0 && scene.size.width() <= 30000 && scene.size.height() <= 30000 &&
        qint64(scene.size.width()) * scene.size.height() <= 100'000'000, "Canvas budget");
    struct Group { bool visible; std::vector<Mask> masks; };
    QMap<QString, Group> groups;
    QSet<QString> ids;
    qint64 sourcePixels = 0, maskPixels = 0;
    auto records = root["layers"].toArray();
    require(!records.isEmpty() && records.size() <= 10000, "Layer count");
    const QStringList blends = {"Normal", "Multiply", "Screen", "Overlay", "Darken", "Lighten", "Difference",
        "Color Dodge", "Color Burn", "Hue", "Saturation", "Color", "Luminosity"};
    for (auto value : records) {
        require(value.isObject(), "Expected layer"); auto object = value.toObject();
        fields(object, "id name isVisible isGroup parentID imageFile transform opacity blendMode maskFile maskEnabled maskSourceID adjustment");
        Layer layer{};
        layer.id = object["id"].toString(); layer.parent = object["parentID"].toString();
        require(!QUuid(layer.id).isNull() && !ids.contains(layer.id) && object["name"].isString() && object["isVisible"].isBool(), "Layer identity/fields");
        ids.insert(layer.id); layer.visible = object["isVisible"].toBool();
        if (!layer.parent.isEmpty()) {
            require(version >= 2 && groups.contains(layer.parent), "Parent must be an earlier group");
            layer.visible &= groups[layer.parent].visible; layer.folderMasks = groups[layer.parent].masks;
        }
        layer.opacity = number(object, "opacity", 1); layer.blend = object["blendMode"].toString("Normal");
        require(layer.opacity >= 0 && layer.opacity <= 1 && blends.contains(layer.blend), "Appearance unsupported");
        require(version >= 3 || (layer.opacity == 1 && layer.blend == "Normal"), "Appearance requires v3");
        auto t = object["transform"].toObject(); fields(t, "origin size rotation flipX flipY sampling");
        auto origin = pair(t, "origin"), size = pair(t, "size");
        require(std::abs(origin.x()) <= 1'000'000 && std::abs(origin.y()) <= 1'000'000 && size.x() >= 1 && size.y() >= 1 && size.x() <= 300000 && size.y() <= 300000, "Transform budget");
        require(number(t, "rotation", 0) == 0 && !t["flipX"].toBool() && !t["flipY"].toBool() &&
            origin.x() == std::trunc(origin.x()) && origin.y() == std::trunc(origin.y()), "Rotation/flip/fractional origin unsupported");
        auto sampling = t["sampling"].toString();
        require(sampling == "Nearest" || sampling == "Smooth" || sampling == "High quality", "Unknown sampling");
        layer.smooth = sampling != "Nearest"; layer.destination = QRectF(origin, QSizeF(size.x(), size.y()));
        auto load = [&](const QString &file, bool mask) {
            require(file == layer.id + (mask ? ".mask.png" : ".png"), "Asset path must be layer-ID PNG");
            auto bytes = readFile(directory + "/images/" + file); require(bytes.size() <= 512 * 1024 * 1024, "Asset byte budget");
            auto image = decodedImage(bytes, mask);
            auto &pixels = mask ? maskPixels : sourcePixels; pixels += qint64(image.width()) * image.height();
            require(pixels <= 100'000'000, "Decoded asset budget");
            scene.assets[file] = bytes; return image;
        };
        if (object.contains("maskFile")) {
            require(version >= 4, "Mask requires v4");
            auto coverage = load(object["maskFile"].toString(), true);
            if (object["maskEnabled"].toBool(true)) layer.mask = Mask{coverage, layer.destination, layer.smooth};
        }
        layer.clipSource = object["maskSourceID"].toString();
        require(layer.clipSource.isEmpty() || version >= 5, "Clipping requires v5");
        if (object["isGroup"].toBool()) {
            require(version >= 2 && !object.contains("imageFile") && layer.opacity == 1 && layer.blend == "Normal" &&
                origin == QPointF() && size == QPointF(scene.size.width(), scene.size.height()) && layer.clipSource.isEmpty() && !object.contains("adjustment"), "Only pass-through groups supported");
            require(!object.contains("maskFile") || version >= 6, "Group mask requires v6");
            if (layer.mask) layer.folderMasks.push_back(*layer.mask);
            groups[layer.id] = {layer.visible, layer.folderMasks}; continue;
        }
        if (object.contains("adjustment")) {
            auto a = object["adjustment"].toObject(); fields(a, "kind hue saturation lightness colorize levels curves");
            double saturation = number(a, "saturation", 0);
            require(version >= 7 && !object.contains("imageFile") && a["kind"] == "Hue/Saturation" &&
                number(a, "hue", 0) == 0 && number(a, "lightness", 0) == 0 && !a["colorize"].toBool() && saturation >= -100 && saturation <= 0 &&
                !layer.clipSource.isEmpty() && layer.blend == "Normal" && !object.contains("maskFile"), "Only clipped master desaturation supported");
            layer.saturation = saturation;
        } else layer.image = load(object["imageFile"].toString(), false);
        scene.layers.push_back(std::move(layer));
    }
    require(ids.contains(root["activeLayerID"].toString()), "Missing active ID");
    const Layer *base = nullptr;
    for (auto &layer : scene.layers) {
        if (layer.clipSource.isEmpty()) { base = &layer; continue; }
        require(base && base->id == layer.clipSource && base->parent == layer.parent && !base->image.isNull() &&
            (!layer.visible || base->visible), "Only contiguous same-parent clipping supported");
    }
    return scene;
}
QImage Scene::render() const { auto result = rgbaImage(size); composite(result, layers); return result; }
void Scene::saveRenamedCopy(const QString &destination, const QString &name) const {
    require(!QFileInfo::exists(destination), "Destination already exists");
    auto copy = manifest; auto records = copy["layers"].toArray();
    for (qsizetype i = 0; i < records.size(); ++i) {
        auto layer = records[i].toObject();
        if (layer["id"] == copy["activeLayerID"]) { layer["name"] = name; records[i] = layer; }
    }
    copy["layers"] = records; copy["version"] = 8;
    if (!copy.contains("resolution")) copy["resolution"] = 72;
    QTemporaryDir temporary(destination + ".tmp-XXXXXX");
    require(temporary.isValid() && QDir().mkdir(temporary.path() + "/images"), "Cannot create temporary package");
    for (auto i = assets.begin(); i != assets.end(); ++i) writeFile(temporary.path() + "/images/" + i.key(), i.value());
    writeFile(temporary.path() + "/manifest.json", QJsonDocument(copy).toJson());
    auto verified = Scene::read(temporary.path());
    require(verified.manifest == copy && verified.assets == assets, "Package verification failed");
    require(QDir().rename(temporary.path(), destination), "Cannot publish new directory");
    temporary.setAutoRemove(false);
}
QJsonObject compareImages(const QImage &a, const QImage &b, const QString &heatmap) {
    require(a.size() == b.size() && a.format() == QImage::Format_RGBA8888_Premultiplied && b.format() == a.format(), "Comparison format/size");
    qint64 different = 0, aboveOne = 0, sum = 0; int maximum = 0, alpha = 0;
    auto diff = rgbaImage(a.size());
    for (int y = 0; y < a.height(); ++y) for (int x = 0; x < a.width(); ++x) {
        const auto *p = a.constScanLine(y) + x * 4, *q = b.constScanLine(y) + x * 4;
        int pixelMaximum = 0, rgbMaximum = 0;
        for (int c = 0; c < 4; ++c) {
            int d = std::abs(int(p[c]) - int(q[c])); sum += d; pixelMaximum = std::max(pixelMaximum, d);
            if (c == 3) alpha = std::max(alpha, d); else rgbMaximum = std::max(rgbMaximum, d);
        }
        maximum = std::max(maximum, pixelMaximum); different += pixelMaximum != 0; aboveOne += pixelMaximum > 1;
        auto *d = diff.scanLine(y) + x * 4;
        d[0] = uchar(std::min(255, rgbMaximum * 32)); d[2] = uchar(std::min(255, std::abs(int(p[3]) - int(q[3])) * 32)); d[3] = 255;
    }
    if (!heatmap.isEmpty()) require(diff.save(heatmap, "PNG"), "Cannot save heatmap");
    return {{"DifferentPixels", different}, {"MaximumChannelError", maximum}, {"MaximumAlphaError", alpha},
        {"MeanAbsoluteChannelError", double(sum) / (a.width() * a.height() * 4.0)}, {"PixelsWithErrorAbove1", aboveOne}};
}
