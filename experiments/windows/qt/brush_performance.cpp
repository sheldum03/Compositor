#include "brush.hpp"
#include <QApplication>
#include <QBackingStore>
#include <QCloseEvent>
#include <QCryptographicHash>
#include <QDir>
#include <QElapsedTimer>
#include <QFileInfo>
#include <QJsonArray>
#include <QJsonDocument>
#include <QScreen>
#include <QSaveFile>
#include <QSysInfo>
#include <QThread>
#include <QTimer>
#include <QWidget>
#include <functional>
#ifdef Q_OS_WIN
#include <windows.h>
#include <psapi.h>
#endif

namespace {
QJsonValue privateBytes() {
#ifdef Q_OS_WIN
    PROCESS_MEMORY_COUNTERS_EX counters{};
    if (GetProcessMemoryInfo(GetCurrentProcess(), reinterpret_cast<PROCESS_MEMORY_COUNTERS *>(&counters), sizeof(counters)))
        return qint64(counters.PrivateUsage);
#endif
    return QJsonValue::Null;
}

class PerformanceWindow final : public QWidget {
public:
    PerformanceWindow(QString fixtures, QString output, bool nativeWindow)
        : fixtures(std::move(fixtures)), output(std::move(output)), nativeWindow(nativeWindow), measured(nativeWindow ? 30 : 1) {
        setWindowTitle("Compositor Qt S02 — fixed brush replay"); setFixedSize(1000, 1000);
        timeout.setSingleShot(true);
        QObject::connect(&timeout, &QTimer::timeout, this, [&] { finish(false, "No paint callback within 10 seconds"); });
        QTimer::singleShot(0, this, [&] { guard([&] { initialize(); beginTrial(); }); });
    }
protected:
    void closeEvent(QCloseEvent *event) override {
        windowClosed = true; finish(false, "Window closed before replay completed"); event->accept();
    }
    void paintEvent(QPaintEvent *) override {
        guard([&] {
            require(QThread::currentThread() == qApp->thread(), "Paint must run on GUI thread");
            QElapsedTimer paint; paint.start();
            double requestToPaint = pending ? (elapsed.nsecsElapsed() - requestedAt) / 1e6 : 0;
            const double dpr = devicePixelRatioF();
            auto layer = rgbaImage(QSize(qCeil(width() * dpr), qCeil(height() * dpr)));
            layer.setDevicePixelRatio(dpr);
            { QPainter painter(&layer); painter.scale(.25, .25); paintStroke(painter); }
            QTransform deviceTransform;
            {
                QPainter painter(this); deviceTransform = painter.deviceTransform();
                painter.fillRect(rect(), Qt::white); painter.drawImage(QPoint(), layer);
            }
            const double paintMs = paint.nsecsElapsed() / 1e6;
            if (!pending || painted) return;
            const double painterReleasedMs = elapsed.nsecsElapsed() / 1e6;
            if (trial == measured && point == 120) inspectViewport(deviceTransform);
            const double checksFinishedMs = elapsed.nsecsElapsed() / 1e6;
            QJsonObject frame{{"AppendMilliseconds", appendMs},
                {"PaintMilliseconds", paintMs}, {"RequestToPaintMilliseconds", requestToPaint},
                {"UpdateToPainterReleasedMilliseconds", painterReleasedMs}, {"ViewportCheckMilliseconds", checksFinishedMs - painterReleasedMs}};
            painted = true;
            // This callback runs after returning through Qt's raster paint/endPaint/flush path.
            QTimer::singleShot(0, this, [this, frame, checksFinishedMs]() mutable { guard([&] {
                double totalMs = elapsed.nsecsElapsed() / 1e6;
                require(totalMs < 10000, "Frame exceeded 10 seconds");
                frame["sequence"] = ++sequence; frame["UpdateToFrameCallbackMilliseconds"] = totalMs;
                frame["ChecksToCallbackMilliseconds"] = totalMs - checksFinishedMs; frames.append(frame);
                pending = false; painted = false; timeout.stop(); sampleMemory();
                if (++point == 121) endTrial(); else updatePoint();
            }); });
        });
    }
private:
    QString fixtures, output;
    bool nativeWindow, pending = false, painted = false, finished = false, windowClosed = false;
    int measured, scenario = 0, trial = 0, point = 0, sequence = 0;
    QTimer timeout;
    QElapsedTimer elapsed;
    qint64 requestedAt = 0, peak = 0;
    double appendMs = 0, initialDpr = 1;
    BrushSettings settings{800, 1, {1, .3, .1}};
    std::array<std::vector<QPointF>, 2> paths;
    std::array<std::shared_ptr<const TiledRaster>, 2> sources, expected;
    std::unique_ptr<BrushSession> session;
    QByteArray sourceDigest;
    QJsonArray frames, memory, trials, viewportChecks;

    QString scenarioName() const { return scenario == 0 ? "empty" : "existing"; }
    void guard(const std::function<void()> &action) {
        if (finished) return;
        try { action(); } catch (const std::exception &error) { finish(false, QString::fromUtf8(error.what())); }
    }
    void sampleMemory() {
        auto value = privateBytes(); memory.append(value);
        if (!value.isNull()) peak = qMax(peak, value.toInteger());
    }
    void verifyFixtures() {
        auto files = QJsonDocument::fromJson(readFile(fixtures + "/checksums.json")).array();
        require(files.size() == 9, "Expected nine frozen brush files");
        for (auto value : files) {
            auto file = value.toObject(); auto contents = readFile(fixtures + "/" + file["path"].toString());
            require(contents.size() == file["bytes"].toInteger() && QCryptographicHash::hash(contents, QCryptographicHash::Sha256).toHex() == file["sha256"].toString().toUtf8(), "Brush fixture changed");
        }
    }
    void initialize() {
        initialDpr = devicePixelRatioF();
        require(size() == QSize(1000, 1000) && isVisible() && !isMinimized(), "S02 requires a visible 1000x1000 logical viewport");
        if (nativeWindow) {
            require(QGuiApplication::platformName() != "offscreen" && QGuiApplication::platformName() != "minimal", "S02 native mode rejects headless plugins");
            require(screen() && screen()->availableGeometry().contains(frameGeometry()), "S02 window must fit completely within the available screen");
        }
        verifyFixtures();
        auto input = QJsonDocument::fromJson(readFile(fixtures + "/soft-crossing-4k.json")).object();
        require(input["width"].toInt() == 4000 && input["height"].toInt() == 4000 && input["diameter"].toInt() == 800 && input["hardness"].toDouble() == 0, "S02 workload dimensions");
        auto strokes = input["strokes"].toArray(); require(strokes.size() == 2, "Two frozen paths");
        for (int i = 0; i < 2; ++i) {
            auto coordinates = strokes[i].toArray(); require(coordinates.size() == 121, "Pointer-down and 120 updates");
            for (auto value : coordinates) { auto pair = value.toArray(); require(pair.size() == 2, "Coordinate pair"); paths[i].push_back({pair[0].toDouble(), pair[1].toDouble()}); }
        }
        sources[0] = std::make_shared<TiledRaster>(QSize(4000, 4000));
        sources[1] = SoftBrushStroke::replaySettled(sources[0], settings, paths[0]);
        for (int i = 0; i < 2; ++i) expected[i] = SoftBrushStroke::replaySettled(sources[i], settings, paths[1]);
    }
    void beginTrial() {
        sourceDigest = sources[scenario]->digest();
        session = std::make_unique<BrushSession>(sources[scenario]); session->begin(settings);
        point = 0; frames = {}; memory = {}; updatePoint();
    }
    void updatePoint() {
        require(!pending, "Previous paint must complete before next update");
        require(size() == QSize(1000, 1000) && isVisible() && !isMinimized() && devicePixelRatioF() == initialDpr, "Viewport visibility, size or DPI changed during S02");
        elapsed.start(); session->active->append(paths[1][size_t(point)]);
        appendMs = elapsed.nsecsElapsed() / 1e6;
        requestedAt = elapsed.nsecsElapsed(); pending = true; timeout.start(10000); update();
    }
    void paintStroke(QPainter &painter) {
        if (!session) return;
        if (session->active) session->active->paint(painter); else session->current()->paint(painter);
    }
    void inspectViewport(const QTransform &transform) {
        // The backing-store device is borrowed only inside paintEvent, never cached.
        auto store = backingStore(); auto device = store ? store->paintDevice() : nullptr;
        require(device && device->devType() == QInternal::Image, "S02 requires a readable raster QImage backing store");
        const auto &backing = *static_cast<QImage *>(device);
        auto bounds = transform.mapRect(QRectF(rect())).toAlignedRect();
        require(backing.rect().contains(bounds), "Backing-store viewport bounds");
        auto actual = backing.copy(bounds).convertToFormat(QImage::Format_RGBA8888_Premultiplied);
        actual.setDevicePixelRatio(1);
        auto layer = rgbaImage(bounds.size());
        { QPainter painter(&layer); painter.translate(-bounds.left(), -bounds.top()); painter.setTransform(transform, true); painter.scale(.25, .25); paintStroke(painter); }
        auto reference = rgbaImage(bounds.size()); reference.fill(Qt::white);
        { QPainter painter(&reference); painter.drawImage(QPoint(), layer); }
        auto result = compareImages(actual, reference);
        result["scenario"] = scenarioName(); result["width"] = bounds.width(); result["height"] = bounds.height();
        viewportChecks.append(result);
        require(actual.save(output + "/" + scenarioName() + "-backing-preview.png", "PNG") && reference.save(output + "/" + scenarioName() + "-reference-preview.png", "PNG"), "Save viewport evidence");
        require(result["DifferentPixels"].toInteger() == 0, "Backing-store preview differs from reference");
    }
    void endTrial() {
        require(frames.size() == 121 && !pending && !windowClosed, "Complete live frame sequence before commit");
        QElapsedTimer commit; commit.start(); session->commit(); double commitMs = commit.nsecsElapsed() / 1e6; sampleMemory();
        require(session->undoCount() == 1 && session->current()->samePixels(*expected[scenario]) && sources[scenario]->digest() == sourceDigest, "Commit/oracle/source immutability");
        session->undo(); require(session->current()->samePixels(*sources[scenario]), "Undo pixels");
        session->redo(); require(session->current()->samePixels(*expected[scenario]), "Redo pixels");
        if (trial == measured) session->current()->exportPng(output + "/" + scenarioName() + "-final.png");
        auto pointerDown = frames.takeAt(0);
        trials.append(QJsonObject{{"scenario", scenarioName()}, {"trial", trial}, {"warmup", trial == 0}, {"pointerDown", pointerDown},
            {"updates", frames}, {"commitMilliseconds", commitMs}, {"privateBytes", memory},
            {"correctness", "settled replay/immutable source/undo/redo exact"}, {"digest", QString::fromLatin1(session->current()->digest())}});
        save(false, {});
        if (++trial > measured) { trial = 0; ++scenario; }
        if (scenario == 2) { verifyFixtures(); require(viewportChecks.size() == 2, "Both viewport checks required"); finish(true, {}); }
        else beginTrial();
    }
    void save(bool completed, const QString &error) {
        QJsonObject report{{"completed", completed}, {"error", error.isEmpty() ? QJsonValue(QJsonValue::Null) : QJsonValue(error)},
            {"nativeWindow", nativeWindow}, {"windowsExecuted", QSysInfo::kernelType() == "winnt"}, {"platformPlugin", QGuiApplication::platformName()},
            {"qt", qVersion()}, {"os", QSysInfo::prettyProductName()}, {"architecture", QSysInfo::currentCpuArchitecture()},
            {"windowClosed", windowClosed}, {"updatesCompleted", sequence}, {"measuredCountPerScenario", measured},
            {"diameter", 800}, {"hardness", 0}, {"opacity", 1}, {"color", QJsonArray{1, .3, .1}},
            {"viewport", "1000x1000 logical; 4000x4000 document; scale .25"}, {"renderScaling", devicePixelRatioF()},
            {"clientWidth", width()}, {"clientHeight", height()}, {"sampledPrivatePeak", peak > 0 ? QJsonValue(peak) : QJsonValue(QJsonValue::Null)},
            {"memoryNotes", "Windows PrivateUsage after each frame and commit; null elsewhere. Sampled high-water mark, not continuous peak."},
            {"timingNotes", "Sequential synthetic GUI updates until a queued callback after QWidget raster paint/endPaint/flush returns. Includes callback scheduling and final measured-frame readback/reference/PNG checks; not physical presentation. Painter-release timing is also recorded separately."},
            {"performanceAccepted", false}, {"viewportChecks", viewportChecks}, {"trials", trials}};
        auto bytes = QJsonDocument(report).toJson(); QSaveFile file(output + "/report.json");
        require(file.open(QIODevice::WriteOnly) && file.write(bytes) == bytes.size() && file.commit(), "Cannot save S02 report");
    }
    void finish(bool passed, const QString &error) {
        if (finished) return;
        finished = true; pending = false; timeout.stop();
        if (!passed && session && session->active) session->cancel();
        try { save(passed, error); } catch (const std::exception &) { passed = false; }
        QApplication::exit(passed ? 0 : 1);
    }
};
}

int runBrushPerformanceProbe(const QString &fixtures, const QString &output, bool nativeWindow) {
    require(!QFileInfo::exists(output) && QDir().mkpath(output), "S02 output must be a new directory");
    QApplication::setQuitOnLastWindowClosed(false);
    PerformanceWindow window(fixtures, output, nativeWindow); window.show();
    return QApplication::exec();
}
