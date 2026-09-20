# F12 字体导入、冲突和恢复样本

这些字体由 `scripts/windows/generate-font-fixtures.py` 从自绘三角形/矩形轮廓生成，包含 `.notdef`、空格、A、B 四个 glyph；无系统字体或第三方字体轮廓。随仓库 MIT 许可分发，完整文本在 `LICENSE.txt`。它们用于字体文件/注册边界，不替代思源字体、中英混排、Emoji 或真实文字排版验收。

| 输入 | 固定语义 |
| --- | --- |
| fixture.ttf | glyf 轮廓，PostScript 名 `CompositorFixtureTTF-Regular` |
| fixture.otf | CFF 轮廓，PostScript 名 `CompositorFixtureOTF-Regular` |
| two-faces.ttc | `CompositorFixtureCollection-Regular` 与 `CompositorFixtureCollection-Bold` 两个 face |
| duplicate.ttf | 与 fixture.ttf 逐字节相同，但文件名不同；再次导入应去重 |
| conflict.ttf | 有效字体，同 fixture.ttf 的 PostScript 名，A 的 advance 从 600 改为 800；导入 fixture.ttf 后应拒绝冲突并保留原字体 |
| wrong-extension.zip | 有效 TTF 字节放在不支持的扩展名下；应报 unsupported |
| empty.otf | 0 字节；应报 empty |
| damaged.ttf | 明确非字体文本；应报 damaged |
| truncated.ttc | TTC 仅保留前 16 字节；应报 damaged |

`cases.json` 是跨平台预期清单。单位 em=1000，常规 face 的 A advance=600；TTC Bold 为 800，因此 100 pt 下分别为 60/80 pt。两个 face 的度量不同，测试不会只验证枚举条数而忽略选到了错误 face。

## 重现

使用固定 fontTools 4.59.2；生成器禁止覆盖已有目录，固定字体时间戳并关闭重新打时间戳。API 来自 [FontBuilder](https://github.com/fonttools/fonttools/blob/main/Lib/fontTools/fontBuilder.py) 和 [TTCollection](https://fonttools.readthedocs.io/en/latest/ttLib/ttCollection.html)。运行测试读取已入库的文件，不需要安装 fontTools。

```sh
python3 -m venv /tmp/compositor-font-fixture-venv
/tmp/compositor-font-fixture-venv/bin/python -m pip install fonttools==4.59.2
/tmp/compositor-font-fixture-venv/bin/python scripts/windows/generate-font-fixtures.py /tmp/compositor-font-fixtures-rebuilt
```

`checksums.json` 固定 11 个字体/许可/清单文件的 SHA-256（不含 README 和 checksums 自身）。在新目录生成的 11 个文件已与入库版本逐字节比较相同。不存在从系统 Helvetica 复制或仅改扩展名构造“有效 TTC”的操作。

## Mac 验证范围

2026-09-21 在 macOS 26.5.1 arm64、Xcode 26.6 执行 `FontLibraryTests`：6 tests，0 failed，0 skipped。原依赖 `/System/Library/Fonts/Helvetica.ttc` 且缺失会直接返回的测试，已替换成必需的固定 TTC；新增 TTF/OTF、重命名重复、明确错误分类、拒绝后目录不变与原 face 度量保留。清理由 CoreText 注销本次导入，再删除测试临时目录，未写用户字体库。

进程恢复探测直接编译仓库 `FontLibrary.swift`，仅补上 CompositorApp 的同一 String.localized 表达式。以下 import/restore 是两个独立程序调用，实际 PID 不同；每个都先确认四个名称尚不可用，随后检查四个 face 的可用性、字宽、选择器列表和字体来源 URL。restore 只从持久化目录注册，不重新导入原文件：

```sh
xcrun swiftc -swift-version 6 -parse-as-library \
  Compositor/Document/FontLibrary.swift experiments/windows/font-library-probe.swift \
  -o /tmp/compositor-font-library-probe
/tmp/compositor-font-library-probe import docs/windows/fixtures/fonts /tmp/compositor-font-library-restart-192b
/tmp/compositor-font-library-probe restore docs/windows/fixtures/fonts /tmp/compositor-font-library-restart-192b
```

import 的输出目录必须尚不存在；重复验证请换一个新路径，避免覆盖。实际导入后的三个文件与源字节一致。探测是进程范围注册，不安装系统字体；进程退出后注册随之结束，保留临时目录用于恢复步骤。

证据：[单元测试](../../evidence/w002-font-summary.json)、[独立进程](../../evidence/w002-font-processes.json)。这些只证明 Mac 库级行为；没有执行完整 Compositor 窗口退出/重开，也没有执行 Windows、输入法或跨平台字形对齐验收。W-002/W-024/V-06 未因这些测试自动完成。
