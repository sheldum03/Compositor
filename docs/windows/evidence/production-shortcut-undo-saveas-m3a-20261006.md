# Production undo and Save As shortcut evidence

日期：2026-10-06

实现提交：`bbaf60b`

## 范围

正式 Avalonia 窗口现在支持 PRD 建议的 `Ctrl+Shift+Z` 重做和 `Ctrl+Shift+S` 另存为入口。`Ctrl+Shift+I` 反选继续保留；文本框和图层名称输入框仍优先接收自己的快捷键与 IME 输入。

## 验证

固定 SDK 10.0.401，Release 构建 `Compositor.App.Checks` 后运行正式窗口检查。检查通过：

- `Ctrl+X` 清除选区像素并建立历史步骤。
- `Ctrl+Z` 恢复保存像素和保存状态。
- `Ctrl+Shift+Z` 重做剪切并恢复脏状态。
- 再次 `Ctrl+Z` 回到保存像素。
- 既有保存、关闭保护、跨工程复制、组结构和图层检查继续通过。

结果：0 warning / 0 error，`results.json` 的检查项包含 `Ctrl+X cut and Ctrl+Shift+Z redo shortcuts with pixel history restore`。

## 限制

Headless 检查不能替代 Windows 原生另存为文件夹选择器、键盘布局、IME 和系统剪贴板验收；`Ctrl+Shift+S` 的真实文件对话框仍属于 Windows 实机闸门。
