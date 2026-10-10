# M0 准备退出条件复核

日期：2026-09-21。以 development-plan.md 原定义检查 W-001–004；未缩减 P-01–19、V-01–12 或 M0–M7 范围。原始基线 `d562565`，当前整合基于 `4a12ca0`，另含本次三个诊断测试的显式启用条件修正。**M0 未通过，Windows 产品未交付。**

## W-001：基线准备就绪

| 原要求 | 实际证据 |
| --- | --- |
| 修正测试接口漂移，相关回归通过 | LayerTests/SmartEditTests/SelectionTests 已修复；Levels alpha 与 sRGB blend 两处实质修复已回归。当前全部 CompositorTests 整合执行见 evidence/m0-freeze-summary.json |
| 盘点工具、菜单、格式 | baseline-rules.md 的 P-01–19 源文件/测试入口表、code-coupling-audit.md 的依赖调查、docs/project-format.md 的当前 v8 字段与行为 |
| 登记合成规则、资产身份、编辑状态 | baseline-rules.md 的明确规则；History/Transform/Gradient/Selection/Text 等既有测试，加上 ProjectOperationStateTests 的 18 个组合场景；未验证的窗口入口明确列出 |
| 明确文本缩放/栅格化的现状，为 D-11 提供证据 | TextToolTests 实测尺寸不变的移动/旋转/镜像保留图像，缩放重绘；缺字体缩放会替换缓存；Image Size 改像素清除 text、DPI-only 保留，均检查 undo/redo |
| 不把未执行测试计作通过 | 整合结果 327 passed / 0 failed / 3 skipped，总计 330 声明；参数展开 361 passed / 3 skipped。3 项都是可选性能/诊断入口，详见下节 |

基线准备就绪不等于所有 P 项已经验收。未完成的原生面板、窗口、设备组合仍要在 Windows 阶段按 V 编号验证；D-11 的产品选择属于 W-003。

## W-002：固定样本准备就绪

| 原要求 | 实际证据 |
| --- | --- |
| 有来源的非空 v1–8 工程，历史字段合法 | F01–F08 逐版本 schema 重建；WindowsFixtureTests 检查字段白名单及保存/读回/升级。README 明示非历史发布包输出 |
| 13 混合、组、剪贴、调整、形状和文本 | B01–B13 为 v8；F01–F08 按版本引入语义；extended/F09 复杂结构、12 个 F11 文本/DPI 组合、F12 缺字体缓存 |
| Mac 参考 PNG 与固定身份 | 入库 37 个有效工程（主组 21、extended 14、brush 2），参考图由真实 Mac 渲染生成；冻结重放测试在整合回归中通过。5 组共 242 个受 hash 管理文件核验通过，见 evidence/m0-fixture-integrity.json |
| 可移交的拒绝/笔划/字体输入 | invalid 的 14 个 F10 工程、同一 4K CPU/Metal 笔划输入与各自参考、fonts 的 TTF/CFF OTF/双 face TTC/冲突/重复/损坏输入，均有来源与生成步骤 |

Windows 的 20 个合成工程及 1 个笔刷工程已由 Mac 实际读回通过；文字视觉存在缺字失败，跨平台浮点/字形容差、完整应用字体重启仍未验收。它们属于后续原型/验证门槛，不再作为无上限扩充 Mac 样本的理由。D-11 产品规则已确认，行为实现仍待验证；D-03 未决定时不得从已有样本推定统一笔刷参考算法。

## 未启用的三项诊断

- BrushPerformanceTests.fourKInteractiveStroke：需要 BRUSH_BENCHMARK=1，单独执行性能场景。
- BrushIntersectionTests.exportCrossingExample：同一开关，输出人工查看的交叉笔划图。
- LevelsTests.panelPreview：需要 LEVELS_PREVIEW=1，输出面板截图。

此前三者通过函数内 return 退出，测试框架仍计 passed。早期记录只标明了第一个，这是统计解释上的遗漏；旧 xcresult 原始值保留，但不得把其中三个诊断计作实际执行。现使用 Swift Testing ConditionTrait，未开启时真实报告 skipped。未开启运行不会证明启用后的诊断路径或性能达标；本轮只修正报告语义，不改变诊断主体。

## 尚未满足的门槛

- W-003：D-01/D-04/D-11 的产品决定已收到。父任务于 2026-09-21 转达用户对明确问题的回复“按建议实施”：Windows 11 x64、首版 v8 .comp 文件夹工程；原字体可用时缩放重绘，缺字体保留缓存原画面并提示选择，不静默替换。关闭这三项待决定状态；不等于字体行为测试通过，也不授权公开推送、签名或性能容差变更。
- W-004：已有 Windows Server 2022 x64 轻量服务器环境检查，并新增用户操作的 Windows 11 Pro x64 实机（Build26200、i9-13900、64 GiB、4090D，驱动32.0.15.9186）。见 `evidence/windows-server-environment.json` 与 `evidence/native-probe-windows11.json`。Server节点另已完成官方便携.NET10.0.12的hash及实际执行核验，见 `evidence/windows-server-runtime.json`，不计产品测试通过。干净虚拟机、集显参考设备及 S01–S05/实际 GPU 路径尚待验证，W-004 保持部分完成。
- W-005/006：用户已在上述 Windows 11 实机运行 LLVM-MinGW C++/ctypes 探针，通过8个C算法的既有合约、19个DLL导出和1,000次分配/释放检查；随后同一DLL的.NET10.0.12 C# P/Invoke也通过17个接口与1,000次分配/释放检查。原生与框架原始输出已收到并复核，见 `windows11-results-review.md`；独立 P/Invoke 原始结果文件未随包提供。MSVC/CMake、完整固定样本兼容性仍未完成。Windows CI 已准备，公开推送仍待确认。
- W-007/008 可继续做受限且可逆的原型源码准备；只有实机四路径证据才能完成任务及供 W-009 选型。不能预选路线、进入大规模生产 UI 或把 Mac 原型当 Windows 验收。

公开推送问题已提交用户，不重复询问、不将自动目标续跑视为确认。M0 通过仍需其原定退出条件，1.0 完成仍需全部发布门槛。
