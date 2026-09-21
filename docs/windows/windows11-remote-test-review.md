# Windows 11 UU 远程测试记录（2026-09-21）

状态：已完成当前原型的大部分自动化测试，完整测试及 Windows 1.0 **未完成**。Mac 锁屏使 CUA 无法继续访问 UU；Qt 对照已启动但结果尚未取回，真实窗口输入、完整可见的性能复测仍待继续。依据为原始归档和独立复核，不以脚本 exit 0 代替产品验收。

## 环境与修复

实体机 PEIXU7-GY，Windows 11 Pro 10.0.26200 x64，i9-13900、64 GB、4090D；本轮报告记录 .NET 10.0.12、PowerShell 5.1.26100.9444。显示缩放 150%，Avalonia 使用软件渲染。通过 UU 自带文件传输和远程终端执行；终端与 Explorer 均为 Session 1。

旧 `window-run-20260921-154725.zip` 实际退出 -532462766，61 次原生预编辑变化、4 次文字输入、4 次文字导出，未完成笔刷或合成保存。日志中的 `SpacedTextPresenter.CreateTextLayout` 在输入缩短文本时发生 `CaretIndex=45 > Text.Length=42`。旧归档保留于 [原始记录](evidence/text-selection-preparation/windows11-old-run/summary.json)，不得将该轮称为完整通过。

修复提交：`90a09b1` 补齐文本区域透明命中背景和选区颜色绑定；`24bc505` 将当前布局快照使用的 caret 限制在当前文本长度内，不回写编辑光标。真实 TextBox 末尾选区替换回归在旧版异常退出 134，修复后通过。

## 本轮已取得证据

| 项目 | 结果与边界 |
| --- | --- |
| 文字选择 | Windows headless 48 个组合：中部/末尾、50/100/150%、0/13°、翻转及双向拖选；精确选中「测试」、可见高亮、替换、撤销和重做均通过。不是物理鼠标或原生 IME 验收。 |
| 合成 | 20 样本预览/导出独立 RGBA 比较精确；20 个工程实际 Mac reader 读回且保持原像素。 |
| 文字样本 | 12 样本预览/导出、取消恢复精确；60 张 PNG 与上一轮已核验的 Windows 灰度修复输出逐字节相同。跨平台文字差异仍未接受。 |
| 笔刷准备检查 | 242 次更新及 13 项会话检查通过；保存工程在 Mac 实际读回且与导出一致。仍非完整工具或 S02 性能验收。 |
| S02 headless 自检 | 4 笔重放完成，不能替代真实窗口性能。 |
| HEIC | Windows 11 实际 16 样本、23 原生调用通过。16 个 raw 与既有 Server 输出逐字节一致；对 Mac 参考的全部差分指标独立复算吻合，alpha 精确、预乘 RGB 最大差 1/255，容差未批准。真实照片、ICC/HDR、干净机与发行许可未验收。 |
| AI 在线下载 | 连接约 20.10 秒后 curl 28 超时，收到 0 字节，未调用原生推理；有界失败和归档生效，下载成功路径未通过。 |
| AI 本地模型 | 8 次原生调用及错误路径通过；中文/Emoji 路径输出一致，两份 profile 各含 1460 个 CPU 节点事件；推理约 377/367/362 ms。仅冻结输入张量，不代表 Windows 图像预后处理、模型效果或分发许可通过。 |

Mac 读回使用 Swift Testing 精确函数选择（含 `()`），最终 **2 passed / 0 failed / 0 skipped**。首个不含括号的筛选执行 0 测试，未计为验证通过。见 [xcresult 摘要](evidence/windows11-remote-suite/mac-readback-summary.json)。

## S02 首轮诊断

两组各 1 笔预热 + 30 笔测量，1000×1000 逻辑视口、4000×4000 文档、800 px / 0 硬度 / 100% 不透明度。真实 Windows 窗口完成 62 笔，无正确性异常；专用内存采样高水位 452,321,280 字节。

| 场景 | 更新 P95 | 提交 P95 |
| --- | --- | --- |
| 空层 | 27.0772 ms | 14.3882 ms |
| 已有层 | 32.5546 ms | 13.6883 ms |

更新 P95 高于拟定 16.7 ms。该轮存在 UU 终端遮挡部分画布，已判为诊断轮；随后还发生结果文件传输。不能作为正式性能验收，须完整可见且无其他测试负载时复测。采样范围止于 canvas lease 释放，不是物理屏幕呈现或 GPU 延迟。见 [采样复核](evidence/windows11-remote-suite/s02-diagnostic-review.json) 和 [截图](evidence/windows11-remote-suite/s02-native-partially-obscured.png)。

## 恢复后待办

1. 手动解锁 Mac，继续现有 UU 连接。此前自动点击/按键无法操作远程桌面，但 UU 自带终端和文件传输正常；鼠标模式对照和重连未解决，用户物理点击对照尚无回复。
2. 取回已启动的 Qt 对照、字体目录 A/B、新版 `--window-check` 的归档，独立审查 Emoji 与像素。自动事件检查不等于真实 Qt IME。
3. 保存所有已接收归档的远端 SHA-256，核对本地身份；保留第一轮性能结果，关闭遮挡命令窗口后单独重跑 S02。
4. 在最新修复版窗口完成物理双向拖选、原生微软拼音、取消、撤销重做、焦点、三页保存和正常关闭；原生窗口的小视口拒绝及中途关闭取消仍待测。
5. 多显示器 DPI、集显参考机、干净安装/更新、未实现的完整编辑器功能、完整 S01–S05、字体缺失与工程文字事务仍不具备全部验收证据；本台机器不能代替设备矩阵。

## 可复核产物

[独立复核 JSON](evidence/windows11-remote-suite/independent-review.json)、[自动化执行摘要](evidence/windows11-remote-suite/summary.json)、[收到的原始归档身份](evidence/windows11-remote-suite/received-archives.json)。本轮完整归档、517 文件测试包、391 文件 Qt 对照包、源码副本及测试日志位于：

`/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/windows11-remote-suite`

Windows 上独立目录为 `C:\Users\Administrator\Desktop\CompositorTest\remote-suite` 与 `qt-remote-suite`；不覆盖原 `window-test`。本地模型仅用于本次获授权的私有测试，不进入结果归档或公共仓库。
