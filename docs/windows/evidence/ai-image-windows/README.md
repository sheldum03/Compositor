# Windows AI 完整图像流程与 Mac 读回（2026-09-23）

**固定 NASA 照片在 Windows 11 上完成预处理 → 原生 CPU 推理 → Gray8 蒙版 → 分离图像/蒙版的 v8 工程，原始结果独立复核通过。该 Windows 工程也通过实际 Mac reader/exporter 的蒙版及保存重开测试。** 这补齐一张真实图像的可行性证据，未关闭质量、模型分发、GUI 事务或 Windows 1.0 门槛。

## 执行与身份

复用已完成五轮活动取消的原生探针及 ONNX Runtime 1.30.0，不修改 C++ 推理程序、冻结照片、模型或 `screen.py`。原生包 SHA-256 为 `c51297c2a6a614554ea10bab686e8bb7f8603d21024255bf40ab61a5c75e9203`。完整 harness 来自 `b9345e3`。

检查用 Python 3.11.9 x64 embeddable、ONNX 1.17.0、NumPy 2.0.2、Pillow 11.3.0、protobuf 6.33.6 放在本次输出下的新目录。Python 官方 ZIP 的发布页 MD5 匹配，另冻结 SHA-256；四个 wheel 的大小及 SHA-256 均匹配 PyPI 版本元数据。解释器使用显式 `._pth`，不启用用户 site、不安装到现有 Python、不修改系统 PATH，也不在 Windows 下载依赖。该版本沿用已有 CI 实验的固定配置，不作为生产 Python 版本建议。来源：[Python 发布页](https://www.python.org/downloads/release/python-3119/)、[嵌入包说明](https://docs.python.org/3.11/using/windows.html#the-embeddable-package)；实际 wheel URL/hash 见清单。

`CompositorAiImage.zip` 为 54,856,513 B、44 条目，SHA-256 `43255c98d8f7db21bd17a28100e13fc03e9f6f37c2af2e989fe17c1c2ac2704a`，与 Windows 终端核验一致。包不含 `.onnx`；runner 验证远端已有模型后复制到本次私有输出目录，模型不进入结果归档。独立解包检查了 66 个 AMD64 二进制，已有输出及改动 payload 均拒绝。

在 `C:\Users\Administrator\Desktop\CompositorTest` 解包至 `ai-image-kit`，执行 `python ai-image-kit/run.py`。原输出 `ai-image-20260923-153903`，harness PID 45296、退出码 0、stderr 为空。结果 ZIP 为 3,697,251 B，SHA-256 `e555a25b3ae633cb70dedd792830e892c69d9fa115b10b752e17dc8947fb1037`；终端、本地身份及 CRC 一致，不含权重。之前两条哈希查询因 UU 漏送符号未成功，未启动测试；随后简单 `Get-FileHash` 成功，不把命令输入失败记作推理失败。

## 独立核验

- Windows 生成的 float32 输入与此前固定张量逐字节一致，SHA-256 `d085f68b79a2a856160a0ea207180658a3d58da7d77feaca7b1fffcca4136951`。
- 普通路径和中文/空格/Emoji 路径的预测一致，且与此前 Windows 五轮结果一致；SHA-256 `c3a085aa0b8bbce9edefef1adb6c7853c628837558b8e3ef9df7371a8993f8e5`。两份原始 profile 各有 1,460 个 CPU 算子事件，未出现其他 provider。
- 独立从原始预测复算的 512×512 Gray8 蒙版与输出逐像素相同；108,712 个零覆盖、395 个全覆盖、153,037 个中间值。
- cutout RGB 精确保留输入图像，alpha 精确等于蒙版；工程的原图与蒙版文件分别与输入/输出匹配。
- 原 harness 记录短张量、NaN、缺失模型、损坏模型退出 1 且无最终 mask；已有输出拒绝并保持预测不变。独立检查确认归档未含这些失败路径的 mask；未额外声称重放了失败过程。

原生加载为 53.0361 ms，三次推理为 407.4727 / 406.9732 / 363.2715 ms。它们只覆盖原生阶段，不包含 Python 图像前后处理、GUI、最终保存时间；不是产品性能门槛。

Mac 使用 `TEST_RUNNER_AI_SCREENING_DIR=<本次 Windows screening>` 运行 `WindowsFixtureTests/aiMaskPackageRetainsEditableCoverage()`，实际 **1 passed / 0 failed / 0 skipped**，测试函数 0.109 秒。它验证 v8 原图/Gray8 蒙版分开可读、导出 alpha 精确、禁用蒙版恢复原图、保存重开保留蒙版与原图。XCTest 兼容层的“0 tests”不是本次结论；Swift Testing 的实际测试及 xcresult 摘要均为 1 项通过。

## 限制与证据

目视检查仍有左侧旗帜/背景残留及边缘瑕疵，与既有单张 Mac 可行性观察一致。不能用这张无标注样本宣布质量通过。本轮原始 `screening.json` 中通用 status 文案仍写“local feasibility”，但 `windowsExecuted=true`、执行日志及 Windows 路径明确记录实际 Windows 执行；保留原文，不修改历史报告。活动取消本轮为 false，另见[已完成的 Windows 五轮证据](../ai-active-cancellation/windows-review.md)。

模型准确权重授权、成功获取路径、质量样本集、已有蒙版/撤销/取消/过期文档事务、GPU/资源及最终安装包仍需后续完成。Python 只是本次检查工具，不是新增桌面产品运行依赖。

[包身份](package-identity.json)、[包清单和来源](package-manifest.json)、[准备检查](preparation-checks.json)、[原始报告](screening.json)、[独立复核及文件 hash](independent-review.json)、[Mac 实际测试摘要](mac-readback-summary.json)、[私有实验 runner](run.py)。完整 [Windows 原始 ZIP](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/ai-image-windows-preparation/ai-image-20260923-153903.zip)、[可视对照](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/ai-image-windows-preparation/raw/screening/contact-sheet.png)、[Mac 日志](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/ai-image-windows-preparation/mac-readback.log)和 xcresult 保存在本任务证据目录。
