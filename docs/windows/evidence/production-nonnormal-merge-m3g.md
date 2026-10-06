# 非 Normal 外观合并证据（M3g）

日期：2026-10-06

功能提交：`336cd57 Support non-normal appearance layer merge`

## 已实现范围

- 同级、连续、无组、未变换的平面图层合并现在允许源图层使用已支持的透明度和混合模式。
- 合并前由正式平面渲染器按源 opacity/blendMode 生成可见结果，再写入一个默认 Normal、100% 的平面层；不会把源外观元数据错误地留在合并层上。
- 组、变换层和不完整剪贴栈仍拒绝，复杂 Alpha 组合继续按计划追踪。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-nonnormal-merge-m3g-20261006-b
```

结果：Release 构建 0 警告、0 错误；三个生产检查全部 PASS。新增场景使用 Multiply/Screen 与半透明像素，验证合并前后逐 tile 像素一致、输出元数据归一化、Undo/Redo 和保存重开；此前非 Normal 图层会被合并按钮拒绝。

该证据关闭“受限非 Normal/透明度平面层合并”切片；复杂 Alpha、组/变换合并和 Windows 实机验收继续按计划追踪。
