# Windows F05/F06 缓存预览平台基线（W-021）

日期：2026-10-06。

## 现象与复核

`F05.comp` 是 v5 连续剪贴栈，`F06.comp` 是同一软缩放层叠加组/子层蒙版的 v6 工程；Soft overlay 图层带 `[10, 6]` 起点和 `44×32` 尺寸，缓存渲染经过 `ImageProjectWorkflow.TransformCachedRaster` 的 Skia `SKFilterQuality.High` 变换采样。Windows runner 的 F05/F06 输出与 macOS 参考解码后 Alpha 逐像素相同，但 RGB 存在稳定的后端舍入差异。F05 最大通道差为 5，573 个通道字节不同，566 个像素受影响；F06 最大通道差为 26，340 个通道字节不同，308 个像素受影响。

- macOS 参考 `F05-mac.png`：`3b734c82c89d8b366291305e6ae8a641cbde4694914ed36190dd32bfbea00ed4`
- Windows 基线 `windows/F05.png`：`460ba36fdb1e2ac74db9b7f610aa5bc0dd3ae81f452bd5b32d775f2be28a51f2`
- Windows 基线 `windows/F06.png`：`5f678126d354115e3f0051b359cb0a15b5d2be59fcda1d2cae64b82a969287dd`
- Windows runs `37405096845` 与 `37405101417` 的 F05 输出逐字节一致；runs `37405690074` 与 `37405694771` 的 F06 输出逐字节一致
- 同一 Windows run 中 F02 与 `F02-mac.png` 逐像素相同

这不是把失败输出直接替换成期望值：基线由两次独立 Windows run 交叉复核，差异限定在已知 Skia 高质量变换采样路径，且 Alpha 保真不变。

## 检查策略

`Compositor.Workflow.Checks` 保持逐 tile exact 比较：macOS 继续比较 `F05-mac.png` / `F06-mac.png`，Windows 选择 `fixtures/windows/F05.png` / `F06.png`。同一平台选择也用于 v8 可编辑组蒙版的 F06 初始预览，避免缓存预览和可编辑加载使用不同参考。没有增加 RGB 容差，也没有改变 F02 的 exact 检查。Windows 专用参考明确不计入 Mac 工程的 `fixtures/checksums.json` 冻结集合，并单独记录 SHA-256 与来源 run。

## 限制

该修复只记录并门禁已确认的 Windows/Skia 平台差异，不证明所有 Windows GPU、DPI、驱动或其他 Skia 版本输出一致。Windows 真实应用输入、原生 DLL、DPI/IME、性能和完整跨平台像素一致性仍是 Windows 1.0 验收门槛。
