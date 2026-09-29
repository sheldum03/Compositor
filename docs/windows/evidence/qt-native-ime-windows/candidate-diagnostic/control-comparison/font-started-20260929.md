# Qt 字体对照启动记录（未完成）

2026-09-29 经用户明确授权重启本机 UU 客户端。重启后实际 Windows 桌面及远端磁盘列表恢复，成功将 QtImeFont.zip 传至 CompositorTest。Windows PowerShell 显示 SHA-256 `6146304951F44BEAA5090D23EC64676997D81C6FF03D960C84AE51D6297ABF1D`，与本地准备记录一致；`test-path qtimefont` 返回 False 后解压到新目录。见[本地包身份与构建记录](font-preparation-20260928/README.md)。

执行 `python qtimefont/run.py --font-comparison`，输出目录 `C:\Users\Administrator\Desktop\CompositorTest\qt-ime-font-20260929-093610`。第一进程 QTextEdit / Source Han Sans SC，PID 17968；第二进程 QTextEdit / Microsoft YaHei UI，PID 28284。

## 第一轮可见观察

代理先有一次 `z` 落在启动终端的微软拼音预编辑，用 Esc 取消，未提交；这一段排除。鼠标未将 Qt 窗口置前，之后通过 Alt+Esc 切换并确认标题和真实候选栏。

在思源字体窗口，以原始样本末尾为起点，`zhon` 尚在旧行末尾，输入 `g` 后 `zhong` 自动移至下一行；候选栏仍停在旧行末尾附近。继续 `wen` 成为 `zhong'wen` 后仍未跟随。全过程未用 Enter 人为换行。Esc 后可见原文完整保留，随后 Alt+F4 正常关闭该窗口并由启动器进入第二轮。此处为会话截图的代理可见观察；完整日志尚未取回复核。

## 第二轮待完成

标题显示 Microsoft YaHei UI / PID 28284，但多次按键、Tab、Ctrl+End 以及点击文字后未出现可见预编辑，Ctrl+A 也未显示预期高亮。尚不能证明测试输入已送达，不把它记为候选定位通过或字体错误。已请用户手动点击文字、Ctrl+End、输入 `z`，候选出现后保持窗口打开。

后续在第二轮实际预编辑出现后继续同样的 `zhongwen` 自动换行观察；若没有触发自动换行，则记为条件未满足，不记通过。随后 Esc 检查原文并正常关闭，取回启动器生成的 ZIP，核验摘要、CRC、7 项成员摘要、两个进程的字体解析、退出码及最终原文。原始包尚未产生或取回，不更新最终比较结论，不关闭 Qt IME 或 M1。
