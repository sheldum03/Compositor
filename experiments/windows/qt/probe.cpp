#include "scene.hpp"
#include "brush.hpp"
#include "window.hpp"
#include <QApplication>
#include <QCryptographicHash>
#include <QDir>
#include <QElapsedTimer>
#include <QFileInfo>
#include <QJsonArray>
#include <QJsonDocument>
#include <QPainter>
#include <QSysInfo>
#include <QTemporaryDir>
#include <QWidget>
#include <functional>
#include <iostream>

namespace {
class SceneWidget final : public QWidget {
public:
    explicit SceneWidget(const Scene &scene) : scene(scene) {
        resize(scene.size); setAttribute(Qt::WA_TranslucentBackground); setAutoFillBackground(false);
    }
    int draws = 0;
protected:
    void paintEvent(QPaintEvent *) override {
        QPainter painter(this); painter.drawImage(0, 0, scene.render()); ++draws;
    }
private:
    const Scene &scene;
};
void verifyCorpus(const QString &root) {
    auto files = QJsonDocument::fromJson(readFile(root + "/checksums.json")).array();
    require(files.size() == 91, "Expected fixed base corpus");
    for (auto value : files) {
        auto file = value.toObject(); auto bytes = readFile(root + "/" + file["path"].toString());
        require(bytes.size() == file["bytes"].toInteger() &&
            QCryptographicHash::hash(bytes, QCryptographicHash::Sha256).toHex() == file["sha256"].toString().toUtf8(), "Corpus hash mismatch");
    }
}
bool equal(const QImage &a, const QImage &b) { return compareImages(a, b)["DifferentPixels"].toInteger() == 0; }
void reject(const std::function<void()> &action) {
    bool rejected = false;
    try { action(); } catch (const std::exception &) { rejected = true; }
    require(rejected, "Expected rejection");
}
void verifySaved(const Scene &source, const Scene &saved, const QString &name) {
    auto expected = source.manifest; auto layers = expected["layers"].toArray();
    for (qsizetype i = 0; i < layers.size(); ++i) {
        auto layer = layers[i].toObject();
        if (layer["id"] == expected["activeLayerID"]) { layer["name"] = name; layers[i] = layer; }
    }
    expected["layers"] = layers; expected["version"] = 8;
    if (!expected.contains("resolution")) expected["resolution"] = 72;
    require(expected == saved.manifest && source.assets == saved.assets, "Only expected manifest fields may change; PNG bytes preserved");
}
void guardChecks(const QString &fixtures) {
    auto source = Scene::read(fixtures + "/F03.comp");
    auto mutate = [&](const std::function<void(QJsonObject &)> &edit) {
        auto manifest = source.manifest; edit(manifest);
        QTemporaryDir directory; require(directory.isValid() && QDir().mkdir(directory.path() + "/images"), "Temporary guard package");
        for (auto i = source.assets.begin(); i != source.assets.end(); ++i) writeFile(directory.path() + "/images/" + i.key(), i.value());
        writeFile(directory.path() + "/manifest.json", QJsonDocument(manifest).toJson());
        reject([&] { Scene::read(directory.path()); });
    };
    mutate([](QJsonObject &m) { m["version"] = 9; });
    mutate([](QJsonObject &m) { m["unexpected"] = true; });
    mutate([](QJsonObject &m) { auto layers = m["layers"].toArray(); layers.append(layers[1]); m["layers"] = layers; });
    auto changeBase = [&](const std::function<void(QJsonObject &)> &edit) {
        mutate([&](QJsonObject &m) { auto layers = m["layers"].toArray(); auto layer = layers[1].toObject(); edit(layer); layers[1] = layer; m["layers"] = layers; });
    };
    changeBase([](QJsonObject &l) { l["imageFile"] = "../escape.png"; });
    changeBase([](QJsonObject &l) { l["parentID"] = "00000000-0000-4000-8000-999999999999"; });
    changeBase([](QJsonObject &l) { l["opacity"] = 2; });
    changeBase([](QJsonObject &l) { auto t = l["transform"].toObject(); t["rotation"] = 5; l["transform"] = t; });
    changeBase([](QJsonObject &l) { l["text"] = QJsonObject(); });
    reject([&] { Scene::read(fixtures + "/F08.comp"); });
    reject([&] { decodedImage(source.assets.begin().value().left(30)); });
    reject([&] { decodedImage(source.assets.begin().value(), true); });
}
void compositionChecks(const QString &fixtures) {
    auto f4 = Scene::read(fixtures + "/F04.comp"); auto f3 = Scene::read(fixtures + "/F03.comp");
    f4.layers[0].mask.reset(); require(equal(f4.render(), f3.render()), "Disabled layer mask");
    auto f5 = Scene::read(fixtures + "/F05.comp"); auto base = f5; base.layers.resize(1);
    auto stack = f5.render(), alpha = base.render();
    for (int y = 0; y < stack.height(); ++y) for (int x = 0; x < stack.width(); ++x)
        require(stack.constScanLine(y)[x * 4 + 3] == alpha.constScanLine(y)[x * 4 + 3], "Clipping preserves base alpha");
    auto f6 = Scene::read(fixtures + "/F06.comp"); auto masked = f6.render();
    const auto &mask = f6.layers[0].folderMasks[0].coverage;
    for (int y = 0; y < stack.height(); ++y) for (int x = 0; x < stack.width(); ++x) for (int c = 0; c < 4; ++c)
        require(masked.constScanLine(y)[x * 4 + c] == (stack.constScanLine(y)[x * 4 + c] * mask.constScanLine(y)[x] + 127) / 255,
            "Group coverage applies exactly once to completed stack");
    for (auto &l : f6.layers) l.folderMasks.clear(); require(equal(f6.render(), stack), "Disabled group mask");
    auto f7 = Scene::read(fixtures + "/F07.comp"); auto adjusted = f7.render();
    for (int y = 0; y < masked.height(); ++y) for (int x = 0; x < masked.width(); ++x)
        require(adjusted.constScanLine(y)[x * 4 + 3] == masked.constScanLine(y)[x * 4 + 3], "Adjustment preserves alpha");
    auto original = f7.layers.back();
    f7.layers.back().opacity = 0; require(equal(f7.render(), masked), "Zero adjustment opacity");
    f7.layers.back() = original; f7.layers.back().saturation = 0; require(equal(f7.render(), masked), "Identity saturation");
    auto hidden = Scene::read(fixtures + "/F03.comp"); for (auto &l : hidden.layers) l.visible = false;
    require(equal(hidden.render(), rgbaImage(hidden.size)), "Hidden layers");
    // QImage copies must detach before C writes; the retained image cannot change.
    auto retained = stack; auto detached = stack; auto *bytes = detached.bits(); bytes[0] ^= 1;
    require(equal(retained, stack) && !equal(detached, stack), "QImage copy-on-write protects retained pixels");
    for (const QString &mode : {QString("Hue"), QString("Saturation"), QString("Color"), QString("Luminosity")}) {
        auto transparent = rgbaImage(stack.size()); blendOnto(transparent, stack, mode);
        require(equal(transparent, stack), "Nonseparable blend over transparent preserves source");
        auto preserved = stack; blendOnto(preserved, rgbaImage(stack.size()), mode);
        require(equal(preserved, stack), "Transparent source preserves backdrop");
    }
    // Analytic opaque red backdrop + green source cases from the nonseparable definitions.
    const QMap<QString, QColor> expected = {{"Hue", QColor(0, 130, 0)}, {"Color", QColor(0, 130, 0)},
        {"Saturation", QColor(255, 0, 0)}, {"Luminosity", QColor(255, 106, 106)}};
    for (auto i = expected.begin(); i != expected.end(); ++i) {
        auto red = rgbaImage(QSize(1, 1)), green = red; red.fill(QColor(255, 0, 0)); green.fill(QColor(0, 255, 0));
        blendOnto(red, green, i.key()); require(red.pixelColor(0, 0) == i.value(), "Analytic nonseparable " + i.key());
        auto gray = rgbaImage(QSize(1, 1)); gray.fill(QColor(128, 128, 128));
        auto copy = gray; blendOnto(copy, gray, i.key()); require(equal(copy, gray), "Neutral nonseparable " + i.key());
    }
}
}

void runTextProbe(const QString &fixtures, const QString &output);

int main(int argc, char **argv) {
    QApplication app(argc, argv);
    try {
        auto args = app.arguments();
        if (args.size() == 4 && (args[1] == "--s02-window" || args[1] == "--s02-check")) return runBrushPerformanceProbe(QFileInfo(args[2]).absoluteFilePath(), QFileInfo(args[3]).absoluteFilePath(), args[1] == "--s02-window");
        if (args.size() == 4 && args[1] == "--brush") { runBrushProbe(QFileInfo(args[2]).absoluteFilePath(), QFileInfo(args[3]).absoluteFilePath()); return 0; }
        if (args.size() == 4 && args[1] == "--text") { runTextProbe(QFileInfo(args[2]).absoluteFilePath(), QFileInfo(args[3]).absoluteFilePath()); return 0; }
        if (args.size() == 4 && (args[1] == "--window" || args[1] == "--window-check")) return runWindowProbe(QFileInfo(args[2]).absoluteFilePath(), QFileInfo(args[3]).absoluteFilePath(), args[1] == "--window-check");
        require(args.size() == 3, "Usage: qt_probe [--brush|--text|--window|--window-check|--s02-window|--s02-check] <fixed-fixtures> <new-output>");
        QString fixtures = QFileInfo(args[1]).absoluteFilePath(), output = QFileInfo(args[2]).absoluteFilePath();
        verifyCorpus(fixtures);
        require(!QFileInfo::exists(output) && QDir().mkpath(output), "Output must be a new directory");
        QStringList names;
        for (int i = 1; i <= 7; ++i) names << QString("F%1").arg(i, 2, 10, QChar('0'));
        for (int i = 1; i <= 13; ++i) names << QString("B%1").arg(i, 2, 10, QChar('0'));
        QJsonArray results;
        for (const auto &name : names) {
            auto scene = Scene::read(fixtures + "/" + name + ".comp");
            QElapsedTimer timer; timer.start(); auto exported = scene.render(); auto exportNs = timer.nsecsElapsed();
            require(exported.save(output + "/" + name + "-export.png", "PNG"), "Save export");
            SceneWidget widget(scene); auto preview = rgbaImage(scene.size); timer.restart();
            widget.render(&preview, QPoint(), QRegion(), QWidget::DrawChildren); auto previewNs = timer.nsecsElapsed();
            require(widget.draws == 1 && equal(preview, exported), name + ": widget/export mismatch");
            require(!equal(exported, rgbaImage(scene.size)), "Nonempty specimen must not render blank");
            require(preview.save(output + "/" + name + "-preview.png", "PNG"), "Save preview");
            require(equal(decodedImage(readFile(output + "/" + name + "-export.png")), exported), "PNG encode/decode preserves premultiplied bytes");
            QString renamed = "跨平台 renamed " + name, destination = output + "/" + name + ".comp";
            scene.saveRenamedCopy(destination, renamed); auto reopened = Scene::read(destination);
            verifySaved(scene, reopened, renamed); require(equal(reopened.render(), exported), "Rename must not change pixels");
            reject([&] { scene.saveRenamedCopy(destination, "overwrite"); }); verifySaved(scene, Scene::read(destination), renamed);
            auto mac = decodedImage(readFile(fixtures + "/" + name + "-mac.png"));
            results.append(QJsonObject{{"fixture", name}, {"widgetPaintCalls", widget.draws},
                {"previewExport", compareImages(preview, exported)}, {"exportNanoseconds", exportNs}, {"previewNanoseconds", previewNs},
                {"renameReadback", "passed"}, {"macReference", compareImages(exported, mac, output + "/" + name + "-diff.png")}});
        }
        guardChecks(fixtures); compositionChecks(fixtures); verifyCorpus(fixtures);
        QJsonObject report{{"status", "local preparation checks passed; pixel differences are not acceptance"},
            {"qt", qVersion()}, {"os", QSysInfo::prettyProductName()}, {"architecture", QSysInfo::currentCpuArchitecture()},
            {"platformPlugin", QGuiApplication::platformName()}, {"windowsExecuted", QSysInfo::kernelType() == "winnt"},
            {"rendering", "QPainter CPU into owned premultiplied sRGB RGBA8; fresh render in QWidget paint event"},
            {"nonseparableModes", "custom CPU W3C blend math; absent from QPainter composition modes"},
            {"sampling", "Nearest maps to fast; Smooth and High quality both map to QPainter SmoothPixmapTransform"},
            {"corpusHashesVerified", 91}, {"guardChecks", 11},
            {"compositionChecks", "masks, clipping/adjustment alpha, group coverage once, identity/zero adjustment, hidden layers, copy-on-write, transparent nonseparable blends"},
            {"nativeFunctions", QJsonArray{"layer_extract_alpha", "layer_unpremultiply_opaque", "layer_restore_alpha"}}, {"results", results}};
        auto json = QJsonDocument(report).toJson(); writeFile(output + "/qt-report.json", json); std::cout << json.constData();
        return 0;
    } catch (const std::exception &e) { std::cerr << e.what() << '\n'; return 1; }
}
