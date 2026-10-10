# AI 下载整体时限：整合与独立复测

父任务在 Windows Server2022 实际执行旧包，下载在 TAT 240秒时限后中断，仅收到4,058,944/4,574,861字节；限定进程/目录诊断确认没有原生调用、无summary/结果ZIP。不是推理失败；不将这一次观察概括成PowerShell通用缺陷。原运行信息见server-first-timeout.json。

修正版runner使用系统curl.exe，首参数-q禁用用户配置，连接20秒、传输总时限120秒，无自动重试。捕获curl退出码/耗时/字节数，失败由原有wrapper归档download.log和summary，再返回1。模型长度与SHA守卫、-ModelPath、原生推理不变。唯一包变更是runner、README及manifest。本任务核验7718022字节ZIP SHA256 1daadfffcb64eacd2bc2f5319d7612b297df0c22a9728e636a0bff695af26a8c、CRC和27文件身份，见identity-check.json；确认没有onnx权重。

本任务在新的Mac适配副本独立重新执行全部三个case，实际本地HTTP服务+真实Mac原生推理，未执行附件或Windows二进制。正常下载完成8调用，wrapper0；慢服务先发送1024字节，测试副本仅把总时限改成2秒，curl28/2.092秒、wrapper1/2.919秒，日志/summary/ZIP保留；同长度错误hash在原生调用前拒绝，wrapper1。三个ZIP均CRC通过且无权重。checks.json、各case完整原始ZIP与适配脚本保留；check-download.py记录完整适配差异与本次绝对路径。原Windows修正版未改成Mac脚本。

仓库experiments/windows/ai/run-ai.ps1是与修正版ZIP逐字节一致的源文件，仍需配套app/fixtures/files.json才能执行。未上传新包、未安装系统curl、未运行Windows下载分支；该修复的WindowsPowerShell5.1行为、默认网络链路及真正Windows推理仍待独立实测。M1/D08/W009/W032不据此通过。
