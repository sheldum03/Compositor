# 嵌套目标组跨工程拖放证据（M3f）

日期：2026-10-06

验证提交：`172b6b7 Cover nested target group copy`

## 已验证范围

- 根组子树可以插入另一个工程的嵌套目标组，复制后的组根 `parentID` 指向目标组。
- 目标组内保留完整子树数量，保存重开后父级关系和子层数量保持不变。
- 该场景与组内完整连续剪贴栈复制组合验证；剪贴关系、蒙版像素和组蒙版边界沿用[组内剪贴栈拖放证据](production-grouped-clipping-stack-drag-m3f.md)。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-nested-group-20261006-a
```

结果：Release 构建 0 警告、0 错误；App.Checks 三项生产检查全部 PASS。检查先创建嵌套目标组，再把源根组拖入目标组，核对目标父级、子树数量、保存和重开。

以上是 macOS Headless 证据，不能替代 Windows 原生拖放、输入、DPI、原生 DLL 或性能验收。更深层级、跨变换快照、复杂目标组混合和移动而非复制仍未交付。
