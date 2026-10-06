# 根组可见结果选区复制证据（M3g）

日期：2026-10-06

实现提交：`5cad6c8 Support root group visible-result layer copy`

## 已验证范围

- 正式窗口对根组启用“选区复制为图层”，复制源使用完整组可见结果，因此包含组内剪贴栈、组级 Gray8 蒙版、组透明度/混合模式和非恒等 group transform。
- 复制结果按当前文档选区裁切，作为根级、默认 Normal 的新平面兄弟层插入组子树之后；组层级、源关系和选区外透明度保持正确。
- `ImageProjectWorkflow.RenderLayerForCopy` 复用缓存组渲染路径，保存的根组可见结果保持 Alpha 与像素；其他组结构编辑仍按现有边界拒绝。

## 回归证据

`Compositor.Workflow.Checks` 构造带半透明剪贴目标、组蒙版、组级 `Multiply`/透明度和旋转 transform 的根组，逐像素核对完整预览与可复制组结果，并保存重开后再次核对。

`Compositor.App.Checks` 在正式 Avalonia 窗口中建立相同组合，先选取左半画布，再点击“选区复制为图层”；检查按钮状态、根级插入位置、选区内像素和选区外透明度。既有合并、撤销/重做和组结构保护回归继续运行。

本地固定 SDK 10.0.401、Avalonia Headless、无原生选择库的 Release 构建与运行均通过；Windows production core 仍以 CI runner 结果为准。

## 明确边界

- 只开放根组可见结果复制；嵌套组作为源、跨父级复杂关系和更完整的组层级编辑仍未开放。
- 未覆盖真实 Windows 应用启动、原生文件对话框、DPI/多显示器、IME/候选窗、完整文字重排、真实压感、M5–M7 和复杂 v8 写回。
