# S05 R5：补充 GC 归因证据（2026-09-22）

**诊断准备已验证，R5 尚未在 Windows 执行。资源稳定性、Qt 原生 IME、M1 和 Windows 1.0 均未放行。** 当前会话没有上一轮的桌面控制工具，无法继续操作 UU；没有把工具缺失写成 Windows 测试失败或再次锁屏。

R4 的第六轮自然内存回落、末轮峰值继续上升，既不能认定每轮不可回收泄漏，也不足以证明稳定平台。R5 在原有采样点增加累计近似分配字节、Gen0/1/2 回收次数、最近 GC 编号及代数、堆大小、提升字节数和回收后 LOH 大小，并记录运行时及 server GC 模式。没有改变笔刷算法、工作量、文档释放、等待时间或强制 GC 策略。

这些字段用于区分分配/回收与堆容量保留，不直接定位 native 分配所有者；累计分配也包含报告生成等测试代码。GC 快照不是与当前进程内存同时采样的数据，不能把两者相减当作精确 native 内存。依据见 [.NET GCMemoryInfo](https://learn.microsoft.com/en-us/dotnet/api/system.gcmemoryinfo?view=net-10.0)。

## 本地验证

- 固定 SDK 10.0.401 的官方 SHA-512 校验通过；依赖按原 lock 文件恢复。Release 编译零警告、零错误。
- 九轮 900 编辑、900 撤销/重做、18,900 回调及九组 4000×4000 图像逐像素通过，新增字段完整、累计计数非递减，最终诊断 GC 被计数记录：[九轮复核](r5-local-soak-review.json)。
- 默认三轮 300 编辑及三组图像通过：[默认入口回归](r5-local-default-review.json)。这些都是本地 headless 正确性证据，不是 Windows 内存结论。
- R4 按原模式复核仍通过；要求 `--gc-diagnostics` 时拒绝旧版缺字段报告；把新报告中 Gen2 计数改成倒退后也被拒绝。
- Windows x64 framework-dependent 发布通过。交付沿用已有 `dotnet.exe` 启动 DLL；无需替换运行时或原生库。包 CRC、基础 DLL 身份及补丁重建逐字节一致均已验证，安装脚本拒绝覆盖已有目标。
- [包身份](s05-gc-r5-package.json) / [本地完整原始证据身份](r5-local-evidence-identity.json)。未把临时目录中的原始输出作为唯一归档，完整报告和图像另存 ZIP。

## Windows 实机执行

将 [CompositorS05GcR5Delta.zip](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/windows11-remote-suite/CompositorS05GcR5Delta.zip) 复制到 Windows 桌面的 `CompositorTest` 文件夹。然后在 PowerShell 执行以下准备命令；它会校验包并新建 R5 目录，保留已有各版。

```powershell
$root = Join-Path ([Environment]::GetFolderPath('Desktop')) 'CompositorTest'
$zip = Join-Path $root 'CompositorS05GcR5Delta.zip'
if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne '5e21749fc1adc8dc3ce009a6eb6a66dd7195a13c5ca6283f07a96a825f0bdfa1') { throw 'R5 package hash mismatch' }
$install = Join-Path $root 's05-gc-r5-install'
Expand-Archive $zip $install
python (Join-Path $install 'apply.py') $root $zip
if ($LASTEXITCODE -ne 0) { throw 'R5 installation failed' }
```

看到 `PATCH VERIFIED` 后运行九轮诊断。保持窗口可见，期间避免其他测试或文件传输。此命令沿用已验证的原生库与 Windows 运行时，预计数分钟。

```powershell
$out = Join-Path $root ('s05-gc-r5-' + (Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory $out | Out-Null
& (Join-Path $root 'pinvoke-test/runtime/dotnet.exe') `
  (Join-Path $root 's02-lifecycle-r5-app/Compositor.AvaloniaProbe.dll') `
  --s05-soak-window (Join-Path $root 'remote-suite/fixtures/brush') `
  (Join-Path $out 'run') (Join-Path $root 'native-run-20260921-111947/compositor_native.dll') `
  1> (Join-Path $out 'stdout.log') 2> (Join-Path $out 'stderr.log')
$probeExit = $LASTEXITCODE
$probeExit | Set-Content (Join-Path $out 'exit-code.txt')
python (Join-Path $install 'review-s05.py') (Join-Path $out 'run/report.json') --soak --gc-diagnostics `
  1> (Join-Path $out 'review.json') 2> (Join-Path $out 'review-error.log')
$reviewExit = $LASTEXITCODE
Compress-Archive -Path (Join-Path $out '*') -DestinationPath ($out + '.zip')
[pscustomobject]@{ProbeExit=$probeExit; ReviewExit=$reviewExit; Evidence=($out+'.zip')}
Get-FileHash ($out + '.zip') -Algorithm SHA256
```

即使失败也保留 ZIP 和日志；不能只看压缩成功就记为通过。实机结果取回后还需独立比较九组 PNG，关联 GC 计数、堆容量和专用内存曲线，再确定是否需要分配跟踪。`resourceAccepted` 继续为 false。
