# Windows 源码构建与回归（W-005）

**已完成真实 Windows 离线源码构建、三项无界面回归和 Mac 工程读回；完整 W-005/M1 未因此关闭。** 初次准备时，现有机器没有在 `dotnet --list-sdks` 中报告 SDK，`CompositorTest/experiments/windows` 只有 `native`。官方 SDK 地址在 Windows 的只读 HEAD 请求连接超时。以上为本次 UU 独立 session3 的观察，不代表磁盘上绝无其他 SDK；当时原生 S05 PID 48672 持续运行，没有被中断或重启；本轮构建在该长测结束后执行。

## 已固定的输入

- 源码来自提交 `58f2ca79fc501356a5fb668be1a7b29b9e6a99f4` 的 Git 归档，包含 Avalonia 原型、链接的 C# native 声明、实际字体、OFL 和项目 LICENSE；不含 bin/obj、未提交的开发计划或其他工作区内容。
- `AvaloniaSource.zip`：13,846,406 字节，SHA-256 `15eada3dac9e020ded7decff6674239c9af77049459ea4679ef44443caf1cd12`。
- `AvaloniaOfflineFeed.zip`：164,766,406 字节，SHA-256 `2f547b6c0c30f5b9a1ccfc8d196b39cc74f17b1bef112f9e060c539a04decf4b`。24 个 nupkg 的 ID、版本、归档摘要及锁文件内容哈希与既有 R9 盘点全部一致；没有新增或升级依赖。
- 使用 `global.json` 指定的 SDK **10.0.401**。Windows x64 ZIP 的已归档官方发布元数据 SHA-512 为 `24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430`。SDK 本地下载已完成：300,608,304 字节，完整 SHA-512 与上述官方值一致，5,577 个 ZIP 条目 CRC 校验通过；随后已通过 UU 传入并在 Windows 核验、解压和执行。见 [SDK 身份](sdk-identity.json)。

逐源码文件和 NuGet 包身份见[清单](source-feed-manifest.json)，环境观察见[记录](environment-observation.json)。归档位于本任务外部证据目录 `windows-clean-build-20260924`，没有向公共仓库上传二进制。

## 已执行的本地预检

将两个 ZIP 解压到新目录，使用空的 `NUGET_PACKAGES`、固定 Mac SDK 10.0.401，执行 `restore --locked-mode --source <解压的 feed> -p:NuGetAudit=false -p:UseAppHost=false`，随后 `build -c Release --no-restore -p:UseAppHost=false`。两步退出 0，构建零警告、零错误，锁文件摘要未变。见[步骤结果](local-preflight.json)、[恢复日志](local-restore.log)、[构建日志](local-build.log)。

这里关闭 NuGet 在线漏洞审计是为了验证固定离线恢复，不等于完成依赖漏洞或发行审查。禁用 apphost 沿用既有 `dotnet <dll>` 启动方式，不将本次原型构建冒充安装包。

## Windows 实测与独立复核

2026-09-24 在 `C:\Users\Administrator\Desktop\CompositorTest\clean-source-build-20260924-103752` 执行 [runner](run-windows.py)。Windows 重算三个输入归档和既有 native DLL 摘要通过；使用解压后的 SDK 10.0.401 / runtime 10.0.12，以及该新目录下的空依赖缓存。

- SDK 身份、离线锁定恢复、Release 构建及合成/笔刷/文字六步均退出 0；构建零警告、零错误，锁文件摘要前后不变。
- 合成 20 例、文字 12 例、笔刷 13 项会话检查通过。独立解码检查 67 对预览/导出/重开/取消图像，全部 RGBA 精确一致；与此前 Windows `remote-batch-20260921-183510` 的 67 张文字/笔刷同名输出也精确一致。
- Mac 实际应用读回两项测试通过，零失败、零跳过：20 个合成工程保留原清单/资源和 Mac 渲染像素；笔刷工程读入、再保存、重开像素一致。见 [读回结果](windows-run/mac-readback-result.log)。
- 结果 ZIP 8,333,896 字节，SHA-256 `211a5520445b27e2797f812bd283552c455f2496b8cb284a27b71f16ea2f0fb4`，与独立 Windows `Get-FileHash` 一致；CRC 和 230 个成员摘要全部核对通过。
- SDK 初次运行在 stderr 输出欢迎信息及开发 HTTPS 证书创建提示，没有执行信任命令；这是 SDK 的初次运行行为，不是恢复失败。其他五步 stderr 为空。完整原始日志保留在结果 ZIP。

[复核汇总](windows-run/review.json)、[Windows 身份与命令](windows-run/identity.json)、[逐文件摘要](windows-run/files.json)、[独立像素复核](windows-run/independent-pixels.json)已归档。二进制/图像 ZIP 和完整 xcodebuild 结果仍位于任务外部证据目录 `windows-clean-build-20260924`。

此项补齐固定 Avalonia 原型从源码构建证据，沿用已验证的 C DLL，未重新编译 native 核心。两笔 40% 透明度无界面计时不是 S02，合成/文字与 Mac 参考差异仍为观察。Qt、普通用户安装、原生 IME、发布签名、完整 M1 和 Windows 1.0 均不因此完成。下一步继续处理 S05 资源候选规则失败、原生输入与设备覆盖缺口。
