# M3g 同父组间断剪贴链可见结果复制证据

日期：2026-10-06  
实现提交：`f58d227`  
CI push：[37452219188](https://github.com/sheldum03/Compositor/actions/runs/37452219188)  
CI PR：[37452224610](https://github.com/sheldum03/Compositor/actions/runs/37452224610)  
范围：同一父组内，剪贴源与目标之间存在无关兄弟层的栅格剪贴链。

`ImageProjectWorkflow.ValidateGroupedLeafCopy` 现在从当前目标沿 `maskSourceID` 反向解析完整链，再按源到目标的顺序校验同父关系、源在目标之前、节点类型、资源和祖先组状态；不再要求链节点在扁平列表中连续。无关兄弟不会被纳入复制栈，复制层仍插入所选目标之后。跨父组、循环、缺失源、组/文字节点和缺失栅格蒙版继续拒绝。

`RenderGroupedClippingStackForCopy` 继续只合成显式链中的源和目标，按层透明度与混合模式生成默认 Normal 栅格结果。原间断链的 `maskSourceID` 关系保留，复制像素独立保存。

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
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-discontinuous-copy-20261006-4
```

结果：构建 0 警告、0 错误；新增 `PASS: same-parent discontinuous clipping-stack visible-result copy preserves target semantics and save/reopen`，并通过既有 Workflow 场景。

正式窗口检查：

```sh
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-discontinuous-copy-20261006-4
```

结果：构建 0 警告、0 错误；Avalonia Headless 正式窗口确认 `Layer via Copy` 启用、复制层插在所选目标之后、选区外透明、原关系/父级/像素在保存重开后保持，并通过既有 App 场景。Windows CI push 与 PR 的 Smoke、Imaging、Workflow、SaveCrash、App 五项均通过；该结果仍不等价于 Windows 原生窗口、IME、DPI、系统剪贴板或腾讯云实机验收。

## 边界

本证据只关闭同父组间断栅格剪贴链复制切片。跨父组剪贴关系、复杂剪贴栈合并、复杂 Alpha 组合、文字剪贴链、变换目标组层级和腾讯云 Windows 实机仍未关闭。
