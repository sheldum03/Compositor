# Qt Windows 原生输入法阶段性证据（2026-09-23）

**预编辑取消后的原文和导出已独立核验；完整原生 IME 验收未完成。** 本轮通过 UU 操作既有 Qt 原型 PID 15572，未调用合成 `inputCheck()`，未重启窗口。它仍使用 `native-input-20260922-175634/qt-window` 输出目录。

远程画面观察到 Windows 拼音预编辑与候选栏，按 Esc 后恢复原文。首次 `Export + check` 显示完成，并产生 `001-text`。已取回的 [原始报告](text.json)记录 8 次非空预编辑事件、0 次提交事件，`nativeImeAccepted` 保持 false。独立检查确认：

- `content` 与构造时的中文、英文和 Emoji 原文逐字一致：`中文输入 / Windows IME\nSelect, replace, undo, redo. 😀`。
- 预览和导出均为 900×600，RGBA 字节完全一致；报告中的所有像素差异字段为零。
- ZIP 远端/本地 SHA-256 一致、CRC 通过，共 3 个原始文件；Windows 反斜线目录仅在解压时规范化。

见 [复核及逐文件身份](review.json)、[预览](text-preview.png)、[导出](text-export.png)；[完整原始 ZIP](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/qt-native-ime-partial/QtNativeImePartial-20260923.zip)。

后续尝试提交时出现远程按键未送达、非预期拉丁字符和焦点变化；画面上撤销了非预期字符，但没有对应导出，不计为已核验的提交/撤销/重做通过。Qt 窗口随后不在前台，只读 `Get-Process -Name qt*` 确认 PID 15572 仍在运行，不能将窗口不可见归因为进程崩溃。

用户允许独占操作后，UU 原生终端通过逐字输入的只读命令和打包命令恢复可用，文件传输成功；桌面点击和 Alt+Tab 仍未可靠恢复 Qt 前台。粘贴及部分字符输入也有异常，均未将命令发送等同执行成功。已请求手动聚焦既有 Qt 文字控件；未更改远程按键映射、安全设置或断开连接。

尚需原生选词提交、撤销/重做及对应内容导出、拖选和变换矩阵，最后正常关闭并取回 `window-report.json`。本包不含关闭报告，不关闭 Qt 原生 IME、M1 或 Windows 1.0 门槛。

## 2026-09-23 后续直接操作（新导出待取回）

UU 切为全屏后，下半部分空白问题消失，既有 Qt 文字区可完整观察；未改变 Windows 分辨率、DPI 或重启 Qt。通过 Ctrl+Z 清除了先前未完成尝试留下的 `zho`。英文模式的 `zhongwen` 尝试也撤销恢复；任务栏切换中文模式后实际显示微软拼音预编辑与候选栏。

直接远程键盘操作观察到：Esc 取消恢复原文；再次输入后按空格提交“中文”；Ctrl+Z 恢复原文、Ctrl+Y 恢复“中文”。各状态分别点击了 `Export + check`。随后 Rotation=30、Flip X 勾选、Scale=1.25，变换文字正确显示。候选栏在组合文字换行后出现在其右侧，尚未接受其准确定位。

下一次导出/选区操作的工具调用因 Mac 锁屏、自动解锁失败而中断，该次调用是否部分执行不明确。新导出尚未取回，故这些是 [GUI 观察记录](followup-ui-observations.json)，不新增“独立复核通过”项。继续使用原输出 `native-input-20260922-175634/qt-window`；恢复后先核对当前状态，完成变换选区输入并取回原始导出，最后正常关闭取得完整报告。不要因观察中断而重启窗口或清除历史。

## 预编辑换行坐标的本地诊断

2026-09-23 使用同一 Qt 6.11.2、固定思源字体、32 px 字号、540 px 文本宽度和原始内容，在独立 offscreen 程序中测试末尾 `z` 与 `zhong'wen` 预编辑。后者实际把第二段从一行变为两行；同时检查无变换和旋转 30°/水平翻转/125% 缩放。四例中 item 的 `ImCursorRectangle` 均与共享布局的实际预编辑光标位置相符，view 的矩形也与 item→scene→viewport 映射精确相等。见 [原始结果](wrapped-cursor-local.json)及[执行身份](wrapped-cursor-local-identity.json)。

本地未复现几何错误，故未修改 Qt 控件或推断 Windows 候选栏偏移原因。该程序通过合成 `QInputMethodEvent` 测坐标，不能覆盖 Windows 输入法查询时机或候选栏放置策略；实际 Windows 位置验收仍开放。第一次以相对字体路径启动未加载字体、退出 2，不产生几何结论；改用已核验绝对路径后退出 0。诊断[源码、构建日志及完整结果](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/qt-ime-wrapped-cursor)保留于实验目录。
