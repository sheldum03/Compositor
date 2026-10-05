# 选区复制为图层证据（W-018）

更新日期：2026-10-06。

本轮在 codex/windows-implementation 的实现提交中加入 macOS 基线中的 Layer via Copy 切片：在无组、无蒙版、未变换的平面图层上建立选区，执行“选区复制为图层”会把选中像素原位写入当前图层上方的新平面图层，选区随事务清除；整个操作只产生一个工程历史步骤。没有选区时，既有“复制”仍复制整层。组、蒙版和非恒等变换不会被静默转成错误像素，按钮保持禁用。

同时修复了一个窗口状态缺陷：画布选区完成后，图层按钮现在立即重新计算启用状态，避免选区已经存在但 Layer via Copy 仍显示禁用。

## 验证结果

- Compositor.App.Checks Release 构建：0 警告、0 错误；真实 Headless 指针选区验证 Layer via Copy 按钮启用、原图层像素不变、新图层仅保留选区内像素、选区清除，以及 Undo/Redo 返回正确的层数和脏状态。
- Compositor.Workflow.Checks Release 构建：0 警告、0 错误；既有 v8 多层像素/结构、保存重开、蒙版、组、剪贴和 13 模式回归通过。
- Compositor.Imaging.Checks Release 构建：0 警告、0 错误；图像、蒙版、PNG/JPEG 与拒绝路径通过。
- Compositor.SaveCrash.Checks Release 构建：0 警告、0 错误；14 个真实子进程保存中断场景通过。

本轮使用的 App 检查输出目录为 /tmp/compositor-app-checks-layercopy-20261006-f，其余输出目录为 /tmp/compositor-workflow-layercopy-20261006、/tmp/compositor-imaging-layercopy-20261006 和 /tmp/compositor-savecrash-layercopy-20261006。这些仍是 macOS Avalonia Headless 证据，不是 Windows 真机验收。

## 尚未覆盖

跨项目拖放、图层列表拖放排序、带蒙版/剪贴关系的可见结果复制、非恒等变换图层复制、系统剪贴板图片导入和任意粘贴定位仍未完成；这些继续留在 W-018/P-05/P-07 的后续范围。
