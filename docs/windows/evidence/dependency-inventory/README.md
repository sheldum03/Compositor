# M1 依赖清单补充（2026-09-23）

本轮核对已准备的 R9 Windows 发布目录、24 个锁定 NuGet 包和包内许可元数据，归档声明原文。**D-07 未关闭，未改变框架选型或测试包。** R8 的最终 Windows 结果仍待取回，R9 尚未在 Windows 执行。本记录不新增实机通过项。

## 上游原文补齐与伴随材料包

随后按 nuspec 的不可变源码提交补齐四份原文：Avalonia 的 `licence.md` 和 `NOTICE.md`、Avalonia.BuildServices 的 `LICENSE`、Tmds.DBus 的 `COPYING`。每份均校验 GitHub contents API 提供的 Git blob SHA-1，并与 raw 下载逐字节比较，另记 SHA-256；[来源与身份](upstream-notices/manifest.json)和原文已纳入仓库。Avalonia NOTICE 包含其复用组件的声明，不能只保存顶层 MIT 表达式。

已制作独立的 `CompositorProbeNotices-20260923.zip`，包含 Windows 运行资产对应的包内声明、补齐的上游原文、项目 MIT 和黑体 OFL，并提供逐包索引。17 个提供运行资产的包中，16 个已关联包内或 nuspec 指定提交的声明；剩余 MicroCom.Runtime 单独列为待核对。BuildServices 材料标为构建用途，不算作第 18 个运行包。见[覆盖明细](runtime-notice-coverage.json)与[归档身份](notice-bundle-identity.json)。

MicroCom 包无源码提交号、NuGet 包无 PDB，其 DLL 中观察到版本 `0.11.0` 和仓库 URL，未取得精确源码身份。因此只归档上游 `4b8a38f…` 提交中的历史许可到 `Unresolved`，`commitMatchesNuspec=false`；未将它列作已经匹配的运行声明。这个状态不否认 nuspec 中已有 MIT 声明，只区分已知许可表达式与尚未核实的二进制源码对应。

此伴随包是审查材料，尚未安装到 Windows，也未改写 R9 的已发布测试包身份。全部材料 hash、28 个资产的覆盖关系、MicroCom 未匹配状态和 R9 原始 ZIP 未改均已独立核对。外部 .NET runtime、自有 C DLL 的加载依赖、最终原生嵌入组件、Qt/HEIC/模型及正式打包仍需处理，不能以收集到声明文件代替发行验收。

## 核对结果

- `packages.lock.json` 的 24 个包均找到相同 ID/版本的本地 `.nupkg`，记录归档 SHA-256、锁文件 contentHash、nuspec、仓库元数据和声明文件。
- 按 R9 `Compositor.AvaloniaProbe.deps.json` 的 `net10.0/win-x64` 目标映射，17 个包提供 28 个发布文件；全部与 `.nupkg` 内选定 runtime/native 资产逐字节一致。其余 7 个是构建服务或非 Windows 原生资产包，不计作这 28 个 Windows 运行文件。
- 收集 20 份包内许可/第三方声明，原文保持不变。这包括非 Windows 包的声明，归档保留包身份，不将它们误算为 Windows 必须装载的库。
- 当前发布目录的 `Licenses` 中只有 `SourceHanSans-OFL.txt`；整个目录中没有随 NuGet 原生资产一起复制的 LICENSE/NOTICE 文件。包内找到的原文已单独归档，尚未接入发行打包。
- 声明元数据多为 MIT；ANGLE 包指定 `LICENSE` 文件。这里只记录包内声明，不据顶层 MIT 推断嵌入组件全部使用 MIT。Skia、HarfBuzz 的第三方声明仍需按最终产物核对。
- 构建面向 Windows 时仍复制了 Avalonia.X11、FreeDesktop、Native、Metal 等托管程序集。它们在上述 28 个文件中按真实产物保留，不仅按文件名推断是否执行，也未在本次盘点中删除。

完整机器可读结果见 [avalonia-r9-inventory.json](avalonia-r9-inventory.json)，原文 ZIP 身份见 [archive-identity.json](archive-identity.json)。本次没有独立验证 NuGet 签名或重算 NuGet 的规范化 contentHash；逐字节匹配证明的是本地发布文件与已缓存包的对应关系。

## 其他候选组件与剩余工作

| 范围 | 已有事实与证据 | 待完成 |
| --- | --- | --- |
| Avalonia 11.3.22 / Tmds.DBus.Protocol 0.21.3 | 包内有许可表达式；已按 nuspec 提交补齐上游许可及 Avalonia NOTICE | 按最后选定发行包核对适用组件并集成声明 |
| MicroCom 0.11.0 | 包内 MIT 表达式和仓库 URL 已归档；历史 LICENSE 已收集但二进制源码提交未核实 | 补精确版本来源对应，保留当前身份缺口 |
| SkiaSharp 2.88.9 / HarfBuzzSharp 8.3.1.1 / ANGLE 2.1.25547.20250602 | 已取得包内许可和第三方声明；3 个 Windows 原生 DLL 对应包内字节精确 | 核对嵌入组件及所需材料，将适用声明放入正式包 |
| 思源黑体 2.005R / 宋体 2.003R | 两份仓库字体重新计算 SHA-256，均与 [SOURCES.md](../../../../Compositor/Resources/Fonts/SOURCES.md) 一致；仓库已有两份 OFL。当前原型仅嵌入黑体 | 生产包若加入宋体，必须同步其来源和声明；用户导入字体另按既有设计处理 |
| .NET 10.0.12 / 自有 C DLL | 当前原型是 framework-dependent，两者从外部路径提供。已补官方 runtime 193 文件清单、原文条款，以及已收回 C DLL 的 PE 依赖检查，见下节 | 核对远端安装字节、工具链附带材料及最终加载模块，补干净普通用户验收 |
| Qt 6.11.2 | CMake 锁定 Widgets；[已有 SDK 记录](../qt-compositor-preparation.json)及[上游许可目录元数据](../qt-windows-server/qt-license-upstream.json)可追溯 | 最终实际部署模块、插件、嵌入依赖及对应源码/构建材料逐项核对；许可路线仍未决定 |
| ONNX Runtime 1.30.0 | [资产记录](../../../../experiments/windows/ai/assets.json)保存 runtime 来源、hash、LICENSE 和 ThirdPartyNotices 身份 | 与最终 Windows runtime 包逐项对照；不能以运行库许可代替权重许可 |
| U2NetP 转换权重 | 上述资产记录中独立权重许可未验证、再分发未批准；现有诊断包未带 `.onnx` | 明确该具体权重的获取/发行依据，或换用依据清楚的候选权重；模型选型仍开放 |
| libheif 1.23.4 / libde265 1.1.1 | [已有来源、COPYING 和构建记录](../heic-preparation.json)；两个独立共享库候选 | 根据最终产物落实既有发行待办；解码正确性不自动关闭 D-07 |

Qt、AI、HEIC 行沿用已有证据；本轮未重新下载它们的 SDK 或执行 Windows 程序，也未完成发行审查。

## 外置运行时和 C DLL

本轮单独下载 Microsoft 官方 .NET 10.0.12 win-x64 runtime ZIP，36,960,258 字节；与[官方发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)给出的 SHA-512 精确一致，也与此前 Server 下载记录的 hash 一致。它是新取得的本地官方归档，不是从当前 Windows 11 安装目录取回的文件。

- [Runtime 清单](dotnet-runtime-inventory.json)：193 个文件，逐项大小/SHA-256；LICENSE 和 ThirdPartyNotices 原文见 [dotnet-runtime-notices](dotnet-runtime-notices/LICENSE.txt)。该 Windows 二进制包附带的是 Microsoft .NET Library 条款，不能用源码仓库的 MIT 标签代替这份包内文件。本轮只记录实际材料，不作发行接受结论。
- [C DLL 清单](compositor-native-inventory.json)：已归档实机 DLL 的 SHA-256 仍为 `046ad66d…`。本地 objdump 独立读出的 7 项直接导入与 19 个导出，和原 Windows llvm-readobj 原始日志一致；直接导入为六个 `api-ms-win-crt-*` 名称及 `KERNEL32.dll`，无延迟导入表。静态导入分析不能证明所有动态加载或静态嵌入代码的来源已完整核对。
- [Runtime 证据包](runtime-archive-identity.json)：含发布元数据、文件清单、两份原文、PE 检查输出和核对脚本，不含 runtime 二进制 ZIP。它与前述声明伴随包分别归档，二者均不改已有 R9 测试包。

新增 [verify-runtime-inventory.py](../../../../scripts/windows/verify-runtime-inventory.py) 只读比对安装目录，记录缺失、改动及额外文件，不启动 `.NET`，报告已存在时拒绝覆盖。本地从已校验 ZIP 解压后 193 文件通过；修改/缺失 `dotnet.exe`、增加额外文件以及覆盖旧报告的反例均按预期拒绝。Mac 上的这些检查不计为 Windows 执行。

2026-09-23 已在 Windows 11 测试机执行该核验：`pinvoke-test/runtime` 的 193 个文件全部匹配，无额外文件。取回报告后独立核对全部路径、大小和 SHA-256，均与已验证的官方 10.0.12 win-x64 清单一致，清单自身 SHA-256 和官方 ZIP 身份也匹配。见 [Windows 原始报告](dotnet-runtime-windows-identity.json)及[独立复核](dotnet-runtime-windows-review.json)。这是现有测试安装目录的文件身份核验，未启动新的运行库测试，不代替干净机、安装更新或发行接受；此前本地清单的 `windowsExecuted=false` 保留为该次准备工作的事实。

本次核验在 R9/R8 交替性能测试结束后执行，避免磁盘扫描干扰计时。将证据 ZIP 解压至新目录 `runtime-inventory-kit` 后，在 Windows `CompositorTest` 目录可复现（输出文件必须不存在）：

```powershell
python .\runtime-inventory-kit\verify-runtime-inventory.py .\runtime-inventory-kit\inventory.json .\pinvoke-test\runtime .\runtime-identity-20260923.json
```

即使文件一致，也只证明安装字节对应官方归档，不替代干净普通用户启动、最终加载模块检查或发行验收。

## 复现与检查

使用 [盘点脚本](../../../../scripts/windows/inventory-probe-dependencies.py)：

```sh
python3 scripts/windows/inventory-probe-dependencies.py \
  <R9-windows-publish> <nuget-cache> <新的证据目录>
```

实际检查通过：24 包/28 资产正向核对；向 `Avalonia.Base.dll` 追加字节后拒绝通过；输出目录已存在时拒绝覆盖；原文归档 ZIP CRC 通过。所有反例使用临时副本，R9 原始包及发布目录未改。此脚本只适用于当前平铺的探针发布布局，不声称是通用 SBOM 或生产发行验收工具。
