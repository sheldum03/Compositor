# M2 生产 v8 工程的真实 Mac 读回（2026-10-02）

输入来自生产 `Compositor.Workflow.Checks` 在 macOS .NET 10.0.401 的实际输出 `/tmp/compositor-workflow-check-20261002-r3`；不是手写或删改字段后的兼容样本。三组配对为 `Image.comp` / `memory-export.png`、`Edited.comp` / `export.png`、`Oriented.comp` / `oriented-export.png`，分别覆盖撤销后恢复原像素、保存像素编辑、EXIF 6 方向归一化。**该生产代码尚未在 Windows 执行，本页不算 Windows 实机验收。**

## Mac 应用实际通过

新增 `WindowsProductionWorkflowTests`，运行实际 `ProjectStore.shared.load`、`ImageExporter.shared.render`、`EditorSession` 改名/撤销/重做及真实保存/重开。三个 v8 工程都保持尺寸、活动图层、图层身份与原始像素；Mac 重开后的改名和文档/图层 ID 也符合预期。不是只用通用 JSON/PNG 库解析。

`xcodebuild test` 退出 0。Swift Testing 为 **1 个参数化测试、3 个 case 通过、0 失败、0 跳过**；[xcresult 摘要](production-workflow-mac-readback/xcresult-summary.json)记录同样结果。[原始测试尾段](production-workflow-mac-readback/readback.txt)中 XCTest 的“0 tests”是旧测试框架的空汇总，不代替后续实际执行的 Swift Testing 结果。

输入和 Mac 再保存输出的 [15 文件归档](production-workflow-mac-readback/workflow-roundtrip.zip)及[逐文件 SHA-256](production-workflow-mac-readback/sha256.json)已固定；没有覆盖原输出。完整构建日志、两次 xcresult 和原始输出保留在 `/Users/admin/.codex/visualizations/2026/10/02/production-workflow-mac-readback`。第二次测试增加保留 Mac 输出，原三项断言继续通过。

复现：将 ZIP 解压到新目录，以 `TEST_RUNNER_WINDOWS_PRODUCTION_WORKFLOW_DIR=<解压目录>/inputs` 运行 `xcodebuild test -project Compositor.xcodeproj -scheme Compositor -configuration Debug -destination platform=macOS -parallel-testing-enabled NO -only-testing:CompositorTests/WindowsProductionWorkflowTests CODE_SIGNING_ALLOWED=NO`。如需保留新的 Mac 输出，另设 `TEST_RUNNER_MAC_PRODUCTION_ROUNDTRIP_DIR=<新的输出目录>`；不设置时测试自动清理自己的临时工程。

## 反向兼容发现的缺口

对上述真实 Mac 保存输出调用当前 C# `ProjectStore.Open`，三个工程均返回 `CanEdit=false`，独立探针退出 1，见[反向失败记录](production-workflow-mac-readback/reverse-before.txt)。Mac 常规保存显式写入 `blendMode: "Normal"`、`opacity: 1`、`isGroup: false`，当前单图层白名单尚不接受这些语义等价的默认值。这个结果不推翻 Mac 方向的通过，但说明双向可编辑链路尚未完成。

已交给生产整合任务修复：保留默认字段并识别其语义，同时继续拒绝尚未支持的混合、不透明度和分组；必须用原始 Mac 输出重新执行编辑/保存/导出，不能删字段绕过。完整多图层、蒙版、文字及所有 v1–8 语义仍按原 M2/M6 计划推进，不以这三个单图层样本代替。
