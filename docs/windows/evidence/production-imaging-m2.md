# M2 PNG/JPEG 生产像素 IO 切片（2026-10-02）

新增 [Compositor.Imaging](../../../windows/Compositor.Imaging/README.md)，以已有 `TileRaster` 为输入/输出，采用路线固定的 SkiaSharp 2.88.9。它实际解码 PNG/JPEG、转换到 sRGB 预乘 RGBA、应用 EXIF 方向，导出透明 PNG 或带调用方指定背景的 JPEG。它尚未接入工程 reader、导入事务和生产窗口，不代表 W-015 或 M2 全部完成。

## 本地验证

macOS arm64、固定 .NET SDK 10.0.401，使用此前归档的本地 NuGet feed，锁定恢复通过；离线恢复未做在线漏洞审计。Release [构建日志](production-imaging-m2/build.txt)为 0 警告、0 错误。[检查输出](production-imaging-m2/run.txt)退出 0；[逐项数值](production-imaging-m2/results.json)与[独立 Pillow 复核](production-imaging-m2/pillow-review.json)均保留。

- 自制 259×257 PNG 跨四个瓦片，包含 alpha 0/1/64/128/254/255；与独立整数预乘参考逐字节一致。
- 八种 JPEG EXIF 方向，包括转置/镜像和宽高交换；与 Pillow `exif_transpose` 参考比较，本机全部最大通道差 0。测试预留的 JPEG 解码差异上限 3 是此小样本检查条件，不是全产品跨平台图像容差决定。
- LittleCMS 独立生成线性 RGB ICC，八级灰度按 sRGB 公式生成参考；解码最大差 0，检查允许的取整误差上限 1。此样本证明颜色变换确实生效，不代表所有 ICC/广色域输入已验证。
- 上述十组 PNG 导出再读回的预乘像素全部精确；Pillow 独立打开导出后重新预乘，也与参考精确，均有颜色元数据。
- 透明与半透明红色导出 JPEG，显式背景 `(20,40,60)`；两个独立解码器的最大通道误差均 1，阈值 3。导出未改变输入瓦片快照。
- 截断 PNG/JPEG、超单边/总像素输入、GIF、非法预乘像素、非法 JPEG 质量和已有输出均按预期拒绝；原输出字节不变，无正式失败输出或遗留临时文件。

夹具为本项目自行生成，不含用户图片；生成和复核脚本随检查项目提交。PNG 参考由 Python 标准库写入，JPEG 方向参考来自 Pillow，线性 ICC 来自 LittleCMS。固定样本及本轮源码摘要见 [SHA-256 清单](production-imaging-m2/source-fixture-sha256.json)。导出原文件位于 `/Users/admin/.codex/visualizations/2026/10/02/windows-production-imaging/output`。

API 核对依据为固定版本的 [SkiaSharp SKCodec 源码](https://github.com/mono/SkiaSharp/blob/v2.88.9/binding/Binding/SKCodec.cs)：使用 `GetPixels` 返回码，不把不完整输入当作成功，并单独应用 `EncodedOrigin`。运行行为以上述实测为准。

## 仍需完成

Windows 原生加载/执行、与生产编辑/导出事务的集成、完整 PNG CRC/尾部结构、真实相机/CMYK/广色域/16-bit 样本、尺寸上限附近的资源压力和取消均未验收。当前接口拒绝覆盖已有输出，产品中的覆盖确认及替换流程另行接入。工程核心仍只有 PNG 头部检查，不能据本模块的独立通过宣称 reader 已完成完整解码校验。Windows r3 源码包也不含本模块，应在整合后生成新版统一测试包。
