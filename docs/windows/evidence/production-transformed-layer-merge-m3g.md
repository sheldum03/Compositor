# 变换平面层合并证据（M3g）

日期：2026-10-06

功能提交：`e865e48 Support transformed flat-layer merge`

## 已实现范围

- 同级、连续、无组的平面图层即使带有非恒等位置、缩放、旋转或翻转，也可以通过正式渲染器合并。
- 合并前按文档坐标渲染每层及其透明度、混合模式和受限蒙版/剪贴关系，合并后写入画布尺寸的可见结果。
- 输出层统一为默认 `Normal`、100% 透明度、恒等 transform；操作支持 Undo/Redo 和保存重开。
- 组、复杂剪贴栈、跨变换快照浮动选区及 Windows 实机验收仍按计划追踪。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages

dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-transformed-merge-m3g-20261006-a
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-transformed-merge-m3g-20261006-a
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-restore -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-imaging-transformed-merge-m3g-20261006-a
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-restore -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-savecrash-transformed-merge-m3g-20261006-a
dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release -r win-x64 \
  --self-contained true --no-restore -o /tmp/compositor-win-x64-transformed-merge-e865e48
```

结果：App Checks 3/3、Workflow 全部固定场景、Imaging 全部场景和 SaveCrash 14/14 均 PASS；Release 构建与 `win-x64` self-contained 发布均为 0 警告、0 错误。新增窗口场景验证两个带位置、缩放和旋转的平面层合并前后预览逐 tile 一致、transform 归一化、Undo/Redo 和保存重开。

发布产物为 224 个文件，入口 `Compositor.App.exe` SHA-256 为 `89925c808e00c074f78ee79ed0356aae2b7d2a370dc52ebb5a9010486f96579b`，目录为 `/tmp/compositor-win-x64-transformed-merge-e865e48`。这是 macOS Headless/交叉发布证据，不是 Windows 实机证据，目录不含真实 `compositor_native.dll`。
