# S02 重放入口准备：已整合，未运行原生窗口

父任务reviewed-v2补丁已审查并应用到权威源码，独立新增--s02-window/--s02-check。源码与reviewed-v2三文件逐字节一致；当前用户正在运行的Windows IME包未覆盖，也未打包或部署性能入口。

本任务实际Mac Release构建0警告0错误；--s02-check退出0，空层/已有层各预热1+测量1笔，共484绘制回调，逐序号独立复算通过；两张最终PNG与父任务修正版逐字节一致。原--brush回归退出0、13会话检查通过，全部9个PNG/工程文件与旧输出逐字节一致。父任务Windows publish34文件身份逐项重验，但本任务未执行Windows二进制。

审查发现并修正：①最后帧完成与关闭取消竞争会假成功，提交前和最终Passed前加取消检查；②GC计数原先含oracle/undo/redo/导出，现于commit后、正确性检查前冻结。父任务在独立两个副本注入同一第484帧先SetResult再Cancel：旧版exit0/completed=true/4trial；修正版exit-6/completed=false/3trial，保留OperationCanceledException。此-6源自headless Check抛异常，不能当Windows原生退出码。我们核对注入源码精确差异、原始报告与摘要一致；此负例由父任务执行，本任务独立执行正常整合回归。

headless-report.json/brush-report.json与取消红绿源码/日志/报告保留。完整PNG、发布二进制和原始日志在双方artifact。check-cancel-race.py及parent-README保留父任务原路径与来源；不是已经执行GUI的说明。

尚需前台可用时真实验证窗口成功、视口不足拒绝、中途关闭/取消；随后才在Windows11参考机运行空层/已有层各30笔。逻辑可见性检查不能证明窗口未被遮挡或完全处于显示器内，必须实际观察。计时末端只是Skia canvas lease释放，非物理显示/输入设备延迟/GPU；专用内存为采样高水位，Mac不可用时为null。当前正确性回归与并发时序不构成S02性能通过，performanceAccepted始终false，W008/W030/M1不关闭。
