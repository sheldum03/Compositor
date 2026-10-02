# M2 保存中的真实进程终止检查（2026-10-02）

此前的[保存故障检查](production-core-m2-save-faults.md)通过受控异常验证回滚，异常仍会执行 `catch/finally`。本轮新增独立 `Compositor.SaveCrash.Checks`：父进程启动新的保存子进程，子进程在指定边界输出并刷新 `READY`、保持存活，父进程核对边界后调用 `Process.Kill`。子进程没有机会执行保存清理。所有目录都是本次测试新建，不操作用户工程。

macOS arm64 / .NET SDK 10.0.401 下，从固定 `6f739b7` 源码加本轮测试与 friend assembly 的独立目录锁定恢复并 Release 构建，0 警告、0 错误。父进程退出 0，**8 个子进程均被终止并退出 137**。完整[命令与退出码](production-save-process-kill-m2/execution.json)、[源码 SHA-256](production-save-process-kill-m2/source-sha256.json)、[构建日志](production-save-process-kill-m2/build.log)、[运行日志](production-save-process-kill-m2/run.log)和[逐场景结果](production-save-process-kill-m2/results.json)已归档。

| 保存场景 / 中断位置 | 实际恢复结果 |
| --- | --- |
| 替换保存：PNG 仅写入 8 字节头部 | 正式目标仍是完整旧版；不完整资产只在临时目录 |
| 替换保存：临时工程已校验 | 正式目标仍是完整旧版；临时目录有完整新版 |
| 替换保存：旧目标已移入备份 | 重新打开恢复完整旧版；临时目录中的新版也可完整解码 |
| 替换保存：新版已移到正式路径 | 正式目标读回完整新版，完整旧版备份仍可读取 |
| 替换保存：已提交、备份清理到一半 | 正式目标仍是完整新版；未决备份阻止后续保存 |
| 首次保存：PNG 仅写入 8 字节头部 | 无正式目标，只有不完整临时目录 |
| 首次保存：临时工程已校验 | 无正式目标，完整新版仍保留在临时目录 |
| 首次保存：新版已移到正式路径 | 正式目标读回完整新版 |

父进程逐瓦片比较全部恢复像素、名称、文档 ID 和干净状态，核对终止时的目标/备份/临时目录布局。存在未决备份时，再次保存必须失败且不改变已恢复工程；所有输入工程逐文件 SHA-256 保持不变。测试保留临时目录和部分清理的备份以供审查，没有把自动清理当作通过条件。原始输出归档的[路径及摘要](production-save-process-kill-m2/archive.json)和[逐文件摘要](production-save-process-kill-m2/output-sha256.json)可定位到本地证据目录。

复现：在 `windows/` 下对 `Compositor.SaveCrash.Checks` 执行 `dotnet restore --locked-mode`，然后 `dotnet run --project Compositor.SaveCrash.Checks -c Release --no-restore -- Compositor.Imaging.Checks/fixtures <不存在的新输出目录>`。会启动并仅终止本测试创建的 8 个子进程。

Windows CI 源码已扩展为 Core/Imaging/Workflow/SaveCrash 四项矩阵，单独记录恢复、构建和运行日志，立即检查原生进程退出码，并在失败时保留产物。这里只做 YAML 解析和项目/样本路径检查，**没有推送或触发远程 CI，也没有本轮 Windows 执行证据**；r4 固定包不含此新增检查。

该结果只覆盖有确定边界的进程终止，不证明断电、文件系统持久化、磁盘故障、回滚自身失败或并发外部改写。对首次保存时完整但未提交的临时工程，测试证明文件可读，尚未提供产品级恢复界面。W-014 仍未整体关闭。
