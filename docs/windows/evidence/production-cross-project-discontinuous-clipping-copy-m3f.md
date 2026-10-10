# M3f 跨工程间断剪贴链复制证据

日期：2026-10-06  
范围：平面工程和恒等组内的栅格剪贴链，在源与目标之间存在无关兄弟层时跨工程拖放复制。

`ProjectSession.ValidateLayerCopyFrom` 不再把源索引必须连续作为跨工程复制条件。现有源栈收集、父级校验、资源校验、目标组边界、ID/`maskSourceID` 重映射和保存事务继续生效；无关兄弟不进入复制结果。跨父组、组变换、启用组蒙版的孤立复制和外部剪贴关系仍按原边界拒绝。

固定本地环境：

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
```

正式窗口检查：

```sh
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-cross-discontinuous-20261006-5
```

结果：构建 0 警告、0 错误；正式 Avalonia Headless 窗口覆盖平面间断链和组内间断链的跨工程复制、关系重映射、目标父级处理、保存重开和像素/蒙版资产保留，既有 App 场景全部通过。

Workflow 回归：

```sh
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-cross-discontinuous-20261006-5
```

结果：构建 0 警告、0 错误；既有 Workflow 场景及同父组间断剪贴链可见结果复制场景通过。该证据仍不是 Windows 原生窗口、IME、DPI、系统剪贴板、性能或腾讯云实机验收。

## 边界

本证据关闭跨工程平面/同父组间断栅格剪贴链复制切片。跨父组剪贴关系、复杂剪贴栈合并、复杂 Alpha、文字/调整/形状语义和 Windows 实机仍未关闭。
