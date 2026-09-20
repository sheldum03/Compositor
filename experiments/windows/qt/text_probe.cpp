#include "scene.hpp"
#include <QAbstractTextDocumentLayout>
#include <QApplication>
#include <QCryptographicHash>
#include <QDir>
#include <QElapsedTimer>
#include <QFileInfo>
#include <QFontDatabase>
#include <QGraphicsScene>
#include <QGraphicsSceneMouseEvent>
#include <QGraphicsTextItem>
#include <QGraphicsView>
#include <QInputMethodEvent>
#include <QJsonArray>
#include <QJsonDocument>
#include <QPainter>
#include <QRawFont>
#include <QSysInfo>
#include <QTextBlock>
#include <QTextBoundaryFinder>
#include <QTextCursor>
#include <QTextDocument>
#include <QTextLayout>
#include <cmath>
#include <iostream>
#include <memory>

namespace {
class TextItem final : public QGraphicsTextItem {
public:
    using QGraphicsTextItem::inputMethodQuery;
    int draws = 0;
    void paint(QPainter *p, const QStyleOptionGraphicsItem *option, QWidget *widget) override {
        ++draws; QGraphicsTextItem::paint(p, option, widget);
    }
};
class TextView final : public QGraphicsView {
public:
    using QGraphicsView::QGraphicsView;
    using QGraphicsView::inputMethodQuery;
};
void verify(const QString &root) {
    auto records = QJsonDocument::fromJson(readFile(root + "/checksums.json")).array(); require(records.size() == 47, "Extended corpus count");
    for (auto value : records) {
        auto record = value.toObject(); auto bytes = readFile(root + "/" + record["path"].toString());
        require(bytes.size() == record["bytes"].toInteger() && QCryptographicHash::hash(bytes, QCryptographicHash::Sha256).toHex() == record["sha256"].toString().toUtf8(), "Extended corpus hash");
    }
}
void format(TextItem &item, const QFont &font, QString text, double spacing, Qt::Alignment alignment, QColor color, double width) {
    item.setTextInteractionFlags(Qt::TextEditorInteraction);
    item.document()->setDocumentMargin(0); item.setFont(font); item.setPlainText(text);
    QTextCursor cursor(item.document()); cursor.select(QTextCursor::Document);
    QTextBlockFormat block; block.setAlignment(alignment); block.setLineHeight(spacing, QTextBlockFormat::LineDistanceHeight);
    cursor.mergeBlockFormat(block); QTextCharFormat character; character.setForeground(color); cursor.mergeCharFormat(character);
    cursor.clearSelection(); cursor.setPosition(0); item.setTextCursor(cursor);
    item.setTextWidth(width); if (width < 0) item.setTextWidth(item.document()->idealWidth());
    item.document()->clearUndoRedoStacks();
}
QImage preview(QGraphicsScene &scene, QSize size) {
    auto image = rgbaImage(size); QPainter p(&image);
    p.setRenderHints(QPainter::Antialiasing | QPainter::TextAntialiasing | QPainter::SmoothPixmapTransform);
    scene.render(&p, QRectF(QPointF(), size), QRectF(QPointF(), size), Qt::IgnoreAspectRatio); return image;
}
QImage exportImage(TextItem &item, QSize size) {
    auto image = rgbaImage(size); QPainter p(&image);
    p.setRenderHints(QPainter::Antialiasing | QPainter::TextAntialiasing | QPainter::SmoothPixmapTransform);
    p.setTransform(item.sceneTransform()); p.setOpacity(item.opacity());
    item.document()->drawContents(&p, item.boundingRect()); return image;
}
void save(const QImage &image, const QString &path) { require(image.save(path, "PNG"), "Save text image"); }
bool exact(const QImage &a, const QImage &b) { return compareImages(a, b)["DifferentPixels"].toInteger() == 0; }
QJsonObject hits(TextItem &item, QGraphicsScene &scene) {
    auto doc = item.document(); QTextBoundaryFinder boundaries(QTextBoundaryFinder::Grapheme, doc->toPlainText());
    int count = 0; double maximum = 0;
    for (auto block = doc->begin(); block.isValid(); block = block.next()) {
        auto layout = block.layout(); auto origin = doc->documentLayout()->blockBoundingRect(block).topLeft();
        for (int lineIndex = 0; lineIndex < layout->lineCount(); ++lineIndex) {
            auto line = layout->lineAt(lineIndex);
            for (double x = .37; x < doc->size().width(); x += 7.13) {
                QPointF local(x, origin.y() + line.y() + line.height() / 2), canvas = item.mapToScene(local), restored = item.mapFromScene(canvas);
                maximum = std::max(maximum, std::hypot((local - restored).x(), (local - restored).y()));
                require(maximum < .001, "Text transform inverse");
                int expected = doc->documentLayout()->hitTest(local, Qt::FuzzyHit);
                require(expected >= 0, "Layout hit");
                QGraphicsSceneMouseEvent press(QEvent::GraphicsSceneMousePress); press.setButton(Qt::LeftButton); press.setButtons(Qt::LeftButton);
                press.setPos(restored); press.setScenePos(canvas); require(scene.sendEvent(&item, &press), "Dispatch synthetic text press");
                QGraphicsSceneMouseEvent release(QEvent::GraphicsSceneMouseRelease); release.setButton(Qt::LeftButton); release.setPos(restored); release.setScenePos(canvas);
                require(scene.sendEvent(&item, &release), "Dispatch synthetic text release");
                require(item.textCursor().position() == expected, "Editor caret equals shared layout hit");
                boundaries.setPosition(expected); require(boundaries.isAtBoundary(), "Caret splits grapheme"); ++count;
            }
        }
    }
    auto cursor = item.textCursor(); cursor.setPosition(2); item.setTextCursor(cursor);
    return {{"samples", count}, {"maximumInverseError", maximum}, {"inverseTolerancePixels", .001}, {"clusterBoundaryViolations", 0}};
}
void spacingChecks(const QFont &base) {
    auto sample = [&](double spacing, double tracking) {
        auto item = std::make_unique<TextItem>(); auto font = base; font.setPixelSize(18); font.setLetterSpacing(QFont::AbsoluteSpacing, tracking);
        format(*item, font, "AA\nBB", spacing, Qt::AlignLeft, Qt::black, 360); return item;
    };
    auto normal = sample(0, 0), positive = sample(3, 0), negative = sample(-3, 0), tracked = sample(0, 1.25);
    auto secondY = [](TextItem &i) { auto d = i.document(); d->size(); return d->documentLayout()->blockBoundingRect(d->begin().next()).y(); };
    double y = secondY(*normal);
    require(std::abs(secondY(*positive) - y - 3) < 1e-8 && std::abs(secondY(*negative) - y + 3) < 1e-8, "Additive line spacing");
    auto advance = [](TextItem &i) { i.document()->size(); return i.document()->begin().layout()->lineAt(0).cursorToX(1); };
    require(std::abs(advance(*tracked) - advance(*normal) - 1.25) < 1e-8, "Absolute tracking advance");
    double before = advance(*tracked); auto font = tracked->font(); font.setLetterSpacing(QFont::AbsoluteSpacing, 2); tracked->setFont(font);
    require(std::abs(advance(*tracked) - before - .75) < 1e-8, "Tracking relayout");
}
QJsonObject inputChecks(TextItem &item, QGraphicsScene &scene, QSize size, const QString &prefix, const QImage &baseline) {
    auto doc = item.document(); QString original = item.toPlainText(); auto layout = doc->documentLayout();
    auto cursor = item.textCursor(); cursor.setPosition(2); item.setTextCursor(cursor);
    QTextCharFormat underline; underline.setFontUnderline(true);
    QInputMethodEvent preedit("输入法", {{QInputMethodEvent::Cursor, 2, 0, {}}, {QInputMethodEvent::TextFormat, 0, 3, underline}});
    require(scene.sendEvent(&item, &preedit), "Dispatch preedit");
    require(item.toPlainText() == original && doc->begin().layout()->preeditAreaText() == "输入法" && !doc->isUndoAvailable(), "Preedit outside committed history");
    auto preeditImage = preview(scene, size); save(preeditImage, prefix + "-preedit.png"); require(!exact(preeditImage, baseline), "Preedit changes pixels");
    require(layout == doc->documentLayout(), "Preedit uses same document layout");
    auto preeditExport = exportImage(item, size); save(preeditExport, prefix + "-preedit-export.png");
    require(exact(preeditImage, preeditExport), prefix + ": preedit editor/export " + QJsonDocument(compareImages(preeditImage, preeditExport)).toJson());
    QRectF rect = item.inputMethodQuery(Qt::ImCursorRectangle).toRectF(); require(rect.isValid(), "IME cursor rectangle");
    auto block = doc->begin(); auto line = block.layout()->lineForTextPosition(4);
    auto origin = layout->blockBoundingRect(block).topLeft();
    require(std::abs(rect.x() - origin.x() - line.cursorToX(4)) <= 1 && std::abs(rect.y() - origin.y() - line.y()) <= 1, "IME caret uses shared preedit layout");
    for (auto corner : {rect.topLeft(), rect.topRight(), rect.bottomLeft(), rect.bottomRight()})
        require(QLineF(item.mapToScene(corner), item.sceneTransform().map(corner)).length() < .001, "IME scene cursor mapping");
    QInputMethodEvent cancel; require(scene.sendEvent(&item, &cancel), "Dispatch cancel");
    require(item.toPlainText() == original && doc->begin().layout()->preeditAreaText().isEmpty() && !doc->isUndoAvailable(), "Cancel leaves text/history");
    auto canceled = preview(scene, size); save(canceled, prefix + "-cancelled.png"); require(exact(canceled, baseline), "Cancel restores exact pixels");
    QInputMethodEvent commit; commit.setCommitString("中文"); require(scene.sendEvent(&item, &commit), "Dispatch commit");
    QString committed = original; committed.insert(2, "中文"); require(item.toPlainText() == committed, "IME commit text");
    doc->undo(); require(item.toPlainText() == original, "IME commit undo"); doc->redo(); require(item.toPlainText() == committed, "IME commit redo");
    cursor = item.textCursor(); cursor.setPosition(0); cursor.setPosition(2, QTextCursor::KeepAnchor); item.setTextCursor(cursor);
    QInputMethodEvent replace; replace.setCommitString("替换"); require(scene.sendEvent(&item, &replace), "Dispatch selection replacement");
    require(item.toPlainText() == "替换" + committed.mid(2), "IME selection replacement");
    QJsonArray viewQueries;
    TextView view(&scene); view.resize(900, 700); view.show(); view.activateWindow(); view.setFocus();
    QApplication::processEvents(); scene.setFocusItem(&item); scene.setFocus();
    QInputMethodEvent focusedPreedit("候选", {{QInputMethodEvent::Cursor, 1, 0, {}}});
    require(scene.sendEvent(&item, &focusedPreedit), "Focused preedit dispatch");
    require(item.toPlainText() == "替换" + committed.mid(2) && item.textCursor().block().layout()->preeditAreaText() == "候选", "Focused view retains preedit separately");
    require(scene.focusItem() == &item, "Scene text focus item");
    for (double scale : {.75, 1.0, 1.5, 2.0}) {
        view.setTransform(QTransform::fromScale(scale, scale)); view.centerOn(item.mapToScene(item.boundingRect().center()));
        auto local = item.inputMethodQuery(Qt::ImCursorRectangle).toRectF();
        auto actual = view.inputMethodQuery(Qt::ImCursorRectangle).toRectF();
        auto expected = view.viewportTransform().mapRect(item.mapRectToScene(local));
        require(actual.isValid() && QLineF(actual.topLeft(), expected.topLeft()).length() < .001 &&
            QLineF(actual.bottomRight(), expected.bottomRight()).length() < .001, QString("View cursor actual=%1,%2,%3,%4 expected=%5,%6,%7,%8 focus=%9").arg(actual.x()).arg(actual.y()).arg(actual.width()).arg(actual.height()).arg(expected.x()).arg(expected.y()).arg(expected.width()).arg(expected.height()).arg(scene.hasFocus()));
        viewQueries.append(QJsonObject{{"viewScale", scale}, {"x", actual.x()}, {"y", actual.y()}, {"width", actual.width()}, {"height", actual.height()}});
    }
    return {{"viewCursorQueries", viewQueries}, {"preeditCancelCommitUndoRedoSelection", "passed"}, {"preeditPixelsExact", true}, {"cancelledPixelsExact", true}, {"cursor", QJsonObject{{"x", rect.x()}, {"y", rect.y()}, {"width", rect.width()}, {"height", rect.height()}}}};
}
}
void runTextProbe(const QString &fixtures, const QString &output) {
    verify(fixtures); require(!QFileInfo::exists(output) && QDir().mkpath(output), "Text output must be new");
    auto fontBytes = readFile(":/fonts/SourceHanSansSC-Regular.otf"); auto fontHash = QCryptographicHash::hash(fontBytes, QCryptographicHash::Sha256).toHex();
    require(fontHash == "f1d8611151880c6c336aabeac4640ef434fa13cbfbf1ffe82d0a71b2a5637256", "Text font hash");
    int fontId = QFontDatabase::addApplicationFontFromData(fontBytes); require(fontId >= 0, "Load application font");
    auto families = QFontDatabase::applicationFontFamilies(fontId); require(families.contains("Source Han Sans SC"), "Chinese font family");
    QFont base("Source Han Sans SC"); base.setPixelSize(18); require(QRawFont::fromFont(base).supportsCharacter(uint(0x4e2d)), "Chinese glyph");
    spacingChecks(base); QJsonArray results;
    for (const auto &package : QDir(fixtures).entryList({"F11-*.comp"}, QDir::Dirs, QDir::Name)) {
        QString name = QFileInfo(package).completeBaseName(), prefix = output + "/" + name;
        auto manifest = QJsonDocument::fromJson(readFile(fixtures + "/" + package + "/manifest.json")).object();
        auto layer = manifest["layers"].toArray()[0].toObject(), style = layer["text"].toObject(), transform = layer["transform"].toObject();
        double ppp = manifest["resolution"].toDouble() / 72, spacing = style["lineSpacingPoints"].toDouble() * ppp, tracking = style["trackingPoints"].toDouble() * ppp;
        auto font = base; font.setPixelSize(qRound(style["fontSizePoints"].toDouble() * ppp)); font.setLetterSpacing(QFont::AbsoluteSpacing, tracking);
        auto box = style["layout"].toObject()["box"].toObject(); double width = box.isEmpty() ? -1 : box["width"].toDouble();
        QString align = style["alignment"].toString(); auto alignment = align == "center" ? Qt::AlignHCenter : align == "right" ? Qt::AlignRight : Qt::AlignLeft;
        QColor color = QColor::fromRgbF(style["red"].toDouble(), style["green"].toDouble(), style["blue"].toDouble(), style["alpha"].toDouble());
        QGraphicsScene scene; auto item = new TextItem; scene.addItem(item);
        format(*item, font, style["content"].toString(), spacing, alignment, color, width);
        auto natural = item->document()->size(); auto target = transform["size"].toArray(), origin = transform["origin"].toArray();
        double sx = target[0].toDouble() / natural.width() * (transform["flipX"].toBool() ? -1 : 1), sy = target[1].toDouble() / natural.height() * (transform["flipY"].toBool() ? -1 : 1);
        QTransform map; map.translate(origin[0].toDouble() + target[0].toDouble() / 2, origin[1].toDouble() + target[1].toDouble() / 2);
        map.rotate(transform["rotation"].toDouble()); map.scale(sx, sy); map.translate(-natural.width() / 2, -natural.height() / 2);
        item->setTransform(map); item->setOpacity(layer["opacity"].toDouble());
        QSize size(manifest["width"].toInt(), manifest["height"].toInt()); scene.setSceneRect(QRectF(QPointF(), size));
        auto shared = item->document()->documentLayout(); QElapsedTimer timer; timer.start(); auto rendered = preview(scene, size);
        qint64 previewNs = timer.nsecsElapsed(); timer.restart(); auto exported = exportImage(*item, size); qint64 exportNs = timer.nsecsElapsed();
        save(rendered, prefix + "-preview.png"); save(exported, prefix + "-export.png");
        require(item->draws == 1 && shared == item->document()->documentLayout(), "Real item paint and shared layout identity");
        auto difference = compareImages(rendered, exported); require(difference["DifferentPixels"].toInteger() == 0, name + ": editor/export " + QJsonDocument(difference).toJson());
        require(!exact(exported, rgbaImage(size)), "Nonempty text");
        int lines = 0; QStringList resolved;
        for (auto b = item->document()->begin(); b.isValid(); b = b.next()) {
            lines += b.layout()->lineCount(); for (auto run : b.layout()->glyphRuns(0, int(b.text().size()))) resolved.append(run.rawFont().familyName());
        }
        require(!resolved.isEmpty(), "Observe actual shaped font families"); resolved.removeDuplicates(); resolved.sort(); QJsonArray fonts; for (auto f : resolved) fonts.append(f);
        if (width > 0) {
            auto height = natural.height(); item->setTextWidth(width / 2);
            require(item->document()->size().height() > height, "Narrower box wraps to more lines");
            auto narrow = preview(scene, size); require(exact(narrow, exportImage(*item, size)) && !exact(narrow, rendered), "Resized box shares changed layout");
            item->setTextWidth(width); require(exact(preview(scene, size), rendered), "Restore box width restores pixels");
        }
        auto hitResults = hits(*item, scene); auto input = inputChecks(*item, scene, size, prefix, rendered);
        results.append(QJsonObject{{"fixture", name}, {"fontSizePixels", font.pixelSize()}, {"letterSpacingPixels", tracking}, {"lineSpacingPixels", spacing},
            {"naturalWidth", natural.width()}, {"naturalHeight", natural.height()}, {"lines", lines}, {"resolvedFonts", fonts}, {"scaleX", sx}, {"scaleY", sy},
            {"previewNanoseconds", previewNs}, {"exportNanoseconds", exportNs}, {"boxResizeChecked", width > 0}, {"previewExport", difference}, {"transformedHits", hitResults}, {"syntheticInput", input},
            {"macReference", compareImages(exported, decodedImage(readFile(fixtures + "/" + name + "-mac.png")), prefix + "-diff.png")}});
    }
    require(results.size() == 12, "Twelve text styles"); verify(fixtures);
    QJsonObject report{{"status", "local preparation; synthetic events are not Windows IME acceptance"}, {"qt", qVersion()}, {"os", QSysInfo::prettyProductName()},
        {"architecture", QSysInfo::currentCpuArchitecture()}, {"windowsExecuted", QSysInfo::kernelType() == "winnt"}, {"nativeImeExecuted", false},
        {"fontSha256", QString::fromLatin1(fontHash)}, {"corpusHashesVerified", 47}, {"spacingChecks", "positive/negative additive spacing; absolute tracking and relayout passed"},
        {"placement", "shared document fitted to existing layer rectangle; no text cache/scale-policy change"}, {"results", results}};
    auto json = QJsonDocument(report).toJson(); writeFile(output + "/text-report.json", json); std::cout << json.constData();
}
