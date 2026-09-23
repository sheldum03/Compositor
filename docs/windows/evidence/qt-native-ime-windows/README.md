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
