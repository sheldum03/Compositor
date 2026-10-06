# 内部 Lanczos 缩放证据（W-020）

日期：2026-10-06

实现提交：`e850ed2 Expose Lanczos resize choice in image size dialog`

## 已实现范围

- 正式图像尺寸对话框提供“双线性 / Lanczos3”选择；画布尺寸对话框显示同一控件但禁用，避免把滤波器误用于不缩放的裁剪/扩展。
- 选择 Lanczos3 后，窗口通过 `ResizeFilter.Lanczos3` 内部入口执行正式图像缩放；默认选择仍为双线性。
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
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-resize-20261006-2
```

结果：Release 构建 0 警告、0 错误；App.Checks 三项生产检查全部 PASS。窗口检查打开图像尺寸对话框，选择 Lanczos3，将输出逐像素与独立 `EditorWorkspace.ResizeImage(..., ResizeFilter.Lanczos3)` 结果比较；既有双线性中心像素、面积覆盖和固定 4×4→2×2 RGBA/Gray8 检查继续通过。随后撤销恢复原尺寸和保存状态。

以上是 macOS Headless 的窗口与算法证据，不能替代 Windows 原生缩放、DPI、性能和大图压力验收。Lanczos 上采样/混合轴策略、Lanczos 大图性能和真实 Windows 实机仍未交付。
