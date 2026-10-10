# F10 固定损坏工程

由 `scripts/windows/generate-invalid-fixtures.py` 从已固定 F08 派生，重建时传一个不存在的输出目录；脚本拒绝覆盖已有目录。`cases.json` 是逐例预期错误分类，`checksums.json` 固定输入。

14 例覆盖：未来版本、v7 非法文本、重复 ID、失效 parent、自环组、剪贴循环、资源路径逃逸、超限画布、未知混合枚举、负 opacity、缺 PNG、坏 PNG、非数字 rotation、数值 1e999 溢出。NaN 字符串与数值溢出分别测试类型与数值边界，不使用 JSON 非标准 NaN 常量。

`WindowsInvalidFixtureTests.fixedInvalidPackagesFailForTheirExpectedReason` 确认每例失败类别；不会吞掉任意异常作为成功。missing-image 在 Mac 当前为 Cocoa fileReadNoSuchFile，其余按 ProjectError 分类；Windows 可有自身错误类型，但需提供同等可理解的拒绝原因。

这些是 reader 拒绝证据，不证明 Windows UI 已保护活动文档，也不证明保存中断恢复。超大资源、解压炸弹、文件锁/磁盘满/符号链接及最终安全保存故障注入仍需各自测试。故意损坏的 PNG 不应用作普通图片资产。
