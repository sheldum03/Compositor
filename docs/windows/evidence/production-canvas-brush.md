# M3 画布视图与软笔初次集成（2026-10-05）

生产窗口改为独立画布控件，提供围绕鼠标的滚轮缩放、中键/空格加左键平移及适合窗口。视图坐标和文档像素坐标明确转换，导航不修改文档。软笔将已验证 M1 的 CPU Gaussian tip、曲线采样、临时笔尾替换和预乘混合算法接入正式 `TileRaster`；同一事务路径增加硬圆笔模式，硬度只在 0（软）和 1（硬）之间切换，未触碰 Mac 或共享 C 算法。

`EditorWorkspace` 保存当前笔划的临时状态，使用 `29469e1` 的临时像素覆盖接口预览。松开鼠标只调用一次图层像素事务，和正式历史/保存点共用；Esc、捕获丢失、窗口失去活动状态及关闭取消未提交笔划。期间窗口禁用文件/图层命令，工作区也拒绝重叠保存。修改只作用于选中图层，旧不可变快照继续保留。

## 固定源码和参考

固定已提交基线 `5ef92ba`（含 `7a1c08b` 图层结构核心）加十个 App/App.Checks/Brush 文件，记录[身份与覆盖文件 SHA-256](production-canvas-brush/source-identity.json)。完整冻结源码、1913 个文件的构建前摘要与产物保存在 `/Users/admin/.codex/visualizations/2026/10/05/production-canvas-brush/`；锁定恢复、Release 构建和运行后逐文件摘要未改变。

参考像素由原 M1 三个未修改源文件生成，来源摘要、输入及 ZIP 摘要在 `windows/Compositor.App.Checks/fixtures/soft-brush-reference.json`。实际样本为 259×257、sRGB 预乘 RGBA8，包含跨瓦片曲线、1 像素笔尖与重复采样、800 像素笔尖的画布外裁剪；三组各有临时笔尾和结束后的原始像素，每幅 266,252 字节。生成器及原始输出保存在 `/Users/admin/.codex/visualizations/2026/10/05/production-brush-reference/`。初次元数据和结果范围误写 300×300；该描述已纠正，并加入尺寸断言，原始记录保留。这不影响实际六幅像素比较，但不能沿用错误的尺寸声明。此处比较 M1 算法；不声称新笔划已经过真实 Mac 再读回。

## 实际验证

macOS arm64、.NET SDK 10.0.401、Avalonia Headless 11.3.22。固定源码恢复/构建/运行均退出 0，构建 0 警告/错误，见[命令与退出码](production-canvas-brush/verified-commands.json)、[构建日志](production-canvas-brush/verified-build.log)、[运行日志](production-canvas-brush/verified-run.log)、[范围记录](production-canvas-brush/verified-summary.json)及[画布结果](production-canvas-brush/canvas-results.json)。

尺寸更正另固定 `869372f` 加两个检查/元数据文件，加入实际宽高断言和结果字段；恢复、构建、运行再次退出 0，源码摘要保持。见[更正身份](production-canvas-brush/dimensions-correction/identity.json)、[执行记录](production-canvas-brush/dimensions-correction/commands.json)、[构建](production-canvas-brush/dimensions-correction/build.log)、[运行](production-canvas-brush/dimensions-correction/run.log)及[正确尺寸结果](production-canvas-brush/dimensions-correction/canvas-results.json)。前一轮结果中的 300×300 仅为错误描述，保留原始文件以便追溯。

- 三组软笔的临时与最终像素逐字节完全相同，共六幅原始 RGBA 参考。
- 实际窗口滚轮保持文档锚点；中键和空格加左键平移偏移正确，不绘画、不产生历史或脏状态。
- 按下/移动只预览，保存被拒绝；Esc 和实际指针释放捕获都取消，不改源像素或保存点。
- 鼠标松开提交一笔；一次撤销恢复保存点，重做恢复完整笔划；其他层与旧像素快照保持。
- 保存重开仍与参考完全相同；合成与 PNG 导出重读一致。已保存文档在绘制过程中关闭，未提交笔划被取消，窗口正常关闭。
- 独立 21×21 像素检查确认硬笔中心覆盖为不透明、圆外保持透明；正式窗口的 `BrushType` 控件可在软笔/硬笔间切换，默认仍为软笔。
- 原窗口的文件保护、实际图层元数据按钮、PNG/JPEG、关闭保存/取消/不保存和失败保存保护继续通过。

[窗口截图](production-canvas-brush/brush-window.png)已检查中文、控件和笔划显示，未见遮挡。[产物摘要](production-canvas-brush/verified-output-sha256.json)定位完整 `Brush.comp` 和导出文件。

早期检查因 Headless 键盘 API 编译失败；`attempt1` 随后使用旧二进制，虽退出 0，却未执行新画布检查，明确排除。修正为非过时的 physical/QWERTY API 后重新构建，工作区 `attempt2/3` 通过；本记录以最终冻结源码的 `verified-*` 为证据，不沿用旧二进制结果。

## 未完成条件

这仍是未变换的 CPU 笔刷集成；四种颜色是内部参数入口，完整颜色选择、蒙版编辑、压力、变换与项目标签仍待实现。窗口失去活动状态的取消已接线，但原生 Windows 的该事件、DPI、文件对话框和真实设备没有验收。

当前每次笔划更新完整合成并创建位图，未迁移原型的局部绘制/缓存性能路径，**不声称 S02/S05 或 M3a/Alpha 通过**。本轮没有 Windows 执行、安装器、真实 Mac 新笔划读回或完整工具验收；r4 固定包不含本切片。
