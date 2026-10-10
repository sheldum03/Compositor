# 组内连续剪贴栈跨工程拖放证据（M3f）

日期：2026-10-06

功能提交：`6e47154 Support grouped clipping stack copy`

## 已实现范围

- 同窗跨工程拖放从组内平面层扩展到完整、连续的组内剪贴栈；从栈中任一图层发起复制都会带上同级连续的源/目标图层。
- 复制会为栈内图层重新分配 ID，将 `maskSourceID` 重映射到副本，重映射父级到目标活动组，并保留各层像素、Gray8 蒙版、蒙版启停、透明度、混合模式和自身 transform。
- 源栈必须属于同一父组且连续；祖先组必须为恒等 transform，不能有启用的组蒙版、外部剪贴关系、缺失蒙版资源或组节点。复杂变换快照和复杂 Alpha 语义继续拒绝。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-grouped-stack-20261006-g
```

结果：Release 构建 0 警告、0 错误；App.Checks 三项生产检查全部 PASS。新增场景验证变换祖先组拒绝、恒等祖先组中的完整源/目标剪贴栈复制、父级和 `maskSourceID` 重映射、逐 tile 像素与源蒙版一致，以及保存重开后的关系和像素。

同一固定源码随后串行复跑：

```sh
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-grouped-stack-20261006-a
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-imaging-grouped-stack-20261006-b
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-savecrash-grouped-stack-20261006-b
```

Workflow、Imaging 和 SaveCrash 均以退出码 0 完成；SaveCrash 覆盖 14 个真实保存中断场景。以上仍是 macOS Headless 结果，不能替代 Windows 原生拖放、输入、DPI、原生 DLL 或性能验收。

## 剩余边界

复杂目标组层级、变换祖先组下的栈复制、启用组蒙版下的单层/栈复制、外部剪贴关系、移动而非复制和完整 Alpha 组合仍按计划拒绝或未实现。
