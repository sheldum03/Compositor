# F09 / F11 / F12 复杂工程与文字缓存

日期：2026-09-21；生成器 `CompositorTests/WindowsExtendedFixtureTests.swift`，基于 `8596b5c` 的 Mac 产品源码。全部 v8，由真实 ProjectStore/文字与形状栅格器生成；几何自制，内置字体来自仓库，未分发系统字体文件。

- F09-complex：嵌套 pass-through 组、组蒙版、空层、可见子层的隐藏父组；圆角形状带旋转/镜像和独立蒙版 placement/maskLinked=false；椭圆形状带禁用但保留的蒙版；Screen/Multiply 与透明度。
- F11：72/300 DPI × point/box × left/center/right，共 12 份。字号 18 pt、行距 3 pt、字距 1.25 pt；中文/英文/Emoji/组合字符；旋转 13°、水平镜像、颜色 alpha=0.8、图层 opacity=0.65。box width=360 是文档像素，不随 DPI 同比例扩大；300 DPI 因字号转像素更大而换行更多。画布包围完整变换图像并留 60 px 边距。
- F12-missing-font：先用思源黑体形成缓存，然后仅将 PostScript 名改成明确不存在的名称；用于验证保存缓存保真，不是将缺字体 fallback 渲染冒充原字体。

每份测试比较全部持久化元数据、逐图像/蒙版像素、合成像素；安装到 EditorSession 后检查 liveShape/liveText 确实恢复，再次保存与读取不改变合成。固定参考独立读取入库工程并逐像素比较。完整画布尺寸是在目视发现原 300 DPI 框文本被裁切后修正，旧裁切输出没有入库。

`checksums.json` 记录 47 个工程/参考文件。当前 14 份工程通过 Mac exact 像素校验；Windows 重排、IME、命中和 DPI 显示仍未验证。缺字体缩放如何处理仍待 D-11 决定。本目录不包含 F12 TTC/字体冲突/损坏/重启的完整跨平台验收。
