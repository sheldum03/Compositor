# 跨工程图层拖放证据（M3f）

日期：2026-10-06

功能提交：`90b99ef Support restricted cross-project group copy`、`3c68c54 Cover group drag copy in production window`、`7d9ca2f Cover group clipping relationship remap`（基础跨工程图层复制由 `ff8e405` 接入）

## 已实现范围

- 在同窗多工程标签中，将图层列表项目拖到另一工程标签会复制图层；源工程保持不变，目标工程把副本插入当前活动图层上方。
- 单层复制保留像素、蒙版、启停状态、透明度、混合模式和非破坏变换；目标复制作为一个历史步骤，可撤销/重做并保存重开。
- 完整且连续的平面剪贴栈可以整体复制。实现会为栈内图层分配新 ID，重映射 `maskSourceID`，复制各层像素和蒙版资产，并在保存重开后保持栈关系。
- 根组可以整体复制到平面目标工程，保留组/嵌套子层的 `parentID` 关系、组 transform、组蒙版和子层像素；正式窗口已覆盖从图层列表拖到另一工程标签。
- 目标工程必须是可编辑、同画布尺寸的平面 v8 工程；源工程的根组只能整体复制，组内单独栅格层、外部父级/剪贴关系、非连续关系、尺寸不一致及缺失蒙版资源会在拖放前禁用并拒绝。移动而非复制、目标已有组、跨变换快照语义及 Windows 原生实机拖放仍待后续验收。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-group-clip-drag-m3f-20261006-b
```

结果：Release 构建 0 警告、0 错误；三个生产检查全部 PASS。固定检查覆盖单层 API 复制、像素与 Gray8 蒙版逐 tile 一致、opacity/blendMode/transform 保留、源工程未变、目标 Undo/Redo、保存重开、尺寸不一致拒绝；同时覆盖完整连续剪贴栈的两层复制、ID/剪贴关系重映射、两层像素保持和保存重开，以及根组子树的组 transform、组蒙版、父子关系、组内 `maskSourceID` 重映射、子层像素和保存重开；正式窗口检查覆盖单层和根组从图层列表拖到另一工程标签后的复制。

同一提交随后复跑 `Compositor.Workflow.Checks`、`Compositor.Imaging.Checks` 和 `Compositor.SaveCrash.Checks`，固定 Release 构建均为 0 警告、0 错误；Workflow 全部固定场景、Imaging 全部场景及 SaveCrash 的 14 个真实保存中断场景均 PASS。它们仍是 macOS Headless 结果。

该证据把 M3f 从“部分剪贴栈拒绝”推进到“完整连续平面剪贴栈和受限根组子树可复制”切片；目标已有组、复杂/非连续剪贴关系、移动而非复制、Windows 原生拖放和 Windows 实机验收继续按计划追踪。

## 产物追踪

在同一固定 SDK 下，`win-x64` self-contained 发布成功：

- 目录：`/tmp/compositor-win-x64-group-drag-7d9ca2f`
- 文件数：224
- `Compositor.App.exe` SHA-256：`bf027be47860e55cf89f034578836323b22acc88222fa4bcc4aea05957e3ea6e`
- 未包含真实 `compositor_native.dll`；该包不能作为 Windows 实机或完整 Alpha 证据。
