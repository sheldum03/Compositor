// Temporary diagnostic: read-only native queries, no candidate-position overrides.
#include "../qt/window.hpp"
#include <QApplication>
#include <QDateTime>
#include <QFile>
#include <QGraphicsScene>
#include <QGraphicsTextItem>
#include <QGraphicsView>
#include <QInputMethod>
#include <QInputMethodEvent>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QLabel>
#include <QTextBlock>
#include <QTextEdit>
#include <QTextLayout>
#include <QTimer>
#include <QVBoxLayout>
#ifdef Q_OS_WIN
#include <windows.h>
#include <imm.h>
#endif

QJsonArray rectangle(QRectF r) { return {r.x(), r.y(), r.width(), r.height()}; }
class Recorder final : public QObject {
public:
    QFile file;
    QString control;
    QTextDocument *document = nullptr;
    explicit Recorder(const QString &path, const QString &name) : file(path), control(name) {
        if (!file.open(QIODevice::WriteOnly | QIODevice::NewOnly)) qFatal("New trace file required");
    }
    void record(const QString &reason, QJsonObject extra = {}) {
        extra["utc"] = QDateTime::currentDateTimeUtc().toString(Qt::ISODateWithMs);
        extra["reason"] = reason; extra["control"] = control;
        extra["pid"] = qint64(QCoreApplication::applicationPid());
        extra["qtCursorRect"] = rectangle(QGuiApplication::inputMethod()->cursorRectangle());
        extra["qtAnchorRect"] = rectangle(QGuiApplication::inputMethod()->anchorRectangle());
        if (document) {
            extra["content"] = document->toPlainText(); QJsonArray blocks;
            for (auto b = document->begin(); b.isValid(); b = b.next()) {
                auto l = b.layout(); blocks.append(QJsonObject{{"position", b.position()},
                    {"lines", l->lineCount()}, {"preedit", l->preeditAreaText()}, {"preeditPosition", l->preeditAreaPosition()}});
            }
            extra["blocks"] = blocks;
        }
#ifdef Q_OS_WIN
        GUITHREADINFO gui{}; gui.cbSize = sizeof(gui);
        bool guiOk = GetGUIThreadInfo(GetCurrentThreadId(), &gui);
        extra["guiInfoOk"] = guiOk;
        auto hwnd = GetFocus();
        extra["focusHwnd"] = QString::number(quintptr(hwnd), 16);
        extra["caretHwnd"] = QString::number(quintptr(gui.hwndCaret), 16);
        extra["systemCaretRect"] = rectangle(QRect(gui.rcCaret.left, gui.rcCaret.top,
            gui.rcCaret.right - gui.rcCaret.left, gui.rcCaret.bottom - gui.rcCaret.top));
        POINT point{}; SetLastError(0); bool caretOk = GetCaretPos(&point);
        extra["getCaretPosOk"] = caretOk; extra["getCaretPosError"] = int(GetLastError());
        if (caretOk) extra["systemCaretPoint"] = QJsonArray{int(point.x), int(point.y)};
        POINT origin{};
        if (hwnd && ClientToScreen(hwnd, &origin)) extra["focusClientScreenOrigin"] = QJsonArray{int(origin.x), int(origin.y)};
        auto context = hwnd ? ImmGetContext(hwnd) : nullptr;
        extra["hasImmContext"] = context != nullptr;
        if (context) {
            CANDIDATEFORM candidate{}; bool candidateOk = ImmGetCandidateWindow(context, 0, &candidate);
            extra["getCandidateWindowOk"] = candidateOk;
            if (candidateOk) extra["candidateForm"] = QJsonObject{{"style", int(candidate.dwStyle)},
                {"point", QJsonArray{int(candidate.ptCurrentPos.x), int(candidate.ptCurrentPos.y)}},
                {"area", rectangle(QRect(candidate.rcArea.left, candidate.rcArea.top,
                    candidate.rcArea.right-candidate.rcArea.left, candidate.rcArea.bottom-candidate.rcArea.top))}};
            COMPOSITIONFORM composition{}; bool compositionOk = ImmGetCompositionWindow(context, &composition);
            extra["getCompositionWindowOk"] = compositionOk;
            if (compositionOk) extra["compositionForm"] = QJsonObject{{"style", int(composition.dwStyle)},
                {"point", QJsonArray{int(composition.ptCurrentPos.x), int(composition.ptCurrentPos.y)}}};
            extra["releaseContextOk"] = bool(ImmReleaseContext(hwnd, context));
        }
        extra["windowsExecuted"] = true;
#else
        extra["windowsExecuted"] = false;
#endif
        auto bytes = QJsonDocument(extra).toJson(QJsonDocument::Compact) + '\n';
        if (file.write(bytes) != bytes.size() || !file.flush()) qFatal("Trace write failed");
    }
    bool eventFilter(QObject *watched, QEvent *event) override {
        if (event->type() == QEvent::InputMethod) {
            auto ime = static_cast<QInputMethodEvent *>(event);
            record("input-before", {{"receiver", watched->metaObject()->className()},
                {"preedit", ime->preeditString()}, {"commit", ime->commitString()}});
            QTimer::singleShot(0, this, [this] { record("input-after"); });
        }
        return false;
    }
};
int main(int argc, char **argv) {
    QApplication app(argc, argv);
    if (argc != 3 || (QString(argv[1]) != "textedit" && QString(argv[1]) != "windowtext")) return 2;
    QString control = argv[1]; Recorder recorder(argv[2], control);
    QWidget window; window.setWindowTitle("Qt IME control — " + control + " — " + QString::number(app.applicationPid()));
    window.resize(1080, 840); auto layout = new QVBoxLayout(&window);
    layout->addWidget(new QLabel("Diagnostic: click text, Ctrl+End, type zhongwen with Microsoft Pinyin; observe wrap, Esc, close."));
    // Construct the production probe unchanged, also registering the exact embedded font.
    auto probe = new WindowText;
    if (control == "windowtext") {
        layout->addWidget(probe);
        auto view = probe->findChild<QGraphicsView *>();
        for (auto item : view->scene()->items()) if (auto text = dynamic_cast<QGraphicsTextItem *>(item)) recorder.document = text->document();
    } else {
        delete probe;
        auto edit = new QTextEdit; layout->addWidget(edit);
        QFont font("Source Han Sans SC"); font.setPixelSize(32); edit->setFont(font);
        edit->setLineWrapMode(QTextEdit::FixedPixelWidth); edit->setLineWrapColumnOrWidth(540);
        edit->document()->setDocumentMargin(0);
        edit->setPlainText(QString::fromUtf8("中文输入 / Windows IME\nSelect, replace, undo, redo. 😀"));
        auto cursor = edit->textCursor(); cursor.select(QTextCursor::Document);
        QTextBlockFormat block; block.setLineHeight(4, QTextBlockFormat::LineDistanceHeight); cursor.mergeBlockFormat(block);
        cursor.clearSelection(); cursor.movePosition(QTextCursor::End); edit->setTextCursor(cursor);
        recorder.document = edit->document();
    }
    app.installEventFilter(&recorder);
    QObject::connect(app.inputMethod(), &QInputMethod::cursorRectangleChanged, &recorder, [&] { recorder.record("cursor-changed"); });
    window.show();
    QTimer::singleShot(0, &recorder, [&] { recorder.record("opened", {{"qt", qVersion()}, {"platform", app.platformName()}, {"dpr", window.devicePixelRatioF()}}); });
    auto result = app.exec(); recorder.record("closed", {{"exitCode", result}}); return result;
}
