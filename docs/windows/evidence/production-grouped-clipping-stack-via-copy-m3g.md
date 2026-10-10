# 组内连续剪贴栈可见结果复制证据（M3g）

日期：2026-10-06

实现提交：`Support grouped clipping-stack visible-result copy`

## 已验证范围

- 在同一父组、连续同级、单层 `maskSourceID` 关系、源/目标及祖先组变换为恒等的条件下，正式窗口允许对剪贴栈中的源层或目标层执行“选区复制为图层”。
- 复制源按整段连续剪贴栈渲染可见结果，保留源图层蒙版、源透明度/混合模式和目标透明度/混合模式；新层插入整个原栈之后，避免破坏原有 `maskSourceID` 关系。
- 复制结果作为同一父组的平面兄弟插入；父组的混合模式、透明度和启用的 Gray8 组蒙版继续由原组渲染，不烘焙到复制像素。保存重开后父级、栈边界、组蒙版和复制像素保持不变。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-grouped-stack-copy-20261006b
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-grouped-stack-copy-20261006
```

两条 Release 构建均为 0 警告、0 错误。Workflow 检查逐像素确认完整组内剪贴栈的可见结果、栈尾插入、启用父组蒙版裁切和保存重开；App 检查确认正式窗口按钮、同父组栈尾插入、选区内外像素和保存重开。此前既有 Workflow/App 回归仍在同一进程中通过。

## 明确边界

- 当前只覆盖单层、连续、同父级的平面剪贴栈；多级 `maskSourceID`、跨父级关系、非恒等组/图层变换、文字/调整层和复杂 Alpha 组合仍按 W-018 追踪。
- 真实 Windows 应用启动、原生文件对话框、DPI/多显示器、IME/候选窗、真实压感、系统剪贴板、性能和干净机部署尚未由本切片关闭。
