# 生产图层合并证据（M3d）

日期：2026-10-06

代码提交：`ca489fa`（`Support contiguous multi-layer merge`，基于 `cf237fb` 的受限平面层合并实现）。

正式窗口新增“向下合并”。当前切片对无组、无剪贴关系、无变换且均为 `Normal` 的连续平面 v8 图层开放；单层选中时合并当前层与下方一层，多选时仅开放连续图层集合。各层不透明度和全画布 Gray8 蒙版先按文档顺序进入同一栅格合成，再把结果写回最低层并删除其余选中层。操作是一个工程历史步骤，支持 Undo/Redo 和保存重开；合并后将目标层不透明度归一为 1、混合模式归一为 Normal，并按源层可见性恢复最终可见状态；跳层选择、组、剪贴关系、非 Normal 和未烘焙变换由按钮禁用或明确拒绝。

固定回归：

- 使用不同像素和不透明度的上下平面层，比较合并前后预览与预期 `Normal` 合成字节。
- 使用四个连续平面层验证多选合并、合并后的活动层/元数据、脏状态、Undo、Redo 和保存重开。
- 使用跳层多选验证窗口按钮保持禁用。
- 固定 Workflow、Imaging 和 SaveCrash 检查继续通过。

固定命令：

```sh
SDK_ROOT=/tmp/dotnet-sdk-root-401b
export DOTNET_ROOT="$SDK_ROOT"
export PATH="$SDK_ROOT:$PATH"
export NUGET_PACKAGES=/tmp/compositor-nuget-packages

dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-multi-3
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-multi-1
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-imaging-multi-1
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-savecrash-multi-1
dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o /tmp/compositor-win-x64-multimerge-1
```

结果：App Checks 3/3（含连续多选与跳层禁用）、Workflow 全部固定场景、Imaging 全部场景和 SaveCrash 14/14 均通过；Release 构建与发布 0 警告、0 错误。最新交叉发布目录 `/tmp/compositor-win-x64-multimerge-1` 共 224 个文件，`Compositor.App.exe` SHA-256 为 `2c007911fa57c37670d6bde24b0de1067e5ac36f258f6546c262998b603d9694`。

限制：这不是完整的任意混合模式或组/剪贴栈合并；当前多选仅支持连续平面 Normal 层，复杂语义仍保持拒绝，Windows 11 实机、原生 DLL、DPI 和性能尚未验证。
