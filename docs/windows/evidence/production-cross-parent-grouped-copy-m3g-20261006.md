# 跨父组栅格剪贴可见结果复制证据

日期：2026-10-06

实现提交：`a25f771`（受限跨父组关系）、`057107a`（外部源组外观）、`35c38e6`（两级外部栅格剪贴链）、`66d164f`（三层外部栅格剪贴链）、`55896db`（四层外部栅格剪贴链）、`aa0480a`（五层外部栅格剪贴链）、`d5c12aa`（六层外部栅格剪贴链验证）、`0f73ab1`（七层外部栅格剪贴链验证）、`920a379`（循环/缺失剪贴关系回归拒绝）。

## 范围

正式 `Layer via Copy` 支持一个栅格目标位于父组、其 `maskSourceID` 位于另一个父组的受限可见结果复制。复制结果按文档坐标渲染，并提升到目标外层组子树之后的根级平面层，原目标与外部源关系保持不变。

外部源祖先组的有限外观已接通：源组的非恒等变换、启用 Gray8 组蒙版、透明度和混合模式会先沿源路径单独合成为文档坐标的 Alpha 来源，再应用到目标栅格。外部源路径只包含所选源及其祖先，不把同组无关兄弟混入结果。

本轮先开放并验证四层外部栅格剪贴链，又在 `aa0480a` 扩展到五层，在 `d5c12aa` 扩展到六层，并在 `0f73ab1` 将 Workflow/App Headless 验证扩展到七层：目标可以引用外部父组中的栅格源，栅格源再依次引用不同父组中的前置栅格源；每一级先解析自己的外部源可见结果，再向目标传递 Alpha。关系仍要求源先于目标、节点为栅格、资源完整，复制层仍插入目标组子树之后的根级位置。

## 验证

- Workflow Release 检查覆盖基础跨父组关系、根级插入、像素、保存重开和原始关系保留。
- Workflow Release 检查另建独立参考工程，以同一源组的变换、蒙版、透明度和 Multiply 外观计算期望 Alpha，逐 tile 比较复制结果，并验证保存重开。
- Workflow Release 另建七层外部栅格链，使用独立 Alpha 组合期望值，逐 tile 比较目标复制结果，并验证七条 `maskSourceID` 关系、根级插入和保存重开。
- Workflow Release 从合法剪贴工程复制两份损坏 manifest，分别注入缺失源和循环 `maskSourceID`；`ProjectStore.Open` 均在渲染前抛出 `InvalidDataException`，没有把损坏关系当成可编辑工程。
- App Headless 正式窗口检查覆盖外部源组变换、组蒙版、透明度、混合模式、`LayerViaCopy` 按钮启用、根级插入、选区裁切、像素和保存重开；本轮又覆盖七层链的按钮启用、根级插入、选区像素、七条关系和保存重开。
- 固定 SDK 10.0.401，`920a379` 的 Windows production core push [run 37482896032](https://github.com/sheldum03/Compositor/actions/runs/37482896032) 与 PR [run 37482903224](https://github.com/sheldum03/Compositor/actions/runs/37482903224) 的 Smoke、Imaging、Workflow、SaveCrash、App 五项均通过；五层链的 Windows production core push [run 37475254792](https://github.com/sheldum03/Compositor/actions/runs/37475254792) 与 PR [run 37475263657](https://github.com/sheldum03/Compositor/actions/runs/37475263657) 以及六层验证的 push [run 37478569374](https://github.com/sheldum03/Compositor/actions/runs/37478569374) 和七层验证的 push [run 37480103438](https://github.com/sheldum03/Compositor/actions/runs/37480103438) 与 PR [run 37480113279](https://github.com/sheldum03/Compositor/actions/runs/37480113279) 的 Smoke、Imaging、Workflow、SaveCrash、App 五项均通过；七层 App Headless `results.json` 为 `passed: true`。

## 限制

这是 M3g 的受限跨父级切片，不等于所有复杂关系完成。八层以上链、文字或组作为剪贴源、复杂目标组快照拖放、复杂剪贴栈合并和真实 Windows 交互仍拒绝或未验收；循环/缺失 `maskSourceID` 已由 `920a379` 的 Workflow reader 回归验证为打开即拒绝。
