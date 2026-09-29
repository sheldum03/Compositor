# Windows 11 生产核心 r2 实机执行（2026-09-29）

测试对象是 `CompositorProductionCore-r2.zip`，SHA-256 `785a95758df7025088eec2bc81bcf8be6c7abe0e7de5fde6511fd85224531007`。此前已在远端 `C:\Users\Administrator\Desktop\CompositorTest` 核对同一哈希并解压为 `production-core-r2`。此包在瓦片快照和原生 C 像素桥接加入前封包，不能代表当前工作树的全部 M2 代码。

第一次执行包内 `windows/run-smoke.cmd` 时，脚本显示 `PASS`，但这是**误报，不能计入验收**。远端全局 `dotnet` 只有 .NET 6.0.33 运行时，没有任何 SDK；三个原始文件 `production-core-smoke-31449.sdk.txt`、`.build.log` 和 `.run.log` 保留在 [原始日志 ZIP](initial-false-pass-raw-logs.zip) 中，均显示缺少固定的 10.0.401 SDK。原脚本只依赖 `errorlevel`，在这次 CLI 行为下没有识别失败。工作树中的 `windows/run-smoke.cmd` 已增加 SDK 精确版本、冒烟 PASS 行和关键输出文件检查，并在失败时返回非零；这一修改不在已传送的 r2 ZIP 内。

随后直接调用远端既有便携 SDK `clean-source-build-20260924-103752\sdk\dotnet.exe`，`--version` 显示 `10.0.401`。该 SDK 身份此前记录于 [SDK 身份证据](../clean-source-build-20260924/sdk-identity.json)。在 Windows PowerShell 中对 r2 的 `Compositor.Smoke.csproj` 执行 Release 构建，`Compositor.Core` 与 `Compositor.Smoke` 均显示成功。用该 SDK 执行构建后的 `Compositor.Smoke.dll`，以包内 `docs/windows/fixtures` 为输入、全新 `production-core-smoke-evidence-20260929` 目录为输出，终端实际显示：

```text
PASS: edit, undo, redo, safe save, rejected-save protection, reopen, export, backup recovery, unsupported-project protection
```

冒烟程序在输出 PASS 前，实际重开保存的工程，检查 `Edited.comp/manifest.json` 不因拒绝保存而改变，读取导出 `export.png` 并与原图逐字节 SHA-256 对比，确认备份恢复及不支持工程不能写入。故 r2 的这条受限工程读写链路在 Windows 11 实机上通过。该结果不覆盖最新版瓦片/原生桥接，也不等于 M2 整体通过。终端成功画面已由 UU 远程观察。[取回的 PowerShell 转储](success-commands-transcript.zip)记录了主机、SDK、构建和运行命令，但没有捕获原生程序的标准输出；完整输出目录尚未取回。因此本记录对成功项的独立文件复核仍低于失败日志的原始文件复核。
