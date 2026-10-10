# 组蒙版与剪贴栈复杂 Alpha 证据（M3g）

日期：2026-10-06

回归提交：`9ca2d6e Cover masked clipping group merge`

## 已验证范围

- 组内包含半透明剪贴目标；组根同时使用组级 Gray8 蒙版、非 100% 透明度、`Multiply` 混合模式和非恒等 transform。
- 组根向下合并时，`RenderLayersForMerge` 先按正式缓存组路径计算剪贴栈与组蒙版，再将结果写入下方平面层；逐 tile 比较合并前后渲染结果，像素保持一致。
- 合并结果明确归一为默认 `Normal`、100% 透明度、无蒙版的平面层；Undo/Redo、保存重开和输出像素继续通过。
- 这只关闭了“组蒙版 + 剪贴栈 + 非 Normal/透明度 + 组 transform”的受限组合。不同父级、非连续选择、外部剪贴关系、复杂组层级和未验证的 13 种模式仍由校验拒绝，现有 5/13 exact 门槛没有放宽。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-complex-alpha-20261006-a
```

结果：Release 构建 0 警告、0 错误；App.Checks 三项生产检查全部 PASS。新增场景逐 tile 比较带组蒙版的剪贴栈组在合并前后的渲染，并验证输出元数据、Undo/Redo、保存重开；既有检查继续覆盖复杂剪贴关系和组结构拒绝。

以上是 macOS Headless 证据，不能替代 Windows 原生窗口、输入、DPI、原生 DLL 或性能验收。调整层、完整 Alpha 组合、复杂目标组层级和未达 exact 门槛的混合模式仍未交付。
