# BiRefNet 官方候选来源与本地筛查（2026-09-24）

**本地 CPU 冒烟通过；未在 Windows 执行，未选定生产模型，也未完成分发或效果验收。** 在 UU 因 Mac 锁屏不可操作期间，补充 W-009 的具体模型来源候选；不覆盖既有 U2NetP 结果或其转换权重许可缺口。

## 来源与固定身份

作者 [GitHub 仓库](https://github.com/ZhengPeng7/BiRefNet) 的 README 明确链接其 [v1 发布页](https://github.com/ZhengPeng7/BiRefNet/releases/tag/v1)中的 ONNX 转换文件。本次取得该发布的 `BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx`（资产 ID 186739942），224,005,088 字节，SHA-256 `5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333`。发布 API 没有提供远端 digest，因此该值是本地固定身份，不称与上游公开摘要核对通过。

作者 [BiRefNet_lite 模型卡](https://huggingface.co/ZhengPeng7/BiRefNet_lite/tree/aa62cd87eafb9cc43056d08ef3615a14628b831d)标注 MIT；GitHub 发布标签解析为 `a0cf9925880620000aa2d1948d61bf659ddfdfaa`，该提交的 LICENSE 与当前文档提交 `ebcc0bc8ec7fe919cec829f2dea656b3078acddc` 的 LICENSE 逐字节一致。原文保留于 [release-LICENSE](release-LICENSE)。这提供作者官方模型及转换资产的可追溯候选来源，但尚未证明 HF safetensors 与 ONNX 字节的转换对应，也未据此替用户确认最终发行方案。

[来源清单](source-review.json)固定文档提交、模型卡提交、发布资产 ID/大小/日期、来源 URL 及原文摘要；完整上游材料保存在任务外部目录 `birefnet-source-20260924`。未上传模型或运行库二进制。

## 本地执行

完整大小检查通过后才加载模型；下载过程中一次连接超时、一次本地时间上限和两次短传输均保留记录。最终用明确的 HTTP Range 补齐，每段校验返回范围与字节数。见 [模型身份](model-identity.json)。

本机 macOS arm64、Python ONNX Runtime **1.23.2**，显式 CPU provider、4 个计算线程。使用既有已核验 NASA 照片，按作者固定提交的 ONNX notebook 所示方式做 1024×1024 RGB、ImageNet 归一化，取最后输出并应用 sigmoid；生成 512×512 Gray8 蒙版及带 alpha 的 PNG。没有执行上游 `trust_remote_code` 或把图像传入在线服务。

- 模型实际输入为 float32 `1×3×1024×1024`，输出为 float32 `1×1×1024×1024`，均为预期形状；全部输出有限。
- 单次载入约 1.15 秒、推理约 8.79 秒，只是本机本轮观察，不是 Windows 性能预算。
- 蒙版包含人物轮廓，头盔被分到背景；该单图观察不形成质量通过结论。PNG 的 alpha 范围为 0–255，不将查看器对透明像素的显示当成蒙版正确性判断。
- [原始结果](local-inspection.json)保留模型/样本/输出摘要和环境；[诊断脚本](inspect-local.py)是外部证据目录中运行的原样快照，要求对应模型和相邻既有图片目录，不是生产入口。

后续先用现有 **native ONNX Runtime 1.30.0** 验证这一个候选的输入输出、重复推理、活动取消及 Windows 图像工程读回，再比较内存与等待时间。不能直接把它传给现有 U2NetP 固定契约（320×320、七个已 sigmoid 输出），也不能用本次 Mac Python 结果替代 Windows。最终模型、30 张质量样本、获取/离线恢复和发行资料仍属于后续门槛。
