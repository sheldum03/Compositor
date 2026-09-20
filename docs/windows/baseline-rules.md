# W-001 行为基线与差异登记

基线：`d562565e5f1c53a7ece4dd2d1f6c0ef3547a3c28`。本表按当前源码和实际回归维护；通过数见 [执行记录](execution-log.md)。Windows 尚未实现，本表不是 Windows 验收。

## 功能与验证入口

路径均相对仓库根；测试位于 `CompositorTests/`。菜单入口为 `Compositor/CompositorApp.swift`，工具枚举为 `Document/EditorSession.swift`。下列实现路径省略 `Compositor/`。

| P | 实际功能/状态 | 实现入口 | 行为测试 |
| --- | --- | --- | --- |
| 01 | 新建尺寸、多个项目、关闭取消、工作区目标绑定 | Document/ProjectWorkspace.swift、IO/ProjectController.swift | ProjectWorkspaceTests、CanvasEntryTests、ProjectTests |
| 02 | PNG/JPEG/TIFF/HEIC、EXIF/ICC、导入失败保留当前文档 | IO/ImageImporter.swift | ImageImportTests |
| 03 | v1–8 读取、v8 保存、路径/资源/像素预算验证 | IO/ProjectStore.swift | ProjectTests、GroupTests、LayerMaskTests、TextToolTests |
| 04 | 缩放、平移、100%、适应、像素网格、降采样缓存 | Rendering/CanvasViewport.swift、EditorCanvas.swift、TiledLayerRenderer.swift | CompositorTests、TiledLayerTests |
| 05 | 组与层、原生多选拖放、蒙版复制、跨项目复制、合并、13 混合 | UI/NativeLayerList.swift、Document/LayerGroups.swift、LayerMerge.swift、ProjectWorkspace.swift | LayerTests、GroupTests、GroupingSelectionTests、LayerAppearanceTests、ProjectWorkspaceTests |
| 06 | 非破坏变换、自由扭曲、多层变换、吸附、画布/图像尺寸、裁剪 | Document/LayerTransform.swift、Distort.swift、Crop.swift、CanvasSize.swift、IO/ImageResizer.swift | TransformTests、TransformPressTests、DistortTests、CropTests、CanvasSizeTests、ImageSizeTests |
| 07 | 矩形/椭圆、自由/多边形套索、魔棒、加减选、选区/像素移动、剪贴板 | Document/Selection.swift、MagicWand.swift、FloatingSelection.swift、SelectionClipboard.swift | SelectionTests、SelectionEditTests、SelectionClipboardTests、MagicWandTests |
| 08 | 画笔/橡皮、软硬覆盖、Shift 线、笔尾、瓦片提交、层/组/剪贴蒙版及独立蒙版 | Document/EditorSession+Brush.swift、BrushStroke.swift、LayerMask.swift、LiveLayerMask.swift、Rendering/RasterSnapshot.swift | BrushTests、BrushIntersectionTests、RasterSnapshotTests、LayerMaskTests、LiveMaskTests |
| 09 | 内容填充、修复/仿制、Liquify/Blur/Smudge | Document/ContentFill.swift、CloneStamp.swift、BlurTool.swift、SmudgeLiquify.swift | SmartEditTests、CloneStampTests、BrushTests（Blur/Smudge/Liquify 专项仍待补充） |
| 10 | HSV/Levels/Curves/Exposure/Gradient Map/Grain、反相、Gaussian/Motion/Noise/Lens、调整层 | Document/Filters.swift、Levels.swift、HueSaturation.swift、LayerAdjustment.swift | FilterTests、LevelsTests、HueSaturationTests、AdjustmentLayerTests、ImageAdjustmentTests |
| 11 | 渐变、矩形/圆角/椭圆、形状参数缓存、调色板、吸管 | Document/Gradient.swift、ShapeTool.swift、ColorPalette.swift | GradientTests、ShapeToolTests、ColorPickerTests |
| 12 | 单样式点/框文本、中文/Emoji、IME marked text、变换输入与共享布局 | Document/TextTool.swift、Rendering/TextEditorOverlay.swift | TextToolTests、TextLayoutTests |
| 13 | 内置字体、OTF/TTF/TTC 导入、冲突/损坏/缺字体 | Document/FontLibrary.swift | FontLibraryTests 固定 TTF/OTF/双 face TTC、去重/冲突/拒绝；Mac 两个独立进程恢复通过，完整应用重启与真实 Windows/IME 仍未执行 |
| 14 | Vision raw mask、基础/高级细化、可取消且安装蒙版 | Document/SubjectRemoval.swift、GuidedMatte.swift、Filters.swift | SmartEditTests；Windows 模型替代仍待 M1/M6 |
| 15 | 嵌套编辑合并事务、undo/redo、保存 revision、取消无历史 | Document/DocumentHistory.swift、EditorSession.swift | HistoryTests、ProjectOperationStateTests 及各工具提交/取消测试 |
| 16 | 离屏 PNG/JPEG、透明度/背景/质量、DPI | IO/ImageExporter.swift | ExportTests、JPEGExportTests、ImageSizeTests |
| 17 | 工具单键/焦点、光标、拖放、多屏/DPI | Rendering/EditorCanvas.swift、UI/ProjectWindowBridge.swift | CursorTests、BlendShortcutTests；Windows 实机矩阵未执行 |
| 18 | Mac Sparkle；Windows 安装/卸载/更新均待建设 | CompositorApp.swift、scripts/ | Mac 测试不能证明 Windows 安装更新 |
| 19 | busy、取消、过期任务、错误不覆盖工程、离线 | Document/EditorSession.swift、IO/ProjectController.swift、Filters.swift | HistoryTests、ProjectTests、SmartEditTests；磁盘故障/Windows 无网待验收 |

测试名称映射是入口清单，不能仅因存在文件就认定需求完成。源码文件及实际 suite 名称以执行日志中的测试发现结果为准。

## 编辑状态转换

| 活跃状态 | 提交/取消 | 切工具/切层 | 切项目 | 保存/导出 | 画布失焦 |
| --- | --- | --- | --- | --- | --- |
| Brush/warp | mouseUp 同步提交；Esc 取消未忙笔划 | guard 拒绝 | canStartProjectOperation 拒绝 | guard 拒绝/文件请求等待 | 非 busy 取消笔划 |
| Transform | Enter/鼠标结束提交；Esc 取消；一次历史 | 切换提交 | canSwitch 通过后先提交 | ProjectController.begin 提交 | 恢复本次拖动初值；非 persistent 取消 |
| Text draft | commitText 先确认 marked text；空白不建层；失败留草稿；Esc 取消 | 成功提交才切换 | canStartProjectOperation 拒绝 | 当前 Mac 拒绝；不能声称自动提交 | 原生文本编辑器处理焦点，不等同 Canvas 取消 |
| Crop | Enter 提交；Esc 取消 | 切工具取消 | 检查 canSwitch；保存/加载入口各自处理 | begin 取消 Crop | 清除 cropDrag |
| Lasso/Shape draft | mouseUp/Enter 提交；Esc 取消 | selectTool 取消草稿 | 各会话保有自身草稿；不可臆定统一处理 | canStartProjectOperation 未普遍覆盖所有草稿 | Shape/非多边形 Lasso 取消；多边形保留 |
| Gradient | Enter 异步提交；Esc 取消 | resolveGradient | canSwitch 拒绝 | 实测直接保存已提交文档，保留渐变预览且标记 saved；随后提交渐变才变脏 | 清除 gradientDrag，不等同自动提交 |
| Levels/HSV/Filter/Adjustment | 预览不入历史，提交一次；取消保留原图 | 模态入口 guard | canSwitch 拒绝 | Levels 实测拒绝；HSV/Filter 实测保存原图、保留预览并标记 saved；Adjustment 仍需单独验证 | 不因 canvas 失焦自动提交 |
| Floating/pixel move | 提交合成；取消恢复；无移动保留软边 | 对应工具自行处理 | pixelMove 时拒绝 | pixelMove 实测保存移动前像素、保留预览并标记 saved；floating transform 仍需单独验证 | pixelMove 取消；selection move 结束 |
| Project busy/import | 结束/失败释放 busy；异步结果检查原图身份和变换 | 禁止重叠编辑 | 拒绝 | 防重入 | 不取消正在提交的笔划 |

此处明确记录 Mac 入口之间的差异，不宣称已有通用状态机。Windows P-19 要将不一致入口逐项验证，尤其渐变/浮动选区/文本与文件操作，不从类名推导行为。

### 已执行的状态组合

`ProjectOperationStateTests` 在 macOS 26.5.1 arm64 的测试宿主执行真实 ProjectController 保存/ProjectStore 读回，以及未显示窗口的 NSWindow first-responder 切换。7 个测试声明展开 18 个场景通过；证据 `evidence/w001-state-matrix-summary.json`。没有执行真实鼠标输入、菜单点击、原生文件面板或 Windows 窗口，因此不扩大为完整窗口验收。

| 场景 | 验证结果 |
| --- | --- |
| Brush/Text/Levels/project busy/importing × 保存/切项目/Undo | 返回拒绝；不创建工程文件；原文档、历史和活跃状态保留 |
| Transform × 保存 | 先提交一次历史；读回变换已更新；Undo 变脏，Redo 返回 saved revision |
| Crop × 保存 | 取消裁剪预览；原画布尺寸保存；不新增历史 |
| Gradient/HSV/Filter/pixelMove × 保存/切项目 | 保存成功但切项目被拒；完整 manifest 和解码资产像素仍等于已提交文档；预览保留，保存 revision 更新 |
| Gradient 在上述保存后提交，再 Undo | 提交新增一次历史并变脏；Undo 回到原文档与 saved revision |
| Transform × 切项目 | 提交原项目一次历史；新项目文档/历史不变；切回可 Undo |
| 合法/超限文本 × 切工具 | 合法提交后切换，一次历史可撤销；超限提交失败后保留 text 工具/草稿，文档/历史不变 |
| 笔划 × 原生失焦，busy=false/true | false 取消，true 保留；此处 busy 由测试控制，不代表异步提交压力测试 |
| freehand/polygonal lasso × 原生失焦 | freehand 取消；polygonal 保留；均不新增历史 |

Windows W-011/W-016 必须为“预览中保存”的四类状态规定明确的提交、取消或拒绝处理，并测试保存状态提示与实际文件内容一致。以上观察登记为 Mac 入口差异，不据此决定 Windows 应原样保留。仍缺关闭/退出确认面板、外部文件排队、浮动变换、调整层编辑以及跨窗口真实操作组合。

## 资产与历史契约

- ImageLayer 相等性包含图像对象身份；颜色相同的新图不是同一编辑版本。
- liveText/liveShape 仅在元数据关联图像与当前图像相同对象时有效。像素替换会使其失效，保存省略对应参数。
- 历史共享不可变图像；嵌套 begin/end 只记录最外层事务；无文档变化不清空 redo。保存点为 revision，导出不更新保存点。
- 默认最多 100 历史项、256 MiB 历史独占图像预算。不能把此预算误写为进程峰值 RAM。
- RasterSnapshot 按 256×256 瓦片共享/替换，CGDataProvider 按需物化；笔划提交/下一笔/undo 必须保证旧图不被原地修改。

## 合成规则

1. manifest 层自底向上；层级遍历使组子树连续；继承可见性，组为 pass-through，组 opacity/blend 必须为 1/Normal。
2. 每层图像先按 transform 取样，应用启用的栅格蒙版及独立 placement、opacity、blend；禁用蒙版仍保存。
3. 组蒙版乘到每个后代覆盖，不改变剪贴源覆盖；父组蒙版叠加。1×1 灰度蒙版合法，避免全画布分配。
4. 普通 live alpha 依赖使用源的 alpha/opacity/自身蒙版/上游链接，忽略源可见性与 RGB；依赖缓存防循环且上限 256。
5. 连续同父剪贴栈先取 base alpha，内部颜色在恢复 alpha 前混合，整栈只应用一次 base alpha，防止软边变厚；非连续依赖仍按普通 coverage 处理。
6. 调整层作用于已有底图；剪贴调整在所属栈内部执行。非 Normal 调整混合先按不透明颜色计算、后恢复原 alpha，再应用调整层 opacity/蒙版。
7. 13 模式的名称与顺序固定为 LayerBlendMode.allCases。Color Dodge/Burn 的 Apple 补偿路径不应成为 Windows 必需依赖；要比较数值及半透明边缘。
8. CanvasView 与 ImageExporter 共享底层算子但调度不同；文本编辑预览在当前提交使用 compositor 并隐藏原生字形。旧 project-format.md 的“不应用效果”描述已过期。
9. Gaussian/Motion 会扩展图层再裁掉全透明边界；不再以旧栅格边框裁切。导出以画布边界裁切。

## 已确认的基线偏差与 D-11

| 编号 | 证据/差异 | 处置 |
| --- | --- | --- |
| B-01 | 旧 Layer/SmartEdit/Selection 测试调用不存在/变更的接口 | 修到现有真实路径，保留行为断言 |
| B-02 | 英文字符串断言在中文系统失败，甚至把同名 rename 当实际修改 | 基准命令固定 en-US；中文 GUI 验收另记，不更改系统语言 |
| B-03 | 模式仅工具栏切换、行底部剪贴、v8、13 混合、扩展模糊和吸附已更新，旧断言未更新 | 以历史与源码交叉核对，保留新行为的验证 |
| B-04 | Levels Swift 与 C 重复 unpremultiply/premultiply，半透明反相错误 | 删除包装层重复转换，以现有精确像素断言回归 |
| B-05 | Dodge/Burn CI 默认线性工作空间与 sRGB 混合参考值不一致 | 显式 sRGB 工作空间，沿用原容差；参考图须在修复后生成 |
| D-11a | 移动/旋转/镜像且栅格尺寸不变保留图像；缩放改变尺寸后重绘且 undo/redo 恢复身份 | 新增 TextToolTests 实测通过 |
| D-11b | 缺失字体的 Mac 缩放仍重绘 fallback，原 PostScript 名保留，缓存已替换 | 已复现；Windows 提案：缺字时保留缓存，明确安装/替换后才重绘；待用户决定 |
| D-11c | Image Size 改像素尺寸烘焙并移除 text，分辨率单改保留；undo 恢复 | 新增 TextToolTests 实测通过；不能误判为读写丢失 |

M0 仍需固定参考工程、补齐具体状态组合的窗口验收、决定 D-01/D-04/D-11 并取得 Windows 环境。以上清单不关闭 W-001/M0。
