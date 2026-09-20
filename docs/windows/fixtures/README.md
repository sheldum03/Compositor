# W-002 固定工程与 Mac 参考图

生成日期：2026-09-20。源基线 `d562565`，加上 W-001 的 Levels alpha 与 sRGB Dodge/Burn 修复。精确源码提交和命令见 [执行记录](../execution-log.md)。

这些是**按各版本 schema 重建的非空样本**，不是历史应用发布包输出。仓库可追溯的最早 ProjectStore（`2dae6a2`）已经写 v7，未找到 v1–6 的历史编码器。各版本字段依据 `docs/project-format.md`、当前 reader 的版本约束、真实 Swift Codable 编码。没有从空 v8 manifest 改版本数字。

生成器：`CompositorTests/WindowsFixtureTests.swift`。测试逐版本创建有意义的图像/元数据，仅引入该版本支持的字段，断言 JSON 字段白名单，执行写入→读回→Mac 渲染→安装至 EditorSession→升级保存 v8→再读回；比较全部编码元数据、逐资产像素及合成像素。UUID、几何和颜色固定。所有几何素材由测试生成，无外部图片使用权依赖。文本使用仓库思源黑体，Emoji/组合字符 fallback 来自记录的 Mac 系统；字体不嵌入工程。

| ID | 版本 | 新增覆盖 |
| --- | --- | --- |
| F01 | 1 | 64×48 非空半透明双色像素，resolution 缺省为 72 |
| F02 | 2 | pass-through 组及真实 child parentID |
| F03 | 3 | 第二个不同尺寸/位置图层、Multiply、0.55 opacity |
| F04 | 4 | 64×48 灰度渐变栅格蒙版 |
| F05 | 5 | 同父连续剪贴栈，软 base alpha |
| F06 | 6 | 组蒙版与子层蒙版叠加 |
| F07 | 7 | 剪贴 HSV 调整层、调整 opacity |
| F08 | 8 | 中文/英文/Emoji/组合字符的可编辑框文本与 PNG 缓存 |
| B01–B13 | 8 | 按 LayerBlendMode.allCases 顺序的全部混合；每个有两个半透明彩色层。使用当前格式，不宣称后加的四种非分离模式属于历史 v3 |

每个 `*.comp/` 对应 `*-mac.png`。`checksums.json` 固定所有 manifest、PNG 资产和参考导出的 SHA-256/字节数，hash 验证文件身份而非跨平台编码一致性。跨平台应比较解码像素与元数据语义，不能以 PNG 压缩字节不同判失败。

当前 Mac 冻结像素测试要求 exact；Windows 浮点混合/取样容差尚未决定，不能直接放宽统一阈值。小型 64×48 工程用于正确性，不证明 4K 性能。测试临时输出另含 `*-resaved-v8.comp`，入库保留原 schema 样本即可。

2026-09-21 扩展：[extended](extended/README.md) 新增 F09、12 份 F11 样式/DPI 和 F12 缺字体，共 14 份；[invalid](invalid/README.md) 固定 14 份 F10 拒绝样本；[brush](brush/README.md) 固定同一 4K 事件流的 CPU/Metal 首笔/两笔参考及工程，实测存在 alpha 差异。

剩余：F12 字体冲突/TTC/损坏与重启的可跨平台复现资源；文本缺字/更多组合的行为验证；Windows 修改后 Mac 重开；D-03/D-11 参考语义及逐操作容差冻结。W-002 未完成；这些样本不能替代 Windows 四路径原型。
