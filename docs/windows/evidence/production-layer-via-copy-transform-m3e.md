# 非破坏变换图层复制证据（M3e）

日期：2026-10-06

功能提交：`e7ad710 Copy transformed normal layers as visible results`

## 已实现范围

- Normal 平面图层的“选区复制为图层”不再要求图层变换保持恒等。
- 复制前按正式渲染顺序解析启用的全画布 Gray8 蒙版、连续剪贴源及其透明度，再应用当前图层的非破坏位移、缩放、旋转或翻转。
- 复制结果写入新的像素图层，仍使用当前选区覆盖率；组图层和非 Normal 混合模式继续禁用。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-transform-copy-20261006-b
```

结果：Release 构建 0 警告、0 错误；三个生产检查全部 PASS。新增回归将 Normal 图层移动 `(18, 14)` 后全选复制，并逐字节比较复制像素与变换后的可见结果。

## win-x64 包记录

```sh
dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release -r win-x64 \
  --self-contained true --no-restore -o /tmp/compositor-win-x64-transform-copy-e7ad710
```

- 文件数：224
- 入口：`Compositor.App.exe`
- SHA-256：`f0447d51feb6364e201ff625726c46fe949f3bf6ee0e123fbedbcba61c539cc9`
- `compositor_native.dll`：未包含

该结果只关闭“非恒等变换 Normal 平面层复制”切片；跨项目拖放、非 Normal 复制、复杂 Alpha 组合和 Windows 原生验收继续按计划追踪。
