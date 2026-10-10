# R9 Windows 当前执行状态（2026-09-23）

**已完成并取回原始结果：S05 原生/复核退出 0，正确性独立通过；两轮 S02 的更新 P95 未通过，详见 [最终复核](r9-windows-followup.md)。以下保留执行中断时的历史记录。**

原执行根目录：`C:\Users\Administrator\Desktop\CompositorTest\brush-source-r9b-20260923-141734`。使用 R9 原 DLL、优化前源码的 Windows 基准及 [后续包](r9-windows-followup-identity.json)，基准修正原因见 [诊断](r9-windows-baseline-diagnosis.md)。

| 阶段 | 原 PID | 已观察结果 |
| --- | ---: | --- |
| 完整笔刷回归 | 39860 | exit 0 |
| S02 first | 46804 | 原生程序 exit 0 |
| S02 first review | 45944 | 数值复核 exit 1，未通过 |
| S02 repeat | 38900 | 原生程序 exit 0 |
| S02 repeat review | 29080 | 数值复核 exit 1，未通过 |
| S05 idle | 42164 | 已启动，最终状态待读取 |

上表来自原 UU 终端输出观察；原始报告尚未下载，不能据此声称已独立核验全部帧或判断超标原因。两轮 S02 的未通过结论保留，不能沿用旧版通过记录。UU 桌面截图下半部分白块，无法提供完整无遮挡视口证据。

随后 Mac 锁屏，工具报告检测到物理输入、自动解锁暂停，已请求手动解锁。恢复后优先读取原执行的退出码和归档；不要重新运行 `r9b-kit/run.py` 来替代此次结果。取回 ZIP 后运行已准备的独立复核与逐帧分析，再决定是否需要同环境 R8 控制实验。Qt S02 已准备的独立包仍未执行。

本地复核入口：[review-windows.py](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/brush-r9b-evidence/review-windows.py) 接受原始 ZIP 路径；[analyze-s02.py](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/brush-r9b-evidence/analyze-s02.py) 对照旧版通过记录。脚本仅完成语法与字段核对，尚未在本轮原始数据上运行，不构成测试结果。
