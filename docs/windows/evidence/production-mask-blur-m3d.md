# 生产蒙版模糊证据（M3d）

日期：2026-10-06

代码提交：`3995d41`（`Add mask blur editing`）。

`GrayTileRaster.Blur` 使用水平和垂直两个滑动窗口对 Gray8 覆盖做有界均值，避免按半径重复扫描整幅图。`EditorWorkspace.BlurActiveLayerMask` 将结果交给已有 `ReplaceLayerMask` 事务；窗口提供 1–200 半径控件和“模糊蒙版”按钮。模糊输出保持同一画布尺寸，边缘覆盖产生部分值，支持 Undo/Redo。

固定回归：

- 对生产蒙版执行半径 3 模糊，检查输出存在部分覆盖值。
- 撤销后检查保存点和原始蒙版恢复。
- 窗口检查确认“模糊蒙版”和半径控件在有蒙版图层上可用。

固定命令：

```sh
SDK_ROOT=/tmp/dotnet-sdk-root-401b
export DOTNET_ROOT="$SDK_ROOT"
export PATH="$SDK_ROOT:$PATH"
export NUGET_PACKAGES=/tmp/compositor-nuget-packages

dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-mask-blur-1
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-mask-blur-1
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-imaging-mask-blur-1
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-savecrash-mask-blur-1
```

结果：App Checks 3/3、Workflow 全部固定场景、Imaging 全部场景和 SaveCrash 14/14 均通过；Release 构建 0 警告、0 错误。最新交叉发布目录 `/tmp/compositor-win-x64-mask-blur-1791239604` 共 224 个文件，`Compositor.App.exe` SHA-256 为 `7b475bd92c69e06d00326036fd1832cf31a4f7d548467f91dbf5c4e8c7ca0d25`。

限制：这是 CPU Gray8 蒙版模糊的生产切片，不等于羽化语义；尚未在 Windows 11 实机验证性能、DPI 或真实压感输入，也未完成全分辨率大图压力门槛。
