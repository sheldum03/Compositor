# Production Windows M5 evidence — bounded Gaussian Blur adjustment layer

日期：2026-10-07  
实现提交：`46b95f2`  
关联计划：`windows-part`

## 已交付

- 在现有 `.comp` v8 平面工程中新增 `Gaussian Blur` 调整层元数据，保存为 `adjustment.kind = "Gaussian Blur"` 与 `gaussianBlurSettings`。
- 当前切片提供 `1..32` 的半径参数，使用 CPU 可分离高斯核；水平和垂直阶段都对 premultiplied RGBA 四个通道执行边缘钳制采样。
- 渲染保持预乘不变量，半透明边缘参与卷积，不把透明像素静默当成不透明黑色；调整层仍只允许 Normal 混合。
- 调整层支持预览、修改、撤销/重做、保存和重新打开；正式窗口提供半径控件和应用按钮。
- 运动模糊、选区预览、预览取消/提交事务以及组/蒙版/剪贴/变换组合仍保留在后续切片，避免把未实现语义静默降级为普通栅格层。

## 验证

本地使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
```

Workflow 检查包含 `Gaussian Blur adjustment layer metadata, premultiplied pixels, history and save/reopen`，验证半透明边缘和半径修改；App 检查实际点击新增/应用按钮、修改半径、比对预览像素并验证撤销。GitHub Actions PR run [37522557136](https://github.com/sheldum03/Compositor/actions/runs/37522557136) 的 Smoke、Imaging、Workflow、SaveCrash、App 五个生产检查全部通过。

## 边界

当前切片只覆盖全画布平面 Normal 调整层和 CPU 半径卷积。运动模糊、选区预览、预览取消/提交事务、组/蒙版/剪贴/变换组合以及大画布性能阈值仍在 W-027 后续切片；含这些未实现语义的工程不会被静默降级为普通栅格层。
