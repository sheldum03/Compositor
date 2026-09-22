# S02 原生窗口重放准备

`--s02-window <brush-fixtures> <new-output> <native-library>` 使用独立窗口自动重放冻结软笔路径；`--s02-check` 在真正Skia的headless后端运行少量样本，检验重放器。现有`--window`手动输入法窗口及算法不变。发布前还须验证真实窗口生命周期；当前没有可交付的完整Windows性能包。

## 工作负载与记录

4000×4000文档，1000×1000逻辑视口，25%缩放，800px软笔、0硬度、100%不透明度。空层/预置已有层分别预热1笔、测量30笔；每笔pointer-down后120次固定路径更新。每笔重置到该组固定基线，连续历史增长由S05另测。

更新等待SceneControl实际Skia绘制回调、canvas lease释放后再继续。记录append、paint、更新至lease释放、像素复制量、线程、commit和每帧进程专用内存采样；保留所有原始帧。CPU及GC在commit后截取，之后才做源不变性、settled replay、undo/redo和PNG导出检查。oracle复用笔刷算法，只验证事务/尾迹，不是独立Mac渲染参考。

计时不包含真实输入设备到屏幕的延迟，不证明物理显示、GPU或显存性能。PrivateMemorySize64不可用时记null，采样高水位不是连续峰值。报告始终performanceAccepted=false，须独立复核并与冻结验收要求对照。

10秒无绘制回调、过小客户端视口、关闭取消或正确性失败均保留报告；关闭检查覆盖提交前及最终完成前。程序只检查客户端尺寸和逻辑可见性，实测还需确认窗口在屏幕内且未被遮挡/最小化。

## 已验证与待验证

权威源码Mac Release构建0警告0错误，headless自检4笔/484回调通过，最终图片与reviewed-v2精确。既有两笔13项会话检查和9个PNG/工程字节回归通过。第484回调完成后取消的独立红绿负例由父任务执行，原版假成功、修正版明确失败；headless异常退出不是Windows GUI退出码。

原生窗口成功、视口不足拒绝、中途关闭尚未执行；Windows x64 publish身份验证只证明构建产物，不证明DLL加载或性能。当前Windows用户IME包保持冻结。证据见[整合与负例记录](../../../docs/windows/evidence/s02-window-preparation/README.md)。

## 2026-09-22 性能诊断与候选

Windows 已完成原无遮挡基线、透明快速路径、定时器对照、缓存及向量累积对照，仍未达到 16.7 ms；最新结果见[优化记录](../../../docs/windows/evidence/s02-optimization/README.md)。新增逐帧请求至绘制入口、canvas 获取及释放耗时；`review-s02.py <report.json>` 独立复算拟定门槛，旧基线明确返回 exit 1。软件事件终点仍非物理呈现。

当前覆盖率向量累积版本的本地 484 回调、264 张像素回归和全部 65,536 输入对通过，标量回退也通过。Windows 原生 62 笔 / 7200 更新已完成：Append P95 2.7548/8.1024 ms，更新 P95 25.2184/26.7092 ms，仍未达到门槛；最终两图与原 Windows 基线精确。早期“原生窗口未执行”的段落是当时准备状态，不作为当前结论。
