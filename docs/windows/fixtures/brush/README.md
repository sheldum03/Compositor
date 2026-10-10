# 固定 4K 软笔事件流及两条 Mac 参考

`soft-crossing-4k.json` 是候选共享输入：4000×4000、800 px、0% 硬度、40% 不透明度、RGB=(1,0.3,0.1)，两笔各 121 个文档坐标点。第一笔形成直角与临时笔尾，第二笔斜穿第一笔，用于局部提交、交叉覆盖及历史。它不是 S02 的全部验收输入；S02 仍要求 100% 不透明度、空层/已有层、30 笔及 Release/显示通路。

生成/重放：`WindowsBrushFixtureTests.fixedSoftStrokeSequenceCommitsLocallyAndPreservesHistory`，参数 false 强制 CPU dabs，true 明确要求 Metal 设备存在，不能静默 fallback。只注入覆盖后端，提交走真实 session.finishBrushImmediately / commitPaintSnapshot。每笔检查：无错误、下一笔立即可编辑、一次历史、当前与前一快照均未提前物化。两笔之后验证 undo/redo 的图像身份与像素，保存重开与导出一致。测试同时与本目录冻结参考 PNG 精确比较。

`first-cpu.png` / `first-metal.png` 为第一笔输出，`final-*.png` 为两笔输出，`soft-crossing-4k-*.comp` 为最终工程。工程 UUID 来自真实 session，不作为跨算法差异；比较规范化元数据及解码像素。`checksums.json` 固定全部输入/输出身份。

## 已测差异（Mac M5 Pro / Xcode 26.6 / Debug）

| 阶段 | alpha 不同像素 | alpha 差值 >2 的像素 | 最大 alpha 差值 | 全画布平均 alpha 绝对误差 |
| --- | ---: | ---: | ---: | ---: |
| 第一笔 | 1,006,503 / 16,000,000 | 207,573 | 7/255 | 0.1100701875/255 |
| 两笔 | 1,633,174 / 16,000,000 | 310,212 | 7/255 | 0.1737395/255 |

这些是观察结果，**不是 Windows 通过阈值**。透明边缘反预乘后的 RGB 差值会放大，不能将 straight RGBA 最大差直接解释为可见色差，也不能用低全画布均值掩盖局部边缘差异。完整统计保存在 `../../evidence/brush-*-cpu-metal-difference.json`，由 Pillow 11.3.0 解码后用 ImageChops.difference / ImageStat 计算。

`../../evidence/timings-{cpu,metal}.json` 保留逐 append 和 mouse-up 测量，仅 2 笔、Debug、无画布显示/上传到展示的端到端测量。不是 V-08 成功，也没有决定 CPU/GPU 技术路线。Windows 原型必须重放相同事件且分别说明参考算法，不能把两条 Mac 路径当 bit-exact 等价。
