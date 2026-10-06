# Windows cached-render references

这些文件是 Windows x64 / Skia 运行时专用的缓存预览参考，不属于 Mac 固定工程参考的 `checksums.json` 集合。

`F05.png` 对应 `F05.comp` 的 v5 连续剪贴栈，`F06.png` 对应 `F06.comp` 的 v6 组蒙版与子层蒙版叠加。两者的软缩放层使用 `SKFilterQuality.High`；Windows 与 macOS 的 Skia 后端在 RGB 舍入上有稳定差异，Alpha 保持一致，因此检查程序在 Windows 选择 `windows/F05.png` / `windows/F06.png`，在 macOS 继续选择各自的 `*-mac.png`。

- `F05.png` SHA-256：`460ba36fdb1e2ac74db9b7f610aa5bc0dd3ae81f452bd5b32d775f2be28a51f2`；相对 `F05-mac.png` 最大 RGB 差 5，Alpha 差 0。
- `F06.png` SHA-256：`5f678126d354115e3f0051b359cb0a15b5d2be59fcda1d2cae64b82a969287dd`；相对 `F06-mac.png` 最大 RGB 差 26，Alpha 差 0。

F05 基线在 Windows runs `37405096845` 与 `37405101417` 中逐字节一致；F06 基线在 Windows runs `37405690074` 与 `37405694771` 中逐字节一致。两边仍使用逐像素 exact 断言。
