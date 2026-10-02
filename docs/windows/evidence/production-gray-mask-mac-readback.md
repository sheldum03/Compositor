# M2 Gray8 蒙版的真实 Mac 渲染对照（2026-10-02）

以 `67d8b3a` 生产路径生成的固定 `Masked.comp` 和 `masked-composite-csharp.png` 为输入，由真实 Mac `ProjectStore.shared.load` 与 `ImageExporter.shared.render` 加载并渲染。`WindowsProductionWorkflowTests.productionGrayMaskMatchesMacRenderer` 检查两层、一个蒙版，并通过同一 `BrushRaster` 上下文归一化两端预乘像素后逐通道比较。

样本为 300×300、两层全画布 Normal 位图，顶层启用默认位置 Gray8 蒙版，覆盖率包含 0/128/255。实际比较 **360,000 个通道，最大误差 0，变化通道 0**，见[像素报告](production-gray-mask-mac-readback/comparison.json)。[七文件归档](production-gray-mask-mac-readback/comparison-fixtures.zip)包含原始工程、C# 参考 PNG、Mac 实际 PNG 和比较报告；[SHA-256 清单](production-gray-mask-mac-readback/sha256.json)固定输入与输出。C# 参考 PNG SHA-256 为 `b876a949a071b5fa508c1f383ff717917968df163cd9a0aa1f606d9e5ece9dc6`。

整组测试还重跑无蒙版两层合成和三个单图层工程的编辑/保存往返。Swift Testing 为 **3 个测试函数、5 次 case 执行、0 失败、0 跳过**；[xcresult 摘要](production-gray-mask-mac-readback/suite-readback-summary.json)的函数计数为 3、设备执行计数为 5，[测试输出](production-gray-mask-mac-readback/test-output.txt)包含实际执行情况。`xcodebuild` 退出 0，完整日志及结果包位于 `/Users/admin/.codex/visualizations/2026/10/02/production-gray-mask-mac-validation`。

复现时解压归档，将 `TEST_RUNNER_WINDOWS_PRODUCTION_MASK_DIR` 指向其中的 `inputs`，将 `TEST_RUNNER_MAC_PRODUCTION_MASK_OUTPUT_DIR` 指向不存在的新输出目录；同时设置原有 `TEST_RUNNER_WINDOWS_PRODUCTION_FLAT_DIR` 和 `TEST_RUNNER_WINDOWS_PRODUCTION_WORKFLOW_DIR`，运行：

```sh
xcodebuild test -project Compositor.xcodeproj -scheme Compositor \
  -configuration Debug -destination platform=macOS -parallel-testing-enabled NO \
  -only-testing:CompositorTests/WindowsProductionWorkflowTests CODE_SIGNING_ALLOWED=NO
```

本轮在 macOS arm64 / macOS 26.5.1 执行，不是 Windows 实机结果。只证明固定全画布 Normal/默认位置蒙版场景；独立蒙版位置、缩放/采样、组/剪贴、蒙版编辑保存、其他混合模式及生产性能仍未验收。r4 固定包不含该新增生产代码，本项不能关闭 W-012/W-013。
