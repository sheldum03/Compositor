# Windows 11 原生窗口首轮中间证据：未完成

用户明确要求本任务通过 Mac 的 UU 远程操作已连接的 Windows 11 实体机。实际由本任务经 CUA 操作，非用户转述：传输冻结 CompositorWindowTest.zip，并通过单独 Start-CompositorWindow.cmd 在远端核验 SHA256、只向新 window-test 解压、调用原 run-window.ps1；未覆盖此前测试包。远端显示 Window ZIP SHA256 verified，窗口成功打开。

本轮目录为 C:\Users\Administrator\Desktop\CompositorTest\window-run-20260921-154725。报告确认 Windows 10.0.26200 / nativeWindow=true / scaling=1.5 / 指定软件渲染。点击导出后回收的首对760×520预览/导出PNG已用Pillow/NumPy独立比较完整RGBA，精确相同。

点击文字并发送End/a后看到光标附近的拼音候选栏。随后通过UU发送Esc、Backspace、字母及点击测试按钮/Windows任务栏，画面未见对应反应；但Windows时钟/UU通知仍刷新、UU双向文件传输正常。更早在PowerShell输入长路径时已有空格/反斜杠丢失，不能排除远程输入链路；也没有证据可以认定测试程序或IME死锁。保持窗口运行，已请用户在Windows本机按Esc作对照，尚无回复。没有修改代码或终止程序来掩盖现场。

interim-snapshot是程序尚运行时的一次文件传输快照，不是最终归档。window-report.json在首次导出时保存，nativePreeditChanges=0/textInputEvents=0；候选事件发生在此快照保存之后，不能由该数字推定候选调用是否被记录。截图只证明观察到候选UI，不能单独证明完整Windows输入法验收。窗口仍打开、未测笔刷/工程/撤销/变换/焦点/DPI。整轮未通过，不关闭W008/M1。
