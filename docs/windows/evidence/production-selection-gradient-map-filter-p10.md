# Production Windows M5 evidence — bounded selection RGB Gradient Map filter transaction

## Scope

本条只覆盖无组平面栅格图层上的选区 Gradient Map 破坏性滤镜事务。首版使用两个 RGB 端点：暗部 RGB 与高光 RGB；通过现有选区覆盖率把过滤后的栅格混合回选区，选区外保持原像素。

## Implementation

- `EditorWorkspace.PreviewGradientMapFilter` 复用现有选区预览、提交、取消和撤销事务。
- `MainWindow` 暴露六个 RGB 参数控件；预览期间参数和预览按钮锁定，提交/取消按钮按事务状态启用。
- 本条不扩展多停止点、颜色空间选择、组/蒙版/变换图层和 Windows 原生对话框；这些仍属于后续边界。

实现提交：`04dbcc4`

## Verification

使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj --no-restore -c Release
0 warnings / 0 errors
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj --no-restore -c Release
0 warnings / 0 errors
```

Workflow 输出目录 `/tmp/compositor-selection-gradient-map-workflow-20261007-5`：

```text
PASS: selection Gradient Map blends the filtered raster through coverage without changing outside pixels
```

App 输出目录 `/tmp/compositor-selection-gradient-map-app-20261007-2`：

```text
results.json: passed=true
checks=49
selection RGB Gradient Map preview/cancel/commit with undo
```

App 检查实际点击生产 Avalonia 窗口中的预览、取消、再次预览、提交和撤销，验证预览不改变 dirty/history、预览期间六个参数控件锁定、提交/取消按钮边界、取消恢复原栅格、提交形成一次像素事务、撤销恢复源栅格。

## Remaining gates

GitHub CI、Windows 11 x64 真机启动、原生文件对话框、DPI、IME、剪贴板、性能和安装包验证尚未在本地证实。Windows 真机仍需使用用户提供的腾讯云服务器完成；CI 状态待网络恢复后回查。
