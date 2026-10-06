# 跨工程图层拖放证据（M3f）

日期：2026-10-06

功能提交：`996cc57 Support cross-project clipping stack copy`（基础跨工程图层复制由 `ff8e405` 接入）

## 已实现范围

- 在同窗多工程标签中，将图层列表项目拖到另一工程标签会复制图层；源工程保持不变，目标工程把副本插入当前活动图层上方。
- 单层复制保留像素、蒙版、启停状态、透明度、混合模式和非破坏变换；目标复制作为一个历史步骤，可撤销/重做并保存重开。
- 完整且连续的平面剪贴栈可以整体复制。实现会为栈内图层分配新 ID，重映射 `maskSourceID`，复制各层像素和蒙版资产，并在保存重开后保持栈关系。
- 仅允许两个可编辑、同画布尺寸、无组工程；组、非连续或缺少成员的剪贴关系、尺寸不一致及缺失蒙版资源会在拖放前禁用并拒绝。跨变换快照语义、移动而非复制及 Windows 原生实机拖放仍待后续验收。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-cross-layer-stack-m3f-20261006-a
```

结果：Release 构建 0 警告、0 错误；三个生产检查全部 PASS。固定检查覆盖单层 API 复制、像素与 Gray8 蒙版逐 tile 一致、opacity/blendMode/transform 保留、源工程未变、目标 Undo/Redo、保存重开、尺寸不一致拒绝；同时覆盖完整连续剪贴栈的两层复制、ID/剪贴关系重映射、两层像素保持和保存重开，并保留真实窗口从图层列表拖到另一工程标签后的复制与目标 Undo/Redo。

该证据把 M3f 从“部分剪贴栈拒绝”推进到“完整连续平面剪贴栈可复制”切片；组、复杂/非连续剪贴关系、跨变换快照语义、移动而非复制、Windows 原生拖放和 Windows 实机验收继续按计划追踪。

## 产物追踪

在同一固定 SDK 下，`win-x64` self-contained 发布成功：

- 目录：`/tmp/compositor-win-x64-cross-layer-stack-996cc57`
- 文件数：224
- `Compositor.App.exe` SHA-256：`7eecceb4813251e8a7e9a0596130616da80b70065d4edd974c4a73614cfc8559`
- 未包含真实 `compositor_native.dll`；该包不能作为 Windows 实机或完整 Alpha 证据。
