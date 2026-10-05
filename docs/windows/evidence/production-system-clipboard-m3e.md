# 系统剪贴板图片切片证据（M3e）

日期：2026-10-06

功能提交：`c25fd08 Support system clipboard image paste`

## 已实现范围

- 复制选区、合并复制和剪切会尽力写入 Avalonia 系统剪贴板；内部工程剪贴快照仍保留，系统剪贴板不可用不会破坏内部复制。
- 主窗口接入 `Ctrl+C` / `Ctrl+V`。
- 系统剪贴板位图通过 Avalonia `Bitmap.CopyPixels` 转换为编辑器的预乘 RGBA 瓦片。
- 外部位图在当前工程中居中作为 `Clipboard Image` 新平面图层插入；超出画布的像素裁剪到画布。
- 新图层操作进入工程历史，可撤销、重做、保存并重开。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-system-clipboard-20261006-c
```

结果：Release 构建 0 警告、0 错误；三个生产检查全部 PASS。新增检查覆盖 Avalonia 位图往返的预乘 RGBA 字节一致性、外部位图居中/图层创建、Undo/Redo、保存重开像素一致性。Headless 不提供真实 Windows 系统剪贴板，因此复制到其他应用、从其他应用粘贴和 Windows 剪贴板权限仍需实机验收。

## win-x64 包记录

```sh
dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release -r win-x64 \
  --self-contained true --no-restore -o /tmp/compositor-win-x64-system-clipboard-c25fd08
```

- 文件数：224
- 入口：`Compositor.App.exe`
- SHA-256：`ead431102a37e0d63a73c3077d905a5bdf6397c1fe6a9ad9af707025388ac84b`
- `compositor_native.dll`：未包含；魔棒及真实 Windows 原生 DLL 链路仍未验收

## 未完成边界

本切片不实现任意粘贴定位、跨图层拖放、带蒙版/剪贴关系的可见结果复制、Alpha 组合或完整 Windows 剪贴板格式兼容；这些仍按 W-018/W-029 追踪。Windows 服务器启动、文件对话框、DPI/IME、性能和干净机部署仍未验证。
