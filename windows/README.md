# Windows 生产工程（初始切片）

M1 选用 Avalonia/.NET；此目录是生产实现，`experiments/windows/` 继续保存原型证据。当前仅有与 GUI 无关的工程核心初始切片：读取 v1–8 manifest 基本字段、图像路径及 PNG 头部，识别一个可编辑的 v1 单图层工程；固定 F02～F08 样本可以打开为只读，不允许编辑保存。单图层切片支持图层改名、撤销/重做、安全另存、重开与原 PNG 导出。它尚不是完整 M2，更不是 Windows 编辑器。

固定 SDK 10.0.401。当前只把源 PNG 与画布尺寸一致、未变换的单图层工程标为可编辑；打开后若源 PNG 被外部改动，保存和导出会拒绝写出，以免悄悄替换会话中的图像。验证命令：

```sh
cd windows
dotnet run --project Compositor.Smoke -c Release -- ../docs/windows/fixtures <新的空目录>
```

Windows 上也可从解压后的源码根目录双击 `windows/run-smoke.cmd`。脚本将固定 SDK 版本、Release 构建与托管核心冒烟输出写入源码根目录下的 `production-core-smoke-*` 文件，窗口保持打开以便查看退出结果。需要 .NET SDK 10.0.401；若使用便携 SDK，先把 `DOTNET_EXE` 环境变量设为其 `dotnet.exe` 的绝对路径。

共享 C 算法的正式桥接源码在 `windows/native/`；提供原生库路径作为冒烟第三个参数，才会执行预乘 RGBA 夹取与 alpha 提取的 P/Invoke 检查。Windows CI 以 CMake 构建此库并执行完整冒烟。本地 macOS 无 CMake 时已用 clang 从相同 C 源构建并检查；Windows 结果须独立取得。

冒烟覆盖 F01 的编辑和导出、F02～F08 的只读保护、未来版本拒绝，以及备份目录恢复。输出目录必须不存在。`ProjectStore.Save` 先在目标相邻目录构造并校验完整新副本；若覆盖已有工程，先将旧目录移为 `.backup`，提交新目录并验证后才移除备份。发现既存 `.backup` 时拒绝再次保存；打开目标缺失但备份仍在的工程时先验证备份再恢复。提交前故障会保留旧版，把已移到正式路径但未验证的新版隔离为 `.failed-*`；首次保存也不会留下未验证的正式目录。新版提交后若旧备份清理失败，保留已验证的新版和剩余备份、报告错误并阻止下次保存，等待人工检查。故障注入已覆盖这些阶段；断电、磁盘写入中断等情形仍需验证，Windows 实机也需复核，之后才可作为 W-014 验收。

后续顺序：完整文档事务和资产身份、共享瓦片接入实际像素合成、受保护的 v1–8 reader、安全保存故障矩阵、PNG/JPEG 输入与导出，最后把核心接入唯一 Avalonia 生产窗口。UI、IME、S02/S05 和设备测试均在正式应用上重新验收。
