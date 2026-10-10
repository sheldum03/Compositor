# 多子层剪贴栈合并证据（M3g）

日期：2026-10-06

回归提交：`344a5fe Cover multi-child clipping stack merge`

## 已验证范围

- 同一平面剪贴源下的两个连续剪贴目标可以一起选择并合并。
- 源层和两个子层可使用不同透明度及 `Multiply`、`Screen`、`Overlay` 外观；合并结果按正式渲染器逐 tile 保持一致。
- 合并后关系、透明度和混合模式归一化为单一默认 `Normal` 层，并验证 Undo/Redo、保存重开。
- 组、非连续关系、外部剪贴引用和更复杂的层级语义仍保持限制。

## 固定验证

环境：固定 .NET SDK 10.0.401、Avalonia 11.3.22、Avalonia Headless、无原生选择库。

```sh
export DOTNET_ROOT=/tmp/dotnet-sdk-root-401b
export PATH=/tmp/dotnet-sdk-root-401b:$PATH
export NUGET_PACKAGES=/tmp/compositor-nuget-packages

dotnet build windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-restore
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- \
  windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-checks-multi-clipping-m3g-20261006-a
dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release -r win-x64 \
  --self-contained true --no-restore -o /tmp/compositor-win-x64-multi-child-344a5fe
```

结果：App Checks 3/3 PASS，Release 构建与交叉发布均为 0 警告、0 错误；新增多子层剪贴栈场景验证预览、元数据、Undo/Redo 和保存重开。发布目录 224 个文件，入口 SHA-256 为 `5550e21768993c800da0ea0292010d19be50003d71bf36abb44357fb7498ff27`。

以上仍是 macOS Headless/交叉发布证据，不是 Windows 实机证据；发布目录不含真实 `compositor_native.dll`。
