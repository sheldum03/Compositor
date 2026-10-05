# 生产选区羽化证据（M3d）

日期：2026-10-06

代码提交：`36d9cf8`（`Add selection feathering`）。

正式窗口新增羽化半径控件和“羽化选区”命令。羽化只改变会话内 Gray8 选区，不写入 manifest、不标脏；选区历史可独立 Undo/Redo。实现复用 Gray8 有界均值滤波，半径范围为 1–200 像素，生成部分覆盖边缘，随后仍可用于画笔裁剪、像素移动和蒙版编辑。

固定回归：

- 在正式 Avalonia Headless 窗口通过真实矩形选区启用羽化控件，半径 3 产生部分覆盖且工程保持未修改。
- 在无像素事务的会话中验证羽化前后选区、Undo、Redo 和保存脏状态。
- 固定窗口操作、Workflow、Imaging 与 SaveCrash 检查全部通过。

固定命令：

```sh
SDK_ROOT=/tmp/dotnet-sdk-root-401b
export DOTNET_ROOT="$SDK_ROOT"
export PATH="$SDK_ROOT:$PATH"
export NUGET_PACKAGES=/tmp/compositor-nuget-packages

dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-feather-3
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-feather-1
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-imaging-feather-1
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-restore -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-savecrash-feather-1
dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o /tmp/compositor-win-x64-feather-1791240446
```

结果：App Checks 3/3、Workflow 全部固定场景、Imaging 全部场景和 SaveCrash 14/14 均通过；Release 构建与发布 0 警告、0 错误。最新交叉发布目录 `/tmp/compositor-win-x64-feather-1791240446` 共 224 个文件，`Compositor.App.exe` SHA-256 为 `83aaeb34257171672c19bea32f75be1f1743c46c66c49bc037c2db4e6f47620a`。

限制：这是 CPU Gray8 会话选区羽化切片，当前滤波是有界均值近似，不等于完整生产级高斯/可视化羽化预览；尚未在 Windows 11 实机验证性能、DPI、IME 或真实压感输入。
