# M3 新建画布与图层结构窗口（2026-10-05）

接入 `08817a6` 的纯内存新建与活动层 API。窗口新增新建画布对话框，沿用 Mac 1920×1080 初始尺寸，默认 72 DPI；宽高必须是整数，画布总像素不超过 100 MP。取消或校验失败保持原文档；创建成功后显示未命名及未保存标记，首次保存选择新目录。已有保存来源仍使用原路径。新建异步计算期间阻止关闭对话框，避免取消后后台替换文档。

图层区新增空层、复制、删除按钮，实际选层同步核心 `SelectLayer`，结构撤销按核心恢复活动层。空工程禁用绘画、复制和删除，仍可新增。透明画布显示棋盘背景，使范围可见；背景只参与视图，不进入像素资产。

## 固定验证

固定 `c9ede9a` 加五个 App/App.Checks 文件（包含 `08817a6` 新建核心及已提交软笔），避免依赖实施任务正在修改的混合模式源码。完整冻结目录 `/Users/admin/.codex/visualizations/2026/10/05/production-new-window/final/source`；[基线与覆盖文件摘要](production-new-window/source-identity.json)记录实际文件。完整源码构建前后摘要保持一致。

SDK 10.0.401、macOS arm64、Avalonia Headless 11.3.22，锁定恢复、Release 构建和检查均退出 0，构建 0 警告/错误。见[命令/退出码](production-new-window/verified-commands.json)、[构建](production-new-window/verified-build.log)、[运行](production-new-window/verified-run.log)、[新建结果](production-new-window/new-results.json)及[产物 SHA-256](production-new-window/verified-output-sha256.json)。

- 已有工程下取消新建、超限尺寸与分数像素输入保持同一会话和保存状态；实际创建 259×257、300 DPI 透明新文档，新图层身份独立且无初始撤销步骤。
- 未保存新文档关闭/替换时选择取消，文档和窗口保持。
- 实际新增/复制按钮建立新身份并选中新层；复制当前绘制像素后，修改复制层不改变原层。
- 首次路径通过工作区 SaveAs，后续实际保存按钮接通同一保存来源；保存的尺寸/分辨率与标题正确。选择图层不标脏，显式保存后重开保留活动层。
- 实际按钮删除至零层后，绘画/复制/删除禁用、新增保持；保存移除全部旧资产。一次撤销恢复最后删除层及其完整像素和活动身份，再次保存正确重建资产。
- 原文件/关闭保护和固定软笔检查继续通过；来源工程 manifest 摘要保持。

[新窗口截图](production-new-window/new-window.png)已检查：新建/图层按钮、中文、图层列表、软笔和透明画布边界显示正常。完整 `NewWindow.comp`、截图、导出及原始日志保存在 `/Users/admin/.codex/visualizations/2026/10/05/production-new-window/final/`。

工作区初次编译因检查代码缺少非空标记失败，修正后重新构建；早期工作区试跑和第一次冻结版通过，最终加入透明画布棋盘显示后重新冻结执行。本记录仅以 `final/` 为当前提交证据，不把并行工作区状态当作固定回归。

## 验收限制

原生首次保存文件夹/名称流程仅接线，尚无 Windows 原生对话框执行或取消证据，不能用工作区 SaveAs 代替该验收。没有 Windows 实机、原生 IME/DPI、真实 Mac 新文档读回、结构强杀、新文档强杀或 S02/S05 结论。标签、选区、组/蒙版、变换和其他完整 Alpha 功能仍未齐；M3a/Alpha 不据此关闭。
