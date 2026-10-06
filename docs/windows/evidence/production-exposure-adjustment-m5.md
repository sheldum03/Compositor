# 生产 Exposure 调整层子切片（2026-10-07）

## 结论

`40171e0` 为 Windows Avalonia 生产工程加入一个可保存的 Exposure 调整层切片。它只适用于平面 v8 工程的根级层，参数为曝光（-20–20 stops）、偏移（-0.5–0.5）和伽马（0.01–9.99）；渲染沿用 8-bit 预乘 RGBA，在 sRGB 线性空间计算后重新预乘，保持原 Alpha。正式窗口可以新增调整层、编辑参数、设置透明度、撤销/重做并保存重开。

## 验证

固定 SDK `/tmp/dotnet-sdk-root-401b/dotnet` 下完成：

```text
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-exposure-run-3
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-exposure-run-12
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-imaging-exposure-run-1
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-savecrash-exposure-run-1
```

结果：相关 Release 构建均为 0 警告、0 错误；Workflow 明确通过 Exposure 元数据、预乘像素、Alpha 不变、历史和保存重开；正式 App Headless 检查通过新增按钮、参数控件、预览像素和撤销回退；原有 Imaging 与 SaveCrash 检查也通过。实现分支 Windows production core CI 已触发，待该 run 完成后补入 runner 链接。

## 范围与边界

本切片只关闭 P-10/W-026 的 Exposure 基础路径，不代表完整调整层或滤镜系统已交付。调整层目前不参与组、剪贴源/目标、图层蒙版、像素画笔、选区、变换、Layer via Copy 或合并；这些入口在核心或正式窗口明确拒绝/禁用。只接受根级平面 Exposure，非 Normal 混合模式被拒绝；组内调整、色相/饱和度、色阶、曲线、渐变映射、颗粒、模糊、噪声、镜头校正、预览取消/提交和 Windows 实机验收仍开放。
