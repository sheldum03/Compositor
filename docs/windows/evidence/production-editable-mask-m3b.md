# M3b 全画布 Gray8 蒙版编辑切片

更新日期：2026-10-06。

本轮在 `codex/windows-implementation` 工作树提交 `07dd00a`，以 macOS arm64、.NET SDK 10.0.401、Release 配置验证 M3b 的第一条可交付切片。实现范围是 v8 平面图层的全画布 Gray8 栅格蒙版：载入、启用/停用、选区显示/隐藏、蒙版笔刷显示/隐藏、选区裁剪、蒙版历史、保存/重开，以及画布尺寸变化、90°文档旋转、图层翻转和整数位移时的蒙版同步。蒙版以 `<layer-id>.mask.png` 保存，并核验 8-bit grayscale、无 alpha、尺寸等于画布。`maskSourceID`、组/剪贴蒙版、调整层蒙版、任意位置蒙版仍被拒绝或保留为后续范围；Avalonia Pen 压力已传递到软/硬笔和蒙版笔刷，鼠标输入规范化为压力 1，真实压感设备仍待验收。

## 验证结果

- `Compositor.Workflow.Checks` Release 构建：0 警告、0 错误；13 个工作流场景全部通过。新增场景覆盖单图层和多图层可编辑全画布蒙版、选区修改、启停、保存重开、蒙版像素及合成结果、Undo/Redo，以及错误尺寸/错误 PNG 类型拒绝。
- `Compositor.App.Checks` Release 构建：0 警告、0 错误；无原生选择库的 Headless Avalonia 检查全部通过。窗口层覆盖蒙版按钮和蒙版笔刷控件启用状态、选区显示/隐藏、启停、保存重开、PNG 导出，以及图层位移/翻转与蒙版同步、蒙版笔刷选区裁剪、压力覆盖和单步 Undo/Redo。
- `Compositor.Imaging.Checks` Release 构建：0 警告、0 错误；Gray8 蒙版覆盖、PNG 往返和既有图像 IO 检查全部通过。
- `Compositor.SaveCrash.Checks` Release 构建：0 警告、0 错误；14 个真实子进程保存中断场景全部通过，确认本轮保存路径没有破坏既有单层/多层恢复保护。
- `win-x64` self-contained 发布成功，产物目录为 `/tmp/compositor-win-x64-pressure-07dd00a-v2`，224 个文件；`Compositor.App.exe` SHA-256 为 `6b2eb8ff60409c52309f226edc9d81d542da34a67a6467cfdadf0a1a5ab53c8f`。这只证明发布链路完成，不证明 Windows 启动或真机兼容。

## 本轮命令

命令均在仓库根目录执行；本机使用临时解压的 .NET SDK 10.0.401，等价的正式 SDK 路径可直接替换 `DOTNET_ROOT` 和 `PATH`。

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <new-output>
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <new-output>
dotnet build windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <new-output>
dotnet build windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <new-output>
dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o <new-output>
```

上述结果仍是本地 Headless/发布证据。Windows 11 x64 真机、Tencent Cloud 服务器启动、原生文件对话框、DPI/IME、多显示器、性能和安装器尚未验收；因此本轮不关闭 M3b/Alpha，也不把便携包称为 Windows 可发布版本。
