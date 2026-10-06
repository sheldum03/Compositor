# 组级根图层合并证据（M3g）

日期：2026-10-06

功能提交：`ba17b48 Support grouped root merge with subtree flattening`

## 已实现范围

- 正式窗口的“向下合并”在受限 v8 组工程中支持选中的连续同级根图层；单选组会与其下方同级图层合并，多选仍要求同级且连续。
- 组根的完整子树会作为一个合并单元，保留组内子层像素、组变换、组透明度/混合模式和内部连续剪贴关系的可见结果。
- 合并结果写入下方根图层，统一为默认 Normal、100% 透明度、画布尺寸的平面层；组、子层、蒙版资产和剪贴关系会从新 manifest 与历史快照中清理。
- 不允许跨父级、跳过同级图层或跨越选中子树边界的剪贴关系；不支持的工程仍由按钮和核心校验拒绝。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-group-merge-20261006-e
```

结果：Release 构建 0 警告、0 错误；App.Checks 三项生产检查全部 PASS。新增窗口场景使用下方普通图层和上方带组变换、组混合模式、组透明度及内部剪贴关系的组，逐 tile 比较合并前后预览，随后验证 Undo、Redo、保存、重开和输出仍为单一平面层。

同一固定源码随后复跑：

```sh
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-group-merge-20261006-a
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-restore -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-imaging-group-merge-20261006-a
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-restore -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-savecrash-group-merge-20261006-a
```

Workflow、Imaging 全部场景及 SaveCrash 的 14 个真实保存中断场景均 PASS。以上仍是 macOS Headless 结果，不能替代 Windows 原生窗口、输入、DPI、原生 DLL 或性能验收。

## 发布追踪

同一提交的 `win-x64` self-contained 便携包：

- 目录：`/tmp/compositor-win-x64-group-merge-ba17b48`
- 文件数：224
- `Compositor.App.exe` SHA-256：`f1a3a6c0c9b54ad6d29e4526d138e8837b40d72b3ad15f81c7c3b852cf3f8e8b`
- 当前包不含 `compositor_native.dll`，也未在 Windows 上启动。

该包只证明交叉发布命令可完成和包内容可追踪；Windows 实机验收仍需在腾讯云 Windows 服务器解锁后执行。
