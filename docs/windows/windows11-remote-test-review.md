# Windows 11 UU 远程测试记录（2026-09-21）

状态：已完成当前原型的大部分自动化测试，完整测试及 Windows 1.0 **未完成**。用户解锁后已继续：Qt 同机对照及 Mac 读回已完成；真实窗口输入仍受 CUA 坐标点击错误阻断，无遮挡 S02 已完成但更新延迟未达目标。依据为原始归档和独立复核，不以脚本 exit 0 代替产品验收。

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

## Qt 同机对照

`remote-qt-20260921-184528.zip` 的远端/本地 SHA-256 一致，外层 22 项及内嵌 149/213 项均通过 ZIP CRC。原冻结 Qt 包未改，字体对照两进程 exit 0；新增窗口原生契约及 qwindows synthetic 检查也为 exit 0。

独立解码确认 20 合成预览/导出相同；原文字运行、默认字体目录、系统字体目录三组各 12 样本，预览/导出、取消恢复、预编辑/导出共 108 对精确。两份 qwindows 文字预览/导出也精确。Qt 保存的 20 个合成工程及笔刷工程，经 Mac 实际 reader 验证 **2 passed / 0 failed / 0 skipped**。

默认 offscreen 只解析到 Source Han Sans SC，实图 Emoji 是缺字框；设置 `QT_QPA_FONTDIR=C:\WINDOWS\Fonts` 后解析到 Segoe UI Emoji，实图恢复彩色 Emoji。使用原生 `windows` 插件的新版 synthetic 输出亦显示彩色 Emoji。该结论仅定位字体发现路径，不等于真实微软拼音或跨平台文字效果已验收。原型的 242 笔刷更新、56 共享瓦片通过；两笔 timing 仍仅为观察。

见 [Qt 独立核验](evidence/windows11-remote-suite/qt-independent-review.json)、[字体对照原始摘要](evidence/windows11-remote-suite/qt/font-summary.json)、[原生插件合成事件记录](evidence/windows11-remote-suite/qt/window-report.json) 与 [Mac 读回](evidence/windows11-remote-suite/qt-mac-readback-summary.json)。

## S02 首轮诊断

两组各 1 笔预热 + 30 笔测量，1000×1000 逻辑视口、4000×4000 文档、800 px / 0 硬度 / 100% 不透明度。真实 Windows 窗口完成 62 笔，无正确性异常；专用内存采样高水位 452,321,280 字节。

| 场景 | 更新 P95 | 提交 P95 |
| --- | --- | --- |
| 空层 | 27.0772 ms | 14.3882 ms |
| 已有层 | 32.5546 ms | 13.6883 ms |

更新 P95 高于拟定 16.7 ms。该轮存在 UU 终端遮挡部分画布，已判为诊断轮；随后还发生结果文件传输。不能作为正式性能验收，须完整可见且无其他测试负载时复测。采样范围止于 canvas lease 释放，不是物理屏幕呈现或 GPU 延迟。见 [采样复核](evidence/windows11-remote-suite/s02-diagnostic-review.json) 和 [截图](evidence/windows11-remote-suite/s02-native-partially-obscured.png)。

## S02 无遮挡复测

解锁后最初的隐藏进程启动随 UU 终端退出未留下新运行记录；一次直接启动因输出目录预先存在而被正确拒绝，未计为测试成功。改为独立正常窗口、日志父目录下新建输出子目录后，`s02-visible-20260921-190545` 完成 **62 笔 / 7200 个测量更新**。画布在开始及后半程截图中完整可见；测量期间未运行其他 Windows 测试或传输文件。二进制身份与已验证包一致。

| 场景 | 更新 P95 | 提交 P95 |
| --- | --- | --- |
| 空层 | 26.9577 ms | 15.5731 ms |
| 已有层 | 27.4426 ms | 15.8797 ms |

两组各排除 1 笔预热及 pointer-down，按 nearest rank `ceil(0.95*n)` 独立复算。所有提交、源图不变、撤销/重做检查通过，最终两图与首轮诊断精确一致；专用内存采样高水位 410,767,360 字节。两组更新延迟仍高于 16.7 ms，**性能不通过**。进程对象的 `HasExited=true`，但 `ExitCode=null`，不声称捕获到退出码 0；完成依据为原始报告 `completed=true / error=null` 和全部 62 笔记录。

见 [复测复核](evidence/windows11-remote-suite/s02-visible-review.json)、[二进制身份](evidence/windows11-remote-suite/s02-visible-execution.json)、[开始画面](evidence/windows11-remote-suite/s02-visible-190545.png) 与 [后半程画面](evidence/windows11-remote-suite/s02-visible-midrun-190545.png)。性能终点仍是 canvas lease 释放，非物理呈现或真实输入延迟；4090D 机器上的软件渲染结果不能代替集显/干净机矩阵。

对既有逐帧数据进一步复算：空层/已有层 append P95 为 12.2344/12.2656 ms，paint P95 为 6.2553/8.3527 ms；逐帧两段之和的 P95 为 17.8912/19.9529 ms（不能将两个 P95 直接相加）。总耗时扣除两段后的残余平均为 10.9362/8.6070 ms，尚不能解释为纯等待；GC 次数也不能证明暂停因果。后续可增加阶段时间戳区分排队、canvas lease 和绘制成本。见[既有记录分段分析](evidence/windows11-remote-suite/s02-latency-analysis/README.md)，未因此重跑实机或改动原型。

## 当前待办和阻断

1. Mac 已解锁，UU 终端、文件传输和画面读取正常。桌面坐标点击曾连续返回 `noWindowsAvailable`，重置会话/置前/主控端鼠标对照后仍未恢复有效输入（已恢复智能鼠标）；后续点击返回成功但页签未切换，Tab 也无可见响应。已请求用户手动物理点击对照，尚无回答。
2. 最新修复版 Avalonia 原生测试窗口已通过独立正常进程启动并保留在前方。原生微软拼音、修复版双向拖选/替换、取消、撤销重做、焦点、三页保存和正常关闭仍待真实输入；Qt qwindows 的合成事件不能替代原生 IME。
3. 无遮挡性能复测已完成但超时，应根据现有帧采样进一步定位更新耗时。S02 小视口拒绝及中途关闭取消仍未完成原生操作验证。
4. 多显示器 DPI、集显参考机、干净安装/更新、未实现的完整编辑器功能、完整 S01–S05、字体缺失与工程文字事务仍不具备全部验收证据。完整 Windows 1.0 和 W-008/M1 均不放行。

## 可复核产物

[独立复核 JSON](evidence/windows11-remote-suite/independent-review.json)、[自动化执行摘要](evidence/windows11-remote-suite/summary.json)、[收到的原始归档身份](evidence/windows11-remote-suite/received-archives.json)。本轮完整归档、517 文件测试包、391 文件 Qt 对照包、源码副本及测试日志位于：

`/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/windows11-remote-suite`

Windows 上独立目录为 `C:\Users\Administrator\Desktop\CompositorTest\remote-suite` 与 `qt-remote-suite`；不覆盖原 `window-test`。本地模型仅用于本次获授权的私有测试，不进入结果归档或公共仓库。
