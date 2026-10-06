# 蒙版/剪贴可见结果复制证据（M3e）

日期：2026-10-06

功能提交：`88d270a Copy masked clipping visible results`

## 已实现范围

- `Layer via Copy` 继续只允许 v8、无组、未变换、Normal 平面图层。
- 当前层存在全画布 Gray8 蒙版时，复制前按启用状态应用蒙版。
- 当前层存在连续剪贴源时，递归解析剪贴源 Alpha 及其受限蒙版，再应用当前层透明度，写入新平面层。
- 组、非 Normal 混合、非恒等变换、循环剪贴关系仍明确拒绝，不做静默栅格化。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-visible-copy-20261006-c
```

结果：Release 构建 0 警告、0 错误；三个生产检查全部 PASS。新增 CanvasChecks 构造带蒙版、剪贴源的平面层，选择整幅画布执行 Layer via Copy，并逐像素比较复制层与蒙版/剪贴解析结果；随后 Undo 确认新层事务被移除。该检查仍是 Headless 证据，未覆盖 Windows 原生窗口或复杂组/非 Normal 混合。

## 仍未覆盖

非恒等变换复制、组可见结果复制、非 Normal 混合结果复制、跨图层拖放、任意粘贴定位、Alpha 组合和 Windows 实机验收仍按 W-018/W-029 追踪。
