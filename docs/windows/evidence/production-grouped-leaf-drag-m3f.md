# 组内平面层跨工程拖放证据（M3f）

日期：2026-10-06

功能提交：`81d6f39 Support copying grouped raster leaves`

## 已实现范围

- 同窗跨工程图层拖放现在允许复制组内的平面层，前提是该层的所有祖先组使用恒等 transform，且没有启用的组蒙版或外部剪贴关系。
- 复制会把该层作为目标活动组内的同级图层插入，保留层名称、像素、图层蒙版、蒙版启停、透明度、混合模式和自身 transform；目标复制是独立的历史步骤，可撤销/重做并保存重开。
- 变换祖先组、启用组蒙版、组内剪贴目标、缺失蒙版资源和复杂层级仍会在拖放前拒绝，避免把祖先合成语义静默烘焙到新层。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-grouped-leaf-20261006-c
```

结果：Release 构建 0 警告、0 错误；App.Checks 三项生产检查全部 PASS。新增场景先验证变换组整体复制，再把源组恢复为恒等 transform，将源组内平面层拖入目标组，逐 tile 比较像素、父级、层 transform、目标历史和保存重开结果。

同一固定源码随后复跑：

```sh
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-grouped-leaf-20261006-a
dotnet run --project windows/Compositor.Imaging.Checks/Compositor.Imaging.Checks.csproj -c Release --no-restore -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-imaging-grouped-leaf-20261006-a
dotnet run --project windows/Compositor.SaveCrash.Checks/Compositor.SaveCrash.Checks.csproj -c Release --no-restore -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-savecrash-grouped-leaf-20261006-a
```

Workflow、Imaging 和 SaveCrash 均以退出码 0 完成；Workflow 覆盖缓存组、组蒙版、变换、混合模式、像素/元数据历史和保存往返，Imaging 覆盖图像与蒙版 IO，SaveCrash 覆盖 14 个真实保存中断场景。以上仍是 macOS Headless 结果，不能替代 Windows 原生拖放、输入、DPI、原生 DLL 或性能验收。

## 剩余边界

复杂变换快照拖放、启用组蒙版下的单层复制、组内剪贴栈的单层复制、复杂目标组层级和移动而非复制仍按计划拒绝；需要保留祖先合成语义时继续复制整个组或先显式烘焙。
