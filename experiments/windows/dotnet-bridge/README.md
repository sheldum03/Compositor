# C# / P/Invoke 桥接探测

W-006 的实验程序，调用 `../native` 构建的共享库；不依赖 Avalonia/Skia，不代表已选择 Windows UI 路线。无 NuGet 包依赖，NuGet.Config 清空包源；SDK 由 global.json 固定为 10.0.401，禁止自动滚动到其他版本。

## 执行

先按 [native README](../native/README.md) 构建 Release 动态库，然后在本目录运行：

```sh
dotnet build -c Release
dotnet run -c Release --no-build -- /absolute/path/to/libcompositor_native.dylib
```

Windows 将末尾参数换成构建出的 `compositor_native.dll` 绝对路径。库按传入路径加载，避免误用系统搜索路径中的同名库。成功输出一行 JSON，断言失败抛出异常并以非零退出；使用 Release 也不会移除检查。

Mac 工具准备使用官方 [.NET 10 release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) 中 2026-09-08 发布的 SDK 10.0.401，解压到 `/tmp/compositor-dotnet-10.0.401`，未安装系统 pkg。下载 `dotnet-sdk-10.0.401-osx-arm64.tar.gz` 的 SHA-512 固定为：

```text
69f64eb00dc045398755c440b152225d544301a345a146a16e86a56a0c52b7c94b2c331520e976dbb821f18d31930aafbd25bb85961e3517e0665414ce0cbcff
```

## 实际覆盖的边界

- 8 个现有 C 文件、17 个可供 FFI 调用的入口；两个 legacy `long` API 通过现有 bridge 的 `int64_t` 适配入口调用。
- `size_t` ↔ `nuint`、`int64_t` ↔ C# `long`、`int32_t` ↔ C# `int`；布尔选项保留原 C `int`，不引入默认 bool marshalling。显式 Cdecl，输入均是同步调用期间有效的借入指针，依据 [Microsoft interop guidance](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices)。
- 托管 RGBA 数组在 fixed 内调用，强制一次 compacting GC 后仍由原数组接收结果；RGBA 行尾 padding 与 Gray stride 分开，packed API 按实际像素 count 调用。
- alpha 提取/还原、Levels 恒等 LUT 和 histogram、Gradient Map 恒红表、grain/noise 的 alpha/预乘范围、lens 恒等、clamp；content_fill/spot_heal 在空 coverage 上的返回值与无修改行为。
- 魔棒 packed mask、完整 64 位 bounds、返回计数与 int32 轮廓内存读取；循环 1,000 次后逐一调用库内 `compositor_free`，也验证 null 释放。模块在整个探测进程中保持加载。

C++ contract_tests 覆盖补色/修复的非空输入、grain 分块等更详细行为。本 C# 程序用于参数、缓冲和所有权边界，不将“调用全部函数”扩大成全部算法结果/错误分支/GC 压力已验证。没有使用真实 GUI 位图、异步指针持有、回调、OOM 或同平台预览/导出流程。

## Windows CI 待运行路径

工作流：[windows-native-probe.yml](../../../.github/workflows/windows-native-probe.yml)。只有 `codex/windows-implementation` 分支上指定 C/桥接/工作流路径变化的 push 会自动触发；另保留 workflow_dispatch 入口（GitHub 通常要求工作流已在默认分支注册后才能手动 dispatch）。没有 main 的 push/PR 触发，也不在工作流里提交代码、发消息、签名或发布产品。

工作流使用 `windows-2025` x64 标准 runner、Visual Studio 17 2022、CMake 3.31.6 和 SDK 10.0.401；GitHub Actions 固定 commit SHA。Windows runner 镜像会更新，工作流会记录实际 ImageVersion/OS/工具输出；标签不当作固定机器镜像。依据 [runner 文档](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)，公开仓库的标准托管 runner 免费；不启用付费大规格 runner。

产物保留 14 天：MSVC/CMake/CTest 日志、JUnit、DLL exports、ctypes/PInvoke JSON、DLL 和 C++ 测试 exe。只给 `contents: read` 权限，20 分钟任务时限。若某命令失败，PowerShell native error preference 使步骤失败，`always()` 仍上传已有证据。

该 runner 是服务器 CI 环境，不能关闭 W-004 Windows 11 参考机/干净 VM、GUI/IME、多屏 DPI、基础笔输入或性能门槛。即使 CI 通过，也不能单凭本探测关闭 W-006 的完整框架像素接入与固定样本比较，更不能关闭 M1 或发布门槛。

2026-09-21 本机 Mac arm64 Release 构建零警告/零错误；实际 P/Invoke 探测通过，运行时 .NET 10.0.12，17 个入口、1,000 次分配方释放。结果见 [pinvoke-macos.json](../../../docs/windows/evidence/pinvoke-macos.json)。同日用户在 Windows 11 Pro x64 Build26200 上运行同一托管程序集与便携.NET10.0.12，回传17个入口、1,000次分配方释放检查通过。原生DLL使用LLVM-MinGW构建，见 [pinvoke-windows11.json](../../../docs/windows/evidence/pinvoke-windows11.json)。证据来源为用户贴出的stdout，原始日志尚未独立收集；不是MSVC、框架像素接入或完整W-006验收。公开仓库推送和首次CI运行仍待确认。
