# Production Windows M5 evidence — selection RGB/R/G/B Levels filter transaction

## Scope

本条扩展选区 Levels 的通道选择：RGB 复合以及单独红、绿、蓝通道都使用输入黑/白、伽马、输出黑/白参数，通过现有 Gray8 选区覆盖率混合回当前平面栅格。预览、取消、提交和撤销仍是同一像素事务；选区外像素保持不变。

## Implementation

- `MainWindow` 新增根级和选区 Levels 通道选择器。
- 根级应用保留当前调整层的其他通道设置，只替换选中的 RGB/R/G/B 范围。
- 选区预览按通道构造 `LevelsSettings`，复用现有预览事务。
- 自动色阶、直方图取样、组/蒙版/变换组合和 Windows 原生输入仍不在本条范围。

实现提交：`1942afe`

## Verification

使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj --no-restore -c Release
0 warnings / 0 errors
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj --no-restore -c Release
0 warnings / 0 errors
```

Workflow 输出目录 `/tmp/compositor-selection-levels-channel-workflow-20261007-1`：

```text
PASS: selection Levels blends the filtered raster through coverage without changing outside pixels
PASS: selection red Levels changes only the selected channel through coverage
```

App 输出目录 `/tmp/compositor-selection-levels-channel-app-20261007-1`：

```text
results.json: passed=true
checks=50
selection RGB/R/G/B Levels preview/cancel/commit with undo
```

App 检查实际选择红通道并点击生产 Avalonia 窗口中的预览、取消、再次预览、提交和撤销，验证预览不改变 dirty/history、预览期间通道和参数控件锁定、提交/取消按钮边界、取消恢复原栅格、提交形成一次像素事务、撤销恢复源栅格。

## Remaining gates

自动色阶、直方图取样、GitHub CI、Windows 11 x64 真机启动、原生文件对话框、DPI、IME、剪贴板、性能和安装包验证尚未在本地证实；Windows 真机仍需使用用户提供的腾讯云服务器完成，CI 状态待网络恢复后回查。
