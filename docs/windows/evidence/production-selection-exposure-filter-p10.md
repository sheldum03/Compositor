# Production Windows M5 evidence — bounded selection Exposure filter transaction

日期：2026-10-07  
实现提交：`1ed86e5`  
关联计划：`windows-part`

## 已交付

- 在现有 `.comp` v8 平面工程中增加选区 Exposure 的预览、提交和取消事务，参数为 Exposure、Offset、Gamma。
- 预览复用生产 `RasterCompositor.ApplyExposure` 的预乘 RGBA 路径，以选区覆盖度混合回原栅格；选区外像素保持原值。
- 正式窗口提供曝光、偏移、伽马控件，以及“预览选区曝光 / 提交选区曝光 / 取消滤镜预览”按钮。
- 预览只更新画布，不写入工程历史；取消恢复原画布；提交以一次 `ReplaceLayerRaster` 事务写入像素，支持撤销/重做。
- 当前边界明确为无组、无蒙版、无文字、无调整层、无变换的平面 Normal 栅格；复杂语义继续拒绝执行，不静默降级。

## 验证

本地使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
```

Workflow 检查验证 Exposure 参数 `1.5 / 0.05 / 1.2` 的覆盖区域变化和选区外像素不变；App 检查通过生产 Avalonia 窗口实际点击预览、取消、再次预览、提交和撤销，验证预览不改变 dirty/history、取消恢复原栅格、提交形成一次像素事务。GitHub Actions push [run 37535078527](https://github.com/sheldum03/Compositor/actions/runs/37535078527) 与实现 PR [run 37535084994](https://github.com/sheldum03/Compositor/actions/runs/37535084994) 的 Smoke、Imaging、Workflow、SaveCrash、App 五个生产检查全部通过。

## 边界

当前切片只覆盖 CPU 平面栅格的选区 Exposure 事务；Exposure 调整层及选区 Gaussian Blur、Motion Blur、Add Noise、Lens Correction 已有独立证据。单通道/自动色阶、颜色范围/取样、任意曲线控制点、多停靠点渐变、其余选区滤镜、组/蒙版/剪贴组合、全分辨率性能和 Windows 真机验收仍在后续切片；腾讯云 Windows 真机门禁仍受工具链与宿主条件阻塞。
