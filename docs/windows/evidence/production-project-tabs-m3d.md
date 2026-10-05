# 生产多项目标签证据（M3d）

日期：2026-10-06

代码提交：`3a57359`（`Verify project tab history isolation`，基于 `b8c6db0` 的多项目标签实现）。

本轮补齐 W-016/P-01 的窗口内多项目工作区切片：

- 主窗口维护多个 `EditorWorkspace`，每个标签保留自己的会话、活动图层、预览、选区和脏状态。
- 打开/导入在当前已有工程时创建新标签；空白窗口仍复用当前空工作区。
- 标签切换重新绑定画布和图层面板，关闭标签先处理当前工程未保存状态；窗口关闭会逐个处理所有脏工程。
- 当前最后一个标签关闭后保留一个空白工作区，避免窗口进入无效状态。
- 每个项目维护独立历史；在第二个项目改名后撤销，只回退第二个项目，切回第一个项目时其图层和脏状态保持不变。

固定回归在生产 Avalonia Headless 窗口中创建两个已保存 `EditorWorkspace`，通过实际标签 API 切换、在第二个项目执行改名/撤销并核对两个会话的身份/图层/历史状态，再点击“关闭项目”关闭活动标签，确认剩余工程仍为第一工作区。App Checks 同时覆盖新建/保存/关闭保护、笔刷、选区和组控件，未发现标签切片引入的回归。

固定命令：

```sh
SDK_ROOT=/tmp/dotnet-sdk-root-401b
export DOTNET_ROOT="$SDK_ROOT"
export PATH="$SDK_ROOT:$PATH"
export NUGET_PACKAGES=/tmp/compositor-nuget-packages

dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-tabs-20261006-a
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-multi-1
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-imaging-multi-1
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-savecrash-multi-1
```

结果：App Checks 3/3（包含项目标签历史隔离）、Workflow 全部固定场景、Imaging 全部场景和 SaveCrash 14/14 均通过；Release 构建 0 警告、0 错误。

限制：原生文件夹/文件选择器、真实 Windows 多窗口 DPI、拖放和系统剪贴板尚未在 Windows 11 实机验证；本证据不代表完整 P-01 或 Alpha 已完成。
