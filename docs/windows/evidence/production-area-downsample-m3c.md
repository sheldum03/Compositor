# 生产面积下采样证据（M3c）

日期：2026-10-06

代码提交：`7f218d1`（`Add area downsampling for images and masks`）。

本轮只补齐尺寸缩小时的采样质量，不改变非破坏图层 transform。`EditorWorkspace.ResizeRaster` 和 `ResizeMask` 在宽、高都缩小时采用分离的水平/垂直面积覆盖：每个目标像素按源像素重叠面积加权，图像通道和 Gray8 蒙版分别取有界整数结果。上采样或只有一条轴缩小时继续使用既有双线性路径；Lanczos 尚未实现。

新增固定回归：

- 图像 128×128 下采样：从生产工作区快照计算 64,64 目标像素的面积覆盖参考，并与实际预览像素比较。
- Gray8 蒙版 128×128 下采样：用同一面积覆盖规则比较 64,64 蒙版覆盖值。

固定 SDK 与命令：

```sh
SDK_ROOT=/tmp/dotnet-sdk-root-401b
export DOTNET_ROOT="$SDK_ROOT"
export PATH="$SDK_ROOT:$PATH"
export NUGET_PACKAGES=/tmp/compositor-nuget-packages

dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-area-2
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-area-1
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-imaging-area-1
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-savecrash-area-1
```

结果：App Checks 3/3、Workflow 全部固定场景、Imaging 全部场景和 SaveCrash 14/14 均通过；Release 构建 0 警告、0 错误。交叉发布目录 `/tmp/compositor-win-x64-area-downsample-1791239602` 共 224 个文件，`Compositor.App.exe` SHA-256 为 `7e479befcda56ab0667d83a47409cfdac2050eed25700dc14c874c3767aa895e`。

限制：这些是 macOS arm64 上的交叉构建与生产核心/Headless 检查，尚未证明 Windows 11 的实际启动、DPI、文件对话框、原生 DLL 或硬件性能。面积路径也未替代 Lanczos 或长时间大图性能验收。
