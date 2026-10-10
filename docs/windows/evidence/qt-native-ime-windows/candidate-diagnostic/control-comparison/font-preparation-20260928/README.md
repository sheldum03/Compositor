# Qt 字体对照准备记录

状态：仅本地准备通过，尚未在 Windows 运行。Mac 锁屏且远程工具要求用户手动解锁；此前 UU 出现服务器连接错误，重连后的状态尚不可读取。没有新增候选定位通过结论，也不关闭 M1。

用户已明确确认原生 Win32 EDIT“换行测试通过”，见 [原生复核](../win32-20260928/README.md)。下一步仅在同一 QTextEdit 程序中依次换用 Source Han Sans SC 与 Microsoft YaHei UI，固定原文、32 逻辑像素字号、540 逻辑像素换行宽度、4 逻辑像素行距增量及 Qt 6.11.2 依赖。启动日志增加请求字体与默认字体匹配结果；该字段不保证所有字符（如 emoji）均由同一字体绘制。

必须实际触发拼音预编辑自动换行，观察候选栏跟随，再 Esc 检查原文并正常关闭。未换行、字体替代或远程输入未送达都不能记为候选定位通过。若两字体均失败，思源不是该问题的必要条件；若仅思源失败，只增加字体/布局线索，不认定根因。

## 构建与本地验证

旧临时工具链部分文件已缺失，因此从原固定版本恢复 LLVM-MinGW 与 Qt Windows SDK。Qt 使用官方 mirrorlist 列出的 constant.com 镜像；下载的 19,874,439 字节归档 SHA-256 为 `2b9777d1e669c56f7c898d3b76e1db95aac2c84bc8fd0b8f4da0e482bf66689f`，与已有来源记录一致。LLVM-MinGW 20260908 归档摘要为 `d1dc5d1ecf3a3ced5ed5544c72f1acd0c8e84eb3024d520ecc6b143eec62a149`。

直接重新编译全部 C++ 源文件，启用 `-Wall -Wextra -Werror`；复用经逐字节检查的 Qt 6.11.2 生成字体资源源码（内嵌字体与仓库 OTF 完全相同），以及摘要固定的原 native DLL 和配套导入库。没有执行损坏的旧 Mac Qt 工具或绕过系统签名检查。构建命令、资源身份、源码与负载摘要均附于本目录。

Python 语法检查通过，PE 架构为 x86_64；对当前 EXE 与配套 DLL 的 21 项非系统导入依赖核对导出符号，无缺失。Windows 系统导入未在 Mac 解析，静态检查不能替代 Windows 加载及输入法测试。新 ZIP CRC 通过。

包：`QtImeFont.zip`，13,910,042 字节，SHA-256 `6146304951f44beaa5090d23ec64676997d81c6ff03d960c84ae51d6297abf1d`。

## 恢复步骤

1. Mac 解锁后确认 UU Windows 桌面实际连接可用。
2. 经已授权的 UU 文件传输将新包复制到现有 CompositorTest，核对 SHA-256；确认 `QtImeFont` 尚不存在后解压，保留旧包及旧结果。
3. 在 CompositorTest 中执行 `python QtImeFont/run.py --font-comparison`，按上述条件完成两个独立进程的真实微软拼音测试。
4. 取回新结果 ZIP，验证远端和本地摘要、CRC、成员摘要、进程退出及最终原文，再结合人工候选栏观察记录结论。
