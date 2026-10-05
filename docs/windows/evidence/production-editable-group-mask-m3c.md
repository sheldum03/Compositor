# M3C 受限组工程编辑证据

提交：`ed0f6e2`（`Enable editable pass-through group masks`）

本切片把 v8 的受限 pass-through 组从只读缓存预览推进到可编辑加载。范围是全画布栅格资产、全画布 Gray8 蒙版、组变换为 identity；组内叶子可以保留已有画布内变换和连续 clipping stack。组结构、组变换、调整层、文本/形状编辑仍不在本切片范围，结构操作在含组工程中继续拒绝。

已实现：

- `ProjectStore` 允许符合白名单的 v8 组/父子层，并为组蒙版建立资产哈希；组层不再被误当作必须有 image asset 的平面层。
- `ProjectSession` 暴露 `IsGroup`/`ParentId`，只为叶子加载像素；组蒙版可创建、切换、替换，并共享撤销/重做快照。
- `ImageProjectWorkflow.OpenEditable` 加载叶子栅格和组/叶子蒙版；编辑预览从内存资产渲染，保存时只写入实际像素资产和蒙版资产。
- 应用层的蒙版笔刷对组使用画布尺寸作为笔刷边界，避免向无像素资产的组请求 raster。

验证：

- `Compositor.Workflow.Checks`：F02/F05/F06 缓存预览仍与 macOS 参考逐 tile 相等。
- 新增 v8 F06 编辑回归：组蒙版 editable load、toggle、replacement、undo、save/reopen 全部通过。
- `Compositor.Imaging.Checks`、`Compositor.App.Checks`、`Compositor.SaveCrash.Checks`、`Compositor.Workflow.Checks` 均通过，构建 0 warning / 0 error。
- macOS arm64/.NET `10.0.401` Release 本地验证；Windows 原生启动、DPI、IME、字体和真实笔输入仍需在 Windows 11 x64 主机验证。

可复现的自包含包：`win-x64`，224 个文件，发布目录 `/tmp/compositor-win-x64-editable-group-ed0f6e2`，`Compositor.App.exe` SHA-256：

`507a149a0fd1e0784245bbd6d0c35f0538ea125f83e9e9908fcbca82d43ed2b9`

该包是发布产物，不代表 Windows 启动已经通过；当前环境没有可用的 Windows 运行器，仍需在腾讯云 Windows 服务器执行原生启动和交互检查。
