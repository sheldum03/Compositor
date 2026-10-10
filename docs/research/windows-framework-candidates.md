# Windows 移植：界面、画布与 GPU 开源候选评估

检索日期：2026-09-20。范围：GitHub 官方仓库、许可证文件、发布记录和项目官方文档；本报告仅做技术调查，没有安装候选依赖、修改产品代码或在 Windows 上运行 benchmark。文中的“推荐/风险/适合”是结合本项目的工程判断，不是性能测量结果。

## 结论

值得优先验证两条路线：**Qt Widgets + 自绘画布 + 现有 C 算法**，或 **Avalonia + SkiaSharp + 现有 C 算法**。前者适合愿意维护 C++ 图像核心的团队；后者适合熟悉 C#、更重视桌面 UI 开发效率的团队。没有团队技术偏好和 Windows 实测之前，不宜宣布其中一个全面胜出。

Skia 是 CoreGraphics 部分能力的候选；wgpu 是 Metal GPU 计算/绘制的候选。它们承担不同职责，不能相互替代，更不是安装后就获得图层、蒙版、撤销、滤镜和色彩一致性的完整编辑器引擎。现阶段也不建议同时引入 Qt、Skia、wgpu 三套渲染基础设施：先让一条最小显示与编辑路径跑通，再根据瓶颈增加组件。

尤其要避免未经验证就组合“wgpu 计算 → CPU 回读 → Skia 重新上传”。两个库都支持 GPU，不意味着它们默认共享纹理、设备、队列和同步对象；若原型需要两套后端，必须单独验证互操作与数据传输成本。

## 本项目对应的真实边界

本地 `Compositor/Rendering/EditorCanvas.swift` 的 `EditorCanvas` 是 `NSViewRepresentable`，`CanvasView` 直接继承 `NSView`，包含窗口焦点、光标、修饰键、拖动、裁剪、选区等逻辑。换 GUI 框架意味着重写这些平台事件绑定，不能保留 SwiftUI 文件然后只修改 import。

`Compositor/Rendering/MetalBrushCoverage.swift` 做的是分块笔刷覆盖率计算：使用永久密度和预览缓冲、16×16 compute 调度、提交后同步等待，再拷贝预览到 `CGContext`。可迁移的是笔刷数学与分块策略；Metal 资源生命周期、shader 语言和与 CGContext 的交接均要改写。尤其不能把 Apple 的 shared buffer 模型机械搬到独立显卡上并假定成本相同。

## GUI 候选

| 候选与一手来源 | 已核实能力与许可证 | 针对此项目的判断与主要成本 |
|---|---|---|
| [Qt / qtbase](https://github.com/qt/qtbase) | C++，Windows 官方支持；Core/Gui/Widgets 为基础。开源许可要逐模块确认，存在 LGPLv3/GPLv3 及商业许可，不应笼统写成“Qt 都是 LGPL”。[Windows](https://doc.qt.io/qt-6/windows.html)、[许可说明](https://doc.qt.io/qt-6/licensing.html) | **优先原型 A**。适合密集工具栏、菜单、对话框和自绘编辑区；可以直接接现有 C 算法。代价是 Swift UI/会话绑定重写，以及 C++ 内存管理和 Windows 输入适配。Qt 并不自带本项目的图像编辑语义。 |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) + [SkiaSharp](https://github.com/mono/SkiaSharp) | C#/XAML，支持 Windows；Avalonia 核心与 SkiaSharp 为 MIT。[Avalonia LICENSE](https://github.com/AvaloniaUI/Avalonia/blob/main/licence.md)、[SkiaSharp LICENSE](https://github.com/mono/SkiaSharp/blob/main/LICENSE.txt) | **优先原型 B**。适合 .NET 团队快速重建桌面 UI，用专用自绘控件承载画布。C 算法需通过本地 DLL/PInvoke 适配；CPU/GPU 缓冲和对象生命周期仍需设计。不要把每个像素、笔刷印记或图层瓦片做成普通 UI 控件。商业 XPF/附加控件不能由核心 MIT 许可推断为免费。 |
| [Slint](https://github.com/slint-ui/slint) | Rust/C++ + `.slint`，支持 Windows，提供 Skia/软件等 renderer。框架可选 GPLv3、Royalty-free 或商业许可；示例 MIT 不等于框架 MIT。[LICENSE](https://github.com/slint-ui/slint/blob/master/LICENSE.md) | **保留候选**。适合明确选择 Rust/C++ 和声明式 UI 的团队。其 Royalty-free 桌面方案有使用披露要求。此次没有验证成熟图像编辑器所需的停靠面板、笔输入与自定义 GPU 画布的完整组合，不把“轻量”宣传等同于更低迁移成本。 |
| [Tauri](https://github.com/tauri-apps/tauri) | Web UI + Rust；Windows 使用 WebView2；代码采用 MIT 或 MIT/Apache-2.0（按组件）。仓库具备 Windows 安装器能力。 | **条件候选**。团队偏前端、未来还需 Web 版时值得评估。必须另做 Canvas/WebGPU/WASM 或原生图像核心；每次指针事件通过 IPC 来回传整张位图应避免。WebView2 版本、纹理上传、原生与 Web 数据交接都需要实测；本轮未证明它比原生 UI 更慢，也未证明“更小”意味着画布更快。 |
| [Electron](https://github.com/electron/electron) | JS/HTML/CSS，基于 Chromium + Node，MIT；官方提供 Windows x64/arm64 二进制。[LICENSE](https://github.com/electron/electron/blob/main/LICENSE) | **条件候选**。固定随包发布的浏览器版本有利于统一 Web 能力；相应承担浏览器运行时分发与升级。若需要原生 C 核心还要做 native addon/WASM 边界。没有数据支持给出具体安装体积或内存差距。 |
| [Swift/Win32](https://github.com/compnerd/swift-win32) | Swift 对 Win32 的 MVC 包装，BSD-3-Clause。[LICENSE](https://github.com/compnerd/swift-win32/blob/main/LICENSE.txt)；README 明示仍有未实现接口，调用可能终止程序。 | **不作默认路线**。能保留语言，不代表能保留 AppKit/SwiftUI/CoreGraphics。仍须补 UI 和图像基础设施；此次未核实最新维护日期，不能据此说项目已停止维护，但已知接口缺口足以构成较高集成风险。 |

Qt 官方 `QTabletEvent` 提供压力、倾角、旋转，并说明平滑绘图应处理高频 tablet 事件而非只依赖合成鼠标事件。**这支持把 Qt 列为笔刷输入候选，但不代表所有 Windows Ink/Wintab 驱动组合均已验证。** 原型应使用实际数位板测试。[QTabletEvent](https://doc.qt.io/qt-6/qtabletevent.html)

Avalonia 官方文档支持 `ICustomDrawOperation` 直接使用 SkiaSharp，以及通过 composition surface 导入外部 GPU 图像。文档同时说明 `RenderTargetBitmap` 使用软件渲染，GPU 专用控件未必能正确捕获；因此导出应该基于文档/图像引擎，而不是依赖 UI 截图。[自定义绘制与 GPU 互操作](https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering)

## 绘制、合成和 GPU 候选

| 候选与源码 | 能覆盖的职责 | 不能直接替换的部分与选择意见 |
|---|---|---|
| [Skia](https://github.com/google/skia) / SkiaSharp | C++ 2D 绘图库；路径、图片、变换、混合、部分滤镜与色彩管理；SkiaSharp 为 .NET 接入。Skia BSD-3-Clause。[LICENSE](https://github.com/google/skia/blob/main/LICENSE)、[API](https://skia.org/docs/user/api/) | **CoreGraphics 候选优先级高**。但 `CGImage/CGContext` 数据所有权、字节排列、预乘 alpha、色域和插值需映射。CoreImage 滤镜名称不能一一替换；Skia 不为本项目提供完整非破坏图层/蒙版/撤销模型。GPU 后端和绑定实际开放的能力需按固定版本确认。 |
| [wgpu](https://github.com/gfx-rs/wgpu) / [wgpu-native](https://github.com/gfx-rs/wgpu-native) | Rust 原生跨平台图形与计算 API；Windows D3D12/Vulkan 为一等支持，C 接入用 wgpu-native；MIT/Apache-2.0。[MIT](https://github.com/gfx-rs/wgpu/blob/trunk/LICENSE.MIT)、[Apache](https://github.com/gfx-rs/wgpu/blob/trunk/LICENSE.APACHE) | **Metal compute 的首选跨平台验证对象之一**。现有笔刷 coverage 算法可移写 WGSL；需要重写缓冲、dispatch、同步、readback。它不是路径/文本/ICC/图像滤镜库。非必要时不要为了只加速一个笔刷核先把全编辑器改成 Rust。 |
| Qt `QRhiWidget` / `QRhi` | Qt 内嵌 GPU 画布，支持 D3D11/12、Vulkan 等；Windows 默认 D3D11，支持 RGBA8/16F/32F 等目标格式。[官方 API](https://doc.qt.io/qt-6/qrhiwidget.html) | **Qt 路线中的备选 GPU 实现**。可减少另接 wgpu 的语言栈，但底层 QRhi 类没有源码/二进制兼容保证；需锁定 Qt 版本。控件先绘制到 backing texture，再与 UI 合成，存在额外资源开销；复杂外部引擎的设备配置也受限制。不能据此声称零拷贝或最快。 |
| [Blend2D](https://github.com/blend2d/blend2d) | C/C++ 2D CPU 渲染，JIT/SIMD、多线程、路径、渐变、像素合成，Zlib 许可；官方有 Windows VS 构建文件。[官网](https://blend2d.com/)、[LICENSE](https://github.com/blend2d/blend2d/blob/master/LICENSE.md) | **CPU 基线/回退候选**。C API 对现有算法衔接友好，但不是 Metal GPU 替代品，也不是完整 CoreImage 替代品。没有本项目场景 benchmark，不采用官网性能图来推算大图层编辑速度。维护证据弱于前述主要候选，详见下表。 |

Skia 官方提供 Vulkan 后端及颜色管理文档，但这只能证明相关能力存在。迁移后是否与 macOS 输出一致，要测试 source/destination 色彩空间、预乘 alpha、透明边缘和混合模式；不能仅凭屏幕看上去相似判定成功。[Vulkan](https://skia.org/docs/user/special/vulkan/)、[色彩管理](https://skia.org/docs/user/color/)

## 维护与版本证据快照

下列版本是本轮网页实际返回的标签或文档版本，不是推荐立即升级到该版本。GitHub 页面存在抓取缓存；未显示完整时间或读取失败时不补造日期，也不根据 star 数排名。

| 项目 | 本轮可见证据 | 含义与限制 |
|---|---|---|
| Qt | 官方 API 文档标为 6.11.2；[qtbase](https://github.com/qt/qtbase) 仍提供源码，贡献经 Gerrit 而非 GitHub PR | 不用 GitHub releases 是否为空判断停止维护；正式选型还应定版本与支持周期 |
| Avalonia | [releases](https://github.com/AvaloniaUI/Avalonia/releases) 中 12.1.2 标为 Latest，9 月 2 日；11.3.22 为 9 月 11 日 | 新旧分支均可见更新；修复记录包含 GPU 互操作和资源释放，不是稳定性零风险保证 |
| Skia / SkiaSharp | [Skia commits](https://github.com/google/skia/commits/main/) 可见 2026-09-11；[SkiaSharp releases](https://github.com/mono/SkiaSharp/releases) 可见 4.151.3、4.152.1 于 9 月 18 日，以及后续 RC/Preview | 活跃；Skia 原生 API 与绑定发布不同步，不能随意混用版本 |
| wgpu | [releases](https://github.com/gfx-rs/wgpu/releases) v30.0.1 标为 Latest，8 月 22 日；大版本说明有 Breaking Changes | 活跃；需锁版本、验证 shader 与 GUI 纹理互操作 |
| Slint | [releases](https://github.com/slint-ui/slint/releases) 1.18.0，9 月 16 日，有 Windows MSVC x64/arm64 SDK | 活跃且有 Windows 分发证据，不代表编辑器功能组合已经验证 |
| Tauri | [releases](https://github.com/tauri-apps/tauri/releases) 可见 2.11.6，9 月 19 日；另有 3.0.0-alpha | 稳定线与实验线同时存在，应区分；本轮评估不依赖 alpha 特性 |
| Electron | [releases](https://github.com/electron/electron/releases) 44.4.3 标为 Latest，9 月 18 日，另有 45 alpha | 活跃；需持续跟随浏览器安全维护 |
| Blend2D | [commits](https://github.com/blend2d/blend2d/commits/master/) 返回顶部为 2025-11-29 文档变更，2025-11-03 v0.21.2；官网明确提出资金需求 | 当前检索证据不足以确认 2026 持续发布；风险较高，但不能据缓存直接判定停止维护 |
| Swift/Win32 | README/许可可读；commit 页本轮读取失败 | 当前维护时效未核实；明确记录未知，不冒充已审计 |

## 进入实现前应做的最小验证

1. **共用一份输入和预期结果**：选一个现有工程，覆盖透明图层、蒙版、旋转缩放、代表性混合模式；保存 macOS 导出结果用于像素差异分析。不要把换框架与算法重写混在同一次实验里。
2. **先 CPU，再按证据加 GPU**：优先接现有 C 算法，完成一个画布、图层叠加、蒙版笔刷、撤销与导出。GPU 若确为瓶颈，再把 `MetalBrushCoverage` 的分块 kernel 单独移植到 wgpu 或 Qt GPU 管线。
3. **按同一条件比较**：建议准备 4K/8K 图、多图层与局部蒙版场景，在同一 Windows 机器记录指针到显示延迟的 p50/p95、帧时间、CPU/GPU 占用、峰值 RAM/VRAM、笔刷时的上传/回读字节量。这是建议的验证集合，不是本项目已证实的目标上限。
4. **检查压力事件与 Windows 集成**：数位板快速曲线、笔压/倾角、不同 DPI、多显示器、快捷键、拖放、剪贴板和文件对话框。不得只用鼠标演示认定绘画交互合格。
5. **检查大图内存**：例如 8192×8192 的 RGBA8 单份平面为 256 MiB（宽×高×4），还不含蒙版、撤销快照与 GPU 副本。框架选择不能消除这一数据量，应保留/验证分块与脏区更新策略。

本轮没有建立任何性能排名、复用百分比、开发工期或成本报价；这些必须在上述原型和团队能力明确后才能估算。
