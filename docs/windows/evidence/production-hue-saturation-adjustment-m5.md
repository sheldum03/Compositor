# Production Windows M5 evidence — master Hue/Saturation adjustment layer

日期：2026-10-07  
实现提交：`4212bc3`  
关联计划：`windows-part`

## 已交付

- 在现有 `.comp` v8 平面工程中新增 `Hue/Saturation` 调整层元数据，保存为 `adjustment.kind = "Hue/Saturation"` 与 `hueSaturationSettings`。
- 参数遵循 macOS 主范围语义：色相 `-360..360` 度、饱和度 `-100..100`、明度 `-100..100`，并支持主范围着色模式。
- 渲染在 sRGB 颜色空间执行 HSL 转换，使用 premultiplied RGBA 的 alpha 安全解包/回写；透明度保持不变。
- 调整层支持预览、修改、撤销/重做、保存和重新打开；正式窗口提供新增调整、色相/饱和度/明度/着色控件。
- 调整层继续只允许 Normal 混合，并明确禁止直接像素编辑、蒙版、剪贴、复制、分组和变换等未覆盖语义。

## 验证

本地使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
```

Workflow 检查包含 `Hue/Saturation adjustment layer metadata, premultiplied pixels, history and save/reopen`；App 检查实际点击新增/应用按钮、修改三个数值参数、比对预览像素并验证撤销。GitHub Actions PR run `37516667409` 的 Smoke、Imaging、Workflow、SaveCrash、App 五个生产检查全部通过。

## 边界

当前切片只暴露 macOS 主范围调整和着色模式。红/黄/绿/青/蓝/洋红颜色范围、范围手工边界、取样吸管、选区预览和调整层组合仍在 W-026 后续切片；含这些未实现语义的工程不会被静默降级为普通栅格层。
