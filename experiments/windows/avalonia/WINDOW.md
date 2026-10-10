# 原生窗口原型

`--window <fixtures-root> <new-output-directory> <native-library>` 打开真实桌面窗口。Avalonia Desktop / SimpleTheme 固定 11.3.22，Windows 使用 Software + RedirectionSurface，Mac 使用 Avalonia.Native Software。不是 Headless 窗口，也不是 GPU 路线验收或框架选型。

三页覆盖四条受限路径：共享 TextBox 布局的变换文字输入和 PNG 导出、4K 软笔真实鼠标输入、F04 合成显示、笔刷/F04 工程另存重开。复用原算法与 v8 工程代码；不实现完整编辑器、文字工程事务、缺字体选择或 D-11 验收。

## Windows 11 操作

先完成当前 12 个文字修复样本的复测。另将 `CompositorWindowTest.zip` 中的 `window-test` 文件夹放到 `C:\Users\Administrator\Desktop\CompositorTest`，保留既有 `pinvoke-test/runtime` 和已验证的 native DLL。包自带完整新托管依赖及 win-x64 原生资产，不覆盖原 `render-test/app`。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\Administrator\Desktop\CompositorTest\window-test\run-window.ps1"
```

1. 在「文字 / 输入法」点击蓝色文字，切换微软拼音，输入 `zhongwen`；观察候选窗是否贴近当前光标，有无跳到窗口角落。先 Esc 取消，确认原文字恢复；再次输入并选词，确认中文完整。
2. 用 Ctrl+Z / Ctrl+Y 检查撤销、重做；拖选部分文字，再输入替换。点击「导出并校验」，底部应显示完成。
3. 分别改变旋转、水平/垂直翻转和缩放，再点击文字重试输入、候选选择、取消及导出。「非等比缩放」在横向 100% 与 75% 之间切换，纵向倍率保持不变；界面缩放仍有 50%/100%/150%。用 Alt+Tab 切走再回来，检查焦点和未确认输入。若出现异常，记录具体动作及画面；数字计数不能代替候选位置检查。
4. 在「4K 笔刷」按住鼠标左键画两笔，检查无方形瓦片边界；撤销、重做后保存。再按住绘制时按 Esc 取消，松开后保存，原笔划应保持。缩小时可滚动画布。
5. 在「合成 / 工程」点击「另存工程并校验」。只能在新输出目录保存，不覆盖原样本。
6. 若有不同缩放的第二台显示器，将窗口移过去重复文字检查，记录两屏实际缩放比例。没有第二屏就标为未测，不需要为此改变系统设置。
7. 关闭窗口，等待 PowerShell 打包，回传 `window-run-时间.zip`，附上候选位置、取消、焦点和显示缩放的观察。有异常也保留压缩包。

脚本核对包内文件与已有 DLL 的 SHA-256；记录程序退出码，失败也先归档日志。正常退出及三页保存完成只是原型操作检查，`NativePreeditChanges > 0` 不等于微软拼音验收。原生 IME、DPI、性能和画面仍需独立评审。

每次文字导出另存 `text-state.json`，包括固定测试文字、选区/光标、变换、当前窗口 `renderScaling` 及输入计数；窗口报告同时保留最终状态。该快照便于核对取消前后文字和对应的 PNG，不能以快照存在或计数增加就判定输入行为正确。应为每次操作预先记录预期文字和同变换下的像素参照。这里导出固定测试内容是实验取证，不是生产日志格式。

`dotnet run --project ../text-selection-regression/Regression.csproj -- --window-matrix <fixtures-root> <new-output> <native-library>` 在 Headless 中操作实际原型窗口的新增按钮，验证双向拖选、共享布局导出、状态快照和导出后的撤销/重做；普通选择回归覆盖 192 个组合。它们只验证本地事件路由和导出，不构成真实 Windows IME 或系统 DPI 验收。

## 线程和像素所有权

`window-report.json` 记录真实 managed thread ID 与 `Dispatcher.UIThread.CheckAccess()`。Mac 实测输入/保存在线程 1，笔刷和合成绘制在线程 4；Windows 尚待记录。UI 对 BrushSession 的更新、历史、保存与绘制共用一把锁；FixtureScene 绘制/保存/关闭释放也共用一把锁。关闭标志阻止排队绘制访问已释放场景。

`TiledRaster.Paint` 使用 `SKImage.FromPixelCopy`，本地图片持有自己的像素副本，不向延迟绘制借出可变 byte[]。瓦片的 Src 替换只在独立透明 SaveLayer 内进行，恢复时合成到窗口背景；这避免透明瓦片擦除背景。此选择增加临时表面和复制成本，尚未优化或证明正式性能。鼠标事件日志记录 Append/Commit 时间，不称为端到端输入延迟或正式 S02。

## 已执行的验证

Mac 原生窗口已实际完成粘贴、键盘输入、撤销/重做、变换文字导出、两笔鼠标绘制、历史保存、F04 另存及正常关闭。第一轮 6 个文字导出内部零差异，取消前后 PNG 相同；笔刷撤销后的 PNG 改变、重做后的 PNG 与撤销前逐字节相同。Mac 原生预编辑事件确有记录，不作 Windows IME 证据。

原生窗口发现的背景擦除问题由真实 WindowBrushView 控件的回归捕获：不透明背景上 32,041 个像素变为半透明；SaveLayer 修正后为 0，真实窗口方块消失。最终带滚动布局的构建再次实际完成三页导出/保存，退出 0。原 20 个合成、12 个文字和 13 项笔刷会话检查通过。证据见 `docs/windows/evidence/avalonia-native-window-macos`；详细回归命令见 `../window-regression/README.md`。

Windows 11 原生窗口已通过 UU 远程实际启动，150% 显示缩放下首对预览/导出独立 RGBA 精确；发送字母后看到拼音候选，但随后远程输入无明显响应，正等待用户本机 Esc 对照，尚不能定位原因。此为未完成首轮，见 `docs/windows/evidence/avalonia-native-window-windows11-interim`。真实微软拼音完整行为、显示器 DPI 切换、字体缺失/文字工程事务仍未验收，W-008/M1 不放行。
