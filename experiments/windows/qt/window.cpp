#include "window.hpp"
#include "brush.hpp"
#include <QApplication>
#include <QCheckBox>
#include <QDir>
#include <QDoubleSpinBox>
#include <QElapsedTimer>
#include <QFileInfo>
#include <QHBoxLayout>
#include <QJsonArray>
#include <QJsonDocument>
#include <QKeyEvent>
#include <QLabel>
#include <QMouseEvent>
#include <QPushButton>
#include <QScrollArea>
#include <QSysInfo>
#include <QTabWidget>
#include <QThread>
#include <QVBoxLayout>
#include <cmath>
#include <functional>

namespace {
class BrushCanvas final : public QWidget {
public:
    BrushSession session{std::make_shared<TiledRaster>(QSize(4000, 4000))};
    QJsonArray events;
    QString failure;
    int paints = 0;
    BrushCanvas() { setFixedSize(1000, 1000); setFocusPolicy(Qt::StrongFocus); }
    void cancel() { if (session.active) { session.cancel(); events.append(QJsonObject{{"event", "cancel"}}); update(); } }
protected:
    void paintEvent(QPaintEvent *) override {
        require(QThread::currentThread() == qApp->thread(), "Qt widget paint on GUI thread");
        // Source replacement within tiles must not erase the opaque widget background.
        const double dpr = devicePixelRatioF();
        auto layer = rgbaImage(QSize(qCeil(width() * dpr), qCeil(height() * dpr))); layer.setDevicePixelRatio(dpr);
        { QPainter p(&layer); p.scale(.25, .25); if (session.active) session.active->paint(p); else session.current()->paint(p); }
        QPainter p(this); p.fillRect(rect(), Qt::white); p.drawImage(QPoint(), layer); ++paints;
    }
    void mousePressEvent(QMouseEvent *event) override {
        if (event->button() != Qt::LeftButton || session.active) return;
        setFocus(); act("press", [&] { session.begin({800, .4, {.12, .35, .8}}); session.active->append(event->position() * 4); });
    }
    void mouseMoveEvent(QMouseEvent *event) override {
        if (!session.active) return;
        if (!(event->buttons() & Qt::LeftButton)) { cancel(); return; }
        act("append", [&] { session.active->append(event->position() * 4); });
    }
    void mouseReleaseEvent(QMouseEvent *event) override {
        if (event->button() != Qt::LeftButton || !session.active) return;
        act("release-commit", [&] { session.active->append(event->position() * 4); session.commit(); });
    }
    void keyPressEvent(QKeyEvent *event) override {
        if (event->key() == Qt::Key_Escape) { cancel(); event->accept(); }
        else QWidget::keyPressEvent(event);
    }
    bool event(QEvent *event) override {
        if (event->type() == QEvent::WindowDeactivate || event->type() == QEvent::Hide || event->type() == QEvent::UngrabMouse) cancel();
        return QWidget::event(event);
    }
private:
    void act(const QString &name, const std::function<void()> &action) {
        QElapsedTimer timer; timer.start();
        try { action(); } catch (const std::exception &error) { failure = error.what(); cancel(); }
        events.append(QJsonObject{{"event", name}, {"milliseconds", timer.nsecsElapsed() / 1e6}, {"error", failure}}); update();
    }
};
QPushButton *button(QHBoxLayout *layout, const QString &title, QWidget *context, const std::function<void()> &action) {
    auto result = new QPushButton(title); layout->addWidget(result);
    QObject::connect(result, &QPushButton::clicked, context, action); return result;
}
QString brushProject(BrushCanvas &canvas, const QString &fixtures, const QString &directory) {
    require(!canvas.session.active && canvas.failure.isEmpty(), "Brush must be settled and without event errors");
    require(!QFileInfo::exists(directory) && QDir().mkpath(directory), "New brush export directory required");
    canvas.session.current()->exportPng(directory + "/final.png");
    auto manifest = QJsonDocument::fromJson(readFile(fixtures + "/brush/soft-crossing-4k-cpu.comp/manifest.json")).object();
    auto layers = manifest["layers"].toArray(); require(layers.size() == 1, "Single layer brush fixture");
    auto layer = layers[0].toObject(), transform = layer["transform"].toObject();
    transform["origin"] = QJsonArray{0, 0}; transform["size"] = QJsonArray{4000, 4000}; layer["transform"] = transform;
    layers[0] = layer; manifest["layers"] = layers;
    auto destination = directory + "/brush.comp"; require(QDir().mkpath(destination + "/images"), "New brush project");
    writeFile(destination + "/images/" + layer["imageFile"].toString(), readFile(directory + "/final.png"));
    writeFile(destination + "/manifest.json", QJsonDocument(manifest).toJson());
    require(compareImages(Scene::read(destination).render(), decodedImage(readFile(directory + "/final.png")))["DifferentPixels"].toInteger() == 0, "Brush project pixel readback");
    return destination;
}
QJsonObject brushCheck(BrushCanvas &canvas) {
    auto initial = canvas.session.current();
    auto mouse = [&](QEvent::Type type, QPointF point, Qt::MouseButton changed, Qt::MouseButtons held) {
        QMouseEvent event(type, point, canvas.mapToGlobal(point), changed, held, Qt::NoModifier);
        QApplication::sendEvent(&canvas, &event); require(canvas.failure.isEmpty(), canvas.failure);
    };
    mouse(QEvent::MouseButtonPress, {200, 200}, Qt::LeftButton, Qt::LeftButton);
    mouse(QEvent::MouseMove, {350, 300}, Qt::NoButton, Qt::LeftButton);
    QKeyEvent escape(QEvent::KeyPress, Qt::Key_Escape, Qt::NoModifier); QApplication::sendEvent(&canvas, &escape);
    mouse(QEvent::MouseButtonRelease, {350, 300}, Qt::LeftButton, Qt::NoButton);
    require(canvas.session.current() == initial && !canvas.session.active && canvas.session.undoCount() == 0, "Escape cancels without release commit");
    mouse(QEvent::MouseButtonPress, {200, 200}, Qt::LeftButton, Qt::LeftButton);
    mouse(QEvent::MouseMove, {350, 300}, Qt::NoButton, Qt::LeftButton);
    mouse(QEvent::MouseButtonRelease, {400, 350}, Qt::LeftButton, Qt::NoButton);
    auto committed = canvas.session.current(); require(committed != initial && canvas.session.undoCount() == 1, "One history entry per actual widget stroke");
    canvas.session.undo(); require(canvas.session.current() == initial, "Widget stroke undo");
    canvas.session.redo(); require(canvas.session.current() == committed, "Widget stroke redo");
    mouse(QEvent::MouseButtonPress, {600, 600}, Qt::LeftButton, Qt::LeftButton);
    QEvent deactivate(QEvent::WindowDeactivate); QApplication::sendEvent(&canvas, &deactivate);
    require(!canvas.session.active && canvas.session.current() == committed, "Deactivate cancels incomplete stroke");
    auto captured = rgbaImage(canvas.size()); canvas.render(&captured);
    int transparent = 0;
    for (int y = 0; y < captured.height(); ++y) for (int x = 0; x < captured.width(); ++x)
        if (captured.constScanLine(y)[x * 4 + 3] != 255) ++transparent;
    require(transparent == 0, "Brush tile Source must not erase widget background");
    require(committed->fullRasterExports == 0, "Preview/history must not export full 4K raster");
    return {{"syntheticPointerEvents", true}, {"cancelReleaseUndoRedoDeactivate", "passed"}, {"nonopaqueWidgetPixels", transparent}};
}
}
int runWindowProbe(const QString &fixtures, const QString &output, bool selfTest) {
    require(!QFileInfo::exists(output) && QDir().mkpath(output), "Window output must be a new directory");
    auto scene = Scene::read(fixtures + "/F04.comp"); auto composite = scene.render();
    QWidget window; window.setWindowTitle("Compositor Qt — Windows feasibility probe"); window.resize(1080, 840);
    auto layout = new QVBoxLayout(&window); auto tabs = new QTabWidget; layout->addWidget(tabs);
    auto status = new QLabel("Prototype only. Windows IME, DPI, S02 and missing-font behavior require separate acceptance.");
    status->setWordWrap(true); layout->addWidget(status);
    QJsonArray saves; QStringList errors;
    auto action = [&](const std::function<void()> &work) {
        try { work(); status->setText("Action completed. Output: " + output); }
        catch (const std::exception &error) { errors.append(error.what()); status->setText("FAILED: " + QString::fromUtf8(error.what())); }
    };
    auto next = [&](const QString &name) { return output + "/" + QString::number(saves.size() + 1).rightJustified(3, '0') + "-" + name; };
    auto textPage = new QWidget; auto textLayout = new QVBoxLayout(textPage); auto textBar = new QHBoxLayout; textLayout->addLayout(textBar);
    auto text = new WindowText; textLayout->addWidget(text);
    textBar->addWidget(new QLabel("Rotation")); auto angle = new QDoubleSpinBox; angle->setRange(-180, 180); angle->setSingleStep(15); textBar->addWidget(angle);
    auto flip = new QCheckBox("Flip X"); textBar->addWidget(flip);
    textBar->addWidget(new QLabel("Scale")); auto scale = new QDoubleSpinBox; scale->setRange(.5, 2); scale->setSingleStep(.25); scale->setValue(1); textBar->addWidget(scale);
    auto placement = [=] { text->setPlacement(angle->value(), flip->isChecked(), scale->value()); };
    QObject::connect(angle, &QDoubleSpinBox::valueChanged, &window, placement); QObject::connect(scale, &QDoubleSpinBox::valueChanged, &window, placement);
    QObject::connect(flip, &QCheckBox::toggled, &window, placement);
    auto saveText = [&] { auto directory = next("text"); auto report = text->exportTo(directory); report["kind"] = "text"; report["directory"] = directory; saves.append(report); };
    button(textBar, "Export + check", &window, [&] { action(saveText); }); tabs->addTab(textPage, "Text / IME");
    auto brushPage = new QWidget; auto brushLayout = new QVBoxLayout(brushPage); auto brushBar = new QHBoxLayout; brushLayout->addLayout(brushBar);
    auto canvas = new BrushCanvas; auto scroll = new QScrollArea; scroll->setWidget(canvas); brushLayout->addWidget(scroll);
    button(brushBar, "Undo", &window, [&] { action([&] { canvas->session.undo(); canvas->update(); }); });
    button(brushBar, "Redo", &window, [&] { action([&] { canvas->session.redo(); canvas->update(); }); });
    button(brushBar, "Cancel stroke (Esc)", &window, [&] { canvas->cancel(); });
    auto saveBrush = [&] { auto directory = next("brush"); auto path = brushProject(*canvas, fixtures, directory); saves.append(QJsonObject{{"kind", "brush"}, {"directory", directory}, {"project", path}, {"undoCount", canvas->session.undoCount()}}); };
    button(brushBar, "Save project + check", &window, [&] { action(saveBrush); });
    brushBar->addWidget(new QLabel("4K canvas / 25% view / 800px / 40% opacity")); tabs->addTab(brushPage, "4K brush");
    auto compositePage = new QWidget; auto compositeLayout = new QVBoxLayout(compositePage); auto compositeBar = new QHBoxLayout; compositeLayout->addLayout(compositeBar);
    auto picture = new QLabel; picture->setPixmap(QPixmap::fromImage(composite)); picture->setAlignment(Qt::AlignCenter); compositeLayout->addWidget(picture);
    auto saveComposite = [&] {
        auto directory = next("F04"); auto path = directory + "/F04.comp";
        require(QDir().mkpath(directory), "Create composition output"); scene.saveRenamedCopy(path, "Qt window renamed F04");
        require(compareImages(composite, Scene::read(path).render())["DifferentPixels"].toInteger() == 0, "Composition project readback");
        require(composite.save(directory + "/F04.png", "PNG"), "Save composition PNG"); saves.append(QJsonObject{{"kind", "composition"}, {"directory", directory}, {"project", path}});
    };
    button(compositeBar, "Save renamed project + check", &window, [&] { action(saveComposite); }); tabs->addTab(compositePage, "Composition / project");
    QJsonObject checks;
    if (selfTest) {
        checks["text"] = text->inputCheck(); saveText(); text->setPlacement(30, true, 1.25); saveText();
        checks["brush"] = brushCheck(*canvas); saveBrush(); saveComposite();
    } else { window.show(); QApplication::exec(); }
    QJsonObject report{{"qt", qVersion()}, {"os", QSysInfo::prettyProductName()}, {"platformPlugin", QGuiApplication::platformName()},
        {"windowsExecuted", QSysInfo::kernelType() == "winnt"}, {"syntheticCheckMode", selfTest}, {"nativeImeAccepted", false}, {"productAccepted", false},
        {"saves", saves}, {"errors", QJsonArray::fromStringList(errors)}, {"brushEvents", canvas->events}, {"brushPaints", canvas->paints}, {"brushFailure", canvas->failure},
        {"previewBuffer", "one viewport-sized premultiplied image at device pixel ratio, SourceOver on white; no full 4K preview export"},
        {"checks", checks}};
    writeFile(output + "/window-report.json", QJsonDocument(report).toJson());
    require(errors.isEmpty() && canvas->failure.isEmpty(), "Window recorded failures; inspect report"); return 0;
}
