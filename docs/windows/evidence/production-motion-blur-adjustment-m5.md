# Production Windows M5 evidence — bounded Motion Blur adjustment layer

日期：2026-10-07  
实现提交：`caae87c`  
关联计划：`windows-part`

## 已交付

- 在现有 `.comp` v8 平面工程中新增 `Motion Blur` 调整层元数据，保存为 `adjustment.kind = "Motion Blur"` 与 `motionBlurSettings`。
- 当前切片提供角度 `-90..90` 和距离 `1..32`；CPU 渲染沿角度方向进行均匀线段采样，采样在预乘 RGBA 上执行并对画布边缘夹取。
- 调整层支持预览、修改、撤销/重做、保存和重新打开；正式窗口提供角度、距离控件和应用按钮。
- 半透明边缘通过预乘通道保持颜色不超过 alpha，避免透明边缘出现颜色泄漏。
- 选区预览、异步取消/提交、组/蒙版/剪贴/变换组合和大画布性能仍保留在后续切片。

## 验证

本地使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
```

Workflow 检查包含 `Motion Blur adjustment layer metadata, premultiplied pixels, history and save/reopen`，验证角度/距离变化、预乘透明边缘和重开后的可编辑元数据；App 检查实际点击新增/应用按钮、修改两个参数、比对预览像素并验证撤销。GitHub Actions PR run [37525519081](https://github.com/sheldum03/Compositor/actions/runs/37525519081) 的 Smoke、Imaging、Workflow、SaveCrash、App 五个生产检查全部通过。

## 边界

当前切片只覆盖全画布平面 Normal 调整层和 CPU 线段采样。选区预览、异步取消/提交、组/蒙版/剪贴/变换组合、Add Noise、镜头校正及大画布性能阈值仍在 W-027 后续切片；含这些未实现语义的工程不会被静默降级为普通栅格层。
