# Qt 窗口入口交付与验证

固定实施基线：605a610d4dea0ef6021ca044766f39e6686a3dda。父任务在 `/tmp/compositor-qt-window-parent/source` 独立修改；未编辑实施任务工作区。`qt-window.patch` 包含六个源码/文档文件变更，对当前实施工作区 `git apply --check` 退出0。

新增 `--window` 三页 Widgets 入口以及 `--window-check` 合成事件检查。复用原 Scene、笔刷会话和文字 format/preview/export 函数；保留原探针命令。完整使用范围及手动步骤见 changed-source/experiments/windows/qt/WINDOW.md。

## 已验证

- macOS arm64 Qt 6.11.2 Release 编译通过，仍使用 -Wall -Wextra -Werror。
- Windows x64 Qt 6.11.2 + LLVM-MinGW 23.1.1 交叉编译通过，未改 Qt 或原生算法。
- macOS offscreen 的窗口控件检查通过：两组文字预览/导出像素相同；文本、选择、undo/redo 可用状态保留；活动预编辑禁止导出且无部分输出；笔划 Escape 取消后 release 不提交、undo/redo、失活取消、背景全不透明；笔刷/F04 .comp 重开像素一致。
- 背景负例：独立测试中临时去掉透明缓冲，直接画瓦片。编译退出0，实际 QWidget 检查退出1并报告背景擦除。还原源码，重新编译及重跑退出0。负例源码、日志与退出码均保留。
- 原20个合成样本、12个文字样本和笔刷会话检查回归退出0；完整日志、PNG、工程和 JSON 保留。
- 实际查看旋转/翻转后的文字导出图：蓝色中英文和黄色 Emoji 可见。这是 macOS 画面，不能解释或替代 Server 上已发现的 Emoji 失败。

## 尚未验证

本次没有操作用户正在使用的 Chrome，也没有重新上传/运行服务器。macOS 原生窗口交互、Windows EXE 运行、微软拼音候选位置、真实 DPI 切换未执行。本次二进制仅是构建产物，binaries 不是完整可分发安装包；Windows 运行需匹配 Qt 库/插件及 native DLL，Mac 产物也保留开发机动态链接依赖。

画笔视图25%，800px软笔，40%不透明度，日志为事件处理函数耗时；不满足正式 S02 条件，不报告性能验收。D-11字体缺失处理、文字工程事务、生产存储、安装/签名/更新仍未实现。M1和框架选型不放行。

`files.json` 记录归档时源文件、构建产物及证据的 SHA-256。源码完整身份见 source-identities.json。交叉工具链/Qt SDK 的官方来源、哈希与许可记录沿用兄弟目录 qt-windows-cross-build；本次未下载或引入新依赖。
