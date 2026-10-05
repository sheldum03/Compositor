# M3b 受限平面剪贴蒙版切片

更新日期：2026-10-06。

本轮在 `codex/windows-implementation` 工作树提交 `545ad1d`，补齐受限平面 v8 工程的合法 `maskSourceID` 编辑与合成路径。覆盖范围限定为同一画布、无组/调整层、源图层位于目标图层下方的剪贴关系：源图层的预乘 Alpha、透明度和上游剪贴关系参与目标覆盖率；源图层的可见性、RGB 颜色和混合模式不参与覆盖率。源图层删除和含剪贴关系的重排在当前切片明确拒绝，避免生成未定义的层序语义。

## 验证结果

- `Compositor.Workflow.Checks` Release 构建：0 警告、0 错误；13 个混合/工作流场景及新增剪贴蒙版场景通过。新增场景使用 2×1 双层工程，验证 Alpha 覆盖、源图层隐藏时仍参与覆盖、保存重开保持 `maskSourceID`，并验证删除源图层和重排含剪贴关系的图层会拒绝。
- `Compositor.App.Checks` Release 构建：0 警告、0 错误；现有正式窗口、蒙版笔刷、选区裁剪、压力覆盖、Undo/Redo 和关闭保护回归通过。当前 UI 尚未提供建立/释放剪贴关系的按钮，关系验证通过固定工程路径覆盖。
- `Compositor.Imaging.Checks` Release 构建：0 警告、0 错误；图像/Gray8 蒙版/PNG/JPEG 既有检查通过。
- `win-x64` self-contained 发布成功，产物目录为 `/tmp/compositor-win-x64-clipping-545ad1d`，224 个文件；`Compositor.App.exe` SHA-256 为 `2d8d86e6b3e256ce38089638e40c0f32f86c00d12a9de73b9b42024d2d7d47a7`。这只证明发布链路完成，不证明 Windows 启动或真机兼容。

上述证据均为 macOS arm64 Headless/本地构建；组蒙版、调整层、复杂 `maskPlacement`、完整剪贴关系 UI、真实 Windows 启动、原生 DLL、DPI/IME 和性能仍未验收。
