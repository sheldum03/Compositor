# M2 多层元数据编辑的真实 Mac 往返（2026-10-02）

固定输入来自 `24eff19` 生产工作流：`FlatEdited.comp` 中原顶层已移到最底部，改名 `Overlay` 并隐藏，另一层 `Image` 保持可见。真实 Mac `ProjectStore.shared.load` 检查 v8、两层资产、底到顶顺序、名称、可见性及活动层身份，再由实际 `ImageExporter.shared.render` 渲染。通过同一 `BrushRaster` 归一化后，与 C# 导出逐通道完全相同。

新增 `WindowsProductionWorkflowTests.productionFlatEditsSurviveMacRoundTrip` 随后在真实 `EditorSession` 内将隐藏层改名 `Mac Overlay` 并显示，确认合成画面发生变化；撤销显隐回原画面、再次撤销改名回到未修改保存点，然后重做两步并保存为 `MacFlatEdited.comp`。重开检查文档 ID、活动层 ID、两层 ID 与顺序、名称、可见性以及完整合成像素；分别比较每层全部源像素，确认隐藏层也未丢失。Mac 输出保留原始默认字段，没有为 C# reader 手工删改。

整套检查为 **4 个测试函数、6 次 case 执行、0 失败、0 跳过**，同时重跑两层 Normal、Gray8 蒙版及三个单层工程往返。见[测试摘要](production-flat-edit-mac-readback/suite-readback-summary.json)、[测试输出](production-flat-edit-mac-readback/test-output.txt)及[执行记录](production-flat-edit-mac-readback/execution.json)。初次编译因 Swift Testing 宏对 `allSatisfy` key path 的展开失败，未执行测试；改为普通谓词闭包后重跑退出 0，首次失败日志仍保留在原始目录，不计为通过。

[九文件归档](production-flat-edit-mac-readback/roundtrip-fixtures.zip)含原始 C# 工程与参考 PNG、Mac 保存后的原始工程和修改前后 PNG；[SHA-256](production-flat-edit-mac-readback/sha256.json)固定全部输入输出。原始目录为 `/Users/admin/.codex/visualizations/2026/10/02/production-flat-edit-mac-validation`。测试配置：

```sh
# 环境变量值须指向固定输入和不存在的新输出目录。
# TEST_RUNNER_WINDOWS_PRODUCTION_FLAT_EDIT_DIR=<解压目录>/inputs
# TEST_RUNNER_MAC_PRODUCTION_FLAT_EDIT_OUTPUT_DIR=<新输出目录>
# 同时设置既有 FLAT_DIR、MASK_DIR、WORKFLOW_DIR 三组测试输入。
xcodebuild test -project Compositor.xcodeproj -scheme Compositor \
  -configuration Debug -destination platform=macOS -parallel-testing-enabled NO \
  -only-testing:CompositorTests/WindowsProductionWorkflowTests CODE_SIGNING_ALLOWED=NO
```

本轮两端均在 macOS arm64 执行。Mac 修改后的工程仍需由生产 C# 再次打开、编辑和保存验证反向链路；Windows 实机、多层保存中断、逐层像素、图层增删/复制、组/蒙版及高级混合仍不由此证明。r4 包不含该多层代码，M2/M3 仍未整体验收。
