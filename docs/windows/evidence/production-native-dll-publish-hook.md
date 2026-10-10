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

随后在功能提交 `d720166` 上重新发布：目录 `/tmp/compositor-win-x64-cross-paste-d720166`，224 个文件，`Compositor.App.exe` SHA-256 为 `160b12e30597bca23331fdada2830bed418efa465757e2fa52eb26622d18cace`，仍不含 `compositor_native.dll`。

2026-10-06 Windows production core run `37409393572`（提交 `24188d9`）首次在 `windows-2025` runner 上执行真实原生构建并接入发布目录：Smoke 矩阵通过 CMake/MSVC 构建 `compositor_native.dll`，随后以 `win-x64` self-contained 方式发布 `Compositor.App`。发布包包含 `Compositor.App.exe` 和根目录 `compositor_native.dll`，共 224 个文件；包内清单记录入口 SHA-256 `6558386212433CF845B291935BA9BEB7FDEECE6CF7EB895EB8B1135D0F783947`、原生 DLL SHA-256 `12B8477A2E23C5D7A5EE562718F08ADE3172A6609E5778B9505FE0732F865FAA`。该结果证明 CI runner 的原生构建、DLL 注入和便携目录发布链路已接通；仍未证明腾讯云 Windows 实机启动、文件对话框、DPI/IME、系统剪贴板、性能或干净机安装。

Windows 实机前置：在 Windows x64 使用 `windows/native/CMakeLists.txt` 构建真实 DLL，将其放入 `windows/native/runtimes/win-x64/compositor_native.dll`，记录编译器、导出表和 SHA-256，再运行 `Compositor.Smoke` 及正式窗口检查。当前仍未完成 Windows 运行、原生 ABI、DPI/IME、文件对话框和性能验收。

2026-10-06 Windows native/framework probe run `37405690037` 在 native CMake configure 阶段失败：workflow 硬编码 `Visual Studio 17 2022`，而 `windows-2025` runner 使用 VS18 2026。随后固定 CMake 4.2.3，按 runner 安装版本动态选择 `Visual Studio 18 2026` generator，并修复 Qt probe 在 VS `/WX` 下的 C4458 变量遮蔽；fixture JSON 通过 `.gitattributes` 固定字节。修复后的 run `37407903694` 全部通过 C/MSVC、C++ contract、DLL/ctypes、C# P/Invoke、Avalonia corpus/brush/text 与 Qt 构建及检查。该结果仍是 CI runner 证据，不替代腾讯云 Windows 实机启动、原生 DLL 部署、DPI/IME、文件对话框和性能验收。
