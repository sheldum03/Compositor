# 跨工程变换选区粘贴证据（M3f）

日期：2026-10-06

功能提交：`813d53e Support transformed cross-project selection paste`

## 已实现范围

- 可编辑、同画布尺寸的平面源工程在存在非恒等图层变换时，可以把选区复制到另一个工程。
- 跨工程粘贴会先调用正式图层可见结果渲染，将源像素、受限蒙版/剪贴关系和非破坏变换归一到文档坐标，再进入目标工程的浮动选区。
- 目标工程仍要求活动图层是未变换平面层；粘贴开始时目标源像素保持不变，提交后只产生一个目标像素历史步骤。
- 源图层的变换快照必须仍与当前工程一致；组工程、组图层、变换快照已失效和尺寸不一致会在粘贴前拒绝。
- 目标浮动选区可以取消而不修改工程，也可以提交、保存并重开，重开后的正式渲染与提交前预览逐 tile 一致。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-cross-transform-m3f-20261006-c
```

结果：Release 构建 0 警告、0 错误；三个生产检查全部 PASS。`CanvasChecks` 覆盖非恒等位移源图层、文档坐标选区、目标浮动选区的非破坏开始、目标原像素不变、提交历史、保存和重开渲染一致。

同一固定源码随后复跑 `Compositor.Workflow.Checks`、`Compositor.Imaging.Checks` 和 `Compositor.SaveCrash.Checks`，均为 Release 构建 0 警告、0 错误；Workflow、Imaging 全部场景及 SaveCrash 的 14 个真实保存中断场景均 PASS。它们仍是 macOS Headless 结果。

该证据关闭了“跨工程选区粘贴遇到变换源即拒绝”的受限切片；跨变换快照的同工程浮动选区仍要求同一快照，复杂目标组层级、移动而非复制、Windows 原生剪贴板和 Windows 实机验收继续按计划追踪。
