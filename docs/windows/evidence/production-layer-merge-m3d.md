# 生产图层合并证据（M3d）

日期：2026-10-06

代码提交：`9779dee`（`Protect clipping stack movement controls`，基于 `9d37b32` 的剪贴栈移动实现，并包含 `33a38c8`/`e27a1bf` 的合并与外部关系防护）。

正式窗口新增并验证了三类受限合并：单层选中时合并当前层与下方一层；多选时仅开放连续同级平面层集合；连续剪贴栈在剪贴源与目标全部选中、无组、`Normal`、未变换且不存在外部关系时合成为单一平面层。合并前按文档顺序解析图层像素、图层不透明度、全画布 Gray8 蒙版和受限剪贴 Alpha，结果写回最低层并删除其余选中层。合并后将目标层不透明度归一为 1、混合模式归一为 `Normal`、清除剪贴关系并按源层可见性恢复最终可见状态；操作是一个工程历史步骤，支持 Undo/Redo 和保存重开。

按钮和核心层同时拒绝跳层选择、组工程、非 `Normal`、未烘焙变换、剪贴目标缺少源图层，以及剪贴源仍被未选中目标引用的情况；移动剪贴目标时，源和全部关联目标作为连续栈整体移动，栈不能越过画布边界。复杂组/跨层级剪贴栈和任意混合模式仍保持拒绝，避免把不完整渲染写回工程。

固定回归：

- 使用不同像素和不透明度的上下平面层，比较合并前后预览与预期 `Normal` 合成字节。
- 使用四个连续平面层验证多选合并、活动层/元数据、脏状态、Undo、Redo 和保存重开。
- 使用跳层多选验证窗口按钮保持禁用。
- 构造带 Alpha 和不透明度的连续剪贴源/目标，验证合并预览与原剪贴栈渲染逐字节一致，并验证剪贴关系清理、Undo/Redo、保存重开。
- 移动剪贴目标，验证源/目标连续栈整体移动、关系保持、预览一致、Undo/Redo 和保存重开。
- 固定 Workflow、Imaging 和 SaveCrash 检查继续通过。

固定命令：

```sh
SDK_ROOT=/tmp/dotnet-sdk-root-401b
export DOTNET_ROOT="$SDK_ROOT"
export PATH="$SDK_ROOT:$PATH"
export NUGET_PACKAGES=/tmp/compositor-nuget-packages

dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-clipmove-20261006-d
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-clipmove-20261006-c
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-imaging-clipmove-20261006-c
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-savecrash-clipmove-20261006-c
dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o /tmp/compositor-win-x64-clipmove-9779dee
```

结果：App Checks 3/3（含连续多选、跳层禁用、连续剪贴栈合并、外部剪贴关系防护和剪贴栈移动）、Workflow 全部固定场景、Imaging 全部场景和 SaveCrash 14/14 均通过；Release 构建与发布 0 警告、0 错误。最新交叉发布目录 `/tmp/compositor-win-x64-clipmove-9779dee` 共 224 个文件，`Compositor.App.exe` SHA-256 为 `e5a08f28d816bc5fec5a8c327ea0b128ec55c9b55a01faf3aaaa9dd5b498c177`。

限制：证据在 macOS Headless 和 macOS 交叉发布环境执行，不是 Windows 实机证据；目录不含 `compositor_native.dll`。Windows 11 启动、原生 DLL、文件对话框、DPI/IME、多显示器、真实压感及性能仍待验收。
