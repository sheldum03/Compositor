# BiRefNet Windows x64 运行包准备（2026-09-24）

后续进展：本包已完成 Windows 实际执行、独立像素/取消复核和 Mac 工程读回，见 [实机报告](../birefnet-windows-execution/README.md)。下文保留当时准备阶段的边界。

**交叉构建、隔离环境解包、静态依赖和包完整性检查通过；未上传、未在 Windows 执行，M1 未关闭。** 接续[本地原生检查](../birefnet-native-screening/README.md)，准备将同一个官方候选送入实际 Windows 图像流程。

## 构建与依赖

固定 `3286cd8` 的探针与完整图像检查入口，LLVM-MinGW 20260908 / Clang 23.1.1、CMake 3.31.10、官方 ONNX Runtime 1.30.0 Windows x64 SDK。两个目标 Release 构建退出 0，保留严格警告与宽字符入口。探针源码未再修改。[构建身份](build-identity.json)记录源码和程序入口摘要。

GitHub API 范围下载途中返回 403 速率限制，未解压部分文件。随后发现并重新核验 Qt 实验留下的完整工具链缓存，摘要匹配已固定的官方值；Windows SDK 剩余 7,148,050 字节通过官方发布 URL 补齐，82,645,522 字节完整 ZIP 的 CRC、SHA-256 均通过。没有关闭 TLS 校验或使用镜像。

两个 EXE、四个 DLL 均为 AMD64；241 个包内导入符号逐项解析，ORT 序号 1 对应 `OrtGetApiBase`。四个 DLL 与此前已完成 Windows AI 检查的运行库逐字节相同。系统 MSVC 运行库和 Windows API/UCRT 依赖仍由目标机提供；静态检查不能证明本次程序实际加载成功。见[依赖摘要](pe-summary.json)，完整导入/导出原文随包保存。

## 执行入口与控制

新增 [run-birefnet-kit.py](../../../../experiments/windows/ai/run-birefnet-kit.py) 沿用已有 Windows 图像包的隔离解包/日志/归档流程，只替换固定模型入口，模型路径显式传入。携带已经核验的 Python 3.11.9 x64 嵌入包、NumPy 2.0.2 和 Pillow 11.3.0 原始 wheel；不需要 Python ONNX Runtime，不包含模型，不修改系统安装或 PATH。

- 原始 Windows runner 在 Mac 上只能执行 `--prepare-only`；完整执行会因平台守卫拒绝。解出的 59 个二进制均为 AMD64，显式 `._pth` 和两个包目录检查通过。
- 已有输出目录拒绝；篡改照片后在创建输出前被清单校验拒绝。
- 独立 Mac 适配副本实际完成完整图像流程、原生取消/恢复和结果 ZIP，预测与上轮原生检查逐字节相同。适配仅改变平台守卫、执行解释器、报告平台检查及 Mac 二进制，不执行 Windows Python/EXE。
- 适配首轮因 `copyfile` 未保留 Mac 二进制执行权限而在调用前失败；归档正常保留。仅恢复副本执行位后复测成功，未更改 Windows runner 或模型。失败/成功日志一并保留。
- 错误模型摘要在原生调用前被拒绝，返回 1 并保留错误结果 ZIP。结果归档不含权重或隔离解释器；工程原图/蒙版、原始预测、profile 与日志均保留。

[准备检查](preparation-checks.json)含以上结果与隔离环境二进制摘要。实际 Windows 超时分支、运行库加载和新模型的图像流程均待实机验证，不因 Mac 适配通过而放行。

## 交付与接续

[BiRefNetWindowsKit.zip](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/birefnet-windows-preparation-20260924/BiRefNetWindowsKit.zip)：41,447,113 字节，SHA-256 `dc909088d0b68b59c0d8f6ab0d9121472c147c8082d843f3df4bb641920f4ce8`。40 个文件，CRC 与 39 项清单全部独立核验。见[包身份](package-identity.json)和[清单](package-manifest.json)。许可原文、构建日志、源码及具体操作说明随包保存。

UU 因 Mac 锁屏不可操作。恢复后先检查原 S05 会话及其进程，确认此前启动指令的状态；随后把该包和已核验的官方模型私有传入新的 Windows 目录，校验摘要后执行，取回原始 ZIP 并独立复算蒙版及实际工程读回。两个新的 S05 前瞻运行、原生 IME 后续、M1 决策及生产 M2–M7 仍须完成，不能以本次准备替代。
