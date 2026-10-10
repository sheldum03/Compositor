# S02 审查修正版 v2

本目录替代上一级初稿；不覆盖其原始证据。基线仍为83ccc60，实施工作区未修改，补丁apply-check退出0。测试范围、真实窗口未执行及部署限制沿用上一级README。

根据实施任务静态审查，完成两项修复：提交前及最终Passed=true前检查取消；commit后立即冻结GC计数，然后才执行像素Digest/oracle/undo/redo/PNG导出，避免将测试校验开销算入笔刷GC。CPU计时也在正确性检查前结束。

在两个独立副本加入同一仅测试故障：第484个（最后一个）绘制回调先完成Task，再取消，模拟完成与关闭竞态。旧版实际exit0、completed=true、4个trial，证明存在假成功；修正版实际OS returncode=-6（headless Check发现失败后抛出的未处理异常/SIGABRT），completed=false、保留3个已完成trial和OperationCanceledException。这个负例不是产品异常码约定；真实窗口路径使用显式desktop.Shutdown(1)，仍待GUI实跑验证。两版注入源码/构建日志/运行日志/原始报告保留cancel-before、cancel-after，复现驱动check-cancel-race.py，摘要cancel-race-results.json。

正常修正版Mac headless再次完成4笔/484帧，完整序号/每笔120更新/事务oracle通过。用review-report.py独立检查，报告headless-independent-review.json；并发活动未隔离，这些数字不作性能结论。GC采集位置移动，算法与最终像素未改。Mac Release零警告零错误；Windows x64 publish成功，34文件身份列于win-x64-files.json。

真实窗口成功、1000逻辑viewport不足拒绝、中途关闭三类路径尚未执行。不使用新入口覆盖用户正在运行的IME测试包，不宣称S02或M1已通过。下一步先由实施任务审查本目录补丁，再在可用前台验证生命周期，之后才能安排参考机标准负载。
