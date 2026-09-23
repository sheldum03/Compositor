# Qt S02 重放器准备（2026-09-23）

**2026-09-23 实机更新：Windows 两轮原生重放、视口读回及独立复核已完成；更新 P95 25–28 ms，未通过 16.7 ms 门槛。见 [Windows 原始结果](../qt-s02-windows/README.md)。以下保留准备阶段证据。**

已补齐独立的 Qt S02 原生窗口入口、逐帧原始记录、实际 backing-store 像素核对、异常退出和独立复核器。**此版本尚未在 Windows 执行，Qt 同机性能分布和 M1 仍未通过。** 当前 UU 工具持续报告 Mac 已锁定；此前 Avalonia R8 的最终状态仍待取回，R9 尚未实机运行。

工作负载和计时定义见 [PERFORMANCE.md](../../../../experiments/windows/qt/PERFORMANCE.md)。这里增加测试能力，未改变 Qt 笔刷算法、已冻结参考图或拟定性能门槛。

## 已完成验证

| 检查 | 实际结果 |
| --- | --- |
| Mac Release / Qt 6.11.2 / arm64 | 构建成功；native contract 与 Qt S02 lifecycle 两项 CTest 通过 |
| 100% / 150% 本地 offscreen | 各四笔、484 个实际绘制后回调；历史/源不可变性通过；两组 backing-store/reference 精确；物理缓冲区分别 1000×1000 和 1500×1500 |
| 生命周期 | 700×700 视口拒绝；首笔后关闭明确失败；禁止绘制触发 10 秒超时。均使用实际 Qt 事件循环 |
| ASan/UBSan | 相同两项 CTest 通过；仅自有 C++/C 被插桩，预编译 Qt 未插桩，关闭 leak detection，不作泄漏结论 |
| 复核器负例 | NaN 计时、缺帧、序号错误、像素差异、提前关闭、错误不透明度、摘要改变、把 headless 报告当 Windows 证据均拒绝 |
| 入口负例 | `--s02-window` 拒绝 offscreen 后端；修改冻结输入文件后 `--s02-check` 失败 |
| 原有回归 | 旧两笔检查的九份 PNG/工程文件逐字节不变；20 个合成样本报告除计时外与原证据完全一致 |
| 独立图像复核 | Pillow/NumPy 读取四组缓冲区/参考 PNG，像素完全相同、背景不透明且笔划非空；两种缩放的 4K 最终导出相同 |
| Windows 交叉构建 | LLVM-MinGW 20260908 + 官方 Qt 6.11.2 LLVM-MinGW SDK；四个新增构建产物为 x86-64 PE；七个 Qt/编译器运行依赖与既有 Windows 窗口包逐字节一致；30 项包内导入/导出关系无缺失 |

机器可读 [summary.json](summary.json) 记录源码文件 hash、二进制 hash、负例与图像结果；[local-archive-identity.json](local-archive-identity.json) 指向原始日志/报告/图像/源码快照归档；[dependency-symbol-review.json](dependency-symbol-review.json) 记录静态依赖检查。交叉构建和静态 PE 检查不证明 Windows 动态加载成功。

与 Avalonia R9 本地 S02 导出的直通 RGBA PNG 相比，空层/已有层分别有 115,521 / 218,977 个像素相差至多 1 通道值，Alpha 全部相同。这是跨框架观察，不是已批准容差；不据此关闭 D-03。实际 backing-store 与 Qt 自己的参考图仍要求精确。

较早的本地迭代发现通用 `writeFile` 拒绝覆盖已有检查点，现用 QSaveFile 原子更新报告；最终通过证据来自修正后的计时版本。最后两组主计时截止 paintEvent 返回后的队列回调，并保留 QPainter 释放时间用于分解。不要把早期仅记录 painter 释放的试跑计时当最终结果。

## 实机执行顺序

已生成 `CompositorQtS02R1.zip`（36,056,346 字节），源码提交 `406c239`；[包身份](package-identity.json)及[本地安装核验](kit-verification.json)已记录。包内四个 x64 构建产物、Python 安装/执行/复核脚本和源码 tar；安装时核对原 Qt 窗口 EXE、8 项继承依赖及 10 个冻结输入文件，复制到新的 `qt-s02-r1-app`。本地重建安装目录与 manifest 精确匹配，重复安装拒绝；执行器明确拒绝在非 Windows 系统运行。该包尚未上传到用户 Windows。

UU 恢复后，在 `CompositorTest` 目录确认 ZIP 的 SHA-256 与上述身份一致，再执行短命令：

```powershell
Expand-Archive CompositorQtS02R1.zip qt-s02-r1-kit
python qt-s02-r1-kit/run.py
```

执行器依次运行 C 契约、Qt 生命周期检查和两次完整原生 S02，各子进程的 PID、退出码、原始 stdout/stderr 均保留；性能数值未达标时仍保留第二次测量，不重试或改变负载。测试生成唯一结果目录及带 hash 的 ZIP。环境设置仅传入这些子进程，系统 DPI 自动生效，不修改全局 PATH 或现有 Qt IME 窗口目录。

1. 先确认并取回原来的 Avalonia R8 结果，避免重复启动；然后完成已准备的 R9 测试。
2. 在原 Windows 实体机将 Qt S02 安装到新目录，校验旧包、共享 DLL、冻结输入及新二进制身份，不覆盖当前 Qt IME 窗口目录。
3. 运行原生 C 契约和 Qt 生命周期自检，再顺序运行两次完整 S02。保持系统 DPI、无遮挡视口及电源配置；原生窗口逐次记录 62 笔 / 7200 个测量更新。
4. 取回原始 ZIP，核对远端/本地 SHA-256，独立复算 P95、采样内存、全部样本及四组原生缓冲区 PNG。超过预算必须保留失败结果，不修改工作负载。

本准备不包含 Qt 原生 IME 的补验、物理鼠标/笔、另一输入法、跨屏/其他 DPI 或集显设备结果，也不替代 S05 的资源稳定性验收。
