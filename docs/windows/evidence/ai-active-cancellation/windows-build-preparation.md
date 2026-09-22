# Windows 活动推理取消测试包（2026-09-23）

**交叉构建和包完整性检查通过；Windows 活动取消尚未执行。M1、W-032/033 和 Windows 1.0 未验收。** 本轮没有修改探针行为，使用 `3dc81bc` 的源码生成独立测试包，保留以前的包与结果。

## 已完成的验证

- LLVM-MinGW `20260908` 和 ONNX Runtime Windows x64 `1.30.0` 下载大小、SHA-256 均匹配固定记录。
- CMake `3.31.6` / Clang `23.1.1` Release 构建成功，保留 `-Wall -Wextra -Werror` 和宽字符入口。
- 五个 PE 文件均为 AMD64；对包内 DLL 的 123 个导入符号逐项找到导出，ORT 的导入序号 1 对应 `OrtGetApiBase`。静态符号匹配不证明目标机加载或运行成功。
- LLVM 与 ORT DLL、许可通知和固定输入均核对身份。仍依赖目标机的四个 MSVC 运行库 DLL，详见包内说明；未安装运行库或修改系统。
- ZIP CRC 和全部 32 个受清单管理的文件校验通过，连同清单共 33 个文件；不包含模型权重。

完整命令、编译日志、PE 头及导入/导出记录保存在包内 `build`。源码、复核脚本和许可通知随包保留；[身份记录](windows-package-identity.json) 固定包和源码的 SHA-256。

## 实测入口

[CompositorAiActiveCancel.zip](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/ai-active-cancellation-windows/CompositorAiActiveCancel.zip)，7,331,400 字节，SHA-256：

```text
c51297c2a6a614554ea10bab686e8bb7f8603d21024255bf40ab61a5c75e9203
```

解压到新的目录，按包内 [README](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/ai-active-cancellation-windows/CompositorAiActiveCancel/README.md) 设置已有模型路径，校验身份后执行五轮。说明中的 PowerShell 命令尚未在 Windows 执行，不能把脚本准备记为实测。

每轮必须同时满足原生进程退出码为 0、复核退出码为 0、取消阶段有 CPU 算子事件、线程退出、同会话恢复预测与本轮基准逐字节一致。失败即停止，不自动重试；退出码、日志、原始预测和 profile 一起归档。归档成功本身不代表通过。取回结果后仍需独立复核。

## 当前阻碍及接续

本轮没有桌面控制工具，不能操作 UU。Kimi 浏览器桥接已连接，但导航返回 `No current window`，尚无可操作的 Chrome 窗口；已提交打开窗口的问题。未取得新的 Windows 测试结果，也未公开推送或触发 CI。

恢复连接后，先核对目标机及已有模型，再执行上述五轮并归档。Windows Server 结果仅证明该 Server 上的原生路径，不代替 Windows 11 的 GUI、IME 或设备矩阵。S05 R5 的 Windows 九轮 GC 诊断、Qt 原生 IME 和设备矩阵继续待执行，入口见 [S05 R5](../lifecycle-s05/r5-gc-preparation.md)。
