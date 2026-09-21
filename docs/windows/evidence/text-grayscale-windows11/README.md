# Windows 11 完整文字修复复测

用户在原 Windows 11 x64 实体机执行，上传 text-fix-20260921-152945.zip；1,560,237 bytes，SHA256 cafecf36e32bb646e4301863f070d08ed3939545704d38dbae8e3da58e155f55，64 文件 CRC 通过。receipt.json 保留逐文件身份。本任务只读取报告和 PNG，没有执行附件脚本。

Pillow 11.3/NumPy 独立解码并复算：12/12 预览与导出完整 RGBA 精确相等，12/12 取消后恢复精确；12 组墨量和 Mac 参考差分指标与报告完全一致。普通文字墨量比 0.8053–0.8623，全部通过原有严重丢失防护阈值 0.5；没有放宽阈值，也不将其视为正式像素容差。2,631 个变换命中样本通过。

实际查看 all-12.png 与 contact.png，中文、Latin 字形均恢复、Emoji 可见；镜像是固定样本变换。原严重缺字缺陷已在 Windows 修复。Mac 对照仍有间距、光栅化和 Emoji 外观差异，最大通道误差为 161/166，未接受跨平台像素容差。NativeImeExecuted=false；真实 Windows 窗口、微软拼音候选位置、焦点、DPI 和 D-11 工作流均不据此通过。

完整 ZIP 和 60 张原始 PNG 保存在本任务 artifact 的 text-grayscale-windows11 目录；仓库保留原始摘要、日志、报告、独立复算和两张接触图。后续先启动已准备的 Avalonia 原生窗口，再逐步指导用户测试微软拼音，不重复本轮文字批处理。
