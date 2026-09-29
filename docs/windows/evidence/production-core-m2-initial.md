# 生产工程首条读写链路：本地验证（2026-09-29）

这是 M2 的**初始工程切片**，不是 W-010～W-015 全部验收。源码在 [`windows/`](../../../windows/README.md)。固定 SDK 10.0.401，目标 `net10.0`；本地使用官方 macOS arm64 SDK 归档，SHA-512 `69f64eb00dc045398755c440b152225d544301a345a146a16e86a56a0c52b7c94b2c331520e976dbb821f18d31930aafbd25bb85961e3517e0665414ce0cbcff` 与官方 release 元数据一致。未更改 Mac 产品工程。

Mac arm64 本地 Release 构建：**0 警告、0 错误**。固定样本冒烟退出 0。后来加上的瓦片快照检查与共享 C 算法边界也在同一 SDK 下重新构建/运行通过。macOS 原生库直接用 clang 从 `windows/native/bridge.c` 和既有 `Compositor/Rendering` C 源构建，本机没有 CMake；Windows CMake/CI 路径未执行。当前源码输出：

```text
PASS: edit, undo, redo, safe save, rejected-save protection, reopen, export, backup recovery, v1-v8 recognition and write protection, tile snapshots, native C pixels
```

测试使用 F01 v1 的单一可见、全画布、未变换 PNG 图层：改名后撤销/重做、安全另存、重开；导出 PNG 的 SHA-256 与原图精确相同。预置同名 `.backup` 后再次保存被拒绝，原 manifest 不变，未保存的会话仍为脏；将目标目录临时移为 `.backup` 后，打开时恢复。F02～F08 均实际解析为只读，F04 不能另存，未来版本拒绝打开。新增瓦片快照检查了边缘瓦片尺寸、输入数组拷贝和未修改瓦片共享；原生测试对两像素调用真实 C `rgba_clamp_premultiplied`/`layer_extract_alpha` 并逐字节比较。测试均在新建输出目录进行，不修改固定样本。当前 PNG 只核验文件头和尺寸，尚未做完整解码/像素合成；这些反例不等同故障注入矩阵，也未验证断电或磁盘写入中断。

保存反例版的 Windows 包 `CompositorProductionCore-r2.zip`：21 文件、15,509 字节，SHA-256 `785a95758df7025088eec2bc81bcf8be6c7abe0e7de5fde6511fd85224531007`，ZIP CRC 通过。UU 私有传输显示已发送至 `C:\Users\Administrator\Desktop\CompositorTest`；Windows `Get-FileHash` 与本地值相同，确认 `production-core-r2` 不存在后解压。该包在瓦片快照与生产原生桥接加入前封包，因此不能覆盖最新代码。**截至本记录，Windows 尚未构建或执行该切片**；Windows 结果必须从对应包的 SDK、build/run 日志和输出目录另行复核。旧 `CompositorProductionCore.zip` 是未含保存拒绝反例的上一版，不作为本切片验收包。

下一步是 Windows 实机运行、补全事务/资产身份和像素合成、完整 v1～v8 安全读取、保存故障注入与 PNG/JPEG 路径；随后才接入 Avalonia 生产窗口。
