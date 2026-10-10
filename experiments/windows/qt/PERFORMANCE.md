# Qt S02 原生窗口重放

此入口补齐 M1 的同工作负载测试能力；不改原有两笔 40% 不透明度的 `--brush` 或手动 IME 窗口，也不表示 Windows 性能或框架选型通过。

```sh
qt_probe --s02-window <brush-fixtures> <new-output>
qt_probe --s02-check <brush-fixtures> <new-output>
python3 experiments/windows/qt/review-s02.py <native-Windows-output>/report.json
python3 experiments/windows/qt/review-s02.py <check-output>/report.json --local-check
```

`--s02-window` 显示独立的 1000×1000 逻辑像素 QWidget，使用系统 DPI。4000×4000 文档、25% 缩放、800 px、0 硬度、100% 不透明度、RGB `(1,.3,.1)`；两条冻结路径与 Avalonia S02 一致。路径 0 构造已有层基线，路径 1 用于两组测量。空层/已有层各预热一笔、测量 30 笔，每笔 pointer-down 后 120 次更新；共 62 笔、7502 个完整回调、7200 个测量更新。每笔重置到固定基线，不测试持续增长的历史。

`--s02-check` 每组只测一笔，连同预热共四笔、484 个回调；即便在 Windows 运行也不接受为 S02 性能结果。Mac 可设置 `QT_QPA_PLATFORM=offscreen` 和 `QT_SCALE_FACTOR=1` 或 `1.5`，验证实际 Qt 窗口绘图缓冲区和异步事件流程；这不是 Windows/DPI 设备验收。

## 测量范围

- 一次只允许一个更新在途。更新调用笔刷 append，触发真实 QWidget `paintEvent`，随后在队列回调确认完成，再发送下一个点。程序未额外添加 60 Hz 节流，与当前 Avalonia 的串行重放方法一致。
- 主指标 `UpdateToFrameCallbackMilliseconds` 从 append 前计时，直到该 paintEvent 返回后的队列回调。按 [Qt 6.11.2 raster repaint 路径](https://github.com/qt/qtbase/blob/v6.11.2/src/widgets/kernel/qwidgetrepaintmanager.cpp)，这个边界包含框架的 endPaint/flush 返回及回调调度。它不测屏幕物理呈现；另保存 append、等待 paint、paint、QPainter 释放、像素校验和校验后回调间隔。
- Avalonia 的主指标截止 Skia canvas lease 释放。两种边界分别记录，不将 Qt 较早的 QPainter 释放数字当成完整更新成本，也不直接以两个框架的局部绘制时间排名。
- 每组最后一笔的最后一个更新，在 paintEvent 内借用 [QBackingStore paintDevice](https://doc.qt.io/qt-6/qbackingstore.html#paintDevice) 读取实际 raster QImage，按实际设备矩阵截取视口，与同尺寸的独立参考绘制比较。设备指针不缓存到回调之外；不可读的缓冲区直接失败。读回、比较及 PNG 写出保留在该更新主计时内。
- Windows 通过 `GetProcessMemoryInfo` 的 PrivateUsage，在每帧回调及提交后采样；其他系统记 null。采样高水位不等于连续峰值，也不测 VRAM。
- 更新和提交均保存原始样本。复核器使用 nearest-rank P95，剔除每组预热和 pointer-down；数值门槛保持 16.7 ms / 100 ms / 2 GiB。程序始终写 `performanceAccepted=false`，复核器只输出数值判断，不自动关闭验收。

## 正确性与失败路径

运行前后检查九份固定输入的大小/hash。每笔与无临时笔尾的 settled replay 比较，检查源快照不变、一个历史条目、undo/redo 精确；oracle 共用笔刷原语，不是独立 Mac 算法。两组末尾保留最终 PNG 及缓冲区/参考 PNG。

输出目录必须新建。检查点使用 QSaveFile 原子替换，原有通用 `writeFile` 的拒绝覆盖语义不变。视口不足、运行中尺寸/DPI/可见性改变、关闭窗口、10 秒无绘制回调、像素或历史校验失败均返回失败并尽量保留报告。窗口是否被其他程序遮挡仍需实机观察。

`qt_s02_checks` 由 CTest 执行，使用真实 offscreen 窗口事件流验证四笔正常完成、700×700 视口拒绝、首笔后关闭及禁止绘制后的超时。它不是物理输入测试。Windows Release 构建仍须在实机运行这些检查和原生 30 笔测试；Mac CTest 或交叉编译不能替代。

复测证据、包身份及未完成项见 [Qt S02 准备记录](../../../docs/windows/evidence/qt-s02-preparation/README.md)。
