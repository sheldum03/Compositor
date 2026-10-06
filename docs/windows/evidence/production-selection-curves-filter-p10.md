# Production Windows M5 evidence — bounded selection RGB Curves filter transaction

日期：2026-10-07  
实现提交：`f56c80b`  
关联计划：`windows-part`

## 已交付

- 在现有 `.comp` v8 平面工程中增加选区 RGB 复合 Curves 的预览、提交和取消事务，参数为暗部、中间调和高光三个控制点。
- 预览复用生产 `RasterCompositor.ApplyCurves` 的预乘 RGBA 路径，以选区覆盖度混合回原栅格；选区外像素保持原值。
- 正式窗口提供选区暗部、中间调、高光控件，以及“预览选区曲线 / 提交选区曲线 / 取消滤镜预览”按钮。
- 预览只更新画布，不写入工程历史；预览期间参数锁定、预览按钮禁用且仅提交/取消可用；取消恢复原画布；提交以一次 `ReplaceLayerRaster` 事务写入像素，支持撤销/重做。
- 当前边界明确为 RGB 复合、三点曲线、无组、无蒙版、无文字、无调整层、无变换的平面 Normal 栅格；R/G/B 单通道、任意控制点、完整曲线编辑器和复杂语义继续拒绝或未实现，不静默降级。

## 验证

本地使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-selection-curves-workflow-20261007
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-selection-curves-app-20261007-verified
```

Workflow 检查验证参数 `20 / 160 / 240` 的覆盖区域变化和选区外像素不变，并输出 `PASS: selection Curves blends the filtered raster through coverage without changing outside pixels`。App 检查通过生产 Avalonia 窗口实际点击预览、取消、再次预览、提交和撤销，验证预览不改变 dirty/history、预览期间控件锁定、提交/取消按钮边界、取消恢复原栅格、提交形成一次像素事务；`results.json` 为 `passed=true`，包含 `selection RGB Curves preview/cancel/commit with undo`。

GitHub Actions 实现 push [run 37538234238](https://github.com/sheldum03/Compositor/actions/runs/37538234238) 与实现 PR 对应矩阵会在该提交完成后记录 Smoke、Imaging、Workflow、SaveCrash、App 五项生产检查。

## 边界

当前切片只覆盖 CPU 平面栅格的选区 RGB 复合三点 Curves 事务；根级 RGB/逐通道 Curves、选区 Exposure、RGB Levels 和主范围 Hue/Saturation 已有独立证据。R/G/B 单通道选区、任意控制点、多停靠点曲线、其余选区调整层、组/蒙版/剪贴组合、全分辨率性能和 Windows 真机验收仍在后续切片；腾讯云 Windows 真机门禁仍受工具链与宿主条件阻塞。
