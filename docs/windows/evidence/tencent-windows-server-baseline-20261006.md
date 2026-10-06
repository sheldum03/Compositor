# 腾讯云 Windows 实机基线证据（W-004/W-005）

日期：2026-10-06

## 连接与主机

- 控制台实例：`Windows Server-aqVN`
- IPv4：`119.45.93.179`
- 地域：`ap-nanjing`
- 连接方式：腾讯云 OrcaTerm 内嵌 RDP，会话已进入管理员 Windows PowerShell
- 当前目录：`C:\Sites\markdown-reader`
- 现有站点任务：`schtasks /Run /TN MarkdownReaderSite` 返回“成功：尝试运行”

这证明测试用 Windows 实例可登录、可执行管理员 PowerShell，并且能运行既有站点任务；它还没有证明 Compositor Windows 包已启动。

## 工具链探测

在远程 PowerShell 逐项运行 `Get-Command`：

| 工具 | 结果 |
| --- | --- |
| `git` | `git version 2.53.0.rc0.windows.1` |
| `dotnet` | 未找到命令 |
| `cmake` | 未找到命令 |
| `msbuild` | 未找到命令 |
| `cl` | 未找到命令 |
| `winget` | 未找到命令 |

结论是：Windows x64 实机已经可用，但当前 PATH 中没有 .NET SDK、CMake、MSVC 或 winget。不能把 CI runner 的通过结果直接写成该主机的构建通过，也不能在此基线下执行 `Compositor.Smoke`、原生 DLL 构建或正式窗口验收。工具可能安装在非 PATH 位置，需先做固定路径探测；若确实缺失，按官方来源引导安装固定版本并记录安装前后探测结果。

## 对开发计划的影响

- W-004：**部分通过**。实机与远程管理员会话已确认；硬件、OS/架构、驱动和性能模板仍待写入同一份验收记录。
- W-005：**未通过**。生产构建所需 SDK/包和 C 编译环境还未在该机建立。
- W-006/W-010：**未开始实机执行**。C ABI/PInvoke 与便携包仍只有 Windows CI runner 证据。
- W-022 及 M3b：保持未完成。必须先完成工具链引导，再运行 Release 检查、原生 DLL 冒烟和窗口级验收。

下一步是：固定并安装 .NET SDK 10.0.401、CMake 4.2.3 及可用的 MSVC Build Tools（或确认等价的已安装路径），重新记录 `dotnet --info`、CMake generator、MSVC 编译器和系统版本，再把源码/便携包放入独立测试目录执行。安装来源必须是官方或已验证的包源，安装不改变 CI 证据的判定。
