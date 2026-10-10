# 生产画笔与蒙版增强证据（M3d）

日期：2026-10-06

代码提交：`59c233d`（`Add straight brush and mask fill commands`）。

本轮补齐 W-019 的三个可验证缺口：

- 画布真实指针按住 Shift 拖动时，按起点将笔划约束为水平或垂直直线；约束仍在临时笔划中，抬笔只提交一个历史步骤。
- 当前图层 Gray8 蒙版增加反相命令。
- 当前图层 Gray8 蒙版增加填白、填黑命令；三个命令都复用已有编辑事务和 Undo/Redo，不直接改动源文件。

固定回归包含真实 Avalonia Headless 指针序列：Shift 拖动后检查提交像素变化位于水平笔划带内，并撤销恢复原有变换/笔刷场景；蒙版场景分别执行反相、撤销、重做、填黑、撤销、填白、撤销，并检查覆盖值和保存点。

固定 SDK 与命令：

```sh
SDK_ROOT=/tmp/dotnet-sdk-root-401b
export DOTNET_ROOT="$SDK_ROOT"
export PATH="$SDK_ROOT:$PATH"
export NUGET_PACKAGES=/tmp/compositor-nuget-packages

dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-mask-straight-12
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-mask-straight-1
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-imaging-mask-straight-1
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-savecrash-mask-straight-1
```

结果：App Checks 3/3、Workflow 全部固定场景、Imaging 全部场景和 SaveCrash 14/14 均通过；Release 构建 0 警告、0 错误。

限制：本证据在 macOS arm64 的 Avalonia Headless 和生产工作流中执行，不能证明 Windows 11 的原生笔输入、真实压力曲线、DPI、文件对话框或性能。蒙版羽化/模糊仍未实现，完整 S02/S05 仍未验收。
