# M2 预乘 RGBA Normal 合成基础（2026-10-02）

新增 `RasterCompositor.SourceOver(bottom, top)`，接收同尺寸的不可变 `TileRaster`，按 8-bit 预乘 RGBA 做 Normal/source-over，通道采用 `(bottom * (255 - topAlpha) + 127) / 255` 的整数取整规则。输入瓦片不修改；尺寸不符或颜色通道超过 alpha 时拒绝。此算子是多层 CPU 合成的基础，尚未接入多层工程的图层顺序、透明度、蒙版、组或其余混合模式。

`Compositor.Workflow.Checks` 在 300×300、跨 256 像素瓦片和 44×44 边缘瓦片上验证半透明、完全透明、完全遮挡与跨瓦片位置的预期 RGBA 值；随后把合成结果导出 PNG 并重新解码，逐瓦片比较完全一致。还检查输入快照不变、尺寸不匹配及非法预乘像素被拒绝。macOS arm64 .NET SDK 10.0.401 Release 构建 0 警告、0 错误，含实际 Mac 保存工程反向编辑的完整工作流检查退出 0。

该测试尚不等于画布预览和导出共享完整渲染器，也未在 Windows 实机、4K 大图或长时间编辑中测性能；W-012 仍未完成。
