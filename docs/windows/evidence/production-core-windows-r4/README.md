# Windows 11 生产核心 r4 待复测包（2026-10-02）

统一源码包 `CompositorProductionCore-r4.zip` 固定提交 `1e41027`，含 130 个文件、487030 字节，SHA-256 `0f8ba9c207d0210b6e087807fc92aa75055a9a4b1015a8041d0e631bf7ea9c0f`，ZIP CRC 通过。包位于 `/Users/admin/.codex/visualizations/2026/10/02/production-workflow-integration/CompositorProductionCore-r4.zip`；包含 Core/Smoke、Imaging/Checks、Workflow/Checks、自制图像输入、F01～F08/未来版本反例与正式原生 C 源码，不含构建产物。

从 ZIP 全新解压后，在 macOS arm64 使用固定 .NET SDK 10.0.401 锁定恢复并分别 Release 构建三个检查项目，均为 0 警告、0 错误。Core Smoke、Imaging Checks、Workflow Checks 从解压包运行均退出 0；此外，直接用解压包内的 C 源编译本地动态库，Core Smoke 带真实原生 C 像素调用仍退出 0。这只核查封包完整性和本机可执行性。

**Windows 实机状态：源码包及运行脚本已传输并核对 SHA-256，已解压到新的 `production-core-r4`；构建和测试尚未执行。** Windows 测试需使用既有便携 .NET SDK 10.0.401，从 r4 解压根目录运行三项检查并各用新的输出目录，留存原始构建/运行日志与工程/图片结果。原生 C 桥接须在 Windows 上独立构建并给 Core Smoke 提供 DLL 路径。r2 的 Windows 受限读写通过记录不能替代 r4 的像素、v8 和 PNG 完整性验证；旧 r3 包也不包含当前整合源码。CI、Windows 11 实机和正式应用验收均未据本页放行。

## 实机准备与运行记录器

2026-10-02 通过 UU 的 `session3` 对 PEIXU7-GY 完成只读核验：便携 .NET SDK 10.0.401、Python 3.13.7、既有 LLVM-MinGW 的 `clang.exe` 与离线 NuGet feed 存在。包及脚本的远端 SHA-256 与本地一致，目标目录此前不存在；见[界面观察记录](preflight-observation.json)。这是界面结果的人工转录，不是测试运行日志。

[run-r4.py](run-r4.py) 与源码 ZIP 同放于 `CompositorTest`，通过 `python ./run-r4.py` 执行。它先核验 ZIP 和解压后的全部 130 文件，再使用新的包缓存离线锁定恢复、构建原生 DLL 与三个检查项目；保存每步退出码及完整合并标准输出，核验 PASS 标记和预期产物，最终生成带逐文件 SHA-256 的结果 ZIP。任一步失败保留日志并返回非零。脚本不改变系统安全策略，也不清理已有测试目录。两项[本地故障注入检查](runner-local-checks.json)证明非零子进程和错误 SDK 版本均被判失败并保留日志；这些检查不属于 Windows 执行证据。

当前停在实际执行前，等待 Computer Use 工具内置规则要求的本轮运行确认。r4 仍固定 `1e41027`，不包含后续层级/资产归属和多层合成提交。

r4 封包后又补了 v8 父组与蒙版来源校验，见[本地反例记录](../production-core-hierarchy-m2.md)。这项新代码不在 r4 中，需在下一个统一包或正式 CI 中单独复核。
