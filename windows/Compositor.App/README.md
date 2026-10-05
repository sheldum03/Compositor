# Compositor Windows 生产入口

Avalonia 11.3.22 / .NET 10.0.401，使用已选定的软件绘制路线。此项目引用正式 Core/Imaging；原型代码不进入应用。当前是内部集成窗口，尚未达到 M3 Alpha。

```sh
cd windows
dotnet restore Compositor.App/Compositor.App.csproj --locked-mode
dotnet run --project Compositor.App -c Release --no-restore
```

当前操作：选择 `.comp` 文件夹打开工程；PNG/JPEG 导入为新工程；平面全画布 Normal 图层的改名、显隐、上下移动、撤销/重做；保存、另存为新 `.comp` 文件夹；透明 PNG 或白底/质量 95 的 JPEG 导出。预览和导出使用同一核心合成结果，像素复制到 Avalonia 的预乘 RGBA bitmap。保存/读写/合成在后台执行，期间禁用编辑，避免重叠操作。

关闭或替换有修改的工程时提供保存、不保存、取消；保存失败保持原文档和窗口。打开失败不替换当前会话。另存和导出目前要求新路径，拒绝覆盖已有其他工程/文件。Ctrl+O/S/Z/Y 可用于工程操作；名称输入框保留自己的文本快捷键。

含组、变换、非默认透明度/混合、蒙版等尚不可编辑的工程会明确拒绝打开编辑。该入口尚无新建空画布、项目标签、画布工具/缩放平移、文字/字体编辑、完整快捷键、恢复界面或安装器；不得作为 Alpha/Beta 发布。Windows 原生窗口、文件对话框、IME/DPI、设备及性能验证尚未完成。

窗口检查单独位于 `Compositor.App.Checks`，Headless 不进入生产项目依赖：

```sh
dotnet restore Compositor.App.Checks/Compositor.App.Checks.csproj --locked-mode
dotnet run --project Compositor.App.Checks -c Release --no-restore -- Compositor.Imaging.Checks/fixtures <新的输出目录>
```

检查会真实构造窗口控件、点击命令与确认对话框，验证预览字节、历史、脏状态、失败保存及关闭保护，并生成窗口 PNG。Headless 通过不等同于原生 Windows 验收。思源黑体字体及 OFL 许可证随应用输出；完整第三方发行清单仍按 M7 门槛追踪。
