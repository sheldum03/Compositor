# PNG 完整性拒绝补测（2026-10-02）

继续 M2/W-015 的损坏输入保护。旧 `ImageCodec.Load` 只依据 Skia 像素解码成功码，构造完整像素但损坏容器的反例后，15 项中有 8 项被错误接受：缺少/截断 IEND、IEND 后附加数据、辅助块 CRC 错误、非法块名称、非空 IEND、非法/过晚 PLTE。[修复前检查](production-imaging-png-integrity/before.txt)真实退出 134，[逐项报告](production-imaging-png-integrity/before.json)保留接受列表。

增加内部 `PngIntegrity`，在同一个文件句柄上以 64 KiB 缓冲流式核验块长度、字母名称、全部块 CRC、IHDR/PLTE/连续 IDAT/IEND 顺序和文件尾，随后复位流位置交给既有解码器。未知辅助块仍可接受，未知关键块拒绝；没有改写像素算法或公共 API，也没有新增包依赖。依据为 [W3C PNG 文件结构与完整性规则](https://www.w3.org/TR/png-3/)。本实现不据此宣称覆盖所有辅助元数据语义。

修复后 macOS arm64 / .NET SDK 10.0.401 [Release 构建](production-imaging-png-integrity/build.txt)为 0 警告、0 错误，[完整检查](production-imaging-png-integrity/after.txt)退出 0；15 个损坏 PNG 全部拒绝。另加合法的连续分段 IDAT 和 IDAT 后未知辅助块两项，防止过度拒绝。原 PNG/透明度/EXIF/ICC/JPEG 背景和输出保护继续通过，共 12 组参考输入、2 组 JPEG 背景及 23 条拒绝路径。[检查结果](production-imaging-png-integrity/results.json)和[独立 Pillow 复核](production-imaging-png-integrity/pillow-review.json)保留完整数值，12 组 PNG 参考最大差均为 0，JPEG 背景最大差均为 1。

反例由 Python `struct`/`zlib` 生成，其中错误 CRC、关键块及尾部破坏均直接修改字节，未调用被测校验器；夹具生成器和合法/非法样本随检查项目提交。原始输出目录为 `/Users/admin/.codex/visualizations/2026/10/02/windows-production-imaging/png-integrity-before` 与 `png-integrity-after`。[本轮源码/样本摘要](production-imaging-png-integrity/sha256.json)绑定这次修复。

**Windows 实机尚未运行本轮代码**。本结论只证明本地容器拒绝及既有像素回归，正式工程事务的集成、真实图像覆盖、完整产品 IO 和平台验收仍按原 M2/M6 计划推进。
