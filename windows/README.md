# Windows 生产工程（初始切片）

M1 选用 Avalonia/.NET；此目录是生产实现，`experiments/windows/` 继续保存原型证据。当前与 GUI 无关的工程核心只读取 v1–8 manifest 的基础字段、图像路径及 PNG 头部；其中结构简单、全画布、无变换的 v1 或 v8 单图层工程可编辑，固定 F02～F08 复杂样本仍只读。单图层切片已有改名、像素瓦片快照、共用撤销/重做历史、安全保存与重开。`Compositor.Imaging/ImageProjectWorkflow` 把真实 PNG/JPEG 解码、v8 新工程导入、像素保存及 PNG/JPEG 导出接入此受限链路。它尚不是完整 M2，更不是 Windows 编辑器。

固定 SDK 10.0.401。当前只把源 PNG 与画布尺寸一致、未变换的单图层工程标为可编辑；打开后若源 PNG 被外部改动，保存会拒绝覆盖，Core 的原始文件导出也会拒绝，工作流仍可从已经载入的内存像素快照导出。验证命令：

```sh
cd windows
dotnet run --project Compositor.Smoke -c Release -- ../docs/windows/fixtures <新的空目录>
```

Windows 上也可从解压后的源码根目录双击 `windows/run-smoke.cmd`。脚本将固定 SDK 版本、Release 构建与托管核心冒烟输出写入源码根目录下的 `production-core-smoke-*` 文件，窗口保持打开以便查看退出结果。需要 .NET SDK 10.0.401；若使用便携 SDK，先把 `DOTNET_EXE` 环境变量设为其 `dotnet.exe` 的绝对路径。

共享 C 算法的正式桥接源码在 `windows/native/`；提供原生库路径作为冒烟第三个参数，才会执行预乘 RGBA 夹取与 alpha 提取的 P/Invoke 检查。Windows CI 以 CMake 构建此库并执行完整冒烟。本地 macOS 无 CMake 时已用 clang 从相同 C 源构建并检查；Windows 结果须独立取得。

冒烟覆盖 F01 的编辑和导出、F02～F08 的只读保护、未来版本拒绝、v8 层级/蒙版引用与资产归属反例，以及备份目录恢复。输出目录必须不存在。`ProjectStore.Save` 先在目标相邻目录构造并校验完整新副本；若覆盖已有工程，先将旧目录移为 `.backup`，提交新目录并验证后才移除备份。发现既存 `.backup` 时拒绝再次保存；打开目标缺失但备份仍在的工程时先验证备份再恢复。提交前故障会保留旧版，把已移到正式路径但未验证的新版隔离为 `.failed-*`；首次保存也不会留下未验证的正式目录。新版提交后若旧备份清理失败，保留已验证的新版和剩余备份、报告错误并阻止下次保存，等待人工检查。故障注入已覆盖这些阶段；断电、磁盘写入中断等情形仍需验证，Windows 实机也需复核，之后才可作为 W-014 验收。

后续顺序：完整文档事务和资产身份、共享瓦片的多层合成（已有平面 Normal 图层及全画布 Gray8 蒙版的只读工程渲染，其他变换/蒙版位置/组等仍缺）、受保护的 v1–8 reader、剩余保存故障与图像格式/资源压力验证，随后把核心接入唯一 Avalonia 生产窗口。UI、IME、S02/S05 和设备测试均在正式应用上重新验收。

生产像素链路的独立检查：先在 `windows/` 下对 `Compositor.Workflow.Checks` 执行 `dotnet restore --locked-mode`，再运行 `dotnet run --project Compositor.Workflow.Checks -c Release --no-restore -- Compositor.Imaging.Checks/fixtures <新的输出目录>`。测试会生成新 v8 工程、编辑并撤销/重做像素、保存重开、导出 PNG/JPEG，再检查恢复旧快照、拒绝损坏输入及外部资产变化。Core 不依赖 Imaging；只有工作流调用二者。像素编辑目前仍限单层；平面多层可改名、显隐、排序并安全保存，其他完整 v8 语义和 Windows 实机执行未验收。

真实保存中断检查：锁定恢复 `Compositor.SaveCrash.Checks` 后，执行 `dotnet run --project Compositor.SaveCrash.Checks -c Release --no-restore -- Compositor.Imaging.Checks/fixtures <新的输出目录>`。父进程在 PNG 写入、目录替换及备份清理边界终止本测试创建的 8 个子进程，再核对完整旧版/新版像素及备份保护。详见[进程终止证据](../docs/windows/evidence/production-save-process-kill-m2.md)；本地通过不等于 Windows 或断电持久性通过。
