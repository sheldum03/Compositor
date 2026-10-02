# M2 两层 Normal 合成的真实 Mac 对照（2026-10-02）

以 `b26d34a` 生产路径生成的固定 `Flat.comp` 与 `composite-csharp.png` 为输入，新增 `WindowsProductionWorkflowTests.productionFlatNormalMatchesMacRenderer`。测试使用真实 Mac `ProjectStore.shared.load` 和 `ImageExporter.shared.render`，再将两端图像通过相同 `BrushRaster` 上下文归一化为预乘像素后比较。没有手写替代 Mac 合成器，也没有修改输入图层以规避差异。

输入为 300×300、两层全画布 Normal 位图，覆盖半透明、完全透明、完全遮挡以及跨 256 像素瓦片边界；这是[基础合成切片](production-raster-composite-m2.md)的限定场景。实际比较 **360,000 个通道，最大误差 0，变化通道 0**，见[像素报告](production-flat-normal-mac-readback/comparison.json)。[六文件归档](production-flat-normal-mac-readback/comparison-fixtures.zip)包含原始工程、C# 参考 PNG、Mac 实际 PNG 和比较报告，[SHA-256 清单](production-flat-normal-mac-readback/sha256.json)固定所有输入输出。

运行整组 `WindowsProductionWorkflowTests` 时，新合成检查和原有三个单图层生产工程的编辑/保存往返均通过。Swift Testing 为 **2 个测试函数、4 次 case 执行、0 失败、0 跳过**；[xcresult 摘要](production-flat-normal-mac-readback/suite-readback-summary.json)以函数计数为 2，[测试输出](production-flat-normal-mac-readback/test-output.txt)明确列出参数化测试的 3 个 case。进程退出 0。

首轮仅用未带括号的方法名作为 `-only-testing` 过滤器，虽然 xcodebuild 返回 0，但[摘要](production-flat-normal-mac-readback/readback-summary.json)实际执行 0 个测试，故未采用该轮作为通过证据。随后改为套件过滤并核对真实测试数后得到上述结果。完整日志、两个 xcresult 和原始输出保留在 `/Users/admin/.codex/visualizations/2026/10/02/production-flat-normal-mac-validation`。

复现时将归档解压到新目录，设置 `TEST_RUNNER_WINDOWS_PRODUCTION_FLAT_DIR=<解压目录>/inputs`；同时设置 `TEST_RUNNER_WINDOWS_PRODUCTION_WORKFLOW_DIR` 指向[单图层往返归档](production-workflow-mac-readback.md)的 `inputs`，使用 `xcodebuild test -project Compositor.xcodeproj -scheme Compositor -configuration Debug -destination platform=macOS -parallel-testing-enabled NO -only-testing:CompositorTests/WindowsProductionWorkflowTests CODE_SIGNING_ALLOWED=NO`。可选 `TEST_RUNNER_MAC_PRODUCTION_FLAT_OUTPUT_DIR=<不存在的新目录>` 保留新生成的 PNG 和报告。

**两端代码均在 macOS arm64 执行。** 本项不证明 Windows 平台结果、其他混合模式、蒙版/组/变换、实际画布预览或性能。r4 源码包固定在 `1e41027`，不含后续多层合成代码，不能用 r4 结果关闭本项的平台验证，也不关闭 W-012。
