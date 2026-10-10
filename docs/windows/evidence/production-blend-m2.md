# M2 平面透明度与 13 种混合模式（2026-10-05）

生产核心现支持 `Normal`、`Multiply`、`Screen`、`Overlay`、`Darken`、`Lighten`、`Difference`、`Color Dodge`、`Color Burn`、`Hue`、`Saturation`、`Color`、`Luminosity` 共 13 个模式，以及 0–1 的图层透明度。Core 公开只读 `ProjectSession.SupportedBlendModes`、`SetLayerOpacity(Guid,double)`、`SetLayerBlendMode(Guid,string)`；`FlatLayerInfo` 保留原三参数构造，同时提供 `Opacity` 和 `BlendMode` init 属性。非法模式、非有限或越界透明度、外部图层 ID 均拒绝且不增加历史。属性、像素、名称、显隐和顺序事务共用同一撤销/重做及保存点；临时笔划预览继续调用相同的 `LayerCompositor` 路径。

平面可编辑范围保持：v8、各层全画布、无组/父层、无蒙版/调整/变换、总源像素不超过 100 MP。v1 及不满足该范围的已有工程仍只读，保存不会静默摊平未支持语义。旧的 Normal 默认字段可以缺省，非默认外观在 v8 明确保存；当前模式名与 Mac `LayerBlendMode.allCases` 一一对应。M0 的 B01–B13 原始工程保留只读，因为它们含 pass-through 组、64×48→44×32 缩放和偏移，不能被误当作平面模式样本。

## 本地验证

固定 SDK 10.0.401、macOS arm64、Release 的 Workflow.Checks：13 个模式在 300×257（跨 256 瓦片边界）工程中分别保存、预览、导出、重开；0/0.25/0.55/1 透明度、隐藏、重排、逐层像素编辑、改名和混合属性联合撤销/重做均通过；底层未编辑资产保持原字节。完整 Workflow 原有导入、空文档、图层结构、Gray8 蒙版和 Mac 输入继续退出 0。Core.Smoke 的原生 C、历史预算和旧保存保护也退出 0。

本地检查没有用统一 SSIM 或未经批准的阈值放行。`LayerCompositor` 输出与同一 Skia 后端的 PNG/重开逐通道一致；“WholeM1”帮助器保留作算法探针，跨后端判断看独立 Mac 套件。

## Mac 13 case 对照结果

新增 `CompositorTests/WindowsProductionBlendTests.swift`，独立 `derivedDataPath` 与 `resultBundlePath`，只筛 `WindowsProductionBlendTests`，环境变量为 `TEST_RUNNER_WINDOWS_PRODUCTION_BLEND_DIR` 和 `TEST_RUNNER_MAC_PRODUCTION_BLEND_OUTPUT_DIR`。它读取每个正式 C# `.comp` 与导出的 PNG，经真实 Mac `ProjectStore` / `ImageExporter` 重开和保存；逐模式保存 Mac RGBA、正式 PNG 解码 RGBA、绝对差 RGBA、直方图及 JSON 报告，不更改正式输入。

`attempt-2/blend-2.xcresult` 的 `xcresulttool get test-results summary`：13 个动态参数实际执行、0 跳过、0 expected failure；5 个模式逐通道 exact（Multiply、Screen、Overlay、Darken、Lighten），8 个模式仍失败 exact gate，但 alpha 全部一致、最大 RGB 差 1：

| 模式 | 最大通道差 | 差异像素（通道统计见 JSON） |
| --- | ---: | ---: |
| Normal | 1 | 15,372 像素 / 27,844 通道 |
| Difference | 1 | 77,100 / 80,000 |
| Color Dodge | 1 | 24,827 / 24,827 |
| Color Burn | 1 | 15,372 / 30,744 |
| Hue | 1 | 64,745 / 129,490 |
| Saturation | 1 | 77,100 / 157,100 |
| Color | 1 | 77,100 / 154,200 |
| Luminosity | 1 | 64,628 / 114,001 |

完整原始产物在 `/Users/admin/.codex/visualizations/2026/10/05/production-blend-m2/attempt-2/`：`workflow-output-9/blend/`、`mac-output-2/`、`blend-2.xcresult`、`xcode-2.log`。`fixture-adaptation.json` 记录 M0 PNG 来源 SHA-256、300×257 重复/相位适配和原始 B 工程几何限制；`*-comparison.json` 记录每个模式的直方图，`*-mac.rgba` / `*-production-cg.rgba` / `*-absolute-diff.rgba` 保留原始数据。

差异诊断已缩小：输入 PNG 在 Mac 与 C# 解码 RGBA 完全一致；Multiply/Screen/Overlay/Darken/Lighten exact，非零差只发生在混合数学/舍入；尝试 `SKPaint.ColorF` 后结果未关闭其余差异，未放宽门槛。Normal 首像素示例 Mac `[101,98,143,222]`、C# `[100,98,142,222]`；Difference `[83,96,122,222]` 对 `[83,97,122,222]`。这属于观察和未决校准，不是 13 模式验收通过。

该切片已提交的代码只能声明“本地模式属性、历史、保存和同后端一致”；不能声明完整 Mac 逐像素兼容。需继续校准 premultiplied sRGB 混合与平台舍入，或取得明确的模式特定容差决策；随后重新运行同一 13 case 套件。Windows 11 实机、正式窗口模式下拉框、组/蒙版/变换混合及性能仍未验收。
