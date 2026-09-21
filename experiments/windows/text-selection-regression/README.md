# 文字拖选回归

```sh
dotnet run --project experiments/windows/text-selection-regression/Regression.csproj -- /tmp/text-selection-results
```

使用真实 TextProbe.Editor、SpacedTextPresenter 和 SimpleTheme，在 Headless 窗口注入鼠标按下、移动、松开及文字输入。48 个组合覆盖文本中部/末尾选区、 50%/100%/150%、水平翻转、0°/13°和两个拖动方向。逐项检查撤销/重做、选中「测试」、可见高亮和输入 `1` 后精确替换。截图比较时隐藏闪烁光标，避免将光标变化误判为选区高亮。

修复前：24 项高亮像素变化均为 0；其中反向拖选的 12 项选区为空且替换失败。只设置 presenter 的透明背景后，24 项选择及替换通过，但高亮仍为 0。补齐 SelectionBrush / SelectionForegroundBrush 模板绑定后，24 项全部通过。

这是本地鼠标路由与绘制回归，不代替 Windows 11 实体机、UU 远程输入、真实微软拼音或显示缩放验收。用户实际窗口仍需复测。原有 12 个文字样本的 60 张 PNG 在修复前后逐字节一致。

2026-09-21 取回旧 Windows 原生窗口归档，发现退出码 -532462766：输入替换使文本缩短时，旧 CaretIndex=45 超过新 Text.Length=42。末尾选区真实 KeyTextInput 回归在旧版退出134，修正当前布局快照的 caret 后48项含撤销重做通过；不回写TextBox索引。原归档保留于 docs/windows/evidence/text-selection-preparation/windows11-old-run。
