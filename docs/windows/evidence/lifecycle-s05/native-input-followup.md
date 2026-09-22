# 连续输入与九轮诊断准备（2026-09-22）

本轮继续使用已核验的 R3 Windows DLL，输出根目录为 `C:\Users\Administrator\Desktop\CompositorTest\native-input-20260922-175634`。**连续 OS 笔刷输入及六组保存重开已独立逐像素通过；Qt 真实 IME 尚未通过。** [观察摘要](native-input-observations.json)明确区分终端观察和独立核验。

## 连续输入

1. 新建 Avalonia 窗口 PID 42624，以 Windows SendInput 选择笔刷页并保存空图，成功。
2. 连续笔划执行后，一度只看到首次保存的报告。光标诊断确认期望/实际屏幕坐标相符；远程画面可见笔划，但画布获焦后小窗口自动滚动，工具栏移出旧坐标，后续保存点击未触发。该窗口已正常关闭并保留诊断日志，不将其算成完成的连续保存测试。
3. 新建 PID 29424 并最大化。窗口扩大后画布居中，先前靠左的路径不在画布内；六次空图保存只作为坐标诊断保留。随后按居中画布重放三条多点路径，第二条在松键前 Esc 取消。
4. 居中重放后终端报告 **2 次提交、68 次 pointer 更新**，最后六次保存的 UndoCount 为 **0、1、1、2、1、2**，无 action-error。它们依次对应空图、第一笔、第二笔取消、取消后下一笔、撤销、重做。生成目录 `max-window/007-brush` 至 `012-brush`；原始包取回后已核验 008=009=011、010=012，007 全透明，008/010 确有变化，六组 4000×4000 final/reopened PNG 均完全一致。窗口已正常关闭且报告包含关闭事件。

这些输入走 Windows OS 事件及实际控件路径；不代表物理鼠标、数位笔或输入到屏幕呈现延迟验收。输入脚本并未因输出“发送成功”就将测试标为通过。

## Qt 对照

同机启动 `qt-remote-suite/window-app/qt_probe.exe --window`，PID 15572，输出 `qt-window`。原生窗口可见中文、英文和 Emoji；尚未证明原生 IME 输入。直接 UU 输入曾落在同组控制台的预编辑区，已按 Esc 取消，不计 Qt 证据。改用定向输入脚本后，前台窗口检查在发送前拒绝；AppActivate 返回 true 也未使检查通过。没有移除前台保护或将此直接归因于 Qt 产品缺陷。

后续确认没有 QT 环境变量覆盖；Qt 仍在运行，前台检查仍未通过。最大化 Avalonia 测试窗口已正常关闭，用户原有窗口保留。

## S05 九轮诊断包 R4

等待解锁期间新增独立的 `--s05-soak-window` / `--s05-soak-check`，同进程重复九轮，以检验已观察到的自然内存增长是否进入平台。每轮内容不变，全部自然关闭样本先记录，最后一轮之后才做诊断 GC；原定三轮入口仍保留。报告标记 `S05-soak-diagnostic`，独立复核必须显式加 `--soak`，不会把九轮诊断自动写成 S05 验收通过。

- 本地九轮：900 编辑、900 撤销、900 重做、18900 回调及九组 4000×4000 保存重开像素通过，最终旧文档引用为零。[复核](compositor-s05-soak-review.json) / [原始报告身份](local-soak-report-identity.json)。
- 默认三轮重跑通过：[复核](compositor-s05-default-after-soak-review.json)。现有 S02 本地四笔及原生读回对照保持精确。
- 三轮报告冒充九轮证据被复核脚本拒绝：[负例](compositor-s05-soak-negative.log)。
- Windows x64 Release 构建及包 hash/CRC 已核验；[R4 包身份](s02-lifecycle-r4-package.json)。**R4 已传输并核验包及 DLL hash，Windows 九轮原生诊断已启动。** 输出 `C:\Users\Administrator\Desktop\CompositorTest\s05-soak-20260922-185546`，PID 45392。运行期间未启动其他 Windows 测试或文件传输。再次查询时工具报告 Mac 锁屏、自动解锁失败，最终结果尚未取回；Windows 资源稳定性仍未通过。

## 原始证据与复核

原始 ZIP 为 `NativeBrushInputEvidence.zip`，5,490,254 字节，SHA-256 `d370e0c7689b784c8231679d55a6f4bfd2e665da242fb3de857135d106542cd3`；远端和下载文件身份一致，CRC 通过。完整 ZIP/PNG 位于 [本地证据目录](/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/windows11-remote-suite/native-brush-input)。仓库保留 [独立复核](native-brush-review.json)、[完整窗口报告](native-brush-window-report.json)、[原始文件 hash 清单](native-brush-file-hashes.json)、[输入动作](centered-brush-actions.json)与[注入日志](centered-brush-input.json)。001–006 空图属于坐标准备，未计入通过样本。仓库 JSON 副本仅统一为 UTF-8/LF；原始字节及 hash 以 ZIP 和清单为准。

可复现命令（需要 Pillow、NumPy）：

```sh
python experiments/windows/avalonia/review-native-brush.py <解压后的证据目录>
```

恢复连接后先检查九轮进程及输出，取回原始报告和九组图像，独立复核完整性及资源趋势；再继续 Qt 焦点与真实拼音对照。设备矩阵、M1 和完整 Windows 1.0 尚未放行。
