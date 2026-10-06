# 启用组蒙版子树复制证据（M3f）

日期：2026-10-06

验证提交：`c8ef0e8 Cover enabled group mask copy`

## 已验证范围

- 带启用组蒙版的完整组子树可以复制到嵌套目标组，副本保留组蒙版资产与 `maskEnabled` 状态。
- 复制后的组按正式缓存渲染器计算，逐 tile 结果与源组预览一致；保存重开后渲染仍一致。
- 组内单层在启用组蒙版时继续拒绝隔离复制，避免丢失祖先 Alpha 语义；需要保留该语义时必须整体复制组。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-enabled-group-mask-20261006-b
```

结果：Release 构建 0 警告、0 错误；App.Checks 三项生产检查全部 PASS。检查覆盖启用组蒙版的单层拒绝、完整组复制到嵌套目标组、源/目标渲染逐 tile 一致以及保存重开后的渲染一致。

以上是 macOS Headless 证据，不能替代 Windows 原生拖放、输入、DPI、原生 DLL 或性能验收。调整层、复杂 Alpha 组合、跨变换快照和移动而非复制仍未交付。
