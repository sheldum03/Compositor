# Windows 11 生产核心 r3 待复测包（2026-09-29）

`CompositorProductionCore-r3.zip` 含 67 个文件、52,571 字节，SHA-256 为 `36976ee7d6c87a012f41f2445599504a4364a0ace8952285ce9bfa0e87c73314`，ZIP CRC 通过。包内有固定 SDK 清单、Windows C# 核心与冒烟源码、F01～F08 和未来版本反例、生产原生 C 桥接与所需 C/H 源文件；没有构建产物。包外路径是 `/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/windows-manual-20260929/CompositorProductionCore-r3.zip`。

在全新目录解压此包后，用 macOS arm64 .NET SDK 10.0.401 执行 Release 构建：0 警告、0 错误。对解压包执行完整冒烟，包括真实原生 C 像素调用，退出 0，输出：

```text
PASS: edit, undo, redo, safe save, rejected-save protection, reopen, export, backup recovery, v1-v8 recognition and write protection, scaled-source write protection, changed-asset protection, tile snapshots, native C pixels
```

本轮新增两个安全反例：单图层源 PNG 尺寸与画布不同则只读；打开后源 PNG 被外部改变，则保存和导出必须拒绝且不产生正式输出。外部改动反例在修复前实测失败，修复后通过。

**Windows 状态：尚未传输和执行 r3。** 已在 Windows 11 实机通过的是此前 r2 的受限读写链路，见 [r2 记录](../production-core-windows-r2/README.md)。r3 仍需在该实体机核对 ZIP 哈希、以 10.0.401 SDK 构建并运行冒烟，取回完整运行日志与输出目录；原生 C 桥接还需 Windows 本机编译并将 DLL 路径作为冒烟第三参数。未完成前，不能把本页的 macOS 结果记为 Windows 验收。

2026-10-02 后又修复了备份恢复和保存提交后的清理失败边界，见 [本地故障注入记录](../production-core-m2-save-faults.md)。这些修改不在 r3 ZIP 中；该包继续只代表 2026-09-29 的源码快照。
