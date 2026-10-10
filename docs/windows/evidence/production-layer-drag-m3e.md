# 图层列表拖放排序证据（M3e）

日期：2026-10-06

功能提交：`976ede5 Support layer list drag reorder`

## 已实现范围

- Avalonia 图层列表支持鼠标拖动列表项到另一列表项。
- 拖放在一次工程历史中调用精确目标位置重排，保存活动层、图层像素和剪贴栈连续性。
- 组工程仍禁用该结构操作；剪贴栈只能作为连续单元移动，不能被拆开。
- 既有“上移/下移”按钮继续使用原有相邻移动逻辑。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-layer-drag-final-20261006
```

结果：Release 构建 0 警告、0 错误；三个生产检查全部 PASS。新增检查真实构造图层列表、通过 Headless 鼠标按下/移动/释放把最高层拖到最低层，验证一次历史重排、Undo/Redo 以及保存重开后的图层顺序。未覆盖 Windows 原生触控/高 DPI、多指拖动或复杂组拖放。

## 仍未覆盖

复杂组的可视化拖放、跨项目拖放、任意粘贴定位、非恒等变换复制和 Windows 实机验收仍按 W-017/W-018/W-029 追踪。
