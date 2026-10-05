# Windows 原生 DLL 发布接线证据（W-006/W-010）

日期：2026-10-06

代码提交：`63215b7`（`Wire Windows native DLL into publish`）。

`Compositor.App` 现在读取可选的 `CompositorNativeDll` MSBuild 属性；默认路径为 `windows/native/runtimes/$(RuntimeIdentifier)/compositor_native.dll`。文件存在时，通过 `CopyToPublishDirectory=PreserveNewest` 复制到发布目录根部，正好匹配 `Program` 的 Windows DLL 加载路径。文件不存在时保持当前可构建行为，但魔棒和轮廓功能仍不可用，不能据此宣称完整 Alpha。

固定验证：

```sh
SDK_ROOT=/tmp/dotnet-sdk-root-401b
export DOTNET_ROOT="$SDK_ROOT"
export PATH="$SDK_ROOT:$PATH"
export NUGET_PACKAGES=/tmp/compositor-nuget-packages

dotnet build windows/Compositor.App/Compositor.App.csproj -c Release --no-restore
dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o /tmp/compositor-win-x64-native-hook-63215b7
```

无 DLL 的 Release 构建和交叉发布均为 0 警告、0 错误；发布目录为 224 个文件，`Compositor.App.exe` SHA-256 为 `2481d09bc07e1fbd3ca648fb55124cad9a845f4b0b964f0f01599ac88eaf3899`，且没有 `compositor_native.dll`。另用临时哨兵文件覆盖 `CompositorNativeDll` 属性发布到独立目录，确认文件被复制为根目录 `compositor_native.dll`；该哨兵不是可运行 DLL，也未进入发布包。

2026-10-06 复核 `9779dee`：App.Checks、Workflow Checks、Imaging Checks 和 SaveCrash Checks 均以 Release/固定 SDK 通过，`win-x64` self-contained 发布仍为 224 个文件，入口 SHA-256 为 `262e5e9c0eb38fd64ca0e0f79088ac81ccd4413181ba06cacd6360baed815f0a`，目录为 `/tmp/compositor-win-x64-progress-20261006-b`。与旧记录相比入口哈希发生变化但文件数不变，说明 apphost 发布哈希不能单独证明可复现；本次记录只作为产物追踪。包仍没有真实 `compositor_native.dll`，因此这些检查仍是 macOS 交叉发布和 Headless 证据。

随后在功能提交 `bbca347` 上重新发布：目录 `/tmp/compositor-win-x64-layercopy-bbca347`，224 个文件，`Compositor.App.exe` SHA-256 为 `ef7ce01744e6bf933c570ea28f74e860a93f38736107bbfa5ecbd67503c25d15`，仍不含 `compositor_native.dll`。

Windows 实机前置：在 Windows x64 使用 `windows/native/CMakeLists.txt` 构建真实 DLL，将其放入 `windows/native/runtimes/win-x64/compositor_native.dll`，记录编译器、导出表和 SHA-256，再运行 `Compositor.Smoke` 及正式窗口检查。当前仍未完成 Windows 运行、原生 ABI、DPI/IME、文件对话框和性能验收。
