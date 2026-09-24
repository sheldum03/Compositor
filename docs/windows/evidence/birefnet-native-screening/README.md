# BiRefNet Lite 原生可行性检查（2026-09-24）

**Mac arm64 原生检查和实际工程读回通过；Windows 尚未执行，M1 与生产模型选择仍开放。** 本次接续[官方来源筛查](../birefnet-source-screening/README.md)，使用同一固定 ONNX 资产及 NASA 图片，未修改产品代码或替换 U2NetP。

## 实现与执行

同一 C++ 探针新增 `birefnet_probe` 构建目标，固定 1024×1024 输入和一个有限 logits 输出；既有 `ai_probe` 继续固定 320×320 输入、七个 `[0,1]` 输出。两者不能互换模型。Python 检查入口先校验模型和照片摘要，再按作者 notebook 做双线性缩放、ImageNet 归一化、sigmoid 和 Gray8 蒙版。原照片与蒙版分开写入 v8 工程。

- 官方 ONNX Runtime **1.30.0** Mac arm64 SDK 的 42,373,116 字节归档与发布摘要匹配。一次完整读取中断后，改用逐段范围及最终摘要校验；未使用不完整 SDK。
- 直接 clang++ 严格警告构建和 CMake **3.31.10** Release 两个目标均通过。CMake 来自私有构建目录的 PyPI 工具，不是系统安装；最初请求的 3.31.8 不可用。工具安装报告及原始发布元数据保留于外部证据目录。
- 完整图像/活动取消检查使用直接 clang++ 生成的程序。CMake 生成的两个程序另行实际推理、预终止/恢复，并确认原始预测与前者逐字节一致；不将两套程序身份混写。
- 本次 Python 3.13.5、NumPy 2.3.5、Pillow 12.2.0；没有声称运行了旧 `requirements.txt` 的环境。实际原生运行库是 1.30.0，未调用 Python 的 ONNX Runtime。

## 核验结果

| 检查 | 结果 |
| --- | --- |
| 重复推理 / 预先取消后恢复 | 输出逐字节一致，全部有限 |
| 活动推理取消 | 被取消调用内有 9 个真实 CPU 算子事件；线程退出后同会话恢复有 3,906 个，恢复输出精确一致 |
| 执行后端 | 实际 profile 仅含 CPUExecutionProvider |
| 错误输入 | 短张量、NaN、无效模型拒绝，无最终预测；已有输出拒绝且保留原预测 |
| 模型契约 | 两个程序均拒绝另一个模型，未发布预测 |
| U2NetP 回归 | 修改前 `febf508` 与新程序均通过活动取消复核，预测 SHA-256 均为 `bb24042cd87816ac64340e83aeb3ceb2620478906272bc35ae47abbd9fab7aae` |
| 实际 Mac 工程读回 | 1 项通过、0 跳过；导出 alpha 精确等于蒙版，禁用蒙版恢复原图，保存重开保留蒙版与原图像素 |

完整检查的三次推理约 9.40–10.04 秒，取消到线程退出约 75.47 毫秒；CMake 程序另次运行约 6.22–7.74 秒。它们是有其他本地任务并行时的观察，不据此比较模型速度、选择线程预算或放行 Windows 性能。原始 logits SHA-256 为 `87c2556501859c509ec37ca50715d08493b9823f25fab0a98766bc3af3cfeb71`。

## 证据与边界

[screening.json](screening.json)、[取消复核](cancellation-review.json)、[构建程序复核](cmake-build-validation.json)、[旧模型回归](u2net-regression.json)、[读回摘要](mac-readback-summary.txt)及[源码/环境身份](review.json)归档于此。[artifact-manifest.json](artifact-manifest.json)固定外部目录下原始 profile、预测、图像、日志和工具安装报告的大小与摘要；大文件和运行库不进入仓库。完整 `.xcresult` 保留在同一外部目录。

UU 仍因 Mac 锁屏不可操作，未确认此前 S05 指令是否启动，也未执行本候选的 Windows 程序。后续先检查原 S05 会话，再完成该候选 Windows x64 构建与真实运行。单张蒙版不构成质量验收；模型分发、30 张质量样本、产品取消事务、资源预算和完整 Windows 1.0 仍未通过。
