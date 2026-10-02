# M2 图像工程工作流本地切片（2026-10-02）

在独立像素 IO 的基础上，新增 `ImageProjectWorkflow`：PNG/JPEG 经完整解码、方向和颜色转换后，写成简单单图层 v8 `.comp`；同一 `ProjectSession` 历史同时持有图层名称与不可变瓦片快照。保存时 Core 管理临时工程、备份与回滚，Imaging 仅负责编码并重新解码校验生成的 PNG。Core 不引用 Imaging。复杂 v8（组、蒙版、文字、调整等）仍不可编辑，当前没有生产窗口。

以 macOS arm64 .NET SDK 10.0.401 对 `Compositor.Workflow.Checks` 锁定恢复和 Release 构建，0 警告、0 错误；最新本地输出已归档至 `/Users/admin/.codex/visualizations/2026/10/02/production-workflow-integration/output`，13 个文件的 SHA-256 清单在同级 `output-sha256.json`。检查程序退出 0：

```text
PASS: v8 PNG/JPEG import, pixel and metadata history, safe save, reopen, PNG/JPEG export, prior snapshot restore, changed-asset isolation, rejected import
```

具体校验了 PNG 跨瓦片像素导入、v8 manifest、资产文件名符合 Mac 的大写 UUID 约束；空白图层名被拒绝且不改变保存点。修改像素与改名后两次撤销回到干净初始状态，再重做，并在保存后撤销/重做切换脏状态。未保存像素的 PNG 导出与会话预览一致且不改变保存点；Core 原始字节导出拒绝遗漏像素编辑。保存并重开后瓦片逐字节一致，JPEG 导出保持尺寸。保存后撤销到旧像素再保存能重新编码旧快照；撤销后分叉编辑会丢弃重做分支。JPEG EXIF 6 导入按已定向像素写 v8。目标已存在、截断输入均拒绝且不破坏正式工程。源 PNG 打开后外部改动不污染内存快照；像素编辑后的保存拒绝覆盖改变的资产。

已为真实 Mac reader 对照保留三组生产输出：`Image.comp` 与 `memory-export.png` 为初始像素，`Edited.comp` 与 `export.png` 为编辑后像素，`Oriented.comp` 与 `oriented-export.png` 为 EXIF 6 后像素。[真实 Mac 应用读回记录](production-workflow-mac-readback.md)已验证这三组的像素、身份、编辑保存重开。Mac 正常保存会补写默认的 `blendMode: "Normal"`、`opacity: 1`、`isGroup: false`；反向测试最初复现 Core 拒绝三份 Mac 工程，修复白名单后，直接对三份**原始 Mac 输出**执行 `OpenEditable`、修改像素与名称、安全保存、重开及 PNG 导出均通过；非默认混合、透明度和组层仍保持只读。反向输出保留在 `/Users/admin/.codex/visualizations/2026/10/02/production-workflow-integration/mac-return`，28 文件 SHA-256 清单在同级 `mac-return-sha256.json`。

**上述两方向都在 macOS 上执行，Windows 11 实机尚未执行此整合版。** 单层 v8 支持仅是受限子集，v1～v8 完整语义、性能与内存压力、剩余保存故障和生产窗口仍未验收，不能据此关闭 M2 或 W-015。
