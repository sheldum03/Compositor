# BiRefNet Lite Windows 原生执行及实际 Mac 工程读回

2026-09-24，固定官方候选模型在 Windows 11 x64 完成完整图像、原生推理、活动取消/同会话恢复和分离蒙版工程检查。取回后的独立复算与真实 Mac reader 检查均通过。**此为 M1 单图可行性证据，不是模型质量、生产集成、发行接受或完整 Windows 1.0。**

## 执行身份

在两个 S05 前瞻进程均结束后，通过 UU 私有文件传输发送 [已核验的测试包](../birefnet-windows-preparation/README.md)及官方模型。两条传输任务均显示已发送；Windows `Get-FileHash` 与本地固定摘要一致。确认 `BiRefNetWindowsKit` 尚不存在后才解包，没有覆盖其他运行目录。

执行 `python BiRefNetWindowsKit/run.py --model BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx`，隔离检查进程 PID **47880**；输出 `C:\Users\Administrator\Desktop\CompositorTest\birefnet-image-20260924-155954`。运行包逐项核验、固定模型核验、隔离 Python 3.11.9 / NumPy 2.0.2 / Pillow 11.3.0 解包及原生 ONNX Runtime 1.30.0 流程实际执行。退出码 0、error 为 null、stderr 为空。

原始 ZIP **13,988,393 字节**，SHA-256 `ea0c466bc53971da2de280b6e408fecae12accfa533ea6d31e491b2a615bedaf`。Windows 文件摘要与取回后本地摘要一致，CRC 通过。模型与解释器不在结果归档内。原始输入、预测、profile、图像及日志保留于外部目录 `birefnet-windows-execution-20260924/windows-review/raw`。

## 独立复核

| 检查 | 实测结果 |
| --- | --- |
| 输入/模型/程序身份 | 固定照片及模型摘要匹配，程序对应包内清单；1024×1024 ImageNet 输入张量重新计算逐字节一致 |
| 输出契约 | 单个有限 logits 输出；三次预测相同，预先取消后恢复正常 |
| 活动推理取消 | 被取消调用内有 23 个真实 CPU 算子事件；恢复调用有 3,927 个，恢复预测与基准逐字节一致 |
| 取消耗时 | 请求取消到线程退出约 4.747 ms，仅原生协作取消观察 |
| 执行后端 | profile 仅含 CPUExecutionProvider；完整推理 profile 共 15,708 个算子事件 |
| 蒙版/工程 | sigmoid、Gray8 与双三次缩放独立重算一致；cutout RGB 保留、alpha 等于蒙版；工程原图与蒙版分离且内容精确 |
| 负向输入 | 短张量、NaN、无效模型被拒绝，无预测发布；既有输出保留 |

三次单线程 CPU 推理约 **17.21 / 16.50 / 14.77 秒**，会话加载约 **2.46 秒**。这些为本次可行性观察，未设置或通过产品首次推理/质量/内存预算，不与 Mac 并发环境的计时直接排名。

原始预测 SHA-256 `b8a1fc48ff8499780d9a1cc2b8b40b20768cf7e14f92c75f31f6a1d361290b3d`。与之前 Mac 候选的原始浮点输出不逐字节一致，最大绝对差约 0.000561；最终 512×512 Gray8 蒙版仅 **2 个像素相差 1**。记录见 [跨平台观察](cross-platform-observations.json)，不由此批准通用差分容差。

## 实际 Mac 读回

使用 `TEST_RUNNER_AI_SCREENING_DIR=<本次 Windows screening>`，运行 `WindowsFixtureTests/aiMaskPackageRetainsEditableCoverage()`。Swift Testing 和 xcresult 摘要确认 **1 passed / 0 failed / 0 skipped**，测试函数约 0.121 秒：导出 alpha 精确等于 Windows 蒙版、禁用蒙版恢复原图、保存重开保留蒙版与原图像素。见 [实际读回摘要](mac-readback-summary.json)。

首次调用只设置 `AI_SCREENING_DIR`，Xcode 测试进程未继承该参数，测试被跳过；未计作通过。只修正调用环境后再次运行，原始跳过日志/xcresult 与实际执行日志/xcresult 均保留，没有修改应用代码或 Windows 结果。

## 证据和限制

[Windows 原始报告](screening.json)、[执行身份](execution.json)、[独立复核](independent-review.json)、[取消复核](cancellation-review.json)、[复核脚本](review-results.py)、[传输记录](transfer-observation.json)及 [文件摘要清单](artifact-manifest.json)保存于此。取消复核器在 Mac 上分析 Windows 原始 profile，因此其 `host`/`windowsExecuted=false` 表示复核器所在系统；原始 Windows screening/执行报告与 profile 单独保留，不改写为在 Mac 重新执行推理。

检查脚本先用已有 Mac 归档做控制：原 Windows 守卫拒绝 Mac 结果，显式 Mac 适配副本完成同样的像素与取消复核。这些是复核器控制，不增加 Windows 运行次数。

官方模型来源和 MIT 模型卡见 [来源证据](../birefnet-source-screening/README.md)。本次证明候选在实际 Windows 上可用，未决定最终生产权重、获取/分发安排或 30 张质量样本验收；产品取消事务、错误恢复、资源预算及完整 M6/M7 仍待实现和验证。
