# M2 平面图层增删、复制与空工程（2026-10-05）

在已有全画布、未变换、Normal、透明度 1、无组/蒙版的可编辑 v8 子集上，增加 `AddBlankLayer(name, destinationIndex)`、`DuplicateLayer(layerId, name)`、`DeleteLayer(layerId)` 和 `ActiveLayerId`。新增/复制给出新 GUID 和同身份 PNG 文件名、选中新层；复制在源层上方，保留其显隐和现有语义。删除活动层选中相邻层，删除最后一层得到空透明文档。每次结构变化只形成一步历史，层身份、像素、顺序和活动层一起撤销/重做。v1 结构修改仍拒绝，不自动升级格式。

为使单层、多层和空层间转换有一致快照，将内部像素存储统一为按 GUID 的图层映射；原 `Raster` / `ReplaceRaster` / `ImageName` 单层接口继续有效。复制共享不可变瓦片，修改复制层只替换该层快照。100 步/256 MiB 历史独占图像预算沿用，按所有层的瓦片对象去重。新增/复制继续遵守 100 MP 总源像素及 10,000 层上限。

保存只遍历当前 manifest 引用的资产，避免删除层后复制已不引用的文件。新层或撤销恢复的已删除层从内存编码；仍在源工程中的资产继续检查 SHA-256。已保存删除后再撤销，即使文件已从当前工程移除，像素仍由历史快照完整恢复并可再次保存。空工程保存、重开、新增和透明 PNG 导出都纳入可编辑链路，不使用占位图层。

本地固定 .NET SDK 10.0.401、macOS arm64、Release：

| 检查 | 已验证 |
| --- | --- |
| Workflow.Checks，含真实 Mac 单层/多层输入回归 | 新增空层、复制/独立改像素、删除非活动/活动/最终层；单层→多层→空层；活动身份和脏状态；保存重开只含当前资产；保存删除后撤销恢复；透明导出；空工程重新新增；非法名字/索引/外部 ID/未加载/v1 操作保护。退出 0 |
| Core.Smoke，含共享 C 算法 | 原读写、故障恢复、原生魔棒及历史预算回归；复制不增加独占历史、删除后按共享瓦片准确计量；超出总源像素限制不改变历史。退出 0 |
| SaveCrash.Checks | 原有 14 个真实子进程终止场景全部通过；本切片未增加图层结构变化/空工程/多层像素编码强杀场景 |

初次工作流检查发现新建 `JsonValue<int>` 无法直接按 `double` 读取，已将新层的变换坐标、尺寸和旋转按排版/合成规则构造为浮点值；修正后完整工作流再次退出 0。失败目录 `/tmp/compositor-layer-structure-20261005` 保留，不计为通过证据。

固定输出归档 `/Users/admin/.codex/visualizations/2026/10/05/production-layer-structure-m2/`，附 `sha256.json` 和明确标为观察记录的 `validation-summary.json`。`workflow/LayerStructureAdded.comp` 含两层，`LayerStructureEmpty.comp` 是真正空工程，`LayerStructureRestored.comp` 是保存空工程后撤销恢复的原层，`LayerStructureNewBlank.comp` 是从空工程新增的透明单层；`empty-project.png` 保留透明导出。

这不是原生 Windows 验收。实际 Mac 再打开新增/复制/空工程输出、生产窗口按钮、结构变化时保存中断及正式笔刷/S02/S05 仍需独立证据；组、蒙版、混合和变换语义尚未接入。本切片不关闭 M2/M3 或 Windows 1.0。
