# Windows 源码构建准备（W-005）

**准备完成；尚未在 Windows 构建。** 现有机器没有在 `dotnet --list-sdks` 中报告 SDK，`CompositorTest/experiments/windows` 只有 `native`。官方 SDK 地址在 Windows 的只读 HEAD 请求连接超时。以上为本次 UU 独立 session3 的观察，不代表磁盘上绝无其他 SDK；原生 S05 PID 48672 持续运行，没有被中断或重启。

## 已固定的输入

- 源码来自提交 `58f2ca79fc501356a5fb668be1a7b29b9e6a99f4` 的 Git 归档，包含 Avalonia 原型、链接的 C# native 声明、实际字体、OFL 和项目 LICENSE；不含 bin/obj、未提交的开发计划或其他工作区内容。
- `AvaloniaSource.zip`：13,846,406 字节，SHA-256 `15eada3dac9e020ded7decff6674239c9af77049459ea4679ef44443caf1cd12`。
- `AvaloniaOfflineFeed.zip`：164,766,406 字节，SHA-256 `2f547b6c0c30f5b9a1ccfc8d196b39cc74f17b1bef112f9e060c539a04decf4b`。24 个 nupkg 的 ID、版本、归档摘要及锁文件内容哈希与既有 R9 盘点全部一致；没有新增或升级依赖。
- 计划使用 `global.json` 指定的 SDK **10.0.401**。Windows x64 ZIP 的已归档官方发布元数据 SHA-512 为 `24b670ad3d923bfcf47df6c3b034152398b42f6dbc388e10d783aee1cfb5e5817d399fc0ae2a12cfa822a55e61d34830ccb15c50ef6efee437ab874bb7c79430`。SDK 本地下载未完成校验，不能启动或当作已交付。

逐源码文件和 NuGet 包身份见[清单](source-feed-manifest.json)，环境观察见[记录](environment-observation.json)。归档位于本任务外部证据目录 `windows-clean-build-20260924`，没有向公共仓库上传二进制。

## 已执行的本地预检

将两个 ZIP 解压到新目录，使用空的 `NUGET_PACKAGES`、固定 Mac SDK 10.0.401，执行 `restore --locked-mode --source <解压的 feed> -p:NuGetAudit=false -p:UseAppHost=false`，随后 `build -c Release --no-restore -p:UseAppHost=false`。两步退出 0，构建零警告、零错误，锁文件摘要未变。见[步骤结果](local-preflight.json)、[恢复日志](local-restore.log)、[构建日志](local-build.log)。

这里关闭 NuGet 在线漏洞审计是为了验证固定离线恢复，不等于完成依赖漏洞或发行审查。禁用 apphost 沿用既有 `dotnet <dll>` 启动方式，不将本次原型构建冒充安装包。

## 下一步及判定

1. 完成官方 SDK ZIP 下载并核对 SHA-512 → 校验前不运行；失败时保留下载状态，不能使用部分 ZIP。
2. S05 长测完成后，通过 UU 将源码、离线依赖和 SDK 放入新目录，并在 Windows 重算三个归档摘要 → 不覆盖现有测试副本。
3. 用解压后的明确 SDK 路径、新包缓存执行同样的锁定恢复与 Release 构建 → 保留全部日志、SDK 身份、退出码和锁文件前后摘要。
4. 使用新构建 DLL 和已验证 C DLL 跑合成/工程往返、文字与笔刷检查 → 取回原始输出，核对像素和实际 Mac 读回；不能沿用交叉编译 DLL 的通过记录。

此项只补 Avalonia 从源码构建证据。Qt、干净普通用户安装、发布签名、完整 M1 和 Windows 1.0 均不因此自动完成。
