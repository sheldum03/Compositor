# M1 依赖清单补充（2026-09-23）

本轮核对已准备的 R9 Windows 发布目录、24 个锁定 NuGet 包和包内许可元数据，归档声明原文。**D-07 未关闭，未改变框架选型或测试包。** R8 的最终 Windows 结果仍待取回，R9 尚未在 Windows 执行。本记录不新增实机通过项。

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
| Avalonia 11.3.22 / MicroCom 0.11.0 / Tmds.DBus.Protocol 0.21.3 | 包内有许可表达式，未找到附带 LICENSE/NOTICE 原文；nuspec 和仓库元数据已归档 | 从对应版本来源补全声明；按最后选定发行包收敛清单 |
| SkiaSharp 2.88.9 / HarfBuzzSharp 8.3.1.1 / ANGLE 2.1.25547.20250602 | 已取得包内许可和第三方声明；3 个 Windows 原生 DLL 对应包内字节精确 | 核对嵌入组件及所需材料，将适用声明放入正式包 |
| 思源黑体 2.005R / 宋体 2.003R | 两份仓库字体重新计算 SHA-256，均与 [SOURCES.md](../../../../Compositor/Resources/Fonts/SOURCES.md) 一致；仓库已有两份 OFL。当前原型仅嵌入黑体 | 生产包若加入宋体，必须同步其来源和声明；用户导入字体另按既有设计处理 |
| .NET 10.0.12 / 自有 C DLL | 当前原型是 framework-dependent，运行时和 `compositor_native.dll` 从外部路径提供，未包含在这 28 个 NuGet 文件中 | 对运行时、工具链附带文件和最终加载 DLL 建立独立发行清单，补干净普通用户验收 |
| Qt 6.11.2 | CMake 锁定 Widgets；[已有 SDK 记录](../qt-compositor-preparation.json)及[上游许可目录元数据](../qt-windows-server/qt-license-upstream.json)可追溯 | 最终实际部署模块、插件、嵌入依赖及对应源码/构建材料逐项核对；许可路线仍未决定 |
| ONNX Runtime 1.30.0 | [资产记录](../../../../experiments/windows/ai/assets.json)保存 runtime 来源、hash、LICENSE 和 ThirdPartyNotices 身份 | 与最终 Windows runtime 包逐项对照；不能以运行库许可代替权重许可 |
| U2NetP 转换权重 | 上述资产记录中独立权重许可未验证、再分发未批准；现有诊断包未带 `.onnx` | 明确该具体权重的获取/发行依据，或换用依据清楚的候选权重；模型选型仍开放 |
| libheif 1.23.4 / libde265 1.1.1 | [已有来源、COPYING 和构建记录](../heic-preparation.json)；两个独立共享库候选 | 根据最终产物落实既有发行待办；解码正确性不自动关闭 D-07 |

以上其他组件行汇总已有证据；本轮没有重新下载其 SDK、执行其 Windows 程序或完成发行审查。

## 复现与检查

使用 [盘点脚本](../../../../scripts/windows/inventory-probe-dependencies.py)：

```sh
python3 scripts/windows/inventory-probe-dependencies.py \
  <R9-windows-publish> <nuget-cache> <新的证据目录>
```

实际检查通过：24 包/28 资产正向核对；向 `Avalonia.Base.dll` 追加字节后拒绝通过；输出目录已存在时拒绝覆盖；原文归档 ZIP CRC 通过。所有反例使用临时副本，R9 原始包及发布目录未改。此脚本只适用于当前平铺的探针发布布局，不声称是通用 SBOM 或生产发行验收工具。
