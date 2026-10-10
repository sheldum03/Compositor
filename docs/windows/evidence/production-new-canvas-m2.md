# 新建空白画布与活动层选择（2026-10-05）

`ProjectSession.CreateBlank(int width, int height, double resolution = 72)` 直接创建内存中的 v8 文档，不借助临时工程或 PNG。初始选中透明 `Layer 1`，文档与图层使用新 GUID，瓦片按需分配，无初始撤销步骤。初始图层、sRGB 与 72 DPI 默认值依据 Mac `EditorSession.createDocument(..., emptyLayer: true)` / `NewCanvasSheet`；分辨率边界依据 Mac reader 的有限 1–9600 DPI 规则。宽高各 1–30,000，画布总像素不超过 100 MP。

## 会话与保存 API

- `SavedDirectory`：从未保存时为 `null`，成功保存后为正式工程绝对路径。`HasBeenSaved` 表示是否已建立这个保存来源。
- `SourceDirectory`：保留旧的非 nullable API；从未保存时明确抛 `InvalidOperationException`，不返回当前工作目录或虚构路径。窗口应读取 `SavedDirectory` 判断是否需要首次保存对话框。
- `IsDirty`：首次成功保存前始终为真，包括未作编辑、取消保存和撤销回初始透明状态。首次保存后恢复已有的历史修订号/保存点规则，不清空撤销或重做。
- `ImageProjectWorkflow.Save(session, destination)`：未保存会话自动使用首次保存保护，拒绝覆盖已有文件或目录；源 PNG 不存在的新层由内存编码。编码失败不建立保存来源、清理临时目录并保留历史。完成既有新目录校验/提交之后才设置保存来源和保存点。
- 取消原生对话框时窗口不调用 Save。缺失或空保存路径在核心拒绝；不会据此设置保存状态。原生窗口取消行为由 App 集成测试单独验证。

使用入口：

```csharp
var next = ProjectSession.CreateBlank(width, height, resolution);
// 成功创建之后才替换窗口当前会话；取消或校验失败继续使用原会话。
TileRaster preview = ImageProjectWorkflow.RenderFlatNormal(next);
ImageProjectWorkflow.Save(next, chosenNewDirectory);
```

`SelectLayer(Guid)` 验证图层归属，深克隆当前 manifest，再替换当前快照的 manifest，保留像素和修订号。选择不新增历史或标记像素修改；显式保存会持久化 `activeLayerID`。下一次结构事务会捕获当前选择，撤销恢复相应活动层。其他历史快照，特别是像素事务共享的旧 manifest，不被选择操作改动。非法 ID 不改变状态。

## 固定源码验证

在 `/Users/admin/.codex/visualizations/2026/10/05/production-new-canvas-m2/validation-source-final/` 固定源码副本中，使用 SDK 10.0.401、锁定依赖、macOS arm64 Release 构建/运行，避免与 App 工作区构建竞争。源码 SHA-256 在 `source-final-sha256.json`，文件内容与本切片提交对应；基线为 `5ef92ba` 加本切片，不包含后续 App/笔刷提交。

| 检查 | 结果 |
| --- | --- |
| Workflow.Checks，含真实 Mac 单层/多层输入回归 | 退出 0；新建空白像素/身份、14 组非法尺寸或 DPI、允许边界的稀疏分配、无效创建不替换调用方会话、缺失保存路径、已有目录/文件保护、无编码器失败、首轮编码失败及撤销恢复、初始状态仍未保存、首次保存重开、后续历史/导出一致 |
| 活动层选择（同一 Workflow 检查） | 选择不标脏/不改像素、不改变共享 manifest 的旧快照；显式保存重开保留选择；选择→新增/删除→撤销→保存重开正确；外部 ID 拒绝且历史保持 |
| Core.Smoke，含共享 C 原生库 | 退出 0；原读写、保护、保存故障/恢复、原生魔棒及历史预算回归通过 |
| SaveCrash.Checks | 退出 0；原有 14 个真实保存子进程终止场景全部通过。未新增纯内存新文档的进程终止场景 |

完整 restore/run 日志保存在同一归档根目录的 `workflow-final-*.log`、`core-*.log`、`save-crash-*.log`，观察退出码在 `validation-summary.json`。`workflow-final-output/` 包含：`NewCanvasInitial.comp`（首次保存的透明 300×257、300 DPI 文档）、`NewCanvas.comp`（后续修改保存）、`NewCanvasIndependent.comp`（独立默认 72 DPI 文档）、`NewCanvasRecovered.comp`（首次编码失败后恢复）、`LayerSelection.comp`（选层及结构撤销后保存），以及未保存/已保存 PNG 导出。附资产 SHA-256。

本次不是 Windows 实机、原生对话框或正式新建 UI 验收；新增工程的真实 Mac 打开、尚未保存文档的进程终止以及最终用户操作路径仍需独立验证。M2/M3 与完整 Windows 1.0 不据此关闭。
