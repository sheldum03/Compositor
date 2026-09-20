# Windows 实施记录

持续目标：完整完成 M0–M7、W-001–W-040，满足 P-01–19 和 V-01–12。当前 **M0 进行中**；未取得 Windows 验收证据，没有生产框架决策，没有 Windows 1.0 交付。

## 隔离与环境

- 工作区：`/Users/admin/.codex/worktrees/192b/Compositor`。
- 实施分支：`codex/windows-implementation`；从 detached `d562565e5f1c53a7ece4dd2d1f6c0ef3547a3c28` 建立。
- 首个实施提交：`8596b5c`（Restore Mac regression baseline and seed Windows compatibility fixtures），包含下述源码/测试/固定样本；后续记录提交仅维护证据。
- 整合目标：`windows-part`，同一源基线；后续审查实施提交后整合，不在此任务切换或修改原 main/规划工作区。
- 7 份 Windows 规划和 2 份研究文件从授权来源逐字复制，复制前确认无目标冲突；原始 SHA-256 见 `evidence/planning-source-sha256.json`。已读全部 7 份规划、适用 `/Users/admin/.codex/AGENTS.md` 及 `/tmp/compositor-windows-m0-handoff.md`。
- 本机：macOS 26.5.1 (25F80)、arm64、MacBook Pro Mac17,9、Apple M5 Pro、24 GB RAM、Metal Supported；Xcode 26.6 (17F113)。这些不代表 Windows 参考环境。
- 独立 DerivedData：`/tmp/compositor-windows-192b-derived`；Sparkle 2.10.0，锁定 revision `eef1a539a373c1f1a320624b1130fc5de7b2e100`。
- 初次构建因 sandbox 网络下载失败；正常申请执行权限后下载并继续。创建分支同样通过标准权限申请写共享 Git 元数据，没有绕过拒绝或修改原工作区。
- Windows 实机/干净虚拟机、Qt/.NET 工具链、签名/更新托管尚未确认。没有将 Mac 编译冒充 Windows 运行。

## W-001 / P-01–19 / V-03–06：恢复 Mac 参考

日期：2026-09-20（本地）。实际改动：

1. LayerTests 改为 NSDraggingInfo 输入真实 validateDrop/acceptDrop，保留选择不 reload、排序身份，增加一笔历史与 undo/redo；非法 UUID/负数与越界 row/忙碌时拒绝且文档历史不变。
2. SmartEditTests 显式传 FilterSettings()；只接受原有 noSubject，其他 Vision 错误不吞掉。
3. 新发现 SelectionTests 的光标接口漂移；验证所有选区图标/模式的热点与差异。M/L 测试按 `89dfb56` 已有“仅工具栏切换模式”更新，保留 repeated-key 不切换断言。
4. 全回归暴露 v7 断言、13 混合末尾模式、行底部剪贴、模糊扩展/裁边、移动吸附等旧测试预期。按已存在产品行为修正；移动测试用 Control 明确关闭吸附，未改产品默认；键盘测试显式构造字符，避免依赖当前系统输入源。
5. 真实像素缺陷：Levels Swift 重复执行 C 已完成的 alpha 转换；移除重复循环。SeparableBlend CI 默认线性工作空间造成 Dodge/Burn 偏色；显式 sRGB。原精确像素/0.02 混合误差断言不放宽。
6. 新增 3 个 D-11 测试：移动/旋转/翻转保留缓存，缩放重绘及 undo/redo；缺字体缩放替换缓存；Image Size 与 DPI-only 的元数据/历史差异。

复现与验证：

| 运行 | 结果 | 证据 |
| --- | --- | --- |
| w001-run2 | 新拖放替身缺 override，编译失败 | `/tmp/compositor-windows-192b-w001-run2.log` |
| w001-run3 | 原 SelectionTests.lassoCursors 接口不存在，编译失败 | 同前缀 run3.log |
| w001-run4 | 88 tests / 10 suites，24 issues；中文环境与旧快捷键断言 | 同前缀 run4.log/.xcresult |
| w001-run5 | 316 tests / 51 suites，17 issues；全面暴露过期预期及两处像素缺陷 | 同前缀 run5.log/.xcresult |
| w001-run6 | 316 tests / 51 suites，1 issue；模糊裁透明边后不一定比原图更宽 | 同前缀 run6.log/.xcresult |
| w001-run7 | FilterTests 4 tests 通过 | 同前缀 run7.log/.xcresult |
| m0-regression | **317 tests / 52 suites 通过，0 skipped**；参数展开后设备报告 339 runs | `evidence/m0-regression-summary.json`；`/tmp/compositor-windows-192b-m0-regression.log/.xcresult` |

完整成功命令：

```sh
xcodebuild -project Compositor.xcodeproj -scheme Compositor \
  -configuration Debug -destination 'platform=macOS' \
  -derivedDataPath /tmp/compositor-windows-192b-derived \
  -disableAutomaticPackageResolution -parallel-testing-enabled NO \
  -testLanguage en -testRegion US -only-testing:CompositorTests \
  -resultBundlePath /tmp/compositor-windows-192b-m0-regression.xcresult \
  CODE_SIGNING_ALLOWED=NO test
```

同一命令的前序运行更换 resultBundlePath/only-testing；全部输出重定向到同名前缀 `.log`。固定测试语言用于已有英文文本断言，未更改用户系统语言。`BrushPerformanceTests.fourKInteractiveStroke` 本身在 BRUSH_BENCHMARK 未设时提前返回，虽然测试工具计 passed，本轮**不算性能执行/达标**。没有执行 CompositorUITests、Windows GUI、真实输入法候选或发行构建。原有 Swift actor isolation 警告仍存在，未顺手改无关代码。

规则清单见 [baseline-rules.md](baseline-rules.md)。W-001 仍需补齐状态组合证据并跟随 D-11 决策冻结，不因单元回归通过直接关闭 M0。

## W-002 / P-03/05/08/10/12 / V-01/03/06：第一批固定样本

- 新增 WindowsFixtureTests；21 个真实 schema 工程，8 个版本特征样本和 13 个彩色半透明混合样本，21 份 Mac 参考 PNG；91 个 manifest/资产/参考文件，共 73,625 字节。
- v1–6 明确是 schema 重建，非历史应用输出。验证省略不属于该版本的字段；真实 PNG 资产、层级/蒙版/剪贴/调整/文本按阶段加入，不能只改空 manifest 版本。
- 写入/读回保留全部编码字段与逐资产像素；原始与读回离屏渲染一致；安装 EditorSession 后升级 v8 再保存/重开，元数据和合成像素一致。
- `w002-run1`：1 test 通过，0.508 s。命令同上，only-testing 改为 CompositorTests/WindowsFixtureTests，resultBundlePath 改 `/tmp/compositor-windows-192b-w002-run1.xcresult`。
- 固定输入见 [fixtures/README.md](fixtures/README.md)，冻结 hash 见 `fixtures/checksums.json`。Mac F08 参考图已目视检查有中文/英文/Emoji及半透明组合，图尺寸是小型正确性样本，非产品视觉/性能验收。
- 后补 frozenProjectsMatchTheirMacReferencePixels，独立读取入库样本与参考 PNG 逐像素比较；结果另记 `evidence/w002-frozen-summary.json`。
- `w002-frozen`：2 tests / 1 suite 通过，0 skipped；0.586 s，21 个固定工程均与参考像素一致。命令的 only-testing 为 CompositorTests/WindowsFixtureTests，结果为 `/tmp/compositor-windows-192b-w002-frozen.xcresult`。完整 317 测试之后只新增该冻结样本测试，最终共 318 个测试声明；未把单独执行说成又一次完整回归。
- 尚缺 F09–F12 扩展、真实笔划/CPU–Metal参考、Windows 修改保存后 Mac 重开，W-002 保持进行中。

## W-003/004 / D 决策和外部依赖

已向用户询问 Windows 11 x64 实机/远程方式/干净虚拟机；首发 OS/.comp 文件夹；团队语言/许可偏好；签名证书/更新源。另提交 D-11 提案：字体存在时按 Mac 缩放重绘，缺字体保留缓存直到明确安装/替换。尚未收到决定，不能把建议改写为用户确认。

当前可继续：补齐样本、状态测试、C ABI/stride/allocator 测试准备。Windows 原型的实机运行、同机性能比较和 GUI/IME 必须等待真实环境；不得跳过 W-007/008 的四条路径选择生产路线。

W-005–W-040 尚未验收，M1–M7 未通过。没有外部发布、购买、使用签名身份或发送消息。
