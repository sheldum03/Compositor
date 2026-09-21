# 文字拖选回归

选区内输入法取消回归：

```sh
dotnet run --project experiments/windows/text-selection-regression/Regression.csproj -- --ime-cancel
```

40 项使用真实 TextBox 输入法客户端，覆盖全选、局部/反向选区、无选区、首次/重新挂载，以及 null/空串取消、失焦、提交先于/晚于清除预输入。检查预输入不改正文、取消恢复选区、提交精确替换和撤销重做。修复前“全选→ceshi→取消”正文变成空串；单纯延后 ApplyTemplate 在重新挂载后仍失败。SelectionPreservingImeClient 将原选区保留到 TextInput 提交，由 presenter 临时显示替换后的预输入；取消不修改正文，也不创建恢复用的撤销记录。该回归不替代 Windows 原生微软拼音复测。

第二轮实际切页取消仍失败，补充 Windows 后端启动序列回归：

```sh
dotnet run --project experiments/windows/text-selection-regression/Regression.csproj -- --ime-tabs
```

通过真实 TabControl 和框架输入法管理器取得客户端，测试中的 DispatchProxy 只接收平台接口调用。随后重放固定 Avalonia.Win32 11.3.22 IMM32 启动流程：清空预输入，若 SupportsSurroundingText 且有选区则发送 Delete，再开始预输入。此前直接调用 SetPreeditText 漏掉了这次提前删除。旧修复在此路径正文变空、exit 1；包装客户端不再开放后端环绕文本编辑后，24 项首次/切页、全选/局部/反向/无选区、取消/两种提交次序均通过，含提交后的撤销重做。该能力收缩不阻止普通 Delete 按键；不声称环绕文本编辑或重新转换已验收，仍须真实微软拼音复测。

```sh
dotnet run --project experiments/windows/text-selection-regression/Regression.csproj -- /tmp/text-selection-results
```

使用真实 TextProbe.Editor、SpacedTextPresenter 和 SimpleTheme，在 Headless 窗口注入鼠标按下、移动、松开及文字输入。48 个组合覆盖文本中部/末尾选区、 50%/100%/150%、水平翻转、0°/13°和两个拖动方向。逐项检查撤销/重做、选中「测试」、可见高亮和输入 `1` 后精确替换。截图比较时隐藏闪烁光标，避免将光标变化误判为选区高亮。

修复前：24 项高亮像素变化均为 0；其中反向拖选的 12 项选区为空且替换失败。只设置 presenter 的透明背景后，24 项选择及替换通过，但高亮仍为 0。补齐 SelectionBrush / SelectionForegroundBrush 模板绑定后，24 项全部通过。

这是本地鼠标路由与绘制回归，不代替 Windows 11 实体机、UU 远程输入、真实微软拼音或显示缩放验收。用户实际窗口仍需复测。原有 12 个文字样本的 60 张 PNG 在修复前后逐字节一致。

2026-09-21 取回旧 Windows 原生窗口归档，发现退出码 -532462766：输入替换使文本缩短时，旧 CaretIndex=45 超过新 Text.Length=42。末尾选区真实 KeyTextInput 回归在旧版退出134，修正当前布局快照的 caret 后48项含撤销重做通过；不回写TextBox索引。原归档保留于 docs/windows/evidence/text-selection-preparation/windows11-old-run。
