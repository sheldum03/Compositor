# Production Windows M5 evidence — bounded selection Hue/Saturation filter transaction

日期：2026-10-07  
实现提交：`85ea320`  
关联计划：`windows-part`

## 已交付

- 在现有 `.comp` v8 平面工程中增加选区主范围 Hue/Saturation 的预览、提交和取消事务，参数为 Hue、Saturation、Lightness 和 Colorize。
- 预览复用生产 `RasterCompositor.ApplyHueSaturation` 的预乘 RGBA 路径，以选区覆盖度混合回原栅格；选区外像素保持原值。
- 正式窗口提供色相、饱和度、明度和着色控件，以及“预览选区色相/饱和度 / 提交选区色相/饱和度 / 取消滤镜预览”按钮。
- 预览只更新画布，不写入工程历史；取消恢复原画布；提交以一次 `ReplaceLayerRaster` 事务写入像素，支持撤销/重做。
- 当前边界明确为主范围 HSL 和着色模式、无组、无蒙版、无文字、无调整层、无变换的平面 Normal 栅格；颜色范围、取样吸管和复杂语义继续拒绝或未实现，不静默降级。

## 验证

本地使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
```

Workflow 检查验证主范围参数 `45 / 60 / -20` 的覆盖区域变化和选区外像素不变；App 检查通过生产 Avalonia 窗口实际点击预览、取消、再次预览、提交和撤销，验证预览不改变 dirty/history、取消恢复原栅格、提交形成一次像素事务。GitHub Actions push [run 37536951608](https://github.com/sheldum03/Compositor/actions/runs/37536951608) 与实现 PR [run 37536957686](https://github.com/sheldum03/Compositor/actions/runs/37536957686) 的 Smoke、Imaging、Workflow、SaveCrash、App 五个生产检查全部通过。

## 边界

当前切片只覆盖 CPU 平面栅格的选区主范围 Hue/Saturation 事务；根级主范围调整层、选区 Exposure 和 RGB Levels 已有独立证据。颜色范围、取样吸管、单通道曲线、其余选区调整层、组/蒙版/剪贴组合、全分辨率性能和 Windows 真机验收仍在后续切片；腾讯云 Windows 真机门禁仍受工具链与宿主条件阻塞。
