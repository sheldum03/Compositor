# Production Windows M5 evidence — bounded Grain adjustment layer

日期：2026-10-07  
实现提交：`1c7b8b1`  
关联计划：`windows-part`

## 已交付

- 在现有 `.comp` v8 平面工程中新增 `Grain` 调整层元数据，保存为 `adjustment.kind = "Grain"` 与 `grainSettings`。
- 当前切片提供强度 `0..100`、颗粒尺寸 `0.5..20`、粗糙度 `0..100` 和固定 `uint32` seed；图案由文档像素坐标和 seed 决定，重复渲染保持稳定。
- 颗粒只改变亮度并对 RGB 使用同一变化，中间调更明显；渲染在预乘 RGBA 上解包/回写，透明度和完全透明像素保持不变。
- 调整层支持预览、修改、撤销/重做、保存和重新打开；正式窗口提供强度、尺寸、粗糙度控件和应用按钮。
- 选区预览、异步取消/提交事务、组/蒙版/剪贴/变换组合和大画布性能仍保留在后续切片。

## 验证

本地使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
```

Workflow 检查包含 `Grain adjustment layer metadata, deterministic seed, premultiplied pixels, history and save/reopen`，验证颗粒变化、seed 稳定性、RGB 中性和透明像素；App 检查实际点击新增/应用按钮、修改三个参数、比对预览像素并验证撤销。GitHub Actions PR run [37524106424](https://github.com/sheldum03/Compositor/actions/runs/37524106424) 的 Smoke、Imaging、Workflow、SaveCrash、App 五个生产检查全部通过。

## 边界

当前切片只覆盖全画布平面 Normal 调整层和 CPU 颗粒生成。选区预览、异步取消/提交、组/蒙版/剪贴/变换组合、Add Noise/运动模糊/镜头校正及大画布性能阈值仍在 W-027 后续切片；含这些未实现语义的工程不会被静默降级为普通栅格层。
