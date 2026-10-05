# M3 图层透明度与混合模式窗口（2026-10-05）

`d473a20` 将 M2 的平面图层外观事务接入唯一 Avalonia 生产窗口：透明度 0–100%、`Normal`、`Multiply`、`Screen`、`Overlay`、`Darken`、`Lighten`、`Difference`、`Color Dodge`、`Color Burn`、`Hue`、`Saturation`、`Color`、`Luminosity` 13 个模式均可从图层区选择。控件沿用 `ProjectSession` 的事务和 `LayerCompositor`，预览、PNG/JPEG 导出、撤销/重做和保存重开使用同一合成路径。

窗口检查验证了真实下拉框/数值框/按钮：选中图层设置 25% 与 `Multiply` 后变脏，连续撤销恢复保存点，连续重做恢复两个属性，保存后重新打开仍保留属性；其余新建、图层结构、空工程、透明背景、软笔和关闭保护回归继续通过。外观属性当前各自产生一个历史步骤，这是 Core 现有两个事务 API 的直接语义，后续若产品要求一次点击合并为单步再单独设计复合事务。

## 固定验证

固定提交 `d473a20` 的完整源码归档在 `/Users/admin/.codex/visualizations/2026/10/05/production-appearance-window/final/source`，源码身份见同目录 `source-identity.json`，文件摘要见 `source-sha256.json`。使用 SDK 10.0.401、macOS arm64、Avalonia Headless 11.3.22、锁定恢复和 Release 构建；三步退出码均为 0，详见 `verified-commands.json`、`verified-restore.log`、`verified-build.log`、`verified-run.log`。运行产物及摘要在 `verified-output/` 和 `verified-output-sha256.json`。

这份证据只证明本地生产窗口和 Core 的事务接线。13 模式真实 Mac 对照仍是 5/13 逐通道 exact，另外 8 个模式 alpha 相同但最大 RGB 差为 1，详见 [M2 混合模式对照](production-blend-m2.md)；不能把窗口控件通过写成跨平台合成已通过。

## 验收限制

检查仍是 macOS Headless，未执行 Windows 11 原生窗口、文件夹选择器、DPI/IME、真实指针设备、长时间性能或安装包。v1 非默认外观、组、蒙版和变换继续只读保护；完整 M3a/Alpha 及 Windows 1.0 仍未交付。
