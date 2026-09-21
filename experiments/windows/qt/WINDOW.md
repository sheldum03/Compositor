# Qt 原生窗口可行性入口

运行 `qt_probe --window <fixtures-root> <new-output>` 打开三页 Widgets 窗口。使用现有 `Scene`、`BrushSession`、`SoftBrushStroke` 和 `QGraphicsTextItem/QTextDocument` 格式化、预览、导出函数。目录中须包含既有 `F04.comp` 和 `brush/soft-crossing-4k-cpu.comp`。Qt 与 native 库的部署要求沿用本目录 README。

这仍是 M1 实验：不实现完整编辑器、文字工程事务或 D-11 缺字体处理，不表示框架选型、S02 性能、Windows 11 输入法或发布验收通过。

## 手动检查

1. Text / IME 页点击蓝字，在 Windows 11 切换微软拼音，输入 `zhongwen`。观察候选位置，先取消再提交；检查选择替换、Ctrl+Z/Y、切换窗口后的焦点和预编辑状态。
2. 改变 Rotation / Flip X / Scale，重复输入、取消、选词及 Export + check。未提交的预编辑禁止导出；先提交或取消。保存时暂时隐藏光标和选择区，随后恢复选择、文本和撤销状态。PNG 相等不能代替字形完整性检查。
3. 4K brush 页按住左键绘制两笔，用 Undo / Redo 检查历史；绘制时 Esc 或切走窗口取消当前笔划。画布为 4000×4000，显示 25%，固定软笔 800 px、40% 不透明度；这不是 S02 的 100% 不透明度、120 次更新、30 笔性能测试。
4. Save project + check 将笔刷写入新 `.comp` 并重开比对；Composition / project 页另存 F04 改名工程并重开比对。原样本不覆盖。
5. 有不同 DPI 的第二屏时移动窗口重复输入；没有则记录未测。不要为测试更改服务器全局字体或系统显示设置。
6. 正常关闭生成 `window-report.json`，保留输出文件和过程日志。报告记录保存、输入事件及错误；`nativeImeAccepted`、`productAccepted` 始终为 false，需要独立人工评审。

## 自动检查与限制

`QT_QPA_PLATFORM=offscreen qt_probe --window-check <fixtures-root> <new-output>` 在同一控件上注入合成事件，检查预编辑取消/提交/撤销/重做、预编辑时拒绝导出且不创建部分目录、带选择区的导出状态保留、文字旋转/翻转/缩放后预览与导出一致，以及笔划取消后松鼠标不会提交、撤销/重做、失活取消、背景不被透明瓦片擦除、笔刷/F04 工程重开一致。

鼠标更新和 QWidget 绘制在 GUI 线程执行。每次画笔预览创建一张与控件大小及实际 DPR 相应的透明 RGBA 缓冲，在其中按 Source 画瓦片，再以 SourceOver 叠到白色背景，避免透明像素擦掉窗口背景。1× 为 1000×1000×4 bytes，2× 为其四倍；这些分配/复制代价尚未优化。不会为预览导出完整 4K 图像。事件耗时只有处理函数范围，不能当作端到端呈现延迟。

父任务在固定基线 605a610 的独立源码副本上完成 macOS 与 Windows x64 LLVM-MinGW 交叉编译；macOS offscreen 自动检查及原20合成/12文字/笔刷回归通过。将预览临时改为直接绘制瓦片的负例编译成功，实际控件背景检查退出1；恢复实现后重跑通过。随后已在权威工作区重新构建并完成 macOS cocoa 原生窗口操作（见下）；Windows 新可执行文件运行、真实微软拼音及 DPI 切换尚未执行。本窗口并未修复此前 Qt Server offscreen 缺 Emoji 的问题。

## 整合后的真实窗口检查

Mac cocoa 窗口实际完成文字选择/替换、粘贴、撤销/重做，原生预编辑提交/取消，30°旋转、水平翻转、125%缩放和导出；两笔真实鼠标绘制、撤销/重做保存、F04改名另存也通过。关闭后报告无错误，记录平台cocoa。Mac输入法事件与图片只作为本机原生路径证据，候选位置和Windows微软拼音尚未验收。

真实操作发现全选后键盘/IME及粘贴的新文字变黑，而共享预览/导出相等仍会通过。新增 --window-check 的整段替换蓝色像素断言先失败（exit1）；format中同时设置段落默认字符颜色后通过（exit0）。真实窗口再测保留蓝色；相同粘贴内容修正前深色像素4619、蓝色像素0，修正后深色0、蓝色4951。原12样本全部72PNG逐字节不变；取消输入前后导出、笔刷撤销/重做对应PNG也精确恢复。修正只补默认格式，不改变现有字形布局/算法或Server Emoji诊断结论。

父任务原始交付及本任务编译、原生交互、负例/修正和图片复核证据见 `docs/windows/evidence/qt-native-window-macos`。构建产物与完整交互输出保存在本任务artifact目录。
