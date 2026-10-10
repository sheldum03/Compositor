# Production Windows M5 evidence — bounded selection Add Noise filter transaction

日期：2026-10-07  
实现提交：`07476e8`  
关联计划：`windows-part`

## 已交付

- 在现有 `.comp` v8 平面工程中增加选区内 Add Noise 的预览、提交和取消事务。
- 预览使用固定 seed 的 CPU Add Noise 栅格结果，以选区覆盖度混合回原栅格；选区外像素保持原值，半透明选区按覆盖度线性混合。
- 正式窗口提供数量 `0.1..400`、高斯分布和单色模式控件，以及“预览选区杂色 / 提交选区杂色 / 取消滤镜预览”按钮。
- 预览只更新画布，不写入工程历史；取消恢复原画布；提交以一次 `ReplaceLayerRaster` 事务写入像素，支持撤销/重做。
- 选择、编辑、撤销或重做会先清理未提交预览，避免陈旧结果覆盖新状态。
- 当前边界明确为无组、无蒙版、无文字、无调整层、无变换的平面 Normal 栅格；复杂语义继续拒绝执行，不静默降级。

## 验证

本地使用固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet`：

```text
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures <output>
```

Workflow 检查验证高斯/单色固定 seed 的覆盖区域变化和选区外像素不变；App 检查通过生产 Avalonia 窗口实际点击预览、取消、再次预览、提交和撤销，验证预览不改变 dirty/history、取消恢复原栅格、提交形成一次像素事务。GitHub Actions push [run 37533027083](https://github.com/sheldum03/Compositor/actions/runs/37533027083) 与实现 PR [run 37533032331](https://github.com/sheldum03/Compositor/actions/runs/37533032331) 的 Smoke、Imaging、Workflow、SaveCrash、App 五个生产检查全部通过。

## 边界

当前切片只覆盖 CPU 平面栅格的选区 Add Noise 事务；选区 Gaussian Blur、Motion Blur 已有独立事务证据。组、蒙版、剪贴关系、文字、调整层、非恒等变换、其他选区滤镜、复杂组合、全分辨率性能和 Windows 真机验收仍在 W-027 后续切片；腾讯云 Windows 真机门禁仍受工具链与宿主条件阻塞。
