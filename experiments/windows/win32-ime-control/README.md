# 系统原生 EDIT 输入法对照（临时诊断）

用于回答 Qt 两种控件都出现的候选栏换行偏移，是否也发生在 Windows 系统原生多行 EDIT 中。真实输入仅由用户或 CUA 通过 UU 操作；脚本不注入按键、合成输入法事件或覆盖候选位置。2026-09-28 Windows PID 29256 已正常退出，原文及归档身份通过核验；用户明确确认换行测试通过，记为固定样本人工观察，不是 Qt 修复或代理独立候选定位验证。见[归档复核](../../../docs/windows/evidence/qt-native-ime-windows/candidate-diagnostic/control-comparison/win32-20260928/README.md)。

Python 3 x64 直接使用系统 user32/gdi32/imm32。进程私有加载仓库固定思源字体，目标 32 逻辑像素、540 逻辑像素换行宽度、相同中文/英文/Emoji 原文。记录实际 DPI、GDI 解析字体名、系统光标、预编辑及 IMM 候选查询。系统 EDIT 的排版引擎、行间距、Emoji fallback 和预编辑绘制机制不同于 Qt，不能视为完全相同的布局；必须实际观察换行，不能只比较坐标数值。

在测试根目录解压独立 `Win32ImeControl.zip` 后执行：

```powershell
python Win32ImeControl/run.py
```

确认任务栏为微软拼音中文，点击文字、Ctrl+End，逐键输入 `zhongwen`。分别观察换行前、触发换行后、完整预编辑的实际光标及候选栏位置；按 Esc 核对原文，再正常关闭。若未触发换行，不记为定位通过，保留现场再设计仅改变宽度的独立试验。不要用返回的 IMM 设置点冒充实际弹窗矩形。

每次生成新的 `win32-ime-control-时间` 目录与 ZIP。文件包括执行身份、按状态变化写入的只读采样和摘要；正常退出后的 `finalOriginalPreserved` 只检查最终文本，不自动接受候选定位。启动前验证载荷摘要，私有字体随进程移除，不安装系统字体。

本地只验证 Python 语法、ctypes 类型名称和载荷身份；Win32 API 及结构布局需在 Windows 运行验证。Windows 运行失败应保留原始输出，不能将准备检查称作执行通过。

依据：[微软 EM_SETRECT](https://learn.microsoft.com/en-us/windows/win32/controls/em-setrect)、[进程私有字体加载](https://learn.microsoft.com/zh-cn/windows/win32/api/wingdi/nf-wingdi-addfontresourceexw)。
