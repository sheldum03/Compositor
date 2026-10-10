# Qt 字体目录诊断准备：未执行 Windows A/B

本任务审查并纳入experiments/windows/qt/run-font-diagnostic.ps1。它读取原Server cbc28f5私有包，先核对固定manifest SHA及350payload文件身份；两个独立offscreen进程仅改变QT_QPA_FONTDIR（清空、Windows/Fonts），其余EXE/固定12样本/平台/缩放一致。只记录两个系统字体的身份，不复制或安装字体；finally恢复进程级环境。结果留在新目录，结束再检查原payload并归档。原已上传包未修改。

此前Server的12样本Emoji视觉失败仍未修复；目录对照只是检验原因的准备，不代表系统字体目录等价原生Windows字体数据库。完整结果需实际查看图片及独立像素核算，脚本completed仅表示两轮诊断完成，nativeImeExecuted/productAccepted始终false。

静态审查发现reportComplete置true后读取resolvedFonts若异常，旧catch只记error，完成门槛仍可能true。本任务以原Server报告和删除该字段的副本提取报告处理段，在Mac PowerShell7.6复现；父任务将catch同时置complete=false并实跑修正段。正常报告两版均通过，负例旧版caughtError=true且完成true，新版完成false；before/after脚本只差该修正。原始日志/摘要/最终语法解析记录保存于本目录，整合检查确认runner SHA和原始日志一致。

这是提取处理段的红绿验证，两版测试驱动自身退出0；不是完整wrapper的Windows退出码测试。WindowsPowerShell5.1两轮A/B尚未执行，qwindows/真实IME/最终字体策略未变。父artifact：/Users/admin/.codex/visualizations/2026/09/20/01a0beb1-da38-7b00-9183-d65bff121d78/qt-font-diagnostic。
