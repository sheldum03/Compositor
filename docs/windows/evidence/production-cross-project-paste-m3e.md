# 同窗跨工程选区粘贴证据（W-018/P-05）

更新日期：2026-10-06。

实现提交 d720166 为同一个 Avalonia 窗口的工程标签增加受限跨工程选区粘贴：在工程 A 复制当前选区后切换到工程 B，若两个画布尺寸相同且 B 的活动目标是未变换平面图层，粘贴按钮会启用；粘贴仍先进入浮动选区，目标图层像素在提交前保持不变，取消浮动选区不会标脏。复制的快照由窗口级剪贴来源保存，关闭来源工程后会清除该来源，避免悬挂剪贴数据。

为了避免静默改变坐标语义，目标尺寸不一致、目标为组或目标含非恒等变换时按钮禁用；来源快照来自非恒等变换图层时也不跨工程粘贴。系统剪贴板图片、图层列表拖放和任意位置拖放仍不在本切片内。

## 验证结果

- Compositor.App.Checks Release 构建：0 警告、0 错误；同窗两个工程标签完成复制→切换→粘贴→浮动选区→取消，目标像素在取消前后保持一致，既有项目历史隔离回归通过。
- Compositor.Workflow.Checks Release 构建：0 警告、0 错误；缓存组、蒙版、变换、13 模式、读写和历史回归通过。
- Compositor.Imaging.Checks Release 构建：0 警告、0 错误。
- Compositor.SaveCrash.Checks Release 构建：0 警告、0 错误；14 个真实子进程保存中断场景通过。

本轮 App 输出目录为 /tmp/compositor-app-checks-cross-project-paste-20261006，其余输出目录为 /tmp/compositor-workflow-cross-project-paste-20261006、/tmp/compositor-imaging-cross-project-paste-20261006 和 /tmp/compositor-savecrash-cross-project-paste-20261006。这些仍是 macOS Avalonia Headless 证据，不是 Windows 真机验收。
