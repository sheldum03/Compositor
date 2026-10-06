# 跨工程图层拖放证据（M3f）

日期：2026-10-06

功能提交：`ff8e405 Support cross-project layer drag copy`

## 已实现范围

- 在同窗多工程标签中，将图层列表项目拖到另一工程标签会复制图层；源工程保持不变，目标工程把副本插入当前活动图层上方。
- 目标工程保留源图层的像素、蒙版、启停状态、透明度、混合模式和非破坏变换；目标复制作为一个历史步骤，可撤销/重做并保存重开。
- 仅允许两个可编辑、同画布尺寸、无组工程；源图层不能处于剪贴关系中，也不能带依赖它的剪贴子层。组、部分剪贴栈和尺寸不一致会在拖放前禁用并拒绝。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-cross-layer-m3f-20261006-i
```

结果：Release 构建 0 警告、0 错误；三个生产检查全部 PASS。固定检查覆盖 API 复制、像素与 Gray8 蒙版逐 tile 一致、opacity/blendMode/transform 保留、源工程未变、目标 Undo/Redo、保存重开、尺寸不一致拒绝、部分剪贴栈拒绝，以及真实窗口从图层列表拖到另一工程标签后的复制和目标 Undo/Redo。

该证据关闭“受限跨工程图层拖放复制”切片；组/复杂剪贴栈、跨变换快照语义、移动而非复制、Windows 原生拖放与实机验收继续按计划追踪。
