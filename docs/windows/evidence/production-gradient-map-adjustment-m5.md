# Production Windows M5 evidence — bounded Gradient Map adjustment layer

日期：2026-10-07  
实现提交：`f4f26cd`  
关联计划：`windows-part`

## 已交付

- 在现有 `.comp` v8 平面工程中新增 `Gradient Map` 调整层元数据，保存为 `adjustment.kind = "Gradient Map"` 与 `gradientMapSettings`。
- 当前切片提供两个 RGB 端点：暗部和高光各有 R/G/B `0..255` 参数，按源像素的 sRGB 相对亮度在两个端点之间线性插值。
- 渲染使用 premultiplied RGBA 的 alpha 安全解包/回写；透明度保持不变，调整层仍只允许 Normal 混合。
- 调整层支持预览、修改、撤销/重做、保存和重新打开；正式窗口提供六个 RGB 数值控件和应用按钮。
- 含组、蒙版、剪贴、变换、复制、合并、非 Normal 混合、多停靠点渐变和选区预览的组合仍明确拒绝或保留到后续切片。

## 验证

本地使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
```

Workflow 检查包含 `Gradient Map adjustment layer metadata, premultiplied pixels, history and save/reopen`；App 检查实际点击新增/应用按钮、修改暗部/高光六个 RGB 参数、比对预览像素并验证撤销。GitHub Actions PR run [37520992188](https://github.com/sheldum03/Compositor/actions/runs/37520992188) 的 Smoke、Imaging、Workflow、SaveCrash、App 五个生产检查全部通过。

## 边界

当前切片只覆盖两个 RGB 端点和 Normal 调整层。多停靠点渐变、颜色空间选择、选区预览、预览取消/提交事务以及组/剪贴组合仍在 W-026 后续切片；含这些未实现语义的工程不会被静默降级为普通栅格层。
