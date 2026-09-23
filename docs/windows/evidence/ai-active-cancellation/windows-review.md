# Windows 活动推理取消独立复核（2026-09-23）

**五轮 Windows 11 原生 CPU 活动取消与同会话恢复通过，原始证据已取回并独立复核。** 此结论补齐探针的 Windows 运行证据，不关闭 UI 事务、模型质量、M1 或 Windows 1.0 门槛。

使用已冻结的 `3dc81bc` 源码与 [x64 包](windows-build-preparation.md)，在原 Windows 11 Pro 26200 / i9-13900 / 64 GB / 4090D 实机执行；ORT 使用 CPU 路径，未使用显卡。Windows 端校验包清单与已有模型身份后，连续运行五轮，没有失败后重试。十个退出码（五个原生、五个复核）均为 0。

| 轮次 | 取消阶段 CPU 算子事件 | 恢复阶段 CPU 算子事件 | 取消至线程退出 ms |
| --- | ---: | ---: | ---: |
| 1 | 3 | 365 | 0.4066 |
| 2 | 4 | 365 | 0.1032 |
| 3 | 2 | 365 | 3.5345 |
| 4 | 2 | 365 | 9.8285 |
| 5 | 3 | 365 | 0.9677 |

独立复核直接读取原始 profile，要求第一个 `model_run` 包含实际 CPU 算子，第二个为恢复执行；五轮恢复张量均与各自基准逐字节一致，且五轮之间也一致。共同预测 SHA-256 为 `c3a085aa0b8bbce9edefef1adb6c7853c628837558b8e3ef9df7371a8993f8e5`。取消耗时只是五次本机观察，不设产品门槛；不要求 Windows 与 Mac 的预测逐字节相同。

证据包 `AiActiveCancelEvidence-20260923.zip` 为 2,996,827 字节、58 条目，远端/本地 SHA-256 一致，CRC 通过，不含 ONNX 权重。ZIP 原件保留；本地仅规范化反斜线目录分隔符。

- [五轮完整复核](windows-five-trials.json)保留 Windows 复核和 Mac 上独立复核两组结果。复核器的 `host/windowsExecuted` 描述执行复核脚本的主机，因此本地条目为 false；Windows 执行由实际 Windows 记录、OS 身份和原始结果证明，不改写该字段。
- [执行、包及逐文件身份](windows-identity.json)记录原始结果和来源。
- [完整原始包](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/ai-active-windows-evidence/AiActiveCancelEvidence-20260923.zip)。

仍待生产事务测试：取消后原蒙版/历史保留、关闭或切换文档、输入 revision 失效时丢弃结果、进度与错误交互。质量、真实图像前后处理、模型获取及分发许可继续按原门槛处理；此 CPU 探针不构成完整去背景功能。
