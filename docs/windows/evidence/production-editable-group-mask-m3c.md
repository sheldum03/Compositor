# M3C 受限组工程编辑证据

实现提交：`ed0f6e2`（`Enable editable pass-through group masks`）、`be885fe`（`Add root group and ungroup editing`）；回归提交：`6b759f3`（`Verify editable group appearance history`）、`2702d92`（`Guard grouped layer reordering`）、`3db13af`（`Protect grouped project controls`）、`155a82a`（`Disable raster tools on group selection`）、`6338ac5`（`Cover multi-layer root grouping`）。

本切片把 v8 的受限 pass-through 组从只读缓存预览推进到可编辑加载。范围是全画布栅格资产、全画布 Gray8 蒙版、组变换为 identity；组内叶子可以保留已有画布内变换和连续 clipping stack。组结构、组变换、调整层、文本/形状编辑仍不在本切片范围，结构操作在含组工程中继续拒绝。

已实现：

- `ProjectStore` 允许符合白名单的 v8 组/父子层，并为组蒙版建立资产哈希；组层不再被误当作必须有 image asset 的平面层。
- `ProjectSession` 暴露 `IsGroup`/`ParentId`，只为叶子加载像素；组蒙版可创建、切换、替换，并共享撤销/重做快照；组显隐和透明度也通过同一历史事务渲染。
- 支持连续根级普通图层建立 pass-through 组，并支持无组蒙版组安全解组；嵌套组、带组蒙版解组、复杂排序和多选 UI 仍不在本切片范围。
- 含组工程的增删、复制、删除、排序和剪贴关系结构变更继续明确拒绝；排序路径已补统一保护，避免同位置移动绕过拒绝检查。
- `ImageProjectWorkflow.OpenEditable` 加载叶子栅格和组/叶子蒙版；编辑预览从内存资产渲染，保存时只写入实际像素资产和蒙版资产。
- 应用层的蒙版笔刷对组使用画布尺寸作为笔刷边界，避免向无像素资产的组请求 raster。
- 窗口层对组工程禁用增删、复制、排序、剪贴结构、文档尺寸/旋转等未实现操作；选中组时进一步禁用像素复制/剪切/粘贴、Alpha 载入和组层像素变换，同时保留组蒙版启停与编辑按钮；Headless UI 回归覆盖这些按钮状态。

验证：

- `Compositor.Workflow.Checks`：F02/F05/F06 缓存预览仍与 macOS 参考逐 tile 相等。
- 新增 v8 F06 编辑回归：组蒙版 editable load、组显隐/透明度、蒙版 toggle、replacement、undo、save/reopen，以及结构操作拒绝全部通过；新增多层根组/解组保存重开回归与正式窗口按钮回归。
- `Compositor.Imaging.Checks`、`Compositor.App.Checks`、`Compositor.SaveCrash.Checks`、`Compositor.Workflow.Checks` 均通过，构建 0 warning / 0 error；App Checks 新增组工程按钮保护回归。
- macOS arm64/.NET `10.0.401` Release 本地验证；Windows 原生启动、DPI、IME、字体和真实笔输入仍需在 Windows 11 x64 主机验证。

可复现的自包含包：`win-x64`，224 个文件，发布目录 `/tmp/compositor-win-x64-group-create-6338ac5-final`，`Compositor.App.exe` SHA-256：

`a170628b73a143aa489fd7a44358385c0e306a1e36e688fbbe9e3e5cfa637bb2`

该包是发布产物，不代表 Windows 启动已经通过；当前环境没有可用的 Windows 运行器，仍需在腾讯云 Windows 服务器执行原生启动和交互检查。
