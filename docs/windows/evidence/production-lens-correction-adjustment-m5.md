# Production Windows M5 evidence — bounded Lens Correction adjustment layer

日期：2026-10-07  
实现提交：`08f9328`  
关联计划：`windows-part`

## 已交付

- 在现有 `.comp` v8 平面工程中新增 `Lens Correction` 调整层元数据，保存为 `adjustment.kind = "Lens Correction"` 与 `lensCorrectionSettings`。
- 当前切片提供畸变 `-100..100`；以画布中心和半对角线归一化半径计算径向采样，强度映射为 `k = distortion / 100 * 0.35`，使用双线性采样。
- 采样在预乘 RGBA 上进行；源图范围外的贡献保持透明，避免边缘颜色泄漏，`distortion = 0` 保持原栅格引用。
- 调整层支持预览、修改、撤销/重做、保存和重新打开；正式窗口提供畸变控件和应用按钮。
- 选区预览、异步取消/提交、组/蒙版/剪贴/变换组合和大画布性能仍保留在后续切片。

## 验证

本地使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
```

Workflow 检查包含 `Lens Correction adjustment layer metadata, radial warp, transparent edges, history and save/reopen`，验证正负畸变、透明边缘、预乘像素和保存重开；App 检查实际点击新增/应用按钮、修改畸变控件、比对预览像素并验证撤销。GitHub Actions PR run [37528394911](https://github.com/sheldum03/Compositor/actions/runs/37528394911) 的 Smoke、Imaging、Workflow、SaveCrash、App 五个生产检查全部通过。

## 边界

当前切片只覆盖全画布平面 Normal 调整层和 CPU 径向校正。选区预览、异步取消/提交事务、组/蒙版/剪贴/变换组合和大画布性能阈值仍在 W-027 后续切片；含这些未实现语义的工程不会被静默降级为普通栅格层。
