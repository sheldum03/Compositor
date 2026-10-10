# W-010 / M3 内部窗口集成（2026-10-05）

新增唯一生产入口 `windows/Compositor.App`，引用正式 Core/Imaging，固定 Avalonia Desktop/SimpleTheme 11.3.22 和已有 .NET SDK；新增两个依赖锁文件。Windows 请求软件绘制及 RedirectionSurface，macOS 请求软件绘制。思源黑体字体与 OFL 文件来自项目既有资源。详见[入口说明](../../../windows/Compositor.App/README.md)。

窗口提供打开 `.comp`、PNG/JPEG 导入、图层列表/改名/显隐/排序/历史、保存/另存、PNG/JPEG 导出及共享核心预览。文件操作和合成放入后台任务，主窗口禁用操作直至结束；关闭和替换脏工程使用保存/不保存/取消对话框，保存失败保持当前文档。新工程目录与导出目标使用新路径，原始文件仍由核心安全保存流程保护。此处尚无绘画工具，不宣称完整 M3a 或 Alpha。

`Compositor.App.Checks` 是独立 Headless 项目，不向生产入口引入 Headless 依赖。固定 `275588f` 加 App/App.Checks 源码与锁文件，在独立目录锁定恢复、Release 构建、执行，均退出 0；构建 0 警告/错误。见[固定基线](production-app-integration/baseline.json)、[源码摘要](production-app-integration/source-sha256.json)、[命令与退出码](production-app-integration/execution.json)、[日志](production-app-integration/run.log)和[结果](production-app-integration/results.json)。此前工作树试跑也退出 0，但包含实施任务尚未提交的核心变更，不用它替代这轮固定基线证据。

检查真实创建 `MainWindow`，触发按钮的 Click 和关闭对话框，验证：

- 改名、显示/隐藏、上下移动、撤销与重做改变实际核心状态，窗口脏标记正确。
- 实际 259×257 跨瓦片预览的 RGBA 字节、alpha、行方向与核心全部一致；PNG 导出重读像素一致，PNG/JPEG 导出不清除脏状态。旧说明中的 300×300 是标注错误，原始工程和图像未更改。
- 打开失败保留原会话，另存到已有工程被拒绝且不改变保存点。
- 关闭取消保留文档；源资产被外部改动时保存关闭失败，窗口与未保存内容仍保留；恢复原资产后保存关闭成功；不保存关闭不会把未提交名称写入工程。
- 保存重开后图层状态与合成像素保持。生成的[窗口截图](production-app-integration/window.png)已检查图像：中文文本、图层列表、按钮和预览均显示，无明显遮挡。

原始目录 `/Users/admin/.codex/visualizations/2026/10/05/production-app-integration`；[产物摘要](production-app-integration/output-sha256.json)定位完整工程及导出图片。Windows CI 源码加入第五项 App.Checks 矩阵，已验证 YAML 和项目/样本路径，未推送或执行远程 CI。

**执行环境为 macOS arm64 的 Avalonia Headless。** 未执行原生文件对话框、实际桌面窗口输入、Windows IME/DPI、性能/长时间资源、干净安装或 Windows 运行。生产入口已有源码、可构建产物和窗口集成证据，不代表 Windows 安装包、M3a/Alpha 或完整编辑器已经交付。下一步继续接通图层增删/逐层像素及画布交互，随后按产品范围验收。
