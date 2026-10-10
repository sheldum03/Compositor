# M3b 缓存组蒙版与剪贴栈切片

更新日期：2026-10-06。

本轮在 `codex/windows-implementation` 工作树提交 `0be6f8c`，把只读缓存预览从“仅平面 v1/v8”扩展为按 `parentID` 递归处理 v1–v8 的受限组层合成。支持范围是：pass-through 组、父组显隐继承、全画布 Gray8 组蒙版、子层栅格蒙版、同组连续剪贴栈的 base Alpha 恢复，以及嵌套组的蒙版叠加。剪贴子层先在 base Alpha 恢复前合成，组蒙版在剪贴栈完成后应用，避免重复乘 Alpha。

这是迁移提交 4efbada 之前的缓存预览切片，不能用本文历史结果推断当前所有组工程仍不可编辑。当前兼容的 v2–v6 组/蒙版/剪贴夹具按逐级语义校验进入内存 v8 编辑路径；F07 组内剪贴调整层、F08 缓存文字、组变换边界及本文未覆盖的复杂关系仍由保护边界拒绝或保持后续范围。F04 的非组变换参考仍保留既有平台舍入差异，未放宽逐像素阈值；macOS 固定 exact 回归采用 F02、F05、F06。Windows 的 F05 高质量变换采样存在稳定 RGB 舍入差异，已单独记录 Windows 基线和来源 run，详见[Windows F05 Skia 平台基线](production-windows-f05-skia-m3g.md)。

## 验证结果

- `Compositor.Workflow.Checks` Release 构建：0 警告、0 错误。
- F02 pass-through group、F05 clipping alpha、F06 group mask 使用 `ImageCodec` 解码后的 TileRaster 逐像素比较通过。
- 同一 Workflow 检查中的 13 种混合、v8 编辑、保存/重开、空工程和剪贴关系回归通过。
- `Compositor.App.Checks` Release 通过：正式窗口、真实指针笔刷、图层按钮、选择、蒙版和关闭保护回归通过。
- `Compositor.Imaging.Checks` Release 通过：PNG/JPEG/Gray8 蒙版及坏输入保护通过。
- `Compositor.SaveCrash.Checks` Release 通过：14 个单层/多层子进程保存中断和恢复场景通过。
- Windows CI run `37405101417` 的 Smoke、Imaging、SaveCrash、App 检查通过；Workflow 在 F05 的 Windows/Skia RGB 平台差异处停止。两次 Windows run 的 F05 输出逐字节一致，后续检查按平台选择 exact 参考，未增加统一 RGB 容差。
- 同一 `0be6f8c` 代码发布 `win-x64` self-contained 便携目录：224 个文件，`Compositor.App.exe` SHA-256 为 `91c7ab104efddf0deff30b7d0629235f86ab4e47c0efba26a672276b4ceb4eb2`；该发布只证明交叉发布成功，未在 Windows 启动。
- macOS 命令在 arm64、.NET SDK 10.0.401、Release 下执行；Windows CI 只证明 runner 上的构建和检查命令，Windows 原生启动、原生 DLL、DPI/IME、压感设备和安装部署仍未验证。
