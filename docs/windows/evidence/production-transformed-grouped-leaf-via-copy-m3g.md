# 非恒等组内平面层可见结果复制证据（M3g）

日期：2026-10-06

## 已验证范围

- 组内栅格层存在非恒等祖先组变换时，正式窗口仍允许执行“选区复制为图层”；渲染使用源层和祖先层级的完整可见结果，并把结果固定为文档坐标。
- 复制层作为默认 Normal 平面层插入最外层祖先组子树之后，避免再次应用源组的平移、缩放、旋转或翻转；源组的原始层级、变换和启用的 Gray8 蒙版保持不变。
- 选择区域外像素保持透明，复制像素、根级位置和源组蒙版在保存重开后保持不变。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages
dotnet build windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.Workflow.Checks/Compositor.Workflow.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-transformed-group-copy-20261006
dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-transformed-group-copy-20261006
```

两条 Release 构建均为 0 警告、0 错误。Workflow 检查逐像素确认祖先组外观/变换/蒙版已进入文档坐标结果、根级插入和保存重开；App 检查确认正式窗口按钮、组子树之后的根级插入、选区内外像素和保存重开。既有根组、嵌套组、组内平面层和组内连续剪贴栈回归仍在同一进程中通过。

## 明确边界

- 当前只放开栅格层及单层连续同父级剪贴栈；文字/调整层、多级 `maskSourceID`、跨父级关系和更复杂 Alpha 组合仍按 W-018 追踪。
- 真实 Windows 应用启动、文件对话框、DPI/多显示器、IME/候选窗、真实压感、系统剪贴板、性能和干净机部署尚未由本切片关闭。
