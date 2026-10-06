# Production Windows M5 evidence — RGB Levels adjustment layer

日期：2026-10-07  
实现提交：`1c572b6`  
关联计划：`windows-part` 分支 `09f9932`

## 已交付

- 在现有 `.comp` v8 平面工程中新增 `Levels` 调整层元数据，保存为 `adjustment.kind = "Levels"` 与 `levelsSettings`。
- 色阶范围遵循 macOS 基线：输入黑场 `0..254`、输入白场大于黑场且不超过 `255`、伽马 `0.1..9.99`、输出黑白场 `0..255`。
- 渲染使用 premultiplied RGBA 的 alpha 安全查表插值；RGB 复合范围已接通，透明度保持不变。
- 调整层支持预览、修改、撤销/重做、保存和重新打开；UI 提供新增色阶调整及五个 RGB 复合参数。
- 调整层继续只允许 Normal 混合，并明确禁止直接像素编辑、蒙版、剪贴、复制、分组和变换等未覆盖语义。

## 验证

本地使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
```

Workflow 检查包含 `Levels adjustment layer metadata, premultiplied pixels, history and save/reopen`；App 检查实际点击新增/应用色阶按钮、修改五个参数、比对预览像素并验证撤销。GitHub Actions run `37514865824` 的 Smoke、Imaging、Workflow、SaveCrash、App 五个生产检查全部通过。

## 边界

当前切片只暴露 macOS 默认 RGB 复合色阶。单独 R/G/B 通道编辑、自动色阶、直方图采样、选区预览、曲线和 Levels 与其他调整层组合仍在 W-026 后续切片；含这些未实现语义的工程不会被静默降级为普通栅格层。
