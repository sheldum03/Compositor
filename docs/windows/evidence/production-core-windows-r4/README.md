# Windows 11 生产核心 r4 待复测包（2026-10-02）

统一源码包 `CompositorProductionCore-r4.zip` 固定提交 `1e41027`，含 130 个文件、487030 字节，SHA-256 `0f8ba9c207d0210b6e087807fc92aa75055a9a4b1015a8041d0e631bf7ea9c0f`，ZIP CRC 通过。包位于 `/Users/admin/.codex/visualizations/2026/10/02/production-workflow-integration/CompositorProductionCore-r4.zip`；包含 Core/Smoke、Imaging/Checks、Workflow/Checks、自制图像输入、F01～F08/未来版本反例与正式原生 C 源码，不含构建产物。

从 ZIP 全新解压后，在 macOS arm64 使用固定 .NET SDK 10.0.401 锁定恢复并分别 Release 构建三个检查项目，均为 0 警告、0 错误。Core Smoke、Imaging Checks、Workflow Checks 从解压包运行均退出 0；此外，直接用解压包内的 C 源编译本地动态库，Core Smoke 带真实原生 C 像素调用仍退出 0。这只核查封包完整性和本机可执行性。

**Windows 实机状态：待传输、哈希核验、锁定恢复、构建和执行。** Windows 测试需使用既有便携 .NET SDK 10.0.401，从 r4 解压根目录运行三项检查并各用新的输出目录，留存原始构建/运行日志与工程/图片结果。原生 C 桥接须在 Windows 上独立构建并给 Core Smoke 提供 DLL 路径。r2 的 Windows 受限读写通过记录不能替代 r4 的像素、v8 和 PNG 完整性验证；旧 r3 包也不包含当前整合源码。CI、Windows 11 实机和正式应用验收均未据本页放行。

r4 封包后又补了 v8 父组与蒙版来源校验，见[本地反例记录](../production-core-hierarchy-m2.md)。这项新代码不在 r4 中，需在下一个统一包或正式 CI 中单独复核。
