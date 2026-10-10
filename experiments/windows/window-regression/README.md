# 原生笔刷背景回归

```sh
dotnet run --project experiments/windows/window-regression/Regression.csproj -c Release -- /tmp/window-brush-background.png
```

使用真实 WindowBrushView 的初始化、BrushSession 和 Skia 绘制回调，在不透明背景上渲染一个软笔点，断言全部输出像素保持不透明。通过反射进入原型内部会话，避免扩大正式可见性；若原型字段变化需同步此受限探针。

引入 SaveLayer 前实测退出 1，32,041 个不透明性错误；修正后退出 0，错误 0。它捕获原生窗口中的方形透明瓦片背景擦除，不代替 Windows 输入、视觉或性能验收。
