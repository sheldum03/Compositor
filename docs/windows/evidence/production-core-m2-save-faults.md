# M2 安全保存故障切片（2026-10-02）

本轮检查生产核心 `ProjectStore` 的两个故障边界，输入与输出均为新建临时目录，未修改固定样本。

1. 主目录缺失而 `.backup` 内容损坏：修复前冒烟退出 134，报 `Corrupt backup was moved before validation.`。旧实现先移动备份、再解析，导致损坏备份离开原路径。现改为先按当前 reader 的规则验证备份，成功后才移动；正常备份恢复继续通过。当前 reader 仍只检查 PNG 头部与尺寸。
2. 新版目录已提交且验证通过，但旧 `.backup` 清理到一半失败：注入器先删除备份 manifest，再抛出 `IOException`。修复前冒烟退出 134，报 `Cleanup failure damaged the committed project or session state.`；输出中出现 `Cleanup.comp.failed-*`，正式路径留下缺少 manifest 的旧备份。现把提交边界固定在新版验证成功处，立即将会话标记为已保存；清理异常不再回滚已提交的新目录。剩余备份保留以供检查，下一次保存被拒绝，未保存的新编辑仍保持脏状态。
3. 在临时副本验证后、旧目录移为备份后、新目录移到正式路径后分别注入 `IOException`。覆盖已有工程时，每个阶段都必须保留旧 manifest 与 PNG 的原始字节，会话仍为脏，不能残留 `.tmp-*` 或 `.backup`；第三阶段隔离未提交新版为 `.failed-*`。首次保存另在第三阶段注入：修复前冒烟退出 134，报 `Failed first save left an unverified project at the formal destination.`；修复后正式路径不存在，未校验目录仅留在 `.failed-*` 供检查。

使用 macOS arm64 .NET SDK 10.0.401 对 `Compositor.Smoke` 执行 Release 构建，结果 0 警告、0 错误。带原生 C 像素库的完整冒烟退出 0，输出包含 `validated backup recovery, cleanup-failure commit, precommit rollback`。这证明本地源码的上述受控异常路径；**尚未在 Windows 实机执行当前修改**。进程被强杀或断电时的落盘持久性、底层磁盘写入故障、回滚本身失败及并发外部修改不在本轮覆盖范围，W-014 仍未完成。
