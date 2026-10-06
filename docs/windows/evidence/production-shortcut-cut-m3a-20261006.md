# Production `Ctrl+X` shortcut evidence

日期：2026-10-06

实现提交：`3d8087c`（`MainWindow` shortcut wiring）

检查入口：`windows/Compositor.App.Checks`

## 范围

正式 Avalonia 窗口的 `Ctrl+X` 现在调用既有 `CutSelectionAsync`，与剪切按钮共用复制到内部剪贴板、清除选中像素、发布系统剪贴板和历史事务路径。文本框或图层名称输入框获得焦点时仍保留输入控件自己的快捷键和 IME 行为。

## 验证

固定 SDK 10.0.401，执行：

```text
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet windows/Compositor.App.Checks/bin/Release/net10.0/Compositor.App.Checks.dll \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-shortcut-20261006-1
```

结果：

- Release 构建 0 warning / 0 error。
- 正式窗口检查通过 `Ctrl+X`：半画布选区像素被清除，文档变脏；随后 Undo 恢复原像素并回到保存状态。
- 同一检查继续通过已有的保存、关闭保护、图层、选区、跨工程复制和组结构检查。

## 限制

该证据是 Avalonia Headless 窗口和内部像素回归，尚未证明 Windows 原生键盘布局、IME、系统剪贴板或远程腾讯云 Windows 实机行为。真实 Windows 验收仍需在 `windows-part` 计划的 W-004/W-016/W-038 闸门执行。
