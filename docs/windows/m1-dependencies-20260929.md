# M1 生产依赖与发行阻断清单（2026-09-29）

唯一选定路线见 [M1 选型决定](m1-route-decision-20260929.md)。下表固定**目标版本**，不把原型包当成可发行的生产包。原型 NuGet 完整图及内容哈希以 [`packages.lock.json`](../../experiments/windows/avalonia/packages.lock.json) 为准；正式工程接入 Avalonia 时必须生成并提交自身锁文件，不能借用原型锁文件声称生产依赖已锁定。

| 依赖 | 固定身份 / 当前事实 | 生产处置 |
| --- | --- | --- |
| .NET | SDK 10.0.401、目标 `net10.0`；原型实测 runtime 10.0.12 | 构建使用 [`windows/global.json`](../../windows/global.json)；发布时固定实际自包含 runtime 与摘要，干净 Windows 验证 |
| Avalonia | Desktop、Skia、SimpleTheme 11.3.22；原型 Headless 亦为 11.3.22 | 生产 GUI 只引入实际所需包，锁定直接和传递版本，再核对发布资产 |
| SkiaSharp / HarfBuzzSharp | 原型锁文件分别为 2.88.9 / 8.3.1.1 | 固定 Windows x64 native DLL 的包成员及摘要，预览/导出同规则复测 |
| MicroCom.Runtime | 原型传递依赖 0.11.0；官方 NuGet 包与锁文件、签名已核对，精确二进制源码提交未核实 | 保留 [依赖盘点](evidence/dependency-inventory/README.md)中的身份缺口；发行前补可审计的来源/声明对应或选替代路径 |
| 共享 C 算法 | 现有 DLL 的 C ABI / C# P/Invoke 已在 Windows x64 实测 | 生产版本固定源码提交、工具链、导出表和 DLL 摘要；所有权/释放压力测与 Mac 兼容回归 |
| 内置思源黑体 | 字体及 OFL 文本已随原型归档 | 正式包保留字体身份、许可和缺字策略，防止打开项目时静默重排 |
| HEIC | libheif 1.23.4 + libde265 1.1.1 为可行性候选，动态库原型在 Windows Server 测过；源码标记 LGPL-3.0-or-later | M6 前决定可分发 codec、对应源码/说明、专利风险与实际平台依赖；未决时不发行 HEIC 功能 |
| AI | ONNX Runtime 1.30.0 CPU、BiRefNet tiny 固定候选模型有 Windows 运行证据 | M6 前确认模型权重授权、质量/资源门槛、离线获取及整包声明；未决时不发行 AI 功能 |

最终发行清单从**实际生产包**逐文件生成，包含 .NET runtime、原生 DLL、模型/字体和相应声明。[原型依赖盘点](evidence/dependency-inventory/README.md)只证明其原型身份。MicroCom、HEIC、模型与最终安装包分发决策仍是明确的发布阻断，不能因 M1 选定框架而自动关闭。
