# M3C 受限组工程编辑证据

实现提交：`ed0f6e2`（`Enable editable pass-through group masks`）、`be885fe`（`Add root group and ungroup editing`）、`421b4f2`（`Support nested group structure editing`）、`83b925e`（`Support simple masked group ungrouping`）、`0d9a503`（`Render cached group transforms`）、`1975380`（`Support editable group flips`）、`009d244`（`Add multi-select group UI`）、`1d4045c`（`Support editable group transforms`）、`f2daed9`（`Support editable group movement`）；回归提交：`6b759f3`（`Verify editable group appearance history`）、`2702d92`（`Guard grouped layer reordering`）、`3db13af`（`Protect grouped project controls`）、`155a82a`（`Disable raster tools on group selection`）、`6338ac5`（`Cover multi-layer root grouping`）。

本切片把 v8 的受限 pass-through 组从只读缓存预览推进到可编辑加载，并为缓存组增加非恒等变换预览。可编辑范围是全画布栅格资产、全画布 Gray8 蒙版；组支持水平/垂直翻转、以组中心缩放和 90° 旋转元数据事务，启用组蒙版跟随组变换，禁用组蒙版可安全解组。缓存预览允许组执行翻转/缩放/旋转取样。组内叶子可以保留已有画布内变换和连续 clipping stack；带剪贴栈解组、变换组安全解组/变换烘焙、调整层、文本/形状编辑仍不在本切片范围。

已实现：

- `ProjectStore` 允许符合白名单的 v8 组/父子层，并为嵌套组和组蒙版建立资产哈希；组层不再被误当作必须有 image asset 的平面层。
- `ProjectSession` 暴露 `IsGroup`/`ParentId`，只为叶子加载像素；组蒙版可创建、切换、替换，并共享撤销/重做快照；组显隐和透明度也通过同一历史事务渲染。
- 支持同级连续普通图层或组建立 pass-through 嵌套组，并支持无剪贴栈的启用组蒙版根级/嵌套组安全解组；窗口列表支持连续同级多选建组，禁用组蒙版也可解组；带剪贴栈解组和复杂排序仍不在本切片范围。
- 缓存组的非恒等 `transform` 在子树合成后统一取样；缓存水平翻转已用 F02 逐像素参考验证，并与下面受限的可编辑翻转范围分开记录。
- 可编辑组支持水平/垂直翻转、移动、以中心为基准缩放和 90° 旋转；启用组蒙版在组变换后仍与子树一起取样；变换写入同一 manifest 历史事务，撤销/重做、保存重开和正式窗口按钮回归均通过。变换后的组禁止直接解组，避免未烘焙变换造成画面静默变化。
- 窗口列表启用多选；多选时仅保留“建立组”，复制/删除/剪贴/Alpha/移动、外观和绘制等单选操作全部禁用；连续选中两个同级叶子后建立组，两个叶子的 `parentID` 均指向新组。
- 除上述建组/解组外，含组工程的增删、复制、删除、排序和剪贴关系结构变更继续明确拒绝；排序路径已补统一保护，避免同位置移动绕过拒绝检查。
- `ImageProjectWorkflow.OpenEditable` 加载叶子栅格和组/叶子蒙版；编辑预览从内存资产渲染，保存时只写入实际像素资产和蒙版资产。
- 应用层的蒙版笔刷对组使用画布尺寸作为笔刷边界，避免向无像素资产的组请求 raster。
- 窗口层对组工程禁用增删、复制、排序、剪贴结构、文档尺寸/旋转等未实现操作；选中组时进一步禁用像素复制/剪切/粘贴和 Alpha 载入，同时保留组移动、翻转/缩放/90°旋转、组蒙版启停与编辑按钮；变换组的解组按钮会关闭；Headless UI 回归覆盖这些按钮状态。

验证：

- `Compositor.Workflow.Checks`：F02/F05/F06 缓存预览仍与 macOS 参考逐 tile 相等。
- 新增 v8 F06 编辑回归：组蒙版 editable load、组显隐/透明度、蒙版 toggle、replacement、undo、save/reopen，以及结构操作拒绝全部通过；新增根级与嵌套组建组/解组、启用组蒙版随组翻转、禁用组蒙版解组保存重开、无启用组蒙版组移动/缩放/90°旋转撤销/重做与正式窗口按钮回归；F02 缓存组水平翻转逐像素回归通过。
- `Compositor.Imaging.Checks`、`Compositor.App.Checks`、`Compositor.SaveCrash.Checks`、`Compositor.Workflow.Checks` 均通过，构建 0 warning / 0 error；App Checks 新增两层多选建组及单选操作禁用、组工程按钮保护回归。
- macOS arm64/.NET `10.0.401` Release 本地验证；Windows 原生启动、DPI、IME、字体和真实笔输入仍需在 Windows 11 x64 主机验证。

可复现的自包含包：`win-x64`，224 个文件，发布目录 `/tmp/compositor-win-x64-group-move-41482`，`Compositor.App.exe` SHA-256：

`6dc09b9ca79a735e2d888a16dbd6a0acfe6320e716f6e314d01a0afcbebd29bf`

该包是发布产物，不代表 Windows 启动已经通过；当前环境没有可用的 Windows 运行器，仍需在腾讯云 Windows 服务器执行原生启动和交互检查。
