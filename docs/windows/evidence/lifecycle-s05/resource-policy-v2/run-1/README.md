# S05 v2 第一轮前瞻运行：正确性与候选资源检查通过

规则提交 `8a4d1d8` 后启动的 PID **40220** 已正常完成，原生与 Windows 复核退出码均为 0。此次运行约 **1,205.49 秒**，使用原诊断 DLL、样本与运行库，GC 环境覆盖为空。锁屏时的 [26 轮检查点](../run-1-launch-observation.json)保留原样；恢复后从原会话、同一输出目录取得完成结果，没有重新运行。

## 完整性与正确性

- Windows 输出：`C:\Users\Administrator\Desktop\CompositorTest\s05-extended-20260924-142249`。
- ZIP 为 **37,548,082 字节**，SHA-256 `854049d9b1871e82d7428cfa37dede451d95b75ea7773468f9437b97b74ae440`；Windows 文件摘要与取回后的本地摘要一致，CRC 与 **114 项**文件清单全部通过。
- 27 轮共 **2,700 次编辑、2,700 次撤销、2,700 次重做、56,700 次预览回调**；本地正确性复核与 Windows JSON 完全相同，两份 stderr 均为空。
- **54 张 4000×4000 图像**逐张独立解码，RGBA 摘要全部等于固定 Windows R9 基准 `a8a62c6c48e71e2ce419ee56802d7160c0beaeded89c386d903ac60deba58a8e`。

归档身份、逐图摘要、正确性与资源输出分别见 [archive-review.json](archive-review.json)、[correctness.json](correctness.json)、[resources-v2.json](resources-v2.json)。[本次复核脚本快照](review-archive.py)包含本任务绝对路径；运行依赖 Pillow 和仓库已有判定器。错误归档摘要及 CRC 正常但成员被改动的负控制见 [review-helper-controls.json](review-helper-controls.json)。这些控制不算 Windows 实测。

原始 ZIP、114 项原始文件和复核输出保留在本任务外部证据目录 `s05-resource-v2-prospective-20260924/run-1`；原始 ZIP 位于其父目录。[清单](files.json)及 [Windows 身份](identity.json)在此保留。原报告 SHA-256 为 `a24ac1eaeffc387b976c2a73f2200e35256a63104ea69c8af1b9a01ab9141642`。

## 候选资源判断

运行前固定的 v2 七项检查全部通过，判定器退出 0；其 `resourceAccepted=false` 保持原值，尚需第二个独立进程及完整评审。此次 v1 上限检查也通过，不覆盖先前 v1 实机失败。

| 指标 | 实测 |
| --- | --- |
| 自然采样专用内存最大值 | 592.26 MiB |
| 后九轮最低占用相对中间九轮 | 下降 9.57 MiB |
| 后九轮最高占用相对前十八轮 | 下降 53.30 MiB |
| 后九轮句柄最低/最高值相对中间九轮 | −1 / −11 |
| 自然空闲 | 60.106 秒，Gen2 次数保持 63 |
| 最终诊断回收 | 旧文档弱引用 0，托管存活约 7.55 MiB |

这仅覆盖固定原型的自然采样、句柄和文档释放；不代表连续峰值、VRAM、物理输入、其他负载或完整产品验收。长测期间未启动远端模型推理或其他重负载测试，原 IME 跟进窗口保持打开。
