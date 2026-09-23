# S05 R8：阶段分配与像素比较（2026-09-23）

**已定位并移除像素比较器的两份完整托管图像副本；编辑阶段仍是最大分配来源。** 这不改变 S05 工作负载和像素标准，不构成 Windows 资源验收。

在 R7 同一三轮原生 Skia headless 工作负载的实际边界，用 `GC.GetTotalAllocatedBytes(true)` 采样。每轮编辑加检查点约分配 371.46 MB，撤销及摘要约 0.69 MB、重做及摘要约 0.70 MB、保存重开约 0.62 MB、像素比较约 128.01 MB；数字为托管分配，不包含 Skia native 图像空间。临时日志 `[DEBUG-s05-phases]` 已从交付源码移除。[完整阶段数据](r8-phase-comparison.json)。

`Program.Compare` 原先通过 `Pixels()` 解码，再将每张 PNG 的 RGBA 像素复制到完整托管数组。现在两张 Skia 位图在比较期间保持有效，按同样显式 sRGB、Premul、RGBA8888 解码，用只读 span 比较；退出方法时释放位图。没有修改 `Pixels()` 的其他调用者、误差统计或热力图公式。

新增 [真实比较器回归](../../../../experiments/windows/pixel-comparison-regression/README.md)，通过程序集调用实际 `Program.Compare`。3×2 奇数宽度样本验证完全相同、一通道一级差异、多个 RGB 差异、Alpha 差异、所有五个统计字段、正反顺序、热力图全部像素；半透明样本验证预乘 RGBA；尺寸不符必须拒绝。

4000×4000 同图比较在旧实现上分配 **128,009,560 B**，超过消除整图副本的 1 MiB 回归上限，明确失败；修改后只分配 **4,776 B** 并通过。这个上限只检查比较器的托管复制，不是 S05 进程占用门槛。原实现与新实现的功能断言均通过。

同样三轮阶段测量中，比较阶段由每轮约 128 MB 降至约 5 KB，编辑阶段仍约 372 MB。基线、修改后的插桩版及去掉插桩的三轮版本均正常退出、独立 `review-s05.py --allow-local --gc-diagnostics` 通过。18 张 4000×4000 final/reopened PNG 的 RGBA 均与 R7 Windows 图相同。共同 SHA-256：`a8a62c6c48e71e2ce419ee56802d7160c0beaeded89c386d903ac60deba58a8e`。

清理后的程序还运行了完整 20 个合成样本及项目往返/守卫检查，`results` 的全部比较统计与既有 `avalonia-combination-macos.json` 完全相同。Windows x64 探针及回归器发布成功，两个产物引用的探针 DLL/PDB 字节一致；24 项基础依赖定义、版本、contentHash 未改变。发布过程中产生的 RID 锁文件留在证据目录，仓库锁文件保持原样。

下一步在 Windows 先执行同一比较器回归，再运行九轮加 60 秒自然空闲流程。分配降低不保证专用内存峰值同时下降：原生位图仍占用内存，回收时机也可能变化。Windows 实测、Qt 与设备矩阵、M1 和生产阶段仍需继续。
