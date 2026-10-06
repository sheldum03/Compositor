# M3g 组内多级剪贴栈可见结果复制证据

日期：2026-10-06  
实现提交：`8be1251`  
范围：同一父组、连续排列、按顺序引用前置图层的多级栅格剪贴链。

`ImageProjectWorkflow.ValidateGroupedLeafCopy` 现在沿 `maskSourceID` 向前解析剪贴源，要求所有源和目标属于同一父组、剪贴源位于当前图层之前，并把连续链整体纳入复制范围。组级蒙版、透明度、混合模式和祖先变换仍保持在原层级；复制层插入完整剪贴链之后。缺失源、循环关系、跨父组关系、间断链、组/文字节点和缺失栅格蒙版继续拒绝。

`RenderGroupedClippingStackForCopy` 对链中的每个子层递归解析其前置剪贴源，再按各层透明度和混合模式合成；复制结果仍是同父组下的默认 Normal 栅格层。这样不会把父组外观重复烘焙到复制像素中，也不会改变原始 `maskSourceID` 关系。

固定本地环境：

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
```

Workflow 检查：

```sh
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-nested-copy-20261006-1
```

结果：构建 0 警告、0 错误；新增 `PASS: nested grouped clipping-stack visible-result copy preserves chained relationships and save/reopen`，并通过既有 Workflow 场景。

正式窗口检查：

```sh
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-nested-copy-20261006-2
```

结果：构建 0 警告、0 错误；正式 Avalonia Headless 窗口确认 `Layer via Copy` 按钮启用、复制层插入完整链之后、选区外透明、链关系和栅格在保存重开后保持，并通过既有 App 场景。该检查仍不等价于真实 Windows 原生窗口、IME、DPI 或系统剪贴板验收。

## 边界

本证据只关闭同一父组的连续多级剪贴链复制切片。跨父组剪贴关系、间断/外部关系、复杂 Alpha 组合、文字剪贴链、变换目标组层级和腾讯云 Windows 实机仍未关闭。
