# 窗口生命周期与 S05 执行记录（2026-09-22）

R8 原始 Windows 结果已取回并独立复核：900 次编辑及 18 张图像保持精确，分配估计下降 25.17%，采样专用内存最大值降至 556.88 MiB；资源稳定性仍未自动放行。见 [R8 实测](windows-pixels-r8-followup.md)。R9 笔刷原始瓦片只读共享优化已有本地追加分配约降 39.8% 的证据，首次 Windows 冻结图回归失败已定位为 Mac 基准的平台不匹配；优化前、R8、R9 的 Windows 输出精确一致。新增旧版生成的 Windows 基准后完整回归通过，两轮 S02 数值复核均未通过，S05 最终状态因再次锁屏待读取，见 [当前状态](r9-windows-running.md) 与 [基准诊断](r9-windows-baseline-diagnosis.md)。见 [R9 准备](r9-readonly-brush-preparation.md)。

R7 已在 Windows 完成并独立复核：报告流式写出后，总分配估计下降 24.36%、末尾空闲专用内存降至 768.53 MiB；采样峰值仍达 1204.20 MiB，资源稳定性未放行。见 [R7 优化与同机复测](windows-report-r7-followup.md)。

R6 已在 Windows 实机完成九轮和 60 秒自然空闲观测，原始数据及九组图像独立复核通过。空闲未触发 Gen2 回收、专用内存约保持 1.05 GiB；资源稳定性仍未放行。见 [R6 实测与分析](windows-soak-r6-followup.md)。

**2026-09-23 更新：R5 已在 Windows 实机完成九轮，原生/复核退出码均为 0，900 次编辑及九组图像独立通过。新增 GC 数据显示自然占用多次回落、最终托管对象显著回收但堆容量仍保留；资源稳定性未放行。见 [R5 实测与分析](windows-soak-r5-followup.md)。以下 R4/R5 准备段落保留历史状态。**

**最新进展：Windows R4 九轮诊断已完成并取回；900 次编辑、18,900 回调及九组保存重开图像独立通过。内存第六轮回落，但后段峰值继续抬升，资源稳定性仍未放行。见 [九轮结果](windows-soak-r4-followup.md)。连续 OS 笔刷证据已归档，Qt 原生输入仍在排查焦点。**

**R5 后续准备：新增 GC/分配采样，本地九轮及默认三轮、图像与 Windows 发布已验证；实机执行待恢复桌面控制能力或手动运行。见 [诊断包与执行步骤](r5-gc-preparation.md)。**

**后续更新：R3 已部署，小视口拒绝与中途关闭已在 Windows 通过；R1/R3 原始包已取回，600 次编辑、12600 回调及六组保存重开像素均完成独立复核。资源增长尚未关闭。见 [R3 复测记录](r3-followup.md)。** 以下保留第一次锁屏时的历史状态。

本轮从 `dc858aa` 继续用户四项要求。**S05 已在 Windows 实机执行，但资源稳定性未通过评审；M1 与完整 Windows 1.0 均未完成。** 当前 Mac 锁屏，UU 自动解锁失败，已请求用户解锁。修复包 R2 只传输了部分块，未安装；后续增加堆指标的 R3 已本地构建，未部署。

## 已执行

- 新增 `--s02-small-window`：Windows 700×700 原生窗口确实拒绝测量，报告为 `completed=false`、0 更新。但即时退出触发启动流程 NullReferenceException；[现场](lifecycle-small-result.png)保留，不能记为完整生命周期通过。
- 同一故障在 Mac 原生窗口重现，堆栈为 `ClassicDesktopStyleApplicationLifetime.StartCore`。将工作排到下一次 UI 调度后，原生小视口正常 exit 1、stderr 为空，报告保留明确拒绝原因；Windows 修复后回归因锁屏尚未完成。[修复前日志](compositor-small-native-before.log) / [修复后日志](compositor-small-native-after.log)。
- S05 本地完整 3×100 编辑、300 次撤销、300 次重做、6300 次预览及三次保存重开通过，最终闭合会话引用为零。[独立复核](compositor-s05-local-review.json)。额外记录托管堆保留/碎片；[本地资源摘要](local-resources.json)不是 Windows 内存证据。
- S05 初版引用检查在 1 次编辑、1 轮的缩小复现中失败。只把编辑过程隔离到返回的方法之后再测，即通过；说明测试本身的 JIT 临时引用影响了最后一个会话，不改笔刷产品算法。[最小失败](minimal-before.json) / [最小通过](minimal-after.json)。这两份诊断中的固定工作量描述仍是完整场景，实际仅 1 次编辑，不能计入 300 次验收。
- `review-s05.py` 的缺帧负例明确失败，[日志](compositor-s05-missing-frame.log)。现有 S02 headless 4 笔 / 484 回调及两张原生读回对照仍通过。

## Windows S05

使用 [R1 包身份](s02-lifecycle-package.json)，DLL `7c40d2232d5a7965a23c9a6fc4b19324f72dd80f98da11f0f288601c1bd6a9d2`。原始路径 `C:\Users\Administrator\Desktop\CompositorTest\lifecycle-s05-20260922-162054\s05`。与 S02 相同实体机、软件后端及 150% DPI。测试期间无其他 Windows 测试或传输。见 [运行中的窗口](s05-native-start.png)及[终端结果](s05-native-result.png)。

终端报告 completed=true、300 edits、error 为空；三轮关闭后专用内存为 **428,789,760 / 538,726,400 / 884,883,456 字节**，句柄为 **578 / 572 / 569**。最终诊断 GC 后专用内存 **761,618,432 字节**，旧会话引用为零。没有因引用释放或低于 S02 的 2 GiB 就判定 S05 通过；三轮自然内存增长需进一步区分堆保留、碎片与 native 分配。

**原始 JSON/PNG 尚未下载与独立复核。** 上述为有截图支持的终端摘要，见 [观察记录](windows-observation.json)。本机锁屏在后续 R2 上传中发生，不在 S05 运行中；旧失败记录和 R1 原始输出仍保留在 Windows。

## 解锁后的下一步

1. 直接传输并安装 R3（跳过未完成的 R2），核验包和 DLL hash；重测小视口正常拒绝与中途关闭，保存退出码、stderr 和部分报告。
2. 用新增 GC 堆指标重放 S05，取回 R1/R3 原始数据，核验 PNG、6300 帧及资源趋势；若仍有增长，继续查明资源所有权，不放宽门槛。
3. 在独立原生窗口执行连续输入、取消、立即下一笔、撤销/重做和保存。准备的 Win32 输入脚本必须先验证实际事件和图片，不计为物理输入。
4. 启动已部署 Qt 原生窗口补输入法对照；另一个中文输入法、集显、干净环境、跨屏/数位笔目前尚无新增可执行证据。见 [M1 评审草案](../../m1-selection-review.md)。
