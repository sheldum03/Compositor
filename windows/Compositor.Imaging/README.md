# PNG / JPEG 像素读写

生产图像 IO 模块，依赖 `Compositor.Core.TileRaster` 与路线固定的 SkiaSharp 2.88.9；两个项目的锁文件保留传递依赖及内容哈希。当前模块独立于工程存储和 GUI，后续由导入/导出事务调用，不让 Core 反向依赖 Imaging。

- `ImageCodec.Load(path)`：按实际文件格式接收 PNG/JPEG，检查 512 MiB 文件、30,000 单边及 100 MP 上限。PNG 先流式检查块长度、名称、CRC、关键块顺序及 IEND/文件尾，再调用像素解码器；只接受成功解码结果。请求 sRGB、8-bit 预乘 RGBA，并应用 EXIF 1–8 方向后返回瓦片快照。无内嵌颜色信息的图像按 sRGB 处理。
- `ImageCodec.SavePng(raster, output)`：保存透明 PNG，写入 sRGB 颜色信息；不修改输入快照。
- `ImageCodec.SaveJpeg(raster, output, quality, (R,G,B))`：质量 1–100，必须显式给出不透明背景。先按预乘 RGBA 规则合成背景，避免隐式黑底；不修改输入快照。
- 导出先写同目录临时文件并 flush，再发布；已存在的目标保持不变并报错。覆盖确认及已有文件替换由后续产品导出事务统一处理。非法预乘像素、尺寸和质量不生成正式输出。

在 `windows/` 下使用固定 .NET SDK 10.0.401：

```sh
dotnet restore Compositor.Imaging.Checks --locked-mode
dotnet run --project Compositor.Imaging.Checks -c Release --no-restore -- Compositor.Imaging.Checks/fixtures <新的输出目录>
```

检查使用已提交的自制样本，不依赖 Python。覆盖跨瓦片透明 PNG、八种 JPEG 方向、线性 ICC 转 sRGB、PNG 预乘往返、JPEG 指定背景、截断输入、尺寸限制及既存输出保护。夹具生成脚本使用 Pillow 和其 macOS 随附 LittleCMS；这只是维护工具，不是应用运行依赖。

本地证据见 [M2 图像 IO 记录](../../docs/windows/evidence/production-imaging-m2.md)及[PNG 完整性补测](../../docs/windows/evidence/production-imaging-png-integrity.md)。CMYK/广色域/16-bit/真实相机样本、资源压力、取消及 Windows 原生运行仍需补齐；CRC/容器校验也不等于所有 PNG 元数据语义均已验收，不据此关闭 W-015。
