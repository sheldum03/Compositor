# Windows 原生算法 DLL

`compositor_native.dll` 是现有 C 算法桥接层的 Windows x64 构建产物。先在 Windows x64 工具链中构建 `windows/native/CMakeLists.txt`，再将 DLL 放到：

```text
windows/native/runtimes/win-x64/compositor_native.dll
```

使用 `dotnet publish ... -r win-x64` 时，`Compositor.App` 会在该文件存在时自动把它复制到发布目录根部；文件不存在时仍允许构建，但魔棒/轮廓功能会在运行时显示不可用，不能把缺 DLL 的包当作完整 Alpha 包。

建议的 Windows 构建命令：

```powershell
cmake -S windows/native -B build/windows-native -A x64
cmake --build build/windows-native --config Release --parallel 4
New-Item -ItemType Directory -Force windows/native/runtimes/win-x64 | Out-Null
Copy-Item build/windows-native/Release/compositor_native.dll windows/native/runtimes/win-x64/compositor_native.dll

dotnet publish windows/Compositor.App/Compositor.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o dist/win-x64
```

DLL 必须与发布包同一 x64 架构，并用固定导出表和 `Compositor.Smoke` 验证后才可进入 Alpha 验收。该目录不提交二进制；发布前记录 DLL 的 SHA-256、工具链和导出符号。
