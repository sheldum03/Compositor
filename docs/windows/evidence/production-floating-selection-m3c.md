# M3c 浮动选区事务切片

更新日期：2026-10-06。

本轮在 `codex/windows-implementation` 工作树提交 `3fdaf2f`，把正式窗口的粘贴路径改为受控的浮动选区事务。粘贴只建立当前图层的临时预览，不修改工程像素、脏状态或历史；移动浮动选区只更新预览；提交时产生一个像素历史步骤；取消时恢复粘贴前的选区和工程像素。浮动期间保存、撤销/重做、图层结构、外观和普通绘制按钮被禁用，避免绕过提交/取消边界。

## 验证结果

- `Compositor.App.Checks` Release 构建：0 警告、0 错误；固定窗口检查通过粘贴不变脏、取消不留像素、移动不变脏、提交单步历史和提交后 Undo，并保留既有笔刷、选区、蒙版、关闭保护回归。
- `Compositor.Workflow.Checks` Release 构建：0 警告、0 错误；13 个混合/工作流场景及剪贴蒙版场景通过。
- `Compositor.Imaging.Checks` Release 构建：0 警告、0 错误；PNG/JPEG/Gray8 既有检查通过。
- `Compositor.SaveCrash.Checks` Release 构建：0 警告、0 错误；14 个单层/多层保存中断及恢复场景通过。
- `win-x64` self-contained 发布成功，产物目录为 `/tmp/compositor-win-x64-floating-3fdaf2f-v1`，224 个文件；`Compositor.App.exe` SHA-256 为 `30f4c71683d2bb2f2bb0f602f1c0180629c5ded10884ae414e4375e035eb5b1f`。这只证明发布链路完成，不证明 Windows 启动或真机兼容。

上述证据均为 macOS arm64 Headless/本地构建；浮动选区跨图层拖放、任意粘贴定位、组蒙版、完整 Alpha 组合、真实 Windows 启动、原生 DLL、DPI/IME 和性能仍未验收。
