# M2 逐层像素快照与选择性保存（2026-10-05）

范围仍是 v8 平面、全画布、未变换、Normal、透明度 1、无组/蒙版、总源像素不超过 100 MP 的工程。`GetLayerRaster(Guid)` / `ReplaceLayerRaster(Guid, TileRaster)` 现在按图层身份读取和提交像素；既有单层 API 保持兼容。每次实际像素变化产生一个历史步骤，和改名、显隐、排序共用撤销/重做及保存点。图层增删/复制、组/蒙版编辑和其他混合模式不在本切片内。

打开可编辑多层工程时将各层解码为瓦片快照；编辑只替换目标层的快照，其他层和旧历史仍共享未修改瓦片。所有层共同遵守 100 步撤销及 256 MiB 历史独占图像预算。历史裁剪后按图层保留仍被快照使用的源身份，不因某一层变化而重新编码未修改层。预览/导出读取当前内存像素；安全保存只重新编码改变的 PNG，其他 PNG 保持原始字节，并继续检查源资产 SHA-256。

`Compositor.Workflow.Checks` 新增以下验证：

- 两层工程中只改变上层像素，下层与旧上层快照保持；混合像素、改名和显隐事务可完全撤销至保存点，再重做。
- 保存 `FlatPixels.comp` 后只上层 PNG 字节改变，重开各层及合成/导出像素一致；再修改下层保存 `FlatPixelsBoth.comp`，已保存的上层 PNG 保持。
- 真实 Mac 产生的 `MacFlatEdited.comp` 与 Mac 的 `mac-after.png` 初始合成逐像素一致；在 .NET 工作流中改一层像素、改名、隐藏另一层和排序，四步撤销/重做，保存为 `MacFlatContinued.comp`，重开/导出一致，文档和活动层身份保留，Mac 输入不修改。
- 对真实 Mac 工程连续执行 105 次逐层像素编辑，恰好保留 100 次撤销；裁剪后的保存点仍正确标为脏。重做后保存 `MacFlatBoundedPixels.comp`，未修改的另一层仍与 Mac 原始 PNG 字节完全相同。

本次固定 SDK 10.0.401、macOS arm64、Release 检查均退出 0：

| 检查 | 结果与边界 |
| --- | --- |
| Workflow.Checks，含真实 Mac 单层和多层输入 | 像素/元数据历史、安全保存、重开、导出及选择性编码通过 |
| Core.Smoke，含共享 C 原生库 | 工程保护、瓦片/历史、原生算法与保存回归通过 |
| Imaging.Checks | PNG/JPEG、方向、ICC、Gray8 及拒绝输入回归通过 |
| SaveCrash.Checks | 原有 14 个场景通过：8 个单层、6 个多层元数据。尚未新增多层像素编码时的强杀场景 |
| App.Checks | Avalonia Headless 内部窗口、图层命令、预览及未保存关闭保护回归通过；不代表 Windows 原生窗口验收 |

工作流复现参数（输出目录须不存在）：

```sh
dotnet run --project windows/Compositor.Workflow.Checks -c Release --no-restore -- \
  windows/Compositor.Imaging.Checks/fixtures <新的输出目录> \
  /Users/admin/.codex/visualizations/2026/10/02/production-workflow-mac-readback/mac-produced \
  /Users/admin/.codex/visualizations/2026/10/02/production-flat-edit-mac-validation/output/MacFlatEdited.comp
```

已归档输出与文件 SHA-256：`/Users/admin/.codex/visualizations/2026/10/05/production-layer-pixels-m2/`，含 `workflow/` 中三个新多层工程、PNG 导出、各检查输出、保存中断原始子进程日志和 `validation-summary.json`。该 summary 是工具观察记录，不冒充原始控制台日志。

本次未执行 Windows 实机、正式笔刷性能或资源压力验收；真实 Mac 再次打开这三个新输出工程仍需取得独立证据。r4 固定 Windows 包不含本切片，M2/M3 及 Windows 1.0 不据此关闭。

## 临时像素预览补充

`RenderFlatNormal(session, layerId, overrideRaster)` 通过同一合成器预览某一层的临时像素，不提交文档和历史。单层及多层预览、取消后原合成保留、隐藏层不参与合成、错误尺寸/外部图层 ID 拒绝、只读工程保护已加入工作流检查。2026-10-05 macOS arm64 Release 全部工作流（含两个真实 Mac 输入）再次退出 0，输出 `/tmp/compositor-layer-preview-final-20261005`。正式窗口笔划操作及 Windows 验收仍由后续集成检查覆盖。
