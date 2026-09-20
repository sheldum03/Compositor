# Compositor Windows 版：开源替代方案评估

调研日期：2026-09-20。范围：现有仓库源码、GitHub 上游仓库及官方文档。本轮只做选型研究，没有安装候选库、编写 Windows 版或在 Windows 上测性能。

## 结论与评估前提

**主要 macOS 能力都有开源实现可供替换，但没有找到能让现有 Swift/AppKit 工程直接变成 Windows 应用的完整替代层。** 可复用价值集中在现有算法、文件格式、编辑语义和测试；界面、图像对象、渲染调用、系统集成仍需移植。

以下暂按“Windows 11 x64、本地离线编辑、保留现有图层/蒙版/笔刷行为、与 Mac 工程互读”评估。这是调研基线，不是用户已确认的产品限制；Windows 10、ARM64、商业分发和团队语言经验尚未确定。许可证按具体组件说明，不预设项目必须维持纯 MIT 依赖。

建议保留两条候选路线：

- **Qt Widgets + C/C++ 图像核心**：优先验证。现有 C 算法与 C/C++ 图像库接入自然，适合复杂桌面编辑器；代价是重写 Swift 业务和 UI，并处理 Qt 各模块许可。画布先建立 CPU 正确性基线，GPU 后端由原型结果决定。
- **Avalonia + SkiaSharp + C 算法 DLL**：团队熟悉 C# 时优先级可提高。UI 和 .NET 工具链有吸引力，但频繁跨托管/原生边界、大图内存和 GPU 纹理互操作需要实测。

不建议一开始把 Qt、Skia、wgpu、OpenCV、libvips 全部引入。它们存在重叠；多套像素缓冲和 GPU 设备之间的转换，可能抵消各库的性能优势。GUI 与 GPU 候选的逐项来源见 [框架专项调研](windows-framework-candidates.md)。

“推荐”均为基于源码和公开能力的工程判断，不代表已经完成移植验证。未用 Star 数排名，也未用上游宣传代替本项目性能数据。

## 1. 当前项目实际需要替换什么

扫描 `Compositor/` 得到 92 个 Swift 文件、8 个 C 文件。直接 import 数：AppKit 49、SwiftUI 32、CoreGraphics 19、CoreImage 11、ImageIO 4、Accelerate 2、Vision 1、Metal 1；同一文件可能重复计入多个框架。此统计只描述耦合范围，不能换算成代码复用率或工期。

| 模块 | 当前源码证据 | 移植要求 |
| --- | --- | --- |
| 应用、菜单、面板、图层列表、输入 | `CompositorApp.swift`、`ContentView.swift`、`UI/NativeLayerList.swift`、`UI/FloatingPanel.swift`、`Rendering/EditorCanvas.swift` | 替换 SwiftUI/AppKit，重新实现焦点、快捷键、拖放、DPI、窗口生命周期 |
| 图层合成、形状、坐标变换、蒙版 | `Rendering/LayerRenderer.swift`、`TiledLayerRenderer.swift`、`LiveMaskRenderer.swift` | 替换 CGImage/CGContext；保留组、剪贴蒙版、调整层等组合规则 |
| 实时笔刷 | `Rendering/MetalBrushCoverage.swift`、`Document/BrushStroke.swift` | Metal compute 和资源管理需改写；256×256 瓦片、连续覆盖和临时笔尾语义应保留 |
| 滤镜、透视、混合、LUT | `Document/Filters.swift`、`Distort.swift`、`HueSaturation.swift`、`PixelAdjust.swift`、`Rendering/SeparableBlend.swift` | 替换 Core Image 滤镜并校准参数、透明边界和色彩空间 |
| 缩略图、降采样、反相 | `Rendering/DownsampleCache.swift`、`Document/PixelInvert.swift` | 替换 vImage/Accelerate；质量和边界行为需对照 |
| 一键抠图 | `Document/SubjectRemoval.swift` | 替换 Vision 前景模型；输出仍应是可编辑蒙版 |
| 图像导入导出、ICC、EXIF | `IO/ImageImporter.swift`、`ImageExporter.swift` | 替换 ImageIO/CoreImage 色彩与方向处理，覆盖 JPEG/PNG/TIFF/HEIC |
| 项目存储 | `IO/ProjectStore.swift` | 当前 manifest **v7**、JSON + PNG 目录包；替换 FileWrapper/NSFileCoordinator 的读写实现 |
| 撤销、状态、并发通知 | `Document/DocumentHistory.swift`、`EditorSession.swift`、`ProjectWorkspace.swift` | 移植自身业务语义；UI 框架的通知机制不等于文档事务 |
| 更新与发行 | Sparkle、`appcast.xml`、`scripts/release.sh` | Windows updater、安装器、文件关联与签名流程 |

注意：`docs/project-format.md` 仍描述 v1–6，源码已经写 v7，并包含 adjustment、maskPlacement、shape 等字段。移植以当前源码和测试为准，不能只照旧文档实现。

## 2. 按模块筛选的开源候选

| 替换目标 | 候选与许可证概况 | 适合的部分 | 不覆盖或需要验证的部分 | 本轮判断 |
| --- | --- | --- | --- | --- |
| SwiftUI/AppKit | [Qt qtbase](https://github.com/qt/qtbase)：按模块 LGPL/GPL/商业许可 | 桌面窗口、菜单、树列表、输入、自绘画布宿主 | 不复用 SwiftUI 视图；不自动提供 Compositor 图像引擎 | 首选原型之一 |
| SwiftUI/AppKit | [Avalonia](https://github.com/AvaloniaUI/Avalonia)：核心 MIT | C# UI、数据绑定、自定义画布 | 重写 Swift；原生像素内存与 UI 调度适配 | 首选原型之一 |
| CoreGraphics | [Skia](https://github.com/google/skia)：BSD-3-Clause；[SkiaSharp](https://github.com/mono/SkiaSharp)：MIT 包装层 | 路径、变换、2D 合成、混合和图像绘制 | 不是图层编辑器；要自行实现瓦片、历史、组和调整层；包装层不覆盖所有底层能力 | 强候选，避免与 Qt 渲染无目的重复 |
| CoreGraphics CPU 路径 | [Blend2D](https://github.com/blend2d/blend2d)：Zlib | CPU 2D 栅格化、路径与合成 | 不提供 GPU compute、完整滤镜和编辑器 | CPU 备选 |
| Metal compute | [wgpu](https://github.com/gfx-rs/wgpu)：MIT/Apache-2.0；C/C++ 接入另看 [wgpu-native](https://github.com/gfx-rs/wgpu-native) | 可移植 GPU 计算、纹理和缓冲区 | 必须重写 shader、同步、上传/回读；不是 CoreImage 等价物 | 条件候选，先证明数据通路收益 |
| CoreImage/Accelerate 算子 | [OpenCV](https://github.com/opencv/opencv)：当前版本 Apache-2.0 | 模糊、重采样、透视、卷积、形态学等 | 缺少编辑器事务和色彩管理全链路；不能按同名 API 假定输出相同 | 按需选用 core/imgproc 等模块 |
| 大图导入、缩放、导出 | [libvips](https://github.com/libvips/libvips)：LGPL-2.1-or-later | 按需计算、图像处理管线、格式加载；有 Windows 构建 | 不自动解决逐笔随机写入、画布显示和撤销；后端解码器仍有依赖 | 大图 IO 候选，不默认作为实时笔刷核心 |
| CoreImage 图像处理图 | [GEGL](https://github.com/GNOME/gegl)：库 LGPL-3.0 系列，仓库另含 GPL 内容 | 数据流、浮点、非破坏性图像处理 | C/GLib 生态及处理图接入成本；需按 operations 核查许可；不是 GIMP UI | 若决定整体更换处理图再考虑 |
| Vision 推理 | [ONNX Runtime](https://github.com/microsoft/onnxruntime)：MIT | 本地 C/C++/C# 推理，Windows CPU 与可选执行后端 | 不包含苹果模型；权重、前后处理、算子覆盖、效果另选 | 推荐推理基础设施 |
| Vision 前景模型 | [BiRefNet](https://github.com/ZhengPeng7/BiRefNet)：仓库 MIT；[U²-Net](https://github.com/xuebinqin/U-2-Net)：仓库 Apache-2.0 | 前景分割候选；轻量/质量模型对照 | 具体权重、转换文件的授权与效果需分别验证；不是 Vision 的同一模型 | 进入图片集对比，不承诺等效 |
| ICC 色彩管理 | [Little CMS](https://github.com/mm2/Little-CMS)：核心 MIT | ICC profile 转换 | 不能代替窗口显示色彩管理；还需统一工作空间、alpha 和输出 profile | 推荐补足色彩链路 |
| Sparkle | [WinSparkle](https://github.com/vslavik/winsparkle)：MIT | Windows 自动更新，使用 appcast，有原生 DLL | 仍需 Windows 安装器与平台更新项，不能把 DMG 地址照搬 | Qt/C++ 路线优先候选 |
| 安装及更新 | [Velopack](https://github.com/velopack/velopack)：MIT | 跨平台安装与自动更新框架 | 应用生命周期和包布局需接入；不必再叠加 WinSparkle | .NET 路线优先候选 |

GUI/渲染的 Windows 支持、API 限制和许可来源见 [框架专项调研](windows-framework-candidates.md)。OpenCV 能力依据官方 [滤波](https://docs.opencv.org/4.x/d4/d86/group__imgproc__filter.html) 与 [几何变换](https://docs.opencv.org/4.x/da/d54/group__imgproc__transform.html) 文档；GEGL 的定位依据 [官网](https://www.gegl.org/)。

## 3. 不能忽略的功能差异

### 图层混合与滤镜：相同名字不保证相同结果

项目目前有 13 种混合模式，包括 Hue/Saturation/Color/Luminosity；Color Dodge/Burn 还单独经过 CoreImage。Skia 或 Qt 提供混合枚举，只能说明存在相应基础操作。必须对照以下本项目语义：

- 预乘 alpha 与非预乘转换，RGBA/BGRA 排列；
- sRGB 编码值与线性光计算位置；
- 蒙版、组、剪贴蒙版、调整层的执行顺序；
- 变换后采样、透明像素边缘、模糊溢出原图范围；
- Hue/Saturation 的现有 LUT 和预览/导出一致性。

OpenCV 的滤波接口默认输出与输入等尺寸，边界外推有多种选项。因此替代当前可越出图层边界的模糊，需要主动扩展区域并校准半径，不能只调用 `GaussianBlur`。运动模糊、CIColorCube 和蒙版混合也需要适配层。[官方滤波说明](https://docs.opencv.org/4.x/d4/d86/group__imgproc__filter.html)

### 笔刷：优先保留本项目行为

现有 Metal kernel 使用瓦片局部存储、连续覆盖、永久部分和临时笔尾分离；提交后等待 GPU 完成，再复制结果给 CGContext。这是一个具体的数据通路，不是单纯“GPU 画一个圆”。依据 `MetalBrushCoverage.swift` 和 `docs/brush-performance.md`，移植需同时关注 kernel、数据复制和鼠标抬起时的快照提交。

wgpu 可以提供计算能力，但多一个 GPU 框架也可能多一次回读或上传。**本轮无法证明 wgpu 比 CPU 路径快，也无法证明 Skia 的 GPU 绘制可自动接管该笔刷。** 应先测试当前 4000×4000、800 px 软笔刷场景，再决定是否采用独立 compute 后端。已有 Mac 基准仅作场景定义，不作为 Windows 性能预测。

### C 算法：值得复用，但仍需编译和 ABI 验证

`BrushPixels.c`、`WandPixels.c`、`AdjustPixels.c`、`LevelsPixels.c`、`NoisePixels.c`、`LensPixels.c`、`HealPixels.c`、`ContentFill.c` 及其头文件使用标准 C 头文件，没有发现 Apple 框架 include，属于最直接的复用候选。

仍不能声称“原样全部可编译”：`HealPixels.c` 用到 `M_PI`，头文件还有 `long` 参数；Windows 编译器定义、整数宽度与外部语言 FFI 必须验证。替换 `CGContext` 的上层也要提供相同 stride、预乘 RGBA 和灰度蒙版布局。现有 Swift `GuidedMatte` 的数值运算可移植，CGImage 包装部分需替换。

OpenCV 的 `inpaint` 提供 Telea/Navier–Stokes 算法；现有内容填充使用纹理块搜索，不能认为替换后质量或行为相同。因此魔棒、修复、内容填充等已有 C 实现优先保留，除非对照测试证明替换更有价值。[OpenCV inpaint 文档](https://docs.opencv.org/4.x/d7/d8b/group__photo__inpaint.html)

### 抠图：运行时、模型、权重是三个选择

ONNX Runtime 是执行工具，不是抠图效果本身。BiRefNet 和 U²-Net 可进入样本比较；[rembg](https://github.com/danielgatis/rembg) 可用于研究模型及预处理，但 Python 运行时与打包也有成本，不默认把整个 rembg 带入桌面应用。其 README 明确区分工具与模型权重许可；BRIA RMBG-2.0 不能仅因 rembg 是 MIT 就按 MIT 分发。

一个已确认的兼容性风险：BiRefNet 上游建议关注 opset-22 的 DeformConv；ONNX Runtime 的 DirectML 文档列出的支持范围为 opset 20，并排除 DeformConv。这意味着**不能笼统承诺“下载任意 BiRefNet ONNX，再打开 DirectML 就有 GPU 加速”**。需要固定具体模型及导出方式，检查实际图中的算子；部分模型可用不代表所有变体可用。[BiRefNet ONNX 说明](https://github.com/ZhengPeng7/BiRefNet#onnx-conversion)、[DirectML 文档](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html)

同一官方文档也说明 DirectML 已进入持续维护，新功能方向转向 Windows ML。建议 CPU 推理作为可运行基线，再根据最低 Windows 版本、模型和设备评估 GPU 后端；不要把 DirectML 当作唯一长期路线。保持当前“基础蒙版缓存 + refine/contrast/shift 后处理”有助于减少交互期间重复推理。

## 4. 图像 IO、颜色和项目文件

### ImageIO 替代组合

优先使用所选 GUI/图像框架已有编解码能力，再补缺失项，避免重复打包两三套 JPEG/PNG 解码器。

| 格式/能力 | 开源候选 | 注意事项 |
| --- | --- | --- |
| JPEG | [libjpeg-turbo](https://github.com/libjpeg-turbo/libjpeg-turbo) | C API；有 Windows 构建说明；许可是 IJG/BSD 等组合，不能简单写成 MIT |
| PNG | [libpng](https://github.com/pnggroup/libpng) | PNG Reference Library License；用于工程图像/蒙版需保留灰度和 alpha 语义 |
| TIFF | [libtiff](https://gitlab.com/libtiff/libtiff)，[GitHub SDL 下游副本](https://github.com/libsdl-org/libtiff) | 主上游在 GitLab；本次未完整核验所有 TIFF 压缩依赖及其许可，版本锁定时再核查 |
| HEIC | [libheif](https://github.com/strukturag/libheif) + 选定解码器 | LGPL，编码器/解码器各有许可；当前需求为导入，不必附带 x265 编码器 |
| ICC | [Little CMS](https://github.com/mm2/Little-CMS) | 核心 MIT；库存在并不表示每条导入/导出路径都会正确应用 profile |
| IO 聚合 | [libvips](https://github.com/libvips/libvips) | 适合统一 IO/缩放管线，但实际格式支持由编译时依赖决定 |

依据：[libjpeg-turbo 许可](https://github.com/libjpeg-turbo/libjpeg-turbo/blob/main/LICENSE.md)、[Windows 构建](https://github.com/libjpeg-turbo/libjpeg-turbo/blob/main/BUILDING.md)、[libpng 许可](https://github.com/pnggroup/libpng/blob/libpng18/LICENSE.md)、[libheif 上游说明](https://github.com/strukturag/libheif)。HEIC 的开源组件许可并不自动解决全部编解码专利/再分发问题，需按实际分发方案核实。

libheif 的维护者在 2026-08 的 README 中明确提到维护资源紧张；这不是直接排除理由，但应把及时跟进解码安全修复计入维护成本。不要只因库能打开样例图片就固定旧版本。

### .comp 兼容

当前文件是含 `manifest.json` 和 `images/` 的目录；Mac 把它显示为一个文档包。Windows 版首先可以实现相同目录结构的读写，以验证数据兼容；这不等于已经解决资源管理器双击、文件关联和单文件传输体验。

如果产品要求单文件，应另行设计 ZIP 容器或新扩展名，并给 Mac 端增加对应读取能力。把目录压缩后继续叫 `.comp`，不会自动获得旧版兼容。JSON 在 Qt 可用自身 API，C# 可用标准库；独立 C++ 核心可考虑 MIT 的 [nlohmann/json](https://github.com/nlohmann/json)，但只是序列化工具，不会替代版本验证与迁移。

必须保留 v1–7 校验、图层 UUID、层级顺序、调整层、蒙版链接、独立蒙版变换、形状信息，以及损坏/超限数据拒绝规则。Windows 保存时应重新实现可恢复的临时写入与替换流程，不能假定 NSFileCoordinator 的行为可直接映射为一次 rename。

## 5. 应用状态、系统交互与发布

AppKit 的菜单、剪贴板、拖放、打开/保存对话框、颜色选择器可交给 Qt/Avalonia 对应能力，再做平台行为适配。`UTType` 应映射为扩展名、MIME/格式标识及 Windows 文件关联；macOS 的 Cmd/Option 组合不能机械替换而不检查冲突。

Combine/Observation 的作用可由所选 UI 框架的通知/绑定实现；文档历史中的一次拖拽一次撤销、不可变瓦片共享、取消预览不污染历史等仍是应用代码。当前代码没有完整画布文本层实现，不把未来的 CoreText/TextKit 替代工作计入本轮必需范围。

原生路线可用 WinSparkle 处理更新，再配 [NSIS](https://github.com/nsis-dev/nsis) 生成安装器；NSIS 的 GitHub 是官方镜像，开发入口以其声明为准。[WinSparkle](https://github.com/vslavik/winsparkle) 共享 appcast 格式并提供 x86/x64/ARM64 二进制，但现有 Mac 更新源仍须增加或拆分 Windows 包、版本与签名信息。

.NET 路线可以用 Velopack 统一安装/更新，库采用 [MIT](https://github.com/velopack/velopack/blob/develop/LICENSE)。两套更新机制选其一。任何安装器都不能替代应用构建、依赖打包和 Windows 签名；本轮不制作安装包。

## 6. 完整编辑器项目是否比组件组合更省事

| 项目 | 一手证据 | 与本项目的关系 | 评估 |
| --- | --- | --- | --- |
| [Patchy](https://github.com/SethRobinson/Patchy) | MIT；Qt/C++；可独立构建 core/test；README 列出 0.96、2026-09-17 及 Windows 包 | 工作流接近，有图层/蒙版/文本等；可研究核心模块或局部借用 | 值得认真查看，但未验证其核心是稳定可嵌入 SDK，也未证明与 `.comp` 语义兼容；依赖另见 [第三方清单](https://github.com/SethRobinson/Patchy/blob/main/NOTICE-THIRD-PARTY.md) |
| [Pinta](https://github.com/PintaProject/Pinta) | C#、GTK，Windows 构建说明；原始代码 MIT，图标另有许可 | 可参考工具、撤销和跨平台分发 | 不是 Avalonia 组件，抽取 GTK 耦合代码也有成本；未验证复杂组/调整层等功能等价 |
| [Krita](https://github.com/KDE/krita) | KDE/Qt 跨平台完整绘画应用，GPL 系列许可 | 瓦片和笔刷架构参考价值高 | 不是可直接插入当前 MIT 工程的小组件；若复制/链接其代码，需要按具体文件与组合方式处理许可；直接 fork 是另一条产品路线 |

Patchy 的上游 PSD 对比数据是其作者在特定语料上的测试，不构成本项目质量证明。若目标变成“最快获得一个 Windows 图像编辑器”，fork 现有跨平台编辑器值得另评；当前目标仍是基于 Compositor 保留其体验与工程数据，不能未经决定就更换产品底座。

## 7. 维护与许可证证据的边界

检索到以下发布记录，可说明相关上游并非只有多年未更新的样例；记录是本次可见快照，不作为必须采用的版本建议：

| 项目 | 本次可见证据 | 解释 |
| --- | --- | --- |
| OpenCV | [Releases](https://github.com/opencv/opencv/releases) 列出 4.14.0、5.0.0 与迁移指南 | 新旧主线并存，需要明确选定分支，不能混用不同版本文档 |
| libvips | [v8.18.6 发布记录](https://github.com/libvips/libvips/releases) 标注 8 月 25 日，并链接 Windows 构建 | 有持续修复与 Windows 分发路径 |
| ONNX Runtime | [v1.30.0 发布记录](https://github.com/microsoft/onnxruntime/releases) 标注 9 月 10 日 | 有持续发布，但不同 EP 与模型的兼容性仍须单独验证 |
| Velopack | [Releases](https://github.com/velopack/velopack/releases) 同时有 1.2.0 和 1.2.110 预发布条目 | 不能把页面最上方预发布误当稳定版 |
| BiRefNet | [README](https://github.com/ZhengPeng7/BiRefNet) 有 ONNX 导出资料和模型变体 | 研究模型的更新方式不同于桌面 SDK；需锁定权重与导出参数 |
| libheif | [README](https://github.com/strukturag/libheif) 有 2026 年维护与安全修复说明 | 能力可用，维护资源风险需计入 |

本机终端无法解析 GitHub API 域名，因此本轮使用浏览工具检索 GitHub 与官方文档。部分 GitHub 页面隐藏最新提交或资源列表，未据此编造精确提交时间、维护者数量或 issue 响应速度。没有完成各库依赖树的逐文件许可审计，未完整检出源码，也没有执行二进制安全审计。

LGPL/GPL 不等于“不能商用”；区别在所用模块、链接和分发义务。根仓库标 MIT 也不等于第三方依赖、图标、模型权重全是 MIT。选型时保存具体版本的许可证与第三方清单，避免用一句标签代替核验。

## 8. 建议的验证顺序：每一步都有退出条件

| 步骤 | 最小工作 | 验证方式/决定 |
| --- | --- | --- |
| 1. 建立兼容样本 | 从当前 Mac 版生成含组、13 种混合、蒙版、调整层、形状与变换的 `.comp` 和参考输出 | Windows 读出字段不丢失；v1–7 有代表性往返样本；每类差异单独记录 |
| 2. 编译现有 C 核心 | Windows 编译 8 个 C 文件，统一缓冲区与 FFI 类型 | 对固定输入/随机种子比较输出，验证 stride、灰度和透明度；不以“编译成功”替代正确性 |
| 3. 两个最小 UI 画布原型 | Qt 与 Avalonia 各实现导入、一层画布、缩放平移、笔刷、撤销 | 同一 Windows 机器、同一数据、Release，比较交互延迟、峰值内存、集成复杂度；据此选择一个 |
| 4. 验证合成与性能 | 复用 4K/800 px 软笔刷场景，增加多层与蒙版；只对瓶颈试 GPU | 记录 P50/P95 更新、mouse-up、CPU/GPU 时间、上传/回读量、RSS/显存；帧耗时目标与输入到显示延迟分开 |
| 5. 补齐滤镜和 IO | 半透明边缘、Lanczos、运动模糊、色彩/EXIF、HEIC | 原始像素操作争取逐像素一致；重采样/滤镜先定义误差阈值并人工看边缘，不能一个 SSIM 数值掩盖问题 |
| 6. 对比抠图模型 | 固定人物发丝、毛发、商品、低对比、多主体图片集 | 比较蒙版边缘、首推/热推耗时、模型体积、内存、离线可用性、具体权重许可；不预设优于 Vision |
| 7. 做安装闭环 | 选定安装/更新方案，在无开发环境 Windows 上安装运行 | 文件关联、卸载、更新失败恢复、缺少 GPU/运行库时的行为及用户文件保留 |

原型之前不承诺迁移工期、复用百分比、安装体积或“与 Mac 一样快”。决定路线的关键证据是**编辑结果一致性、笔刷数据通路成本和工程维护负担**，而不是候选库的功能列表长度。
