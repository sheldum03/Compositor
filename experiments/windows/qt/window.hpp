#pragma once
#include <QJsonObject>
#include <QWidget>
class QGraphicsScene;
class QGraphicsTextItem;
class QGraphicsView;
class WindowText final : public QWidget {
public:
    WindowText();
    QJsonObject exportTo(const QString &directory);
    QJsonObject inputCheck();
    void setPlacement(double angle, bool flip, double scale);
private:
    QGraphicsScene *scene;
    QGraphicsTextItem *item;
    QGraphicsView *view;
};
int runWindowProbe(const QString &fixtures, const QString &output, bool selfTest);
