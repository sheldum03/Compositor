# S02 已有 Windows 逐帧记录的耗时拆分

只读取无遮挡原始 report.json，不重跑远程测试，不修改冻结门槛或工作区。analyze.py 核对原报告 SHA 后按测量更新计算，排除预热和 pointer-down。

空层/已有层的 append P95 分别12.2344/12.2656 ms；paint P95分别6.2553/8.3527 ms；总耗时P95为26.9577/27.4426 ms。总耗时扣掉这两段后的残余平均为10.9362/8.6070 ms，不能把残余直接命名为纯调度等待。各阶段P95不能相加。

对应代码：BrushPerformanceProbe.Update计时包含Append、InvalidateVisual到绘制和lease释放；SceneControl在Lease前后的开销未细分。TiledRaster.DrawTile在每次绘制时用FromPixelCopy新建拥有像素副本的SKImage。报告每帧原生复制平均15.85/36.975 MiB；30笔内GC计数为253/70/27和254/71/25，但计数不是GC暂停时长，也不能直接证明它造成慢帧。

下一步建议先增加绘制请求、Paint入口、Paint结束到lease释放三段时间戳，辨认调度与绘制成本；再测量不可变瓦片图像复用是否值得实现。任何图像复用须保持当前快照所有权和延迟绘制安全，不把可变托管数组借给可能延迟执行的Skia绘制。不能仅凭这些数据认定GPU必需、选择框架或改变16.7ms门槛。

当前界面交互恢复及原生IME测试仍由实施任务负责，本分析不触及前台。
