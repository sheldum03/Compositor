# 生产反相图层子切片（2026-10-07）

## 结论

`0834eda` 为 Windows Avalonia 生产窗口增加“反相图层”命令。命令只对活动栅格图层启用，保持 8-bit 预乘 RGBA 的 Alpha 不变，并把反相作为一个像素历史事务提交；组图层、文字图层和浮动选区不会误用该命令。

## 验证

固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet` 下完成：

```text
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-invert-20261007-a
```

结果：Release 构建 0 警告、0 错误；App Checks 通过。窗口检查按实际按钮执行图层反相，核对 `alpha` 保持不变、预乘颜色满足 `output = alpha - input`，并核对 Undo/Redo 恢复和重放像素事务。已有四项 Core/Imaging/Workflow/SaveCrash 回归也通过：

- [push production-core run 37506392315](https://github.com/sheldum03/Compositor/actions/runs/37506392315)
- [PR production-core run 37506399641](https://github.com/sheldum03/Compositor/actions/runs/37506399641)

## 范围与边界

这只关闭 P-10/W-026 中的“反相”基础像素切片；它是破坏性图层操作，不代表调整层、预览取消、色相/饱和度、曲线、曝光、渐变映射、颗粒或滤镜已经交付。文字图层仍由文字渲染器维护，不允许直接改写其像素。Windows 实机、原生文件对话框、DPI/IME、安装和完整 M5 验收仍开放。
