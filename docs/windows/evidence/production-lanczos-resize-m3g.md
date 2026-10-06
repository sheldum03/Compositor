# 内部 Lanczos 缩放证据（W-020）

日期：2026-10-06

实现提交：`6ad615f Add internal Lanczos resize path`

## 已实现范围

- 增加仅供 `Compositor.App.Checks` 使用的 `ResizeFilter.Lanczos3` 内部入口；生产窗口继续使用原有双线性图像缩放，没有宣称已经交付滤波器选择控件。
- 双轴缩小且显式选择 `Lanczos3` 时，RGBA 和 Gray8 蒙版都使用同一套可复现的 3-lobe、边缘夹取、归一化 separable taps。
- RGBA 在预乘空间插值，输出时限制 RGB 不超过 Alpha，避免透明边缘产生非法或发亮像素；Gray8 使用同一空间权重与舍入规则。
- 默认双轴缩小面积覆盖、上采样和混合轴双线性路径保持不变。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-lanczos-20261006-c
```

结果：Release 构建 0 警告、0 错误；App.Checks 三项生产检查全部 PASS。新增固定 4×4→2×2 场景逐像素检查透明边缘 RGBA 输出 `[[22,18,30,47],[53,121,65,213],[91,80,57,176],[44,45,109,144]]`，以及 Gray8 覆盖 `[46,200,55,196]`；既有双线性中心像素和面积覆盖检查继续通过。

以上是 macOS Headless 的内部算法证据，不能替代 Windows 原生缩放、DPI、性能和大图压力验收。窗口滤波器选择、Lanczos 上采样/混合轴策略、Lanczos 大图性能和真实 Windows 实机仍未交付。
