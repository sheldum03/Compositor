# 本地字体加载与 TTC 字面实验

2026-09-23。M1 技术设计明确要求验证自定义字体及 TTC 多字面；此前只有 Mac FontLibrary 的两进程结果，本实验补查 Avalonia 路径。本地和 Windows 同版四字面加载、实际排版均通过，原流失败对照已保留；这不是生产字体导入验收。

## 实验与失败对照

采用既有自制几何字体，fontTools 独立读取名称、字重和度量；未使用或修改商业字体。

| 文件 / 字面 | 字重 | A 在 100 单位字号的 advance | AB 排版宽度 |
| --- | --- | --- | --- |
| fixture.ttf | 400 | 60 | 120 |
| fixture.otf（CFF） | 400 | 60 | 120 |
| two-faces.ttc / Regular | 400 | 60 | 120 |
| two-faces.ttc / Bold | 700 | 80 | 160 |

[基准失败](baseline.json)显示：直接把 TTC 流交给 Avalonia，第二次读取仍获得首字面 Regular/60。`--raw` 保留这一失败控制，必须仅最后一项失败并退出 1；它没有把索引传给一个不存在的流接口参数，也不能以“加载成功”证明第二字面正确。

先考虑首字面限制、缓存混淆、样本错误三种原因。fontTools 确认两个字面与不同度量存在。Mac 上 Skia 2.88.9 的直接索引文件读取在 index 1 返回 null；最终报告明确记为 null/-1，不能以默认字体的字宽代替。该原生行为须在 Windows 单独观察。

受限适配只在字节副本中交换 TTC 目录偏移，让目标字面排首位，再经原流接口加载。四个字面度量通过后，进一步检查 `TextLayout` 的实际字面引用与宽度，暴露实验代码的字体 URI 用法错误：仅传 base URI 和家族名仍回退系统字体，四项排版均失败，见 [排版失败记录](layout-adapted.json)。改用包含 `fonts:…#家族名` 的标识后，[最终本地结果](layout-final.json)四项全部通过，[同版原流对照](layout-final-raw.json)仍仅第二字面失败。每项源文件摘要不变，空文件、损坏 TTF、截断 TTC 均被拒绝。

## 边界和选型影响

`IFontManagerImpl` 流接口在此固定版本的稳定引用 API 中不可调用；项目显式开启 `AvaloniaAccessUnstablePrivateApis`，构建的 AVA3001 警告保留。此为维护成本，不能把这条实验路线写成仅依赖稳定 API。[固定版流加载实现](https://github.com/AvaloniaUI/Avalonia/blob/11.3.22/src/Skia/Avalonia.Skia/FontManagerImpl.cs)和[字体标识实现](https://github.com/AvaloniaUI/Avalonia/blob/11.3.22/src/Avalonia.Base/Media/FontFamily.cs)已用于核对。

适配限定为无签名 TTC v1 自制样本；未实现 TTC v2/签名处理、PostScript 名定位、完整损坏校验、100 MiB 限制、冲突/去重、重启恢复或字体导入 UI。字体表不改写，输入文件不回写。此实验不能替代 W-024 或 D-11 缺字体行为验收；生产阶段不能直接复制该受限助手当作通用导入器。TTC 目录结构参考 [OpenType 文档](https://learn.microsoft.com/en-us/typography/opentype/spec/otff#font-collections)。

## 复核及 Windows 准备

[独立复核](review.json)将 fontTools 解析结果与四项实际排版逐项比较，记录原始报告、样本和 DLL 摘要。固定源码 `075f2009104e28ff43fb111134976c2d4b6ff0c4` 已生成 Windows x64 包；19 个依赖与现有实机包字节一致，不重新传输。包 38,823 字节，SHA-256 `b616eb2321c2df5ef1572ad486bd2afb80179dc9e6b6161baae28b6175785729`。

独立目录入口 `python FontLoadingKit/run-windows.py` 先检查载荷、19 个基础依赖和 193 个官方运行时文件，复制到新结果目录，再执行原流失败控制与适配通过测试；结果退出码/PID/原始 JSON 单独归档。源码、参数和限制见[探针说明](../../../../experiments/windows/font-loading-probe/README.md)。程序不会将生产字体导入标记为通过。

## Windows 同版复测

通过 UU 独立终端 session2 执行，未中断尚待原生输入测试的 session1。包的 Windows `Get-FileHash` 与上列摘要一致。文件校验后在 `C:\Users\Administrator\Desktop\CompositorTest\font-loading-20260923-233736` 运行：原流 PID 30740 退出 1，仅第二字面失败；适配 PID 47216 退出 0，四字面均通过。运行库 .NET 10.0.12；两份 stderr 为空。

取回 2,134 字节 ZIP 后复核 CRC、5 个文件摘要、包清单摘要、PID、退出码、逐项源字体身份与排版指标。四个 `AB` 宽度为 120/120/120/160，均使用加载字面的同一个对象；三份损坏样本均被拒绝。见[Windows 独立复核](windows/review.json)、[适配结果](windows/adapted.json)和[原流控制](windows/raw.json)。ZIP SHA-256 为 `b6fe7af0baa7524f9e9e8a5fb6c9cd5862cd3c14a7b349458f0f1e6630123240`。

平台差异已直接观察：Windows 的 Skia 索引读取能取得第二字面 Bold/80，Mac 同一调用返回 null；Avalonia 的无索引流读取仍只得到首字面。因此 M1 记录为“本机受限字体加载路径有证据，通用导入和稳定 API 路线仍需落实”，不把 TTC v1 小样本的通过扩大到完整字体产品能力。前面的本地复核 JSON 保留当时 `windowsExecuted=false` 的准备状态；本节和 Windows 原始报告补充后续实测。
