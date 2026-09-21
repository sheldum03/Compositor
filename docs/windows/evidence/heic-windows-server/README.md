# HEIC Windows Server 实测与独立复核

2026-09-21，授权测试主机 tencent-cpu-01（Windows Server 2022，10.0.20348 x64），通过 Chrome Computer Use 操作腾讯云文件管理及 TAT。原始测试 ZIP 上传后核对 4,466,298 bytes / SHA256 `3350ac1b5f29157e4d4d11a2b8aef464328b4cb7982c49873f5c1000088d9d45` 才解压；全部工作限于 `C:\CompositorValidation` 新目录，未安装系统依赖或更改系统设置。

## 实测失败与最小修复

首次运行 15:38:14–15:38:18，TAT/wrapper exit 1，错误 `Expected 16 frozen cases`。结果确实为 0 个 decode / 0 个 native invocation；不算解码失败或成功。原始 420 bytes ZIP SHA256 `e6d2840c82f477f2715de43462d65c2e3af13430c249a8f051df207004a09487`，CRC 通过，保留于 `heic-server-results-605a610-20260921.zip`。

在同一服务器只读诊断确认 PowerShell 5.1.20348.3932 的 `@(Get-Content ... | ConvertFrom-Json)` 返回长度 1、首元素 System.Object[]；直接赋值返回长度 16、首元素 PSCustomObject。仅移除样本读取这一行的 `@(...)`。未修改 probe.cpp、EXE、DLL、输入样本或参考像素。

在全新 `heic-605a610-ps51fix-20260921` 目录重新解压原始包；先核对 runner 原 SHA，再替换唯一精确行，并核对固定后 7,211 bytes / SHA256 `23f70a3bc929ede4bd57d22ad7b9a0d5e0d64781b0353545689cb0b3a5373209`。清单仅更新该 runner 身份。完整 payload 仍在执行前后各核对一次。

## 成功重跑与独立检查

15:41:58–15:42:06，TAT/wrapper exit 0。结果 `heic-server-results-ps51fix-20260921.zip` 为 19,585 bytes，SHA256 `c878380e7a4a2dd51b3066e3ff62eb5552daae47053778c274a2241a09d9c2f4`，下载后身份核对及 60 文件 CRC 通过。此时真正调用了 Windows x64 原生程序及随包 DLL。

- 16 冻结样本：8 种方向，各含不透明与透明版本，全部维度、方向、alpha、buffer 长度与预期一致。
- 共 23 次 native invocation：17 次成功（包括中文/空格/非BMP路径），6 次预期拒绝（截断、错格式、不存在、像素预算、既有 ASCII 输出、既有 Unicode 输出）。既有输出哈希保持，4种坏输入未发布结果。
- 独立 Python/Pillow/NumPy 从原始 rgba.bin 重新计算 16 组指标，全部精确吻合 wrapper 报告；不是仅信任 JSON 的 completed 标签。
- 16 组 raw SHA 与既有 macOS libheif 1.23.4/libde265 1.1.1 输出全部精确相等。与冻结的 Mac ImageIO PNG 相比，alpha 完全相等，预乘 RGB 最大误差均为 1/255。此数值差异尚未获产品容差验收。
- 已实际查看全部 16 对联系表，方向、四角色块及透明版本对应一致。全部 sourceIccBytes=0 / warnings=0。

可复算脚本 `evidence/review-server.py`，结构化结果 `evidence/server-independent-review.json`，可视结果 `evidence/server-contact-sheet.png`。`server-fixed-results` 内 PNG 是本地从服务器 raw 转换的检查图；原始下载 ZIP 保持不变。

## 后续交付与边界

为避免后续复发，另生成 `CompositorHeicProbe-ps51fixed.zip`：4,466,303 bytes，SHA256 `15ca24a9bc120da0ed774255723e54c928143d94fd43dd8ff18b0cc2cc60d528`，75个清单文件；相对原包唯一 payload 修改是 run-heic.ps1，另外更新 files.json。这个新 ZIP 本身尚未上传，但其 runner 与其余 payload 与服务器成功执行版本一致。原始包保留，不覆盖失败证据。

结果只证明该 Windows Server 上这些固定样本的 DLL 加载、解码/路径契约和脚本执行；不等于 Windows 11 干净机、真实照片、ICC/HDR、UI 导入、产品安装包或完整 W-031 验收。M1 框架选型、D-07 发行许可/专利、RGB 容差仍未通过。本轮没有推进任何未满足的产品里程碑。

## 实施任务独立整合复核

本目录是父任务 Server 实测的归档。本任务另从未改动 ZIP 直接读取 raw，以仓库固定 PNG 重算全部16组指标，全部吻合；16组 raw 与既有 Mac libheif 文件精确相等，已查看全部联系图。三个 ZIP 的字节数/SHA/CRC及修正包75个清单文件均重新核对。对比原包确认仅 runner 单行与 files.json 身份变化，见 integration-check.json。

original-results 保留成功 ZIP 内原始60文件；first-failure.zip 保留首次0调用失败。ps51-fix 保留实际修正脚本及补丁。server-fixed-run.json/server-independent-review.json 为父任务执行与复核记录；review-server.py 是其原脚本，依赖原 artifact 的目录结构。完整上游 artifact 位于 /Users/admin/.codex/visualizations/2026/09/20/01a0beb1-da38-7b00-9183-d65bff121d78/heic-windows-cross-build。本文上方 evidence/ 与 server-fixed-results 路径均相对此原 artifact。
