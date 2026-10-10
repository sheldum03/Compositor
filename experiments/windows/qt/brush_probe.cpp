#include "brush.hpp"
#include <QCryptographicHash>
#include <QDir>
#include <QElapsedTimer>
#include <QFileInfo>
#include <QJsonArray>
#include <QJsonDocument>
#include <QSysInfo>
#include <QWidget>
#include <algorithm>
#include <cmath>
#include <functional>
#include <iostream>

namespace {
class BrushWidget final : public QWidget {
public:
    BrushWidget(QSize size, std::function<void(QPainter &)> paint) : paint(std::move(paint)) {
        resize(size); setAttribute(Qt::WA_TranslucentBackground); setAutoFillBackground(false);
    }
    int draws = 0;
protected:
    void paintEvent(QPaintEvent *) override { QPainter painter(this); paint(painter); ++draws; }
private:
    std::function<void(QPainter &)> paint;
};
void preview(BrushWidget &widget, const QString &path = {}) {
    auto image = rgbaImage(widget.size()); widget.render(&image, QPoint(), QRegion(), QWidget::DrawChildren);
    if (!path.isEmpty()) require(image.save(path, "PNG"), "Save brush preview");
}
void verify(const QString &fixtures) {
    auto files = QJsonDocument::fromJson(readFile(fixtures + "/checksums.json")).array(); require(files.size() == 9, "Nine-file brush corpus");
    for (auto value : files) {
        auto record = value.toObject(); auto data = readFile(fixtures + "/" + record["path"].toString());
        require(data.size() == record["bytes"].toInteger() && QCryptographicHash::hash(data, QCryptographicHash::Sha256).toHex() == record["sha256"].toString().toUtf8(), "Brush corpus hash");
    }
}
bool equal(const QImage &a, const QImage &b) { return compareImages(a, b)["DifferentPixels"].toInteger() == 0; }
void reject(const std::function<void()> &action) {
    bool rejected = false; try { action(); } catch (const std::exception &) { rejected = true; } require(rejected, "Expected stroke rejection");
}
QImage crop(SoftBrushStroke &stroke) {
    auto image = rgbaImage(QSize(512, 512)); { QPainter painter(&image); stroke.paint(painter); } return image;
}
void sessionChecks(BrushSession &session, BrushSettings settings, std::shared_ptr<const TiledRaster> final) {
    session.undo(); auto before = session.current(); auto digest = before->digest();
    auto canceled = session.begin(settings); canceled->append({100, 100});
    reject([&] { session.undo(); }); reject([&] { session.begin(settings); });
    session.cancel(); reject([&] { canceled->append({101, 100}); });
    require(session.current() == before && before->digest() == digest, "Cancel preserves history/data");
    session.redo(); require(session.current() == final, "Cancel preserves redo");
    session.begin(settings); session.commit(); require(session.current() == final, "Empty stroke leaves history unchanged");
    session.undo(); auto branch = session.begin(settings); branch->append({100, 100}); branch->append({140, 100});
    branch->flush(); auto firstFlush = crop(*branch); branch->flush(); require(equal(firstFlush, crop(*branch)), "Repeated flush stable");
    session.commit(); auto next = session.current(); session.redo();
    require(session.current() == next && next != final, "New edit replaces redo");
    reject([&] { branch->append({160, 100}); });
    require(before->digest() == digest, "Branch does not mutate original history");
}
double percentile(std::vector<double> values) { std::sort(values.begin(), values.end()); return values[size_t(std::ceil(values.size() * .95)) - 1]; }
QJsonArray jsonTimes(const std::vector<double> &values) { QJsonArray result; for (auto v : values) result.append(v); return result; }
QString writeProject(const QString &fixtures, const QString &output, QSize size) {
    auto manifest = QJsonDocument::fromJson(readFile(fixtures + "/soft-crossing-4k-cpu.comp/manifest.json")).object();
    auto layers = manifest["layers"].toArray(); require(layers.size() == 1, "Expected one brush layer");
    auto layer = layers[0].toObject(); auto transform = layer["transform"].toObject();
    transform["origin"] = QJsonArray{0, 0}; transform["size"] = QJsonArray{size.width(), size.height()};
    layer["transform"] = transform; layers[0] = layer; manifest["layers"] = layers;
    QString path = output + "/brush.comp"; require(QDir().mkpath(path + "/images"), "Create brush package");
    writeFile(path + "/images/" + layer["imageFile"].toString(), readFile(output + "/final.png"));
    writeFile(path + "/manifest.json", QJsonDocument(manifest).toJson()); return path;
}
}
void runBrushProbe(const QString &fixtures, const QString &output) {
    verify(fixtures); require(!QFileInfo::exists(output) && QDir().mkpath(output), "Output must be new");
    auto input = QJsonDocument::fromJson(readFile(fixtures + "/soft-crossing-4k.json")).object();
    QSize size(input["width"].toInt(), input["height"].toInt());
    require(size == QSize(4000, 4000) && input["hardness"].toDouble() == 0, "Fixed 4K soft brush input");
    auto color = input["color"].toArray(); require(color.size() == 3, "RGB color");
    BrushSettings settings{input["diameter"].toInt(), input["opacity"].toDouble(), {color[0].toDouble(), color[1].toDouble(), color[2].toDouble()}};
    auto paths = input["strokes"].toArray(); require(paths.size() == 2, "Two strokes");
    auto initial = std::make_shared<TiledRaster>(size); BrushSession session(initial);
    std::vector<std::shared_ptr<const TiledRaster>> snapshots;
    std::vector<std::vector<QPointF>> points;
    QJsonArray timings; QByteArray firstDigest;
    for (auto path : paths) {
        auto coordinates = path.toArray(); require(coordinates.size() == 121, "121 events per stroke");
        points.emplace_back(); auto stroke = session.begin(settings);
        std::vector<double> appendTimes, previewTimes, combined;
        BrushWidget widget({1000, 1000}, [&](QPainter &p) { p.scale(.25, .25); stroke->paint(p); });
        for (auto value : coordinates) {
            auto pair = value.toArray(); require(pair.size() == 2, "Pointer pair"); QPointF point(pair[0].toDouble(), pair[1].toDouble()); points.back().push_back(point);
            QElapsedTimer timer; timer.start(); stroke->append(point); appendTimes.push_back(timer.nsecsElapsed() / 1e6);
            timer.restart(); preview(widget); previewTimes.push_back(timer.nsecsElapsed() / 1e6); combined.push_back(appendTimes.back() + previewTimes.back());
        }
        QElapsedTimer timer; timer.start(); session.commit(); double commitMs = timer.nsecsElapsed() / 1e6;
        require(!session.active && session.undoCount() == int(snapshots.size()) + 1, "One history entry per stroke");
        require(initial->fullRasterExports == 0 && session.current()->fullRasterExports == 0 &&
            std::all_of(snapshots.begin(), snapshots.end(), [](auto &s) { return s->fullRasterExports == 0; }), "No full raster export before commits");
        require(stroke->touchedTiles() < 256 && stroke->commitCopiedBytes < qint64(size.width()) * size.height() * 4, "Commit copies only local tiles");
        require(widget.draws == 121, "Every update must reach real QWidget paint");
        timings.append(QJsonObject{{"stroke", int(snapshots.size())}, {"appendMilliseconds", jsonTimes(appendTimes)}, {"previewMilliseconds", jsonTimes(previewTimes)},
            {"commitMilliseconds", commitMs}, {"touchedTiles", stroke->touchedTiles()}, {"storedTiles", session.current()->tileCount()},
            {"commitCopiedBytes", stroke->commitCopiedBytes}, {"publishedPixelBytes", stroke->publishedPixelBytes}, {"tailBackupBytes", stroke->tailBackupBytes},
            {"explicitPreviewPixelCopyBytes", 0}, {"appendP95", percentile(appendTimes)}, {"previewP95", percentile(previewTimes)}, {"updateAndPreviewP95", percentile(combined)}});
        snapshots.push_back(session.current());
        if (firstDigest.isEmpty()) firstDigest = snapshots[0]->digest();
        else require(snapshots[0]->digest() == firstDigest, "Next stroke preserves prior snapshot");
    }
    auto first = snapshots[0], final = snapshots[1]; require(first->sharedTiles(*final) > 0, "Share unchanged tiles");
    std::shared_ptr<const TiledRaster> settled = initial;
    for (size_t i = 0; i < points.size(); ++i) {
        settled = SoftBrushStroke::replaySettled(settled, settings, points[i]);
        require(settled->samePixels(*snapshots[i]), "Tail replacement equals no-tail replay");
    }
    session.undo(); require(session.current() == first, "Undo snapshot identity"); session.redo(); require(session.current() == final, "Redo snapshot identity");
    sessionChecks(session, settings, final);
    QJsonArray comparisons;
    for (size_t i = 0; i < snapshots.size(); ++i) {
        QString stage = i == 0 ? "first" : "final", path = output + "/" + stage + ".png"; auto raster = snapshots[i];
        raster->exportPng(path); BrushWidget widget(size, [&](QPainter &p) { raster->paint(p); }); preview(widget, output + "/" + stage + "-preview.png");
        auto exported = decodedImage(readFile(path)); require(equal(exported, decodedImage(readFile(output + "/" + stage + "-preview.png"))), "Full preview/export equality");
        bool nonempty = false;
        for (int y = 0; y < size.height(); ++y) for (int x = 0; x < size.width(); ++x) {
            int alpha = exported.constScanLine(y)[x * 4 + 3]; nonempty |= alpha != 0; if (i == 0) require(alpha <= 102, "Whole stroke alpha cap");
        }
        require(nonempty, "Nonempty brush result");
        comparisons.append(QJsonObject{{"stage", stage},
            {"cpuReference", compareImages(exported, decodedImage(readFile(fixtures + "/" + stage + "-cpu.png")), output + "/" + stage + "-cpu-diff.png")},
            {"metalReference", compareImages(exported, decodedImage(readFile(fixtures + "/" + stage + "-metal.png")))}});
    }
    require(!equal(decodedImage(readFile(output + "/first.png")), decodedImage(readFile(output + "/final.png"))), "Second stroke changes pixels");
    auto project = writeProject(fixtures, output, size); auto reopened = Scene::read(project).render();
    require(equal(reopened, decodedImage(readFile(output + "/final.png"))) && reopened.save(output + "/reopened.png", "PNG"), "Brush project readback pixels");
    require(first->digest() == firstDigest, "Export/save preserves history"); verify(fixtures);
    QJsonObject report{{"status", "local preparation checks passed; not Windows or performance acceptance"}, {"qt", qVersion()},
        {"os", QSysInfo::prettyProductName()}, {"architecture", QSysInfo::currentCpuArchitecture()}, {"windowsExecuted", QSysInfo::kernelType() == "winnt"},
        {"algorithm", "24-stop Gaussian CPU dabs; 2.5% spacing; centripetal Catmull-Rom; provisional-tail restoration"},
        {"samplesPerStroke", 121}, {"widgetUpdates", 242}, {"tileSize", TiledRaster::tileSize}, {"sharedTilesAcrossSecondStroke", first->sharedTiles(*final)},
        {"fullRasterExportsBeforeSecondCommit", 0}, {"previewViewport", "1000x1000 at 25%; final correctness preview 4000x4000"},
        {"copyAccounting", "Owned QImage tiles drawn synchronously; no explicit app-side copy for preview; Qt/backend internal allocations not counted"},
        {"sessionChecks", "one history entry; unchanged source; undo/redo identity; cancel preserves redo; branch replaces redo; empty commit; stable flush; finished/active state guards; no-tail oracle; opacity cap; export/readback"},
        {"timings", timings}, {"comparisons", comparisons}};
    auto json = QJsonDocument(report).toJson(); writeFile(output + "/qt-brush-report.json", json); std::cout << json.constData();
}
