# R9 Windows 冻结图回归的基准诊断

**首次 R9 Windows 测试未进入 S02/S05。原因是测试包误用了 Mac 冻结图哈希，而不是 R9 改变了同机输出。** 保留首次失败及跨平台差异，不更改产品代码、原 Mac 基准或验收容差。

## 复现与隔离

已校验安装包、R9 DLL、基准与全部继承依赖。首次 `run.ps1` 在 `BrushUpdateRegression` 第 110 行报 `Brush transient/committed raster differs from frozen pre-optimization output`，退出 -532462766。此前整数覆盖率/混合、源快照、撤销重做、缓存和分配检查均通过；追加分配 154,582,520 B，预算 204,472,320 B，重复尾部更新 24,024 B。没有用这些局部通过覆盖最终失败。

失败包 `brush-source-r9-20260923-140206.zip` 为 857 B，SHA-256 `ea905601b5690d2b7fc2dcef45c23cbec6d91faf2a6f0a36d7d31567ab0aaafe`，CRC 通过。另以原回归器的显式 `--record` 捕获实际值到独立文件 `r9-actual.json`，没有覆盖任何基准：264 个键中 261 个与 Mac 基准不同。

排序验证的假设为：平台渲染差异、R9 共享瓦片回归、包或依赖混用。随后把完全相同的 264 图场景分别链接到优化前 `e6bcc9f`、R8 `459f732`、R9 `df3a903` 的源码。诊断入口使用各版原有未缓存 `Paint`，记录预览和提交 PNG/哈希；不引用 R9 结果来生成旧版输出。

- Mac arm64：三版全部精确匹配原 `baseline.json`。
- Windows x64：三版 264 个原始哈希完全相同；取回的 792 张 PNG 解码后逐像素比较亦精确一致。
- 原 R9 完整缓存回归器捕获的 264 个哈希，与优化前源码在 Windows 生成的哈希完全相同。
- 三版各自对 Mac 的 261 张差异一致；共涉及 5,587,196 个不同像素（跨图累计）。例如 `12/0.01/empty/update-1` 有 36 个不同像素，包含 alpha 差异，不能归结为 PNG 编码或仅哈希字节顺序。具体 Skia 平台采样原因与跨平台容差尚未定案。

Windows 对照包 `brush-platform-20260923-141022.zip` 为 36,827,475 B，终端与本地 SHA-256 一致：`ebb1dee732b9067e9fa3ed24a57fbd1e215c72271e625b1538b65e3770a4389f`，CRC 通过。三版退出码均为 0；执行记录保留各版源标识、程序集、依赖哈希。见 [独立像素复核](r9-windows-baseline-review.json)。

## 最小修正与复测

新增 [baseline.windows-x64.json](../../../../experiments/windows/brush-update-regression/baseline.windows-x64.json)，直接使用优化前 `e6bcc9f` 的 Windows 输出。SHA-256 `13a1a3ac27111ff2a879695f1322cff3105fcf31d91bb8672f64de81ed2b17c0`。原 Mac 基准 SHA-256 仍为 `c01553b28fa3133e0cda755c69920d4613d025c252edad6997a35fc7560456d3`。调用者必须显式选择对应平台，不能从被测候选重新生成基准来消除失败。

后续包仅带 Windows 基准、既有 S02/S05 复核器和执行脚本；完整核验原 R9 应用与依赖后，重新执行完整回归，再运行两次 S02 和一次 S05-idle，保留全部退出码、启动参数和原始结果。复用同一 R9 DLL `176db1d51dce2b7291cf670536a09efd2b095b6ef8e2bc278af294275fb4fa62`，没有改动笔刷、历史、缓存或性能门槛。见 [后续包身份](r9-windows-followup-identity.json)。

后续完整笔刷回归已退出 0，两轮 S02 原生运行均退出 0，但数值复核均退出 1；S05 正确性已通过且全部原始数据已独立复核，见 [最终结果](r9-windows-followup.md)。此修正验证同平台优化不改变行为；跨平台像素一致性、S05 资源稳定性与 M1 选型均不因此自动通过。

归档校验见 [证据包身份](r9-windows-baseline-archive.json)。完整原始包、三版诊断源码/程序集、Mac 原始 PNG、捕获值和构建/复核脚本保存在 [诊断归档目录](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/brush-r9-platform-diagnostic)。后续打包应显式携带平台来源，避免把可复现的同平台哈希误作跨平台标准。
