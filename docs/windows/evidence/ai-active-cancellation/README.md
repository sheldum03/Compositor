# 活动 AI 推理取消实验（2026-09-23）

**后续实机更新：Windows 11 已完成五轮，终端所见原生和复核退出码均为 0；原始包已在远端生成，但取回时 Mac 锁屏。当前仍待本地独立复核，见 [执行与恢复记录](windows-execution-pending-review.md)。以下保留先前本地阶段的证据状态。**

**本地原生运行与独立复核通过；Windows 尚未执行，W-032/033、M1 和产品取消流程未验收。** 既有探针只设置运行前终止标志，新入口补充正在执行时的跨线程取消及同一会话恢复证据，不改变默认调用、权重选择或生产框架。

## 结果

| 实验 | 证据 |
| --- | --- |
| 五轮真实 CPU 推理 | 每次取消阶段记录 3–4 个 CPU 算子事件，恢复阶段各 336 个；恢复预测逐字节等于基准 |
| 取消到线程结束 | 五轮为 3.563 / 4.309 / 1.923 / 4.331 / 0.670 ms；仅本机观察，不设产品延迟门槛 |
| 默认入口回归 | 修改前后原始预测 SHA-256 均为 `bb24042cd87816ac64340e83aeb3ceb2620478906272bc35ae47abbd9fab7aae`；原入口仍仅测试运行前终止 |
| 反例一 | 新复核器拒绝旧探针“只测运行前终止”的报告 |
| 反例二 | 从副本中删除取消阶段算子事件后，复核明确失败；线程已启动不能冒充活动推理证据 |
| 构建 | AppleClang 21 / C++17，警告视为错误；CMake 3.31.6 Release 实际构建及新入口通过 |
| 内存检查 | 自有 C++ 启用 ASan/UBSan 的实际取消/恢复通过；预编译 ORT 未插桩，未启用 LeakSanitizer，不证明线程竞争或长期无泄漏 |
| Windows CI | 已加入五轮活动取消和独立复核步骤，YAML 解析通过；未推送、未运行，不记为 Windows 通过 |
| Windows x64 私下测试包 | LLVM-MinGW Release 构建通过；五个 AMD64 PE 的 123 个包内导入符号匹配导出，ZIP CRC 和逐文件身份通过；尚未在 Windows 执行，见 [构建记录和实测入口](windows-build-preparation.md) |

[五轮结果](five-trials.json)、[CMake Release](cmake-release-review.json)、[ASan/UBSan](sanitized-review.json)、[原始文件及源码身份](identity.json)、[缺算子反例](negative-missing-kernels.log)、[旧探针反例](negative-pretermination-only.log)。完整 profile、预测张量和日志保存在 [本地原始目录](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/ai-active-cancellation)，不含模型权重。

## 方法与复现

新增可选 `--active-cancel` 参数。默认推理及运行前取消验证结束后，建立独立 CPU 会话和 profile；工作线程进入 `Run`，控制线程等待 10 ms 且确认尚未完成，调用 `SetTerminate`，取得终止错误并等待线程结束，再清除标志、恢复同会话推理。所有借入的张量、名称、RunOptions 和 Session 在工作线程结束前保持有效。依据 [ONNX Runtime 跨线程终止 API](https://onnxruntime.ai/docs/api/c/struct_ort_1_1_run_options.html)。

独立脚本要求第一条 `model_run` 区间包含真实 CPU 算子、第二条恢复执行也有算子，且恢复输出等于原始基准。过早完成、取消前未实际计算、非终止异常、恢复差异或缺少 profile 都会拒绝通过；不自动重试以掩盖失败。

```sh
cmake -S experiments/windows/ai -B <BUILD> -DCMAKE_BUILD_TYPE=Release -DORT_ROOT=<VERIFIED_ORT_SDK>
cmake --build <BUILD> --config Release
<AI_PROBE> <VERIFIED_MODEL> <FIXED_INPUT_F32> <NEW_OUTPUT> --active-cancel
python3 experiments/windows/ai/review-active-cancel.py <NEW_OUTPUT>
```

SDK、模型与输入身份继续使用既有 `assets.json` 和固定输入；没有下载用户图片或上传图片。Windows 可在已配置依赖的同一构建环境执行上述入口，CI 使用已有校验过的模型和固定图像预处理结果。

本实验未涉及 UI 进度、关闭文档、切换文档、输入 revision 过期结果丢弃、取消后历史无修改或原有蒙版保留；这些仍须在 W-033 生产事务中验证。Windows 线程/运行库行为、成功模型获取、质量、权重许可、图像前后处理和正式发布门槛也继续保留。
