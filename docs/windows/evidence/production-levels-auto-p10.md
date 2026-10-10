# Production Windows M5 evidence — Levels histogram and automatic modes

## Scope

本条把 Mac 的 Levels 自动算法移植到 Windows 生产核心，并接入正式 Avalonia 窗口。直方图按未预乘 RGB 采样、透明度加权；选区模式再乘以 Gray8 选区覆盖率。自动模式包括共享端点的对比度、逐通道端点的颜色，以及逐通道中性中间调校准。空直方图保持 identity，不静默改变像素。

## Implementation

- `RasterCompositor.ComputeLevelsHistogram` 返回 RGB 与复合四组 256-bin 直方图，支持选区覆盖率。
- `LevelsSettings.FromHistogram` 使用 Mac 相同的 `0.1%` 端点阈值和三种自动模式。
- 平面 Levels 调整层可对调整层之前的合成结果采样；无组、无蒙版、无变换的平面栅格可对当前选区采样。
- 正式窗口新增根级和选区的“自动对比度 / 自动颜色 / 自动中性色”按钮；根级提交为一次 Levels 元数据历史，选区沿用预览/取消事务。

实现提交：`ea4adc5`

## Verification

固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj --no-restore -c Release
0 warnings / 0 errors
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj --no-restore -c Release
0 warnings / 0 errors
```

Workflow 输出目录 `/tmp/compositor-auto-levels-workflow-20261007-2`：

```text
PASS: Levels histogram coverage and contrast/color/neutral auto algorithms
```

Workflow 同时完成既有生产矩阵并退出 0。App 输出目录 `/tmp/compositor-auto-levels-app-20261007-4`：

```text
results.json: passed=true
checks=52
root Levels histogram-driven automatic contrast
selection Levels automatic color preview/cancel
```

App 实际点击根级自动对比度、选区自动颜色，验证直方图设置、预览不写入历史、取消恢复，以及完整正式窗口检查仍通过。

## Remaining gates

任意曲线控制点、多停靠点渐变、颜色范围/取样吸管、复杂组/蒙版/变换组合、全分辨率性能、GitHub CI、Windows 11 x64 真机启动、原生文件对话框、DPI、IME、剪贴板和安装包验收仍未完成。直方图当前是 Levels 采样基础，不代表完整调色面板或 M5 全量完成。
