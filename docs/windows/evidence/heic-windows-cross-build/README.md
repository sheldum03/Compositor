# Windows HEIC 交叉构建与测试包

固定实施基线：605a610d4dea0ef6021ca044766f39e6686a3dda；独立构建目录 `/tmp/compositor-parent-heic-windows`。未修改实施任务工作区。`evidence/mingw-unicode.patch` 仅增加 MinGW 的 `-municode` 链接选项，对实施工作区 apply-check 退出0。

## 已完成的实际工作

- 重新核对既有官方 libde2651.1.1/libheif1.23.4 源码包 byte count/SHA256，再从原始压缩包提取；上游源码未修改。
- CMake3.31.6、LLVM-MinGW20260908/clang23.1.1/ucrt，构建两个共享库。保持原 codec 配置：libde265 内置解码，关闭动态插件和外部编码器/其他解码器。上游3条警告保留；自己的 probe 保持 -Wall/-Wextra/-Werror。
- 原 probe 首次链接实际失败：undefined symbol WinMain。`wmain` 分支已经编译，但 MinGW 未选择宽字符 CRT 入口。仅加入 `if(MINGW) target_link_options(heic_probe PRIVATE -municode) endif()` 后同构建目录链接退出0；未改路径编码或解码实现。
- 暂存 `heic_probe.exe/libheif.dll/libde265.dll/libc++.dll/libunwind.dll` 五个 x64 PE，9条随包依赖边的所有命名导入均在目标导出表中找到。Windows API/UCRT 留作系统依赖。静态检查不证明运行时加载或 ABI。
- Windows PowerShell runner 原文使用 UTF8 BOM，为中文/非BMP字面量保留编码。通过PowerShell7.6语法检查；Windows5.1未执行。
- 为独立检查脚本逻辑，仅在另一个复制目录替换平台守卫和原生程序为既有Mac probe，重新生成清单；原Windows包不修改。适配副本实际完成16个decode、23次native invocation（17成功、6预期拒绝）、中文/空格/Emoji输入输出、既有输出保留及4类错误；退出0并生成ZIP。16个raw hash及所有像素差异指标与既有独立Python报告完全一致。
- 负例副本只翻转一个参考alpha字节并更新其清单，确保进入像素比较。原生decode退出0但alpha检查失败；wrapper最终退出1，错误摘要/日志/ZIP保留。脚本不是只依靠所有命令退出0。

测试包 `CompositorHeicProbe-cross-build.zip`：4,466,298 bytes，SHA256 `3350ac1b5f29157e4d4d11a2b8aef464328b4cb7982c49873f5c1000088d9d45`，75个清单文件，CRC通过。包含原16组HEIC/PNG、独立从PNG展开的参考RGBA、两库原始源码、自己源码/链接补丁和依赖许可。不是产品安装包。

## 未执行与交接

本次 Windows EXE、Windows PowerShell5.1、实际DLL部署尚未执行。用户正在操作Chrome：尝试从已登录腾讯云文件页进入上传时，CUA检测用户切换到新标签页；已停止前台操作。没有上传测试包、没有发起TAT命令、没有更改服务器系统。

后续在原已授权服务器验证目录创建新隔离目录，核对以上完整ZIP hash/大小后解压，运行其中 `run-heic.ps1`，保留进程退出码、summary/原始rgba/日志/结果ZIP并下载独立重算。Server结果仍不等于Windows11参考机、干净机、真实照片/ICC/HDR验收。当前16样本无ICC，RGB最大1/255的Mac基线差异没有获得容差批准。D-07发行许可/专利、W-031全量IO和M1选型仍保持未通过。

证据的Mac副本ZIP内会出现Mac路径；原生输出来自Mac，不能通过windowsExecuted标签改写为Windows证据。`evidence/wrapper-checks.json`明确标注副本变化、原始报告位置与限制。二进制和源码完整身份见包内files.json及evidence/pe-summary.json。
