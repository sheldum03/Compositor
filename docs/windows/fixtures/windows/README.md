# Windows cached-render references

这些文件是 Windows x64 / Skia 运行时专用的缓存预览参考，不属于 Mac 固定工程参考的 `checksums.json` 集合。

`F05.png` 对应 `F05.comp` 的 v5 连续剪贴栈。该场景的软缩放层使用 `SKFilterQuality.High`；Windows 与 macOS 的 Skia 后端在 RGB 舍入上有稳定差异，Alpha 保持一致，因此检查程序在 Windows 选择此参考，在 macOS 继续选择 `F05-mac.png`。

`F05.png` SHA-256：`460ba36fdb1e2ac74db9b7f610aa5bc0dd3ae81f452bd5b32d775f2be28a51f2`。
该字节序列在 Windows runs `37405096845` 和 `37405101417` 中逐字节一致；相对 `F05-mac.png` 的解码像素最大 RGB 差为 5，Alpha 差为 0。两边仍使用逐像素 exact 断言。
