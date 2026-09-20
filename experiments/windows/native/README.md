# 公共 C 算法桥接实验

W-006 的本机准备，**不是 Windows 验收结果，也不选择 Qt/Avalonia 路线**。直接编译现有 8 个 C 文件，Mac 产品源码和 Xcode 工程不变。

## 构建与验证

已执行环境：macOS 26.5.1 arm64、AppleClang 21.0.0、CMake 3.31.6、C17/C++17。CMake 安装于 `/tmp/compositor-windows-cmake-venv`，没有改系统工具链。

```sh
cmake -S experiments/windows/native -B /tmp/compositor-windows-native-release \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_C_FLAGS='-Wall -Wextra' \
  -DCMAKE_CXX_FLAGS='-Wall -Wextra'
cmake --build /tmp/compositor-windows-native-release --parallel 4
ctest --test-dir /tmp/compositor-windows-native-release --output-on-failure -V
python3 experiments/windows/native/ffi_smoke.py \
  /tmp/compositor-windows-native-release/libcompositor_native.dylib
```

当前机器用 `/tmp/compositor-windows-cmake-venv/bin/cmake` 和同目录 `ctest` 替代命令中的短名称。安装复现：`python3 -m venv /tmp/compositor-windows-cmake-venv`，随后用该环境的 `python -m pip install cmake==3.31.6`。

内存检查已执行命令：

```sh
cmake -S experiments/windows/native -B /tmp/compositor-windows-native-sanitized \
  -DCMAKE_BUILD_TYPE=Debug \
  -DCMAKE_C_FLAGS='-Wall -Wextra -fsanitize=address,undefined -fno-omit-frame-pointer' \
  -DCMAKE_CXX_FLAGS='-Wall -Wextra -fsanitize=address,undefined -fno-omit-frame-pointer' \
  -DCMAKE_EXE_LINKER_FLAGS='-fsanitize=address,undefined' \
  -DCMAKE_SHARED_LINKER_FLAGS='-fsanitize=address,undefined'
cmake --build /tmp/compositor-windows-native-sanitized --parallel 4
UBSAN_OPTIONS=halt_on_error=1 ctest --test-dir /tmp/compositor-windows-native-sanitized --output-on-failure -V
```

两个 CTest 运行各 **1 个综合测试**通过；sanitizer 未报告错误，不代表已覆盖全部内存路径或执行 LeakSanitizer。Release ctypes 加载通过，1,000 次成功轮廓分配/释放。未在 Python 中加载 sanitizer 版本。证据在 [native-probe-macos.json](../../../docs/windows/evidence/native-probe-macos.json) 及同目录 `native-{release,sanitized}-macos.txt`。

下列 Windows 命令仅为待执行模板，Visual Studio/SDK 版本仍需 W-005 在实机锁定：

```powershell
cmake -S experiments/windows/native -B build/windows-native -A x64
cmake --build build/windows-native --config Release --parallel 4
ctest --test-dir build/windows-native -C Release --output-on-failure -V
python experiments/windows/native/ffi_smoke.py build/windows-native/Release/compositor_native.dll
```

## ABI 和所有权

- `bridge.h` 提供 `extern "C"` 声明；Windows 构建拟由 CMake `WINDOWS_EXPORT_ALL_SYMBOLS` 生成导出。MSVC 配置 `_USE_MATH_DEFINES` 供现有 `M_PI` 使用。这两项尚未实机验证。
- 8 个源文件共 16 个原函数，再加 3 个适配函数；Mac `nm -gU` 实测 19 个未修饰的 C 函数符号。此实验不承诺稳定的发布 ABI。
- 原 `heal_coverage_bounds` 的 `long[4]` 和 `wand_mask` 的 `long` 返回值不能直接按 C# `long` 绑定。FFI 使用 `compositor_heal_bounds` 的 `int64_t[4]` 与 `compositor_wand_mask` 的 `int64_t`。其余 `int` 按 32 位、`size_t` 按指针宽度绑定。Windows x64 的 `long` 为 32 位，而当前 Mac 为 64 位，依据 [Microsoft LLP64 文档](https://learn.microsoft.com/en-us/windows/win32/winprog64/abstract-data-models)。转换输出宽度不会修复算法内部可能的整数溢出。
- `wand_trace` 的 `points` 为 `2 * pointCount` 个 `int32_t`，`loops` 为 `loopCount` 个 `int32_t`，计数为 `size_t`；成功返回后两个指针分别用 `compositor_free` 释放，允许传 null。不得用调用者的 `free`、`delete`、`Marshal.FreeHGlobal` 或 `FreeCoTaskMem`。理由见 [Microsoft 跨 DLL CRT 分配说明](https://learn.microsoft.com/en-us/cpp/c-runtime-library/potential-errors-passing-crt-objects-across-dll-boundaries?view=msvc-170)。借入的像素/coverage/table 缓冲始终归调用者，调用期间须保持地址稳定。
- 这是可信图像缓冲上的低层实验接口，没有补造通用参数验证框架。调用者仍须执行已有的尺寸/预算检查（单边 ≤30,000、总像素 ≤100,000,000）、确保缓冲长度和 stride 有效、使用已验证参数。MagicWand 现有样本 radius 仅 0/1/2；不得把原 C 的任意 radius 视作已验证能力。Windows 32 位和 ARM64 均未验证。
- 原函数语义、错误值以对应 Rendering 头文件为准。`content_fill` 的 1/0/-1 与 `spot_heal` 的 0/-1 不能混用；`wand_trace` 还有 -2（轮廓过复杂）。本轮不注入分配失败，不声称覆盖 OOM。

## 像素和测试边界

RGBA 为每像素 4 字节、预乘 sRGB 通道、行从上到下，stride 单位是字节。Gray 的 stride 也是字节。不能直接把框架的 BGRA 位图传进来。

| 文件/API | 布局与本轮检查 |
| --- | --- |
| BrushPixels | RGBA 与 Gray 可各有不同 padding；alpha 提取、空/非空半开边界、取消预乘后恢复 alpha |
| AdjustPixels | Gradient Map 为 256×3 straight sRGB 表；恒红映射保留 alpha；grain 按文档坐标验证整图/子块一致；clamp 按 packed count |
| LevelsPixels | 按 packed 像素 count，不能含 RGBA 行尾 padding；恒等/白色 LUT、alpha 与 packed coverage 加权 histogram |
| NoisePixels | 四种 uniform/Gaussian × color/mono 组合的重复确定性、alpha 保留与预乘范围 |
| LensPixels | 相同 src/dst stride；零畸变逐字节一致；边缘部分越界按双线性采样得到半透明 |
| ContentFill | RGBA stride 与 maskStride 独立；单点补色、无可用源返回 0 |
| HealPixels | coverage 必须 packed width×height，不能复用 RGBA stride；三模式空 coverage、均匀图、缺陷修复重复确定性、未选区域不变 |
| WandPixels | 两行 RGBA 有 padding，输出 mask 必须 packed；连续/不连续选择、三个对角相接轮廓与空轮廓的计数/坐标/释放；bounds 的 gray 可含 padding |

测试使用已知输入的行为断言，Release 不依赖会被 NDEBUG 关闭的 assert。首次运行修正了两个测试假设：轻度负畸变的角点部分覆盖应有 alpha；Create Texture 会从缺陷周边估计颗粒，不能要求与平色填充逐像素相同。没有因此修改算法。

仍缺：Windows 编译/运行、实际 DLL 导出表、C# P/Invoke pinned buffer/释放、两框架到 C 的 BGRA/RGBA 接入、固定 Mac 参考的 Windows 差异分析、真实分配失败/压力与性能。ctypes 是另一种 FFI 的冒烟检查，不能记为 P/Invoke 完成。W-006、M0/M1 保持未验收。
