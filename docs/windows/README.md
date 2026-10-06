# Compositor Windows 开发文档

初始规划日期：2026-09-20。分支：`windows-part`。本目录保留原始规划和审计，2026-09-21 补录用户确认的 D-01/D-04/D-11。独立实施已在 `codex/windows-implementation` 开始，最新结果见[开发计划](development-plan.md)与[当前 Windows 实机基线](evidence/tencent-windows-server-baseline-20261006.md)；本页下方初始审计结果不代表最新研发状态。

该分支已建立独立工作目录 `Compositor-windows-part`（与原项目目录并列）。实施任务使用 `codex/windows-implementation`，本规划工作区保留，不切换原 `main` 分支。

代码基线：`d562565e5f1c53a7ece4dd2d1f6c0ef3547a3c28`（`fix: address text layer review findings`）。从该提交建立分支；工作区已有修改不视为已提交的产品基线。

## 文档导航

| 文档 | 用途 |
| --- | --- |
| [代码耦合与计划复评](code-coupling-audit.md) | 当前依赖链、构建实测、基线阻断、复用分级和阶段修订 |
| [产品目标](product-goals.md) | 用户、核心场景、版本目标、成功标准、不做什么 |
| [产品需求 PRD](product-requirements.md) | 功能与交互要求、Alpha/Beta/1.0 范围、需求编号与验收 |
| [技术设计](technical-design.md) | 模块职责、数据与渲染约定、文本与字体、文件兼容、技术决策 |
| [开发计划](development-plan.md) | M0–M7 阶段、任务编号、依赖、交付物和退出条件 |
| [验证与发布](validation-release.md) | 跨平台样本、正确性、性能、Windows 设备矩阵、安装更新与发布标准 |

阅读顺序：代码耦合与计划复评 → 产品目标 → PRD → 技术设计 → 开发计划 → 验证与发布。实现任务应同时链接 `P-*` 需求、`W-*` 任务和 `V-*` 验证记录。

## 原始审计结论与范围更新

- 建设 Windows 本地桌面图像编辑器，逐步覆盖当前 Mac 版能力，包括新加入的可编辑文本与字体库。
- 首发范围已由用户于 2026-09-21 确认为 Windows 11 x64；Windows 10、ARM64、发行许可偏好和最终硬件门槛尚未确定，见技术设计中的决策表。
- Qt/C++ 与 Avalonia/C# 先各做一个受限原型，M1 后保留一条生产路线。没有选定生产框架、GPU 后端或 AI 权重。
- 工程读写基线为 `.comp` **v1–8**，新保存为 v8。第一版已确认保留目录包结构，提供应用内打开与拖放；单文件容器另立决策，不自动升级格式。
- 当前没有独立可移植的 Swift 核心。8 个 C 文件在 Mac 上已独立编译通过；会话、像素所有权、合成调度和文本运行时需要重实现。
- Mac 应用 Debug 构建通过，但测试目标因 LayerTests/SmartEditTests 接口漂移编译失败，M0 尚未通过。
- M1 先验证合成、笔划提交/撤销/导出、文本输入与最小格式往返，再决定框架和必要的 GPU 路径。所有性能数值均为拟定目标。

## 与上次调研的区别

[Windows 替代方案调研](../research/windows-port-open-source-evaluation.md)与[框架比较](../research/windows-framework-candidates.md)仍可作为选型依据，但其中“工程 v7”“尚无画布文本层”是较早代码快照，已被本分支基线中的 v8 和文本工具更新。本规划以当前 [格式说明](../project-format.md)、`ProjectStore.swift` 及文本测试为依据。

旧调研保留原始记录，不把历史结论无声改写成当前事实。如源码、说明、测试之间有差异，M0 必须复现并记录预期行为，不能仅凭任意一个来源决定产品行为。

## 文档维护规则

产品范围变化先改 PRD；技术选型变化先补决策依据；实现完成后在开发计划登记实际提交、测试记录及已知限制。草案中的未确认项不代表用户已经作出决定，也不应阻止可独立完成的样本和原型工作。

原始准备交付：分支、规划、代码耦合审计及 Mac 构建/测试尝试。后续实现与验证以独立实施工作区记录为准，产品目标尚未完成。
