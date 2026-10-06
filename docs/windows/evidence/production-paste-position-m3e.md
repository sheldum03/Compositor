# 任意粘贴定位证据（M3e）

日期：2026-10-06

功能提交：`4aa142b Position pasted clipboard images`

## 已实现范围

- `CanvasView` 记录最近一次文档指针位置。
- 外部系统剪贴板位图粘贴时，若最近指针在画布内，以该文档坐标作为位图中心。
- 没有有效画布指针时继续居中粘贴，超出画布的位图像素裁剪到画布。
- 内部工程选区粘贴仍保持原有浮动选区和变换快照语义。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-arbitrary-paste-20261006-b
```

结果：Release 构建 0 警告、0 错误；三个生产检查全部 PASS。新增检查将 3×2 预乘 RGBA 位图分别按默认居中和 `(10, 12)` 文档坐标粘贴，逐像素检查目标位置及透明边界。Headless 未调用真实 Windows 系统剪贴板，外部应用之间的实际粘贴仍需 Windows 实机验收。

## win-x64 包记录

```sh
dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release -r win-x64 \
  --self-contained true --no-restore -o /tmp/compositor-win-x64-arbitrary-paste-4aa142b
```

- 文件数：224
- 入口：`Compositor.App.exe`
- SHA-256：`dc25c039182269b49763980a0e7b88fccbf224e5ecef30cc5694fdc27a35b81c`
- `compositor_native.dll`：未包含

任意定位仍只针对外部位图新图层；跨图层拖放、复杂 Alpha 组合和 Windows 原生验收继续按计划追踪。
