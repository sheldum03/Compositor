# 非恒等平面层跨工程拖放证据（M3f）

日期：2026-10-06

回归提交：`699ed63 Cover transformed flat-layer drag copy`

## 已验证范围

- 平面层自身的非恒等 transform 已覆盖平移、缩放、任意角度旋转、水平翻转和垂直翻转；跨工程复制保留 transform 元数据、像素、图层蒙版、透明度和混合模式，不把变换快照静默烘焙成新栅格。
- API 复制和图层列表拖到另一工程标签都通过；目标复制仍是独立历史步骤，可撤销/重做，保存重开后 transform 仍与源层一致。
- 复制使用文档像素坐标；源/目标必须是相同像素画布尺寸的可编辑 v8 工程。当前没有放行跨尺寸坐标换算，也没有把不同 DPI 的物理尺寸换算冒充为像素坐标验证。
- 祖先组带非恒等 transform 时，单独复制组内平面层仍拒绝；复制整个组继续保留组 transform。这样保留祖先合成语义，不把组快照隐式烘焙到单层。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-transformed-drag-20261006-d
```

结果：Release 构建 0 警告、0 错误；App.Checks 三项生产检查全部 PASS。新增回归覆盖 API 复制的完整平面 transform、像素/蒙版/外观元数据和保存重开，以及正式窗口列表拖放的完整平面 transform、Undo/Redo 和保存重开。已有组内拖放回归继续验证变换祖先组的单层拒绝与整组复制路径。

以上是 macOS Headless 证据，不能替代 Windows 原生拖放、输入、DPI、原生 DLL 或性能验收。跨变换快照的祖先组/复杂目标组层级、移动而非复制以及不同像素画布坐标换算仍未交付。
