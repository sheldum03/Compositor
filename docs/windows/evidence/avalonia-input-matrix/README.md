# Avalonia 输入矩阵准备与导出失焦修复

2026-09-23。本轮只完成本地 Headless 验证与 Windows 测试包准备，未增加真实 Windows IME 或系统 DPI 通过项。UU 操作因 Mac 锁屏暂停；M1 继续开放。

## 修复及证据

实际原型窗口内拖选“中文”，再通过鼠标点击“导出并校验”，原文保持，但选区/光标变成 0/0/0。仅直接触发按钮 Click 事件的测试未覆盖失焦，曾漏掉此问题。改用 Headless 鼠标按下/松开后，回归以 `Export text state differs` 失败。

按“控件失焦清选区、导出重置、IME 适配器失焦重置”排查，首先将该原型 TextBox 的 `ClearSelectionOnLostFocus` 设为 false。其他输入处理不变，正向/反向拖选、真实鼠标点击导出、再次替换、撤销/重做均通过；两对预览/导出 PNG 字节相同。这里的鼠标事件来自 Headless，不是 Windows 物理鼠标。

新增垂直翻转、横向 75% 非等比缩放按钮。自动选择回归从 48 个组合扩展到 192 个，保留选区高亮像素、精确替换与历史检查；40 个 IME client 取消及 24 个页签切换模拟用例通过。实际原型窗口回归还验证新增按钮、导出状态快照、窗口进程身份及正确的 Headless 标记。

每次文字导出保存 `text-state.json`，窗口报告增加 PID、带输出目录名的标题、当前文字/选区/变换。固定测试文字可以入实验报告；这不是生产文本日志方案。

独立结果见 [local-review.json](local-review.json)。原始日志、状态和 PNG 共 402 个文件保存在该报告指定的本地 `local-evidence.zip`，SHA-256 为 `ec0165dd0f19fa811a79d3ea1f2d9f2edadfb0bbc7e34ad14563aa422af73502`，已检查 ZIP CRC。失败版 `window-final/window-report.json` 在新增明确标记前错误地写有 `nativeWindow=true`；原始证据不改写，该轮实际为 Mac Headless，不算原生窗口执行。最终报告已明确 `nativeWindow=false`。

## 下一步 → 验证方式

1. 在同一 Windows 主机运行独立测试包 → 逐项验证基础 R9 文件、固定 C DLL、样本及新载荷 SHA-256；先执行四组自动回归，任一失败则停止原生测试并归档日志。
2. 用唯一标题辨认新窗口 → 同步记录进程号、输入前快照及操作顺序；真实微软拼音逐项观察候选位置、Esc 保留选区原文、选词精确替换、撤销/重做、失焦/返回及导出。直接注入文字或计数增加不代替 IME 验收。
3. 补变换覆盖 → 旋转 0°/13°、缩放 50%/100%/150%、水平翻转、垂直翻转与横向 75% 非等比缩放，按实际执行组合留图、留文本，未执行组合保留未测。候选栏必须跟随当前光标；取消前后在相同变换下比较原文和 PNG。
4. 补系统 DPI/小视口 → 记录系统实际缩放和窗口 `renderScaling`，区分画布缩放；检查工具栏滚动可达、输入及导出。第二显示器、第二输入法、数位笔、集显设备仍需实际可用设备，不能用合成输入补成通过。
5. 正常关闭新窗口并取回结果 → 核对退出码、窗口 PID、全部文件摘要与原始事件；逐对复核预览/导出，单独评审原生观察。测试脚本不会自动将 `nativeImeAccepted` 或 `systemDpiAccepted` 设为 true。

脚本为 `experiments/windows/text-selection-regression/run-matrix.py`，随私有包放在 `CompositorTest/AvaloniaImeMatrix/` 下。它复制既有 R9 到新结果目录，只在副本替换输入测试程序集；不覆盖旧版应用和证据。正常关闭窗口后才完成归档。

固定源码 `a869a69a02dd4c78c483a31dc0165412d273e5c9` 的 Windows x64 包已生成，29 个沿用依赖与 R9 摘要一致，102 个样本/清单文件已本地核对。包身份见 [package.json](package.json)，尚未传输或在 Windows 执行。解压到 `CompositorTest` 后入口为 `python .\AvaloniaImeMatrix\run-matrix.py`；需等待自动回归结束和唯一标题的原生窗口出现，再进行人工观察。
