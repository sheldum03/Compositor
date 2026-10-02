# M2 Gray8 蒙版限定合成（2026-10-02）

新增单通道 `GrayTileRaster` 和 `RasterCompositor.ApplyMask`。覆盖率直接来自 Gray8 PNG 字节，按 `(premultipliedChannel * coverage + 127) / 255` 缩放四个预乘 RGBA 通道；0 完全隐藏、255 保留原值。`ImageCodec.LoadGrayMask` 先验证 PNG 块/CRC/文件尾，再要求 8-bit 灰度、无 alpha、无旋转，按 Gray8 解码且不做颜色空间转换。真实格式夹具取自 F04 的蒙版，64×48 的位置 `(0,0)/(1,0)/(31,23)/(63,47)` 分别读得 `0/4/125/255`；RGBA PNG 被拒绝。

受限 v8 工程渲染现支持与画布同尺寸、默认位置的每层 Gray8 蒙版和 `maskEnabled` 开关。300×300 两层工程验证覆盖率 0/128/255 的精确预乘结果、禁用蒙版回到未遮罩合成、PNG 导出回读逐瓦片一致；尺寸不符和 RGBA 假蒙版明确拒绝。macOS arm64 .NET 10.0.401 Release 的 Imaging.Checks 与 Workflow.Checks 均退出 0，后者还包含原 Mac 产物反向编辑检查。

未实现独立蒙版位置、缩放/采样、组蒙版、剪贴或编辑保存蒙版；此路径仍只读。后续[真实 Mac 蒙版渲染对照](production-gray-mask-mac-readback.md)已在固定样本的 360,000 个通道取得零差异；Windows 实机、性能与长时间资源检查尚待执行，不能关闭 W-012/W-013。
