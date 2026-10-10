#include "brush.hpp"
#include <QApplication>
#include <QDir>
#include <QEvent>
#include <QFileInfo>
#include <QJsonArray>
#include <QJsonDocument>
#include <QTemporaryDir>
#include <QTimer>
#include <QWidget>
#include <iostream>

namespace {
struct SmallViewport final : QObject {
    bool suppressPaint = false;
    bool eventFilter(QObject *object, QEvent *event) override {
        auto widget = qobject_cast<QWidget *>(object);
        if (event->type() == QEvent::Show && widget && widget->windowTitle().startsWith("Compositor Qt S02")) {
            if (suppressPaint) widget->setUpdatesEnabled(false); else widget->setFixedSize(700, 700);
        }
        return false;
    }
};
QJsonObject report(const QString &directory) { return QJsonDocument::fromJson(readFile(directory + "/report.json")).object(); }
}

int main(int argc, char **argv) {
    qputenv("QT_QPA_PLATFORM", "offscreen"); qputenv("QT_SCALE_FACTOR", "1");
    QApplication app(argc, argv);
    try {
        require(app.arguments().size() == 2, "Usage: qt_s02_checks <brush-fixtures>");
        auto fixtures = QFileInfo(app.arguments()[1]).absoluteFilePath();
        QTemporaryDir temporary; require(temporary.isValid(), "Temporary test directory");
        auto complete = temporary.path() + "/complete";
        require(runBrushPerformanceProbe(fixtures, complete, false) == 0, "Local S02 replay");
        auto good = report(complete);
        require(good["completed"].toBool() && good["updatesCompleted"].toInt() == 484 && good["viewportChecks"].toArray().size() == 2, "All frames and viewport evidence");
        SmallViewport small; app.installEventFilter(&small);
        auto reduced = temporary.path() + "/small";
        require(runBrushPerformanceProbe(fixtures, reduced, false) == 1, "Small viewport must fail"); app.removeEventFilter(&small);
        auto invalid = report(reduced);
        require(!invalid["completed"].toBool() && invalid["updatesCompleted"].toInt() == 0 && invalid["error"].toString().contains("1000x1000"), "Small viewport report");
        auto closed = temporary.path() + "/closed";
        QTimer closer;
        QObject::connect(&closer, &QTimer::timeout, &app, [&] {
            if (!QFileInfo::exists(closed + "/report.json") || report(closed)["trials"].toArray().isEmpty()) return;
            for (auto window : QApplication::topLevelWidgets()) if (window->windowTitle().startsWith("Compositor Qt S02")) { closer.stop(); window->close(); }
        });
        closer.start(1);
        require(runBrushPerformanceProbe(fixtures, closed, false) == 1, "Mid-run close must fail"); closer.stop();
        auto canceled = report(closed);
        require(!canceled["completed"].toBool() && canceled["windowClosed"].toBool() && canceled["updatesCompleted"].toInt() >= 121 && canceled["updatesCompleted"].toInt() < 484, "Mid-run cancellation report");
        small.suppressPaint = true; app.installEventFilter(&small);
        auto stalled = temporary.path() + "/no-paint";
        require(runBrushPerformanceProbe(fixtures, stalled, false) == 1, "Missing paint callback must time out"); app.removeEventFilter(&small);
        auto timeout = report(stalled);
        require(!timeout["completed"].toBool() && timeout["updatesCompleted"].toInt() == 0 && timeout["error"].toString().contains("10 seconds"), "Paint timeout report");
        std::cout << "PASS: 484-frame replay, backing-store equality, small viewport refusal, mid-run close cancellation, missing-paint timeout\n";
        return 0;
    } catch (const std::exception &error) { std::cerr << error.what() << '\n'; return 1; }
}
