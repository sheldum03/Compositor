# 剪贴栈 Alpha 保真证据（M3e）

日期：2026-10-06

功能提交：`fca74ca Preserve alpha when merging clipping stacks`

## 已实现范围

- 平面工程的正式渲染在遇到连续同级剪贴栈时，先以 base 图层建立栈 Alpha，再在不透明工作面上合并内部颜色，最后恢复 base Alpha。
- 图层合并复用同一组图层渲染路径，不再用逐层 `ApplyAlphaMask` 重复叠加半透明边缘。
- 合并仍限制为当前已验证的连续、同级、Normal、未变换剪贴栈；组、复杂层级和其他未验证语义继续拒绝。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-clip-alpha-20261006-b
```

结果：Release 构建 0 警告、0 错误；三个生产检查全部 PASS。现有剪贴栈合并场景先保存渲染结果，再执行合并、撤销、重做和保存重开，逐像素验证合并结果与正式渲染一致；本次修改前该回归在半透明剪贴边缘失败，说明旧路径确实重复了 Alpha。

该证据关闭“受限平面剪贴栈合并的 Alpha 恢复”切片；组/复杂剪贴栈、调整层和 Windows 实机验收继续按计划追踪。
