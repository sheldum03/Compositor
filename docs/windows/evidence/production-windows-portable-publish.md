# Windows x64 便携产物交叉发布检查（2026-10-05）

## 结果

在 macOS arm64 主机使用固定的 .NET SDK 10.0.401、Avalonia 11.3.22 和 `codex/windows-implementation` 提交 `dbccb7b`，为生产窗口执行了 `win-x64` self-contained 发布：

```text
dotnet restore windows/Compositor.App/Compositor.App.csproj \
  --runtime win-x64 --packages /tmp/compositor-nuget-packages --ignore-failed-sources
dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release \
  -r win-x64 --self-contained true --no-restore \
  -p:RestorePackagesPath=/tmp/compositor-nuget-packages \
  -o /tmp/compositor-publish-win-x64-selfcontained-dbccb7b
```

两步均退出 0，构建无警告/错误。发布目录包含 `Compositor.App.exe`、`coreclr.dll`、`hostfxr.dll` 和 `hostpolicy.dll`，共 224 个文件，未包含 `compositor_native.dll`。按发布目录生成的压缩包为 `/tmp/Compositor.App-win-x64-selfcontained-dbccb7b.zip`，SHA-256 为 `138bfabdc348743fb04f10645103df12595c3cfdfd4d5efcaaa70f75f3419ef2`。

## 边界

这是跨平台构建/打包证据，不是 Windows 实机验收。当前尚未在 Windows 上启动 `Compositor.App.exe`，也未验证原生魔棒 DLL、.comp 文件夹打开/保存、文件对话框、DPI、多显示器、输入法、性能或干净机依赖。Windows 实机执行仍需使用同一压缩包，另行编译/放置 Windows 原生 DLL，并保存完整日志和输出哈希；不得用本证据关闭 W-004、W-006、W-010–015、M3a 或 Alpha。
