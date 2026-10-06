# Production Windows M5 evidence — RGB Curves adjustment layer

日期：2026-10-07  
实现提交：`6b1b702`  
关联计划：`windows-part`

## 已交付

- 在现有 `.comp` v8 平面工程中新增 `Curves` 调整层元数据，保存为 `adjustment.kind = "Curves"` 与 `curvesSettings`。
- 当前切片提供 RGB 复合曲线的三个受限控制点：阴影 `0..255`、中间调 `0..255`、高光 `0..255`；通过单调三次 Hermite 插值生成 8-bit LUT。
- 渲染在 RGB 通道执行曲线映射，使用 premultiplied RGBA 的 alpha 安全解包/回写；透明度保持不变。
- 调整层支持预览、修改、撤销/重做、保存和重新打开；正式窗口提供新增调整、三个曲线参数控件和应用按钮。
- 调整层继续只允许 Normal 混合，并明确拒绝直接像素编辑、蒙版、剪贴、复制、分组和变换等未覆盖组合。

## 验证

本地使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
```

Workflow 检查包含 `Curves adjustment layer metadata, premultiplied pixels, history and save/reopen`；App 检查实际点击新增/应用按钮、修改三个曲线参数、比对预览像素并验证撤销。GitHub Actions PR run [37517901479](https://github.com/sheldum03/Compositor/actions/runs/37517901479) 的 Smoke、Imaging、Workflow、SaveCrash、App 五个生产检查全部通过。

## 边界

当前切片只覆盖 RGB 复合、三个固定控制点和 Normal 调整层。每通道曲线、任意数量控制点、完整曲线编辑器、选区预览、预览取消/提交事务以及组/剪贴组合仍在 W-026 后续切片；含这些未实现语义的工程不会被静默降级为普通栅格层。
