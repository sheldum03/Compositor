# 非破坏变换图层复制证据（M3e）

日期：2026-10-06

功能提交：`e7ad710 Copy transformed normal layers as visible results`、`8a5d85d Copy non-normal layer pixels without baking appearance`

## 已实现范围

- 平面图层的“选区复制为图层”不再要求图层变换保持恒等，也不再限制源图层必须是 Normal 混合模式。
- 复制前按正式渲染顺序解析启用的全画布 Gray8 蒙版、连续剪贴源及其透明度，再应用当前图层的非破坏位移、缩放、旋转或翻转。
- 复制结果写入新的默认 Normal 像素图层，源图层自身的 opacity/blendMode 不烘焙进像素，仍使用当前选区覆盖率；组图层继续禁用。

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

结果：Release 构建 0 警告、0 错误；三个生产检查全部 PASS。新增回归将图层移动 `(18, 14)` 后全选复制，并逐字节比较复制像素与变换后的可见结果；另将源图层设为 `Multiply`、opacity `0.37`，确认复制结果仍为源图层自身像素而没有烘焙外观。

## win-x64 包记录

```sh
dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release -r win-x64 \
  --self-contained true --no-restore -o /tmp/compositor-win-x64-layer-copy-8a5d85d
```

- 文件数：224
- 入口：`Compositor.App.exe`
- SHA-256：`9ba6e04482342569636cd395e3de492104bfa2d793479e1923ac1a638d6969e3`
- `compositor_native.dll`：未包含

该结果关闭“非恒等变换平面层复制”和“非 Normal 平面层复制”切片；跨项目拖放、复杂 Alpha 组合和 Windows 原生验收继续按计划追踪。
