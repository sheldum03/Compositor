# M3b 缓存组蒙版与剪贴栈切片

更新日期：2026-10-06。

本轮在 `codex/windows-implementation` 工作树提交 `0be6f8c`，把只读缓存预览从“仅平面 v1/v8”扩展为按 `parentID` 递归处理 v1–v8 的受限组层合成。支持范围是：pass-through 组、父组显隐继承、全画布 Gray8 组蒙版、子层栅格蒙版、同组连续剪贴栈的 base Alpha 恢复，以及嵌套组的蒙版叠加。剪贴子层先在 base Alpha 恢复前合成，组蒙版在剪贴栈完成后应用，避免重复乘 Alpha。

这是缓存预览切片，不能解读为组工程已经可编辑。组工程仍拒绝进入 `CanEdit`，组变换、调整层、任意位置蒙版、文本/形状编辑以及组结构修改仍由保护边界拒绝或保持后续范围。F04 的非组变换参考仍保留既有平台舍入差异，未放宽逐像素阈值；本轮固定 exact 回归采用 F02、F05、F06。

## 验证结果

- `Compositor.Workflow.Checks` Release 构建：0 警告、0 错误。
- F02 pass-through group、F05 clipping alpha、F06 group mask 使用 `ImageCodec` 解码后的 TileRaster 逐像素比较通过。
- 同一 Workflow 检查中的 13 种混合、v8 编辑、保存/重开、空工程和剪贴关系回归通过。
- `Compositor.App.Checks` Release 通过：正式窗口、真实指针笔刷、图层按钮、选择、蒙版和关闭保护回归通过。
- `Compositor.Imaging.Checks` Release 通过：PNG/JPEG/Gray8 蒙版及坏输入保护通过。
- `Compositor.SaveCrash.Checks` Release 通过：14 个单层/多层子进程保存中断和恢复场景通过。
- 所有上述命令在 macOS arm64、.NET SDK 10.0.401、Release 下执行；Windows 原生启动、原生 DLL、DPI/IME、压感设备和安装部署仍未验证。
