# Production Windows M5 evidence — bounded Add Noise adjustment layer

日期：2026-10-07  
实现提交：`b7d2cc4`  
关联计划：`windows-part`

## 已交付

- 在现有 `.comp` v8 平面工程中新增 `Add Noise` 调整层元数据，保存为 `adjustment.kind = "Add Noise"` 与 `noiseSettings`。
- 当前切片提供数量 `0.1..400`、均匀/高斯分布、单色模式和固定 `uint32` seed；实现沿用 macOS `noise_add` 的哈希、Box–Muller 和预乘 RGBA 规则。
- 调整层支持预览、修改、撤销/重做、保存和重新打开；正式窗口提供数量、分布和单色控件及应用按钮。
- 噪声只修改非透明像素的 RGB，alpha 保持不变；单色模式对三通道使用同一随机偏移，非单色模式按通道生成独立偏移。
- 选区预览、异步取消/提交、组/蒙版/剪贴/变换组合和大画布性能仍保留在后续切片。

## 验证

本地使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
```

Workflow 检查包含 `Add Noise adjustment layer metadata, distributions, premultiplied pixels, history and save/reopen`，验证两种分布、单色模式、通道变化和透明像素；App 检查实际点击新增/应用按钮、修改数量/分布/单色控件、比对预览像素并验证撤销。GitHub Actions PR run [37526900136](https://github.com/sheldum03/Compositor/actions/runs/37526900136) 的 Smoke、Imaging、Workflow、SaveCrash、App 五个生产检查全部通过。

## 边界

当前切片只覆盖全画布平面 Normal 调整层和 CPU 噪声生成。选区预览、异步取消/提交、组/蒙版/剪贴/变换组合、镜头校正及大画布性能阈值仍在 W-027 后续切片；含这些未实现语义的工程不会被静默降级为普通栅格层。
