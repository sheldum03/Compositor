# Production Windows M5 evidence — selection RGB/R/G/B Curves filter transaction

## Scope

本条扩展选区 Curves 的通道选择：RGB 复合以及单独红、绿、蓝通道都使用三点曲线（暗部、中间调、高光），通过现有 Gray8 选区覆盖率混合回当前平面栅格。预览、取消、提交和撤销仍是同一像素事务；选区外像素保持不变。

## Implementation

- `MainWindow` 新增选区 Curves 通道选择器，预览期间与三个曲线参数一起锁定。
- `PreviewSelectionCurvesAsync` 根据 RGB/红/绿/蓝选择构造 `CurvesSettings`，复用现有 `RasterCompositor.ApplyCurves` 和选区事务。
- 任意控制点、直方图取样、组/蒙版/变换组合和 Windows 原生输入仍不在本条范围。

实现提交：`9a2d544`

## Verification

使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj --no-restore -c Release
0 warnings / 0 errors
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj --no-restore -c Release
0 warnings / 0 errors
```

Workflow 输出目录 `/tmp/compositor-selection-curves-channel-workflow-20261007-1`：

```text
PASS: selection Curves blends the filtered raster through coverage without changing outside pixels
PASS: selection red Curves changes only the selected channel through coverage
```

App 输出目录 `/tmp/compositor-selection-curves-channel-app-20261007-1`：

```text
results.json: passed=true
checks=50
selection RGB/R/G/B Curves preview/cancel/commit with undo
```

App 检查实际选择红通道并点击生产 Avalonia 窗口中的预览、取消、再次预览、提交和撤销，验证预览不改变 dirty/history、预览期间通道和参数控件锁定、提交/取消按钮边界、取消恢复原栅格、提交形成一次像素事务、撤销恢复源栅格。

## Remaining gates

GitHub CI、Windows 11 x64 真机启动、原生文件对话框、DPI、IME、剪贴板、性能和安装包验证尚未在本地证实；Windows 真机仍需使用用户提供的腾讯云服务器完成，CI 状态待网络恢复后回查。
