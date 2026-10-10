# M2 多层工程保存中的真实进程终止（2026-10-05）

在 `24eff19` 已实现的平面多层元数据保存上扩展 `Compositor.SaveCrash.Checks`，保留原八个单层像素保存中断场景，增加六个多层场景。独立源码快照固定 `2e9bf74` 加本轮检查文件，见[基线](production-multilayer-save-crash/baseline.json)与[源码 SHA-256](production-multilayer-save-crash/source-sha256.json)，不包含同时进行中的逐层像素实现。

每个多层样本是两张不同像素的全画布 Normal 图层。子进程改名顶层、隐藏并移到底部，再保存；父进程在子进程输出并刷新指定边界的 `READY` 后用 `Process.Kill` 强制终止，不执行子进程清理代码。新旧状态的图层名称/顺序/显隐和合成画面均不同。

| 新增场景 | 恢复后的检查结果 |
| --- | --- |
| 覆盖已有工程，临时工程校验完成后终止 | 正式路径仍为完整旧版，临时路径有完整新版 |
| 覆盖已有工程，旧目录移入备份后终止 | 打开自动恢复完整旧版，临时路径中的新版完整 |
| 覆盖已有工程，新目录移到正式路径后终止 | 正式新版和备份旧版均完整可读 |
| 已提交新版，旧备份 manifest 被删除后终止 | 新版保持完整，未决备份阻止再次保存 |
| 首次保存，临时工程校验完成后终止 | 无正式工程，临时目录中的完整新版可读 |
| 首次保存，新目录移到正式路径后终止 | 正式新版完整可读 |

恢复验证逐项比较文档 ID、全部图层 ID/顺序/名称/显隐、完整合成瓦片以及全部原始图层资产 SHA-256，包括隐藏层；同时检查临时/备份/目标布局、源工程逐文件不变、未决备份拒绝保存且目标不被改写。单层场景继续检查修改后的像素，未被多层案例替代。

macOS arm64 / .NET SDK 10.0.401 锁定恢复、Release 构建和运行均退出 0，0 警告/错误；**14 个子进程均被实际终止并退出 137，14 个场景通过，其中六个是多层**。详见[执行命令](production-multilayer-save-crash/execution.json)、[构建日志](production-multilayer-save-crash/build.log)、[运行日志](production-multilayer-save-crash/run.log)、[结果](production-multilayer-save-crash/results.json)、[产物逐文件摘要](production-multilayer-save-crash/output-sha256.json)及[归档路径/摘要](production-multilayer-save-crash/archive.json)。原始目录为 `/Users/admin/.codex/visualizations/2026/10/05/production-multilayer-save-crash`。

复现命令仍为 `dotnet run --project Compositor.SaveCrash.Checks -c Release -- Compositor.Imaging.Checks/fixtures <不存在的新目录>`，在 `windows/` 执行。现有 Windows CI 矩阵会运行新增场景，但本轮未触发远程 CI。

本轮不证明多层像素编码/资产复制中途、图层增删、组/蒙版保存、断电、磁盘故障或 Windows 实机结果。首次保存的完整临时工程可读不代表已提供产品恢复界面。W-014 和 M2 保持未整体验收。
