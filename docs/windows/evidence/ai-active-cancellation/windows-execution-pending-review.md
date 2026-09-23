# Windows 活动取消：已执行，原始包待取回（2026-09-23）

UU 操作恢复后，已在原 Windows 11 Pro 26200 实机部署并执行 [已冻结测试包](windows-build-preparation.md)。包 SHA-256 和全部清单文件在 Windows 再次核验通过；模型使用既有 `remote-suite/private-model/u2netp.onnx`，4,574,861 字节和固定 SHA-256 在运行前通过检查，没有下载或重新分发权重。

## 已观察的执行结果

- 独立程序目录：`C:\Users\Administrator\Desktop\CompositorTest\ai-active-cancel-20260923`。
- 输出目录：`C:\Users\Administrator\Desktop\CompositorTest\ai-active-results-20260923-094727`。
- 五轮终端均显示 `TRIAL n PASSED`；归档命令返回的五条记录显示 `nativeExit=0`、`reviewExit=0`。
- 每轮使用 `--active-cancel` 及包内 `review-active-cancel.py`，没有自动重试。原生日志、profile、基准/恢复张量、复核结果和执行身份保存在输出目录。
- 原始 ZIP 已由远端归档命令创建：`C:\Users\Administrator\Desktop\CompositorTest\AiActiveCancelEvidence-20260923.zip`。
- 专门的取回目录：`C:\Users\Administrator\Desktop\CompositorTest\ai-active-handoff-20260923`，包含该 ZIP 和 `archive-info.json`。

## 证据边界与恢复步骤

以上来自本轮实际 UU 终端观察，**原始包尚未取回，本地尚未独立复核，不记录精确算子数量、耗时、预测 hash 或包 hash 为已验证结果**。切换到文件传输时，桌面工具明确返回 Mac 锁屏且自动解锁失败；已请求用户手动解锁。

恢复后直接从上述取回目录接收两个文件到 [本地接收目录](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/ai-active-windows-evidence)。不要因为观察被锁屏中断而重新运行五轮。核验远端/本地包 hash、CRC、五轮退出码及输入身份，再对原始 profile 和张量执行独立复核。

通过后也只补充原生 CPU 活动取消与同会话恢复证据；不关闭 UI 文档事务、模型质量、权重许可、M1 或 Windows 1.0 门槛。S05 R5 原始包在锁屏前已经取回并完成独立复核，见 [R5 实测](../lifecycle-s05/windows-soak-r5-followup.md)。
