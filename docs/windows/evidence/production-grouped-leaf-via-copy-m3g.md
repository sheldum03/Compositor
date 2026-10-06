# 组内平面层选区复制证据（M3g）

日期：2026-10-06

## 已验证范围

- 在父组和祖先组变换为恒等、源图层自身变换为恒等、源图层不属于剪贴栈的条件下，正式窗口允许对组内平面层执行“选区复制为图层”。
- 复制结果作为同一父组的平面兄弟插入，使用默认 Normal/opacity 1；父组的混合模式、透明度和启用的 Gray8 组蒙版继续由原组渲染，不烘焙到复制像素。
- 源图层自身的完整栅格蒙版会先解析为复制像素；祖先组蒙版资产必须可用，保存重开后父级、蒙版状态和复制像素保持不变。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-grouped-leaf-copy-20261006
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-grouped-leaf-copy-20261006
```

两条 Release 构建均为 0 警告、0 错误。Workflow 检查逐像素确认组内复制、启用父组蒙版裁切和保存重开；App 检查确认正式窗口按钮、同父组插入位置、选区内外像素和保存重开。

## 明确边界

- 组内剪贴栈仍要求整体复制，避免在原剪贴关系中间插入新层；剪贴源/目标复制、复杂 Alpha 和跨父级关系继续按 W-018 追踪。
- 源层或祖先组的非恒等变换仍要求复制整个组或先显式烘焙；文字层、调整层、真实 Windows 输入、DPI、系统剪贴板和性能尚未由本切片关闭。
