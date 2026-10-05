# M3a 最小编辑工作流（2026-10-05）

当前生产窗口的 `NewDocumentChecks` 已把已实现的平面编辑切片串成一条内部回归：新建透明文档、实际指针笔刷、图层增删/复制/选择、透明度/混合模式、画布与图像尺寸、顺/逆时针 90° 旋转、保存、重开和 PNG 导出。旋转后的文档重开后，导出尺寸必须保持 257×259；随后继续用 Undo/Redo 核对保存点语义。

## 固定验证

使用固定 SDK 10.0.401、Avalonia Headless 11.3.22，在 macOS arm64 Release 下执行：

```text
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore \
  -p:RestorePackagesPath=/tmp/compositor-nuget-packages
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-check-m3a-no-native
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-check-m3a-native \
  /tmp/compositor-native-magic-outline/libcompositor_native.dylib
```

构建退出 0，0 警告、0 错误；无原生库和带 `compositor_native` 动态库的检查均退出 0，并报告生产软笔、实际新建/图层/保存回归及 Avalonia 窗口保护回归通过。

## 边界

这是 M3a 的 macOS Headless 内部集成证据，不是 Windows 实机验收，也不是 Alpha 交付。当前仍缺 Windows 原生文件对话框、DPI/IME/数位笔、组/蒙版、非破坏变换、浮动选区、完整跨工程工作流、性能及安装包验收；W-022 继续保持未完成。
