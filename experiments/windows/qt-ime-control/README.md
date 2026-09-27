# Qt 原生输入控件对照诊断

临时诊断程序，不是候选位置修复或生产功能。比较未修改的 `WindowText` 与 Qt `QTextEdit`：同一 Qt 6.11.2、内嵌思源字体、32 px、540 px 换行宽度、段间距 4 和中英 Emoji 原文。每个控件在独立进程运行，窗口标题带控件名与 PID。

每次原生输入事件前、事件返回后、Qt 光标矩形通知及窗口打开/关闭时追加并刷新 JSONL。记录实际文档预编辑布局、Qt 坐标、GetGUIThreadInfo/GetCaretPos、IMM context、ImmGetCandidateWindow/ImmGetCompositionWindow 的成功标记及返回数据。不调用 ImmSetCandidateWindow，不调整系统光标，不注入输入。IMM 返回的位置设置不等同于候选栏实际屏幕位置，必须与原生画面一起观察。输入事件可能经过多个 receiver，不能将日志条数当作独立用户输入次数。

构建步骤：

```sh
cmake -S experiments/windows/qt-ime-control -B <new-build> \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_TOOLCHAIN_FILE=<existing-windows-toolchain> \
  -DCMAKE_PREFIX_PATH=<Qt-6.11.2-Windows> -DQT_HOST_PATH=<Qt-6.11.2-host>
cmake --build <new-build> --parallel 4
```

包内 `run.py` 使用既有 Windows 测试根目录中的 `qt-remote-suite/window-app`，先核对全部包载荷和既有依赖摘要，再复制到新目录。依次运行 QTextEdit、WindowText；每个窗口正常关闭后才进入下一个，进程启动立即保存 PID，结束保存退出码，最后归档诊断记录和摘要清单。启动命令：`python QtImeControl/run.py`。不要因工具观察超时重启；先查询身份文件中的 PID。

实际操作与验证：

1. 聚焦文字并 Ctrl+End；确认任务栏微软拼音中文模式。
2. 输入 `z`，观察预编辑光标与候选栏；再输入 `hongwen` 触发换行，观察候选栏是否随插入行移动。
3. Esc 取消，确认原文保留；重复一次确认现象，再正常关闭窗口。对下一个控件重复相同步骤。
4. 取回完整结果，核验远端/本地 ZIP 摘要、CRC、成员清单、进程身份与退出码；结合真实候选位置和日志时间线比较两控件。

诊断未实现自动候选栏实际坐标读取。日志可以区分“Qt 坐标未更新”和“平台收到新坐标后仍显示旧位置”，不能单独给出候选定位通过结论。窗口关闭、导出或无崩溃都不替代本问题的复现。没有实际 Windows 结果前，不进入推测性布局修复。

原生只读接口依据：[ImmGetCandidateWindow](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immgetcandidatewindow)、[GetCaretPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getcaretpos)。日志保留 API 成功标记；失败时不把默认零坐标解释为有效位置。
