# 根组与嵌套组可见结果选区复制证据（M3g）

日期：2026-10-06

实现提交：`5cad6c8 Support root group visible-result layer copy`、`855d4f7 Support nested group visible-result copy`

## 已验证范围

- 正式窗口对根组或嵌套组启用“选区复制为图层”，复制源使用从所选组到根级祖先的完整可见结果，因此包含组内剪贴栈、组级 Gray8 蒙版、组透明度/混合模式和每级非恒等 group transform。
- 复制结果按当前文档选区裁切，作为根级、默认 Normal 的新平面层插入最外层组子树之后；源组层级、源关系和选区外透明度保持正确，祖先组的外观不会被静默丢弃。
- `ImageProjectWorkflow.RenderLayerForCopy` 复用缓存组渲染路径，保存的根组可见结果保持 Alpha 与像素；其他组结构编辑仍按现有边界拒绝。

## 回归证据

`Compositor.Workflow.Checks` 构造带半透明剪贴目标、内层/外层组蒙版、组级 `Multiply`/`Screen`/透明度和两级旋转 transform 的嵌套组，逐像素核对根组与嵌套组的完整可见结果，并保存重开后再次核对。

`Compositor.App.Checks` 在正式 Avalonia 窗口中建立相同组合，先选取左半画布，再分别检查根组和嵌套组的按钮状态、根级插入位置、选区内像素和选区外透明度。既有合并、撤销/重做和组结构保护回归继续运行。

本地固定 SDK 10.0.401、Avalonia Headless、无原生选择库的 Release 构建与运行均通过；Windows production core 的 push run [37438016040](https://github.com/sheldum03/Compositor/actions/runs/37438016040) 与配对 PR run [37438022988](https://github.com/sheldum03/Compositor/actions/runs/37438022988) 均通过 Smoke、Imaging、Workflow、SaveCrash、App 五项矩阵。

本次 `855d4f7` 又以 `/tmp/dotnet-sdk-root-401b` 和 `/tmp/compositor-nuget-packages` 固定环境重跑：Workflow Checks Release 构建 0 警告、0 错误，并通过根/嵌套组可见结果复制；App Checks Release 构建 0 警告、0 错误，并通过根组与嵌套组按钮、插入位置、选区像素和选区外透明度回归。

## 明确边界

- 只开放根组和嵌套组的可见结果复制；组内单层/剪贴栈、跨父级复杂关系和更完整的组层级编辑仍未开放。
- 未覆盖真实 Windows 应用启动、原生文件对话框、DPI/多显示器、IME/候选窗、完整文字重排、真实压感、M5–M7 和复杂 v8 写回。
