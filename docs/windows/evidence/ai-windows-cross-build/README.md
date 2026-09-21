# Windows AI 原生探针构建与部署检查

基线605a610d4dea0ef6021ca044766f39e6686a3dda，父任务独立目录 `/tmp/compositor-parent-ai-windows`；未修改实施任务工作区。

## 已执行

- 重新核验官方ONNX Runtime1.30.0 win-x64 SDK压缩包：82,645,522 bytes，SHA256 c6ba983baf5681af108599675d2a89c2d145512d02de28aed0bff177cd0ba949，CRC通过。从原始ZIP重新解压使用头文件及导入库。
- CMake3.31.6、LLVM-MinGW20260908/clang23.1.1，首次编译成功但链接失败（WinMain未定义）。仅在MINGW分支增加 `target_link_options(ai_probe PRIVATE -municode)` 后原构建目录链接退出0；保留既有 -Wall/-Wextra/-Werror，未改probe.cpp、ONNX模型或推理配置。
- 5个暂存PE均为AMD64。4条随包导入边已核验：88个命名符号和1个按序号导入。关键的ORT入口是ordinal1，已确认官方DLL将它导出为OrtGetApiBase；没有把空命名导入列表当作完成检查。正式导出/导入文本与汇总在evidence/pe。
- 重新执行原预处理和ONNX图检查，得到1,228,800-byte的NCHW float32 tensor，SHA256 d085f68b79a2a856160a0ea207180658a3d58da7d77feaca7b1fffcca4136951，与既有Mac输入逐字节一致。图仍为IR6/opset11、1,055节点，无外部数据或自定义域；ONNX checker1.17.0、NumPy2.0.2。
- Windows runner原文通过PowerShell7.6语法检查。它记录原生退出码、CPU profile、f32有效范围、Unicode路径、四类错误、已有输出保护；失败先归档诊断再exit1。包中和结果ZIP均不含.onnx模型；默认从固定上游URL下载到新run/inputs目录并校验，亦可指定本地模型路径。
- 单独Mac适配副本只替换平台守卫、明确标为不适用的SystemDirectory检查位置和Mac原生可执行文件，重新生成清单。离线模型运行完成8次native调用（2成功、6预期拒绝），Windows标记false；原始预测hash和1,344个CPU kernel事件与既有独立Python报告相同；Unicode重复输出精确。
- 另一副本将输入第一个float替换为NaN并更新清单，确保进入原生输入校验。native返回1、wrapper返回1，日志、错误摘要及ZIP完整保留。
- Mac适配副本默认网络分支也实际下载4,574,861-byte模型，hash符合锁定值，然后完成全部8次调用并归档；结果ZIP确认不含模型。此证据不覆盖Windows5.1的网络/TLS/路径实现。

## 部署依赖与限制

实际ORT DLL的导入还包含MSVCP140.dll、MSVCP140_1.dll、VCRUNTIME140.dll、VCRUNTIME140_1.dll；这些未打包。另有Windows系统DLL、API sets和UCRT，SHELL32为延迟导入。测试脚本记录System32中四个运行库的存在、hash和版本，但不会仅据该目录未发现文件就断言加载失败；最终须看Windows真实进程结果。

[ONNX Runtime官方安装文档](https://onnxruntime.ai/docs/install/)要求Windows具备Visual C++2019运行库并建议最新版本。[微软部署文档](https://learn.microsoft.com/en-us/cpp/windows/deployment-in-visual-cpp?view=msvc-170)介绍中央安装/应用目录部署，推荐中央部署以便维护更新。本次未安装系统运行库、未复制未经核验的MSVC DLL；最终运行库部署和发行责任仍待处理。

Windows EXE执行、Windows5.1脚本/下载分支、实际DLL装载、同机内存/性能尚未验证。输入来自Mac固定预处理，这个runner不执行Windows图像预处理/后处理，也不生成512px最终蒙版或.comp；Windows返回的raw仍需独立复核后再接入产品。跨平台预测不预设逐字节相同。未验证运行中取消、质量、多模型、GPU或最终模型分发许可，D-08/M1/W-009/W-032均未据此通过。

## 交付

`CompositorAiProbe-cross-build.zip`：7,717,659 bytes，SHA256 61d074d63aa28eba01cedc97730140a32f70d64fa3c80617624b4b5e0037c738；27个清单文件，CRC通过，0个.onnx文件。

`evidence/mingw-unicode.patch` 对当前实施工作区apply-check退出0。原始构建日志包含红/绿两次链接结果；wrapper适配副本、正例/负例/下载分支ZIP保留。Windows原脚本在CompositorAiProbe目录内，从未被改成Mac版本。

本轮没有操作Chrome、上传或执行服务器命令；用户正在使用浏览器的约束保持有效。后续需先读取目标环境运行库状态，在原授权的隔离验证目录核验包身份后运行，并下载原始输出审查；不能仅凭构建/脚本回显勾选Windows验收。

包内README的源码信用路径说明有一处多余的 `source/` 前缀：`probe-source.tar` 内实际路径是 `experiments/windows/ai/assets.json`。代码、构建命令和运行脚本不受影响；已冻结ZIP保留原身份。
