# 生产图层向下合并证据（M3d）

日期：2026-10-06

代码提交：`26bd21e`（`Add restricted layer merge down`）。

正式窗口新增“向下合并”。当前切片只对无组、无剪贴关系、无变换且上下两层均为 `Normal` 的平面 v8 工程开放；上下层的不透明度和全画布 Gray8 蒙版先进入同一栅格合成，再把结果写回下层并删除上层。操作是一个工程历史步骤，支持 Undo/Redo 和保存重开；不满足边界的语义由按钮禁用或明确拒绝。

固定回归：

- 使用不同像素的上下平面层，比较合并前后预览与预期 `Normal` 合成字节。
- 验证合并后的活动层、脏状态、Undo、Redo 和保存重开。
- 窗口检查确认符合边界的 Normal 平面层开放向下合并。
- 固定 Workflow、Imaging 和 SaveCrash 检查继续通过。

固定命令：

```sh
SDK_ROOT=/tmp/dotnet-sdk-root-401b
export DOTNET_ROOT="$SDK_ROOT"
export PATH="$SDK_ROOT:$PATH"
export NUGET_PACKAGES=/tmp/compositor-nuget-packages

dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-merge-1
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-merge-1
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-imaging-merge-1
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-savecrash-merge-1
dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o /tmp/compositor-win-x64-merge-1791240698
```

结果：App Checks 3/3、Workflow 全部固定场景、Imaging 全部场景和 SaveCrash 14/14 均通过；Release 构建与发布 0 警告、0 错误。最新交叉发布目录 `/tmp/compositor-win-x64-merge-1791240698` 共 224 个文件，`Compositor.App.exe` SHA-256 为 `df35e26e1f10b2c54f26b33eea363b48785fb482f45b5ffb8fdbf60c69bdf81c`。

限制：这不是完整的任意混合模式、多选层或组/剪贴栈合并；复杂语义仍保持拒绝，Windows 11 实机、原生 DLL、DPI 和性能尚未验证。
