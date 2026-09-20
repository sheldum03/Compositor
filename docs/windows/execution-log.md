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

## 2026-09-21：W-002 扩展与 D-03 证据

继续前已核对 HEAD `417cde0`、工作区干净、既有规划/执行记录；上轮有产品修复、测试与样本提交，分类为实际进展，不是等待。未取得新的 Windows 设备或用户决策，不改变 M0/M1 门槛。

### 产物与行为

- `WindowsExtendedFixtureTests.swift`：14 份 F09/F11/F12 v8 工程及参考。F09 有嵌套/隐藏组、空层、形状、独立蒙版和禁用蒙版；F11 覆盖 72/300 DPI、点/框文本、三种对齐、tracking/line spacing、旋转/镜像/透明度；F12 保留缺字体的原缓存。保存/重开/再次保存比较全 manifest、资产、合成，并检查 liveText/liveShape 恢复。关联 P-03/05/06/08/11/12/13，V-01/03/06。
- `WindowsInvalidFixtureTests.swift` 与 `scripts/windows/generate-invalid-fixtures.py`：固定 14 份损坏工程并验证具体失败原因；完整列表见 fixtures/invalid/README.md。关联 P-03/19、V-01。仅证明读取拒绝，不扩大成 UI 保存安全证明。
- `WindowsBrushFixtureTests.swift`：同一 4K JSON 事件流强制 CPU/Metal 两后端，真实 session mouse-up 提交，验证未提前物化、立即下一笔、一次历史、图像身份/像素 undo/redo、工程重开、各自冻结参考复现。保存两端首笔/最终 PNG 和最终工程。关联 P-08/15/16、V-03/04/05；D-03 参考语义未定。
- 同输入两笔结果有 1,633,174 个 alpha 不同像素，最大差 7/255；没有据此设容差或宣称两后端等价。`scripts/windows/compare-brush-references.py`（本机 Pillow 11.3.0）可重现分项统计。
- Debug 每笔 append/mouse-up 原始计时保存在 evidence/timings-{cpu,metal}.json。无显示/输入到展示测量、仅两笔 40% opacity，**不符合 S02 Release/100%/30笔协议，不计性能通过**。
- 复核旧 B01–B13 发现 13 模式参考不宜都标历史 v3（历史格式说明只列前 9 种）。现统一声明 v8，仍保持最小两图层场景，PNG 未改变；F01–F08 各版本样本不变。这是样本来源准确性修正。

### 命令与结果

以下每条测试均使用前文同一 project/scheme、独立 DerivedData、Debug、en-US、禁止并行、CODE_SIGNING_ALLOWED=NO，stdout/stderr 写同前缀 `.log`。

| 结果前缀（/tmp/compositor-windows-192b-） | only-testing | 结果 |
| --- | --- | --- |
| extended-generate | 函数选择器缺 `()` | **0 tests**，不可算通过；已查 xcresult 并修正选择器 |
| extended-generate2 | `CompositorTests/WindowsExtendedFixtureTests/complexAndTextFixturesPreserveAllMetadataAndPixels()` | 1 test 通过；随后目视发现 300 DPI 框文本超出固定画布，调整样本画布 |
| extended-generate3 | 同上 | 1 test 通过；画布按旋转后完整边界加留白生成，此版才最终固定 |
| w002-expanded-final | WindowsFixtureTests、WindowsExtendedFixtureTests、WindowsInvalidFixtureTests、WindowsBrushFixtureTests | 6 tests / 4 suites，参数展开 7 runs，0 skipped；证据 `evidence/w002-expanded-summary.json` |
| brush-frozen | WindowsBrushFixtureTests | 1 test / 2 backend runs 通过，0 skipped；含实际重放对照冻结 PNG，`evidence/w002-brush-frozen-summary.json` |
| blend-schema | WindowsFixtureTests | 首次赋值违反 ProjectSnapshot.manifest 不可变约束，编译失败；修成构造新快照 |
| blend-schema2 | WindowsFixtureTests | B 参考声明为 v8 后 2 tests 通过，0 skipped；`evidence/w002-blend-schema-summary.json` |

例：

```sh
xcodebuild -project Compositor.xcodeproj -scheme Compositor \
  -configuration Debug -destination 'platform=macOS' \
  -derivedDataPath /tmp/compositor-windows-192b-derived \
  -disableAutomaticPackageResolution -parallel-testing-enabled NO \
  -testLanguage en -testRegion US \
  -only-testing:CompositorTests/WindowsBrushFixtureTests \
  -resultBundlePath /tmp/compositor-windows-192b-brush-frozen.xcresult \
  CODE_SIGNING_ALLOWED=NO test
python3 scripts/windows/generate-invalid-fixtures.py /tmp/compositor-invalid-fixtures-rebuilt
python3 scripts/windows/compare-brush-references.py
```

没有更改 Mac 生产源码。全部新增/变更测试已专项执行，不把专项执行汇总说成另一次全套回归。所有固定产物分目录记录 SHA-256：原组 91、extended 47、invalid 84、brush 9，共 231 个输入/输出文件均已验证；损坏样本脚本在独立 `/tmp` 重建出的 84 个文件与入库版本完全一致。未覆盖原规划工作区。

W-002 剩余：可分发的 TTC/字体冲突/损坏/重启资源与跨平台验证；更多文字/状态组合；Windows 往返。Mac `/System/Library/Fonts/Helvetica.ttc` 确实存在但不复制系统字体作为 Windows 分发资产。W-001 状态规则与 D-11 决策、W-003 产品确认、W-004 实机仍未完成。下一步继续处理可独立的字体资源/状态验证及 C 桥接实验准备，不能越过 M1 直接选型建生产 UI。

## 2026-09-21：W-006 本机 C 桥接准备（未验收）

从干净的 `19b3932` 开始，在 `experiments/windows/native` 新增独立 CMake 实验，直接链接 8 个现有 Rendering C 文件，未改 Mac 生产源码或 Xcode 工程，未选择 UI 框架。当前仍无 Windows 实机/干净 VM 或新产品决策；这是在外部门槛之前可独立验证的准备工作。

- 3 个适配函数将两个 legacy `long` 输出转换成 `int64_t` 并提供同库 `compositor_free`；统一 C++ `extern "C"` 声明。Mac dylib 导出检查确认 16 个原函数 + 3 个适配函数。Windows export-all/MSVC math 配置仅待验证，不算 DLL 导出检查完成。
- `contract_tests.cpp` 一个综合 C++ 测试实际跨共享库调用全部 8 个算法，检查 RGBA/Gray 不同 padding、packed coverage、预乘 alpha、Levels LUT/直方图、grain 整图/子块一致、noise 确定性、lens 边缘插值、fill 成功/无源、三种 heal 模式、wand 三个对角相接轮廓/空轮廓及分配方释放。Release 检查不会被 NDEBUG 关闭。
- `ffi_smoke.py` 使用 ctypes 显式参数/返回类型，验证 `int64_t[4]` 全宽写入、选择计数、轮廓计数和 1,000 次分配方释放。此项不是 C# P/Invoke 验证。
- CMake 3.31.6 仅安装到 `/tmp/compositor-windows-cmake-venv`；AppleClang 21.0.0，macOS arm64。命令和 Windows 待执行模板见 [实验 README](../../experiments/windows/native/README.md)。
- 首轮测试失败两处，均为测试假设错误：负畸变角点仍有部分采样覆盖；Create Texture 估计颗粒并非平色修复。根据现有算法语义修正断言，未放宽为只检查不崩溃、未修改算法。补充魔棒两行 stride 与空轮廓后重新执行最终测试。
- 最终 Release CTest **1/1 通过**，ASan+UBSan Debug CTest **1/1 通过**，未报告 sanitizer 错误；没有执行 LeakSanitizer、注入分配失败、100MP 压力或性能验收。原始日志与工具/源码 hash 见 `evidence/native-{release,sanitized}-macos.txt`、`evidence/native-probe-macos.json`。没有把这两次综合测试或 ctypes 计入 Swift 单元测试声明数。

W-006 尚需 Windows 编译/导出/C++/C# 实际运行、框架像素接入、同输入 Mac/Windows 输出差异；本次不满足 W-004/005/006 或 M1 的实机门槛。M0–M7 目标保持进行中。下一步继续未完成的 W-001 状态组合与 W-002 字体样本准备。

## 2026-09-21：W-002 / F12 字体样本与 Mac 进程恢复

开始时核对 HEAD `0f81ca9`、工作区干净；上轮提交 C 桥接实验与实际测试，属于进展。当前仍未取得 Windows 环境或 D 决策答复，继续完成可独立的样本准备。

- `scripts/windows/generate-font-fixtures.py` 用固定 fontTools 4.59.2 从自绘几何轮廓生成 TTF、CFF OTF 和两个不同 advance 的 TTC face；另有字节相同但改文件名的重复输入、相同 PostScript 名但改 advance 的有效冲突字体、空/损坏/截断/不支持扩展名。无复制系统或第三方字体轮廓，随仓库 MIT 许可提供。字体工具仅安装于 `/tmp/compositor-font-fixture-venv`。
- 固定资产在 `fixtures/fonts`，含 cases.json、许可证和 SHA-256。11 个受 hash 管理文件在独立 `/tmp/compositor-font-fixtures-rebuilt` 重建后逐字节相同。与生产内置思源字体分开，不将几何样本当成中英混排验收字体。
- `FontLibraryTests` 移除系统 Helvetica TTC 存在才执行的条件，换成必需的固定双 face 样本并验证各 face 字宽；新增固定 TTF/OTF 导入、跨文件名去重、明确错误类别、失败后目录不变和原字体度量未被冲突替换。注销本次测试注册后清理临时目录，不写用户字体库。
- `experiments/windows/font-library-probe.swift` 直接编译真实 FontLibrary.swift；仅为独立构建提供与 CompositorApp 相同的 localized 表达式。两个独立进程分别 import/restore：每个先验证 4 个名称不可用，再确认注册来源为持久化目录、字宽及选择器中都有对应 face。restore 不重新导入；3 个持久化文件与源字节相同。**这是库级进程恢复，不是完整 GUI 应用重启，更不是 Windows 验收**。
- 没有修改 Mac 生产源码；没有新增 Windows UI 或越过 M1 选型。

验证命令：

```sh
xcodebuild -project Compositor.xcodeproj -scheme Compositor \
  -configuration Debug -destination 'platform=macOS' \
  -derivedDataPath /tmp/compositor-windows-192b-derived \
  -disableAutomaticPackageResolution -parallel-testing-enabled NO \
  -testLanguage en -testRegion US -only-testing:CompositorTests/FontLibraryTests \
  -resultBundlePath /tmp/compositor-windows-192b-font-fixtures.xcresult \
  CODE_SIGNING_ALLOWED=NO test
xcrun swiftc -swift-version 6 -parse-as-library \
  Compositor/Document/FontLibrary.swift experiments/windows/font-library-probe.swift \
  -o /tmp/compositor-font-library-probe
/tmp/compositor-font-library-probe import docs/windows/fixtures/fonts /tmp/compositor-font-library-restart-192b
/tmp/compositor-font-library-probe restore docs/windows/fixtures/fonts /tmp/compositor-font-library-restart-192b
```

专项测试 **6 tests / 1 suite 通过，0 skipped，0 failed**（0.519 s）；xcodebuild 日志 `/tmp/compositor-windows-192b-font-fixtures.log`，摘要 `evidence/w002-font-summary.json`。两个探测进程 PID 不同且全部断言通过，原始结果及源码 hash 见 `evidence/w002-font-processes.json`。只执行字体专项，未声称重跑完整回归。

W-002 的字体资源缺口已补充；仍需完整应用/Windows 恢复、跨平台文本/工程往返及 D 决策，不能关闭 M0。下一步推进 W-001 状态组合证据；Windows 实机、输入法、双路线原型和后续发行门槛仍未执行。

## 2026-09-21：W-001 状态组合与保存语义证据

起点 `52cd680`，工作区干净；上轮固定字体与进程恢复是实际进展。本轮新增 `ProjectOperationStateTests.swift`，未改产品代码。关联 P-01/03/07/08/10/11/12/15/19、V-01/04/05；只证明覆盖到的 Mac 入口行为。

- 用真实 ProjectController 保存到唯一临时 `.comp` 路径，再用 ProjectStore 读回；比较全 manifest 和每个图像资产的解码像素。覆盖 5 类拒绝状态、transform/crop 保存处置，以及 gradient/HSV/filter/pixelMove 仍待提交时保存。
- 发现并确认四种预览状态的入口差异：canSwitch 拒绝，但 canStartProjectOperation 允许保存已提交文档并 markSaved，预览继续保留；渐变后来提交会重新变脏，Undo 恢复保存版本。这是待 Windows 状态策略解决的风险，未把它升级为产品期望。
- 验证变换跨项目提交只影响原项目历史、文本提交成功/超限失败的工具切换、NSWindow first-responder 变化引发的笔划/lasso 取消与保留。
- 第一轮 `state-matrix` 编译因测试使用不存在的 `.noise` 失败，修成实际 `.addNoise`；`state-matrix2` 6 tests 通过。补齐文本切工具成功/失败后，最终 `state-matrix-final` **7 tests / 1 suite，参数展开 18 场景，0 failed / 0 skipped**（0.114 s）。均为 Mac Debug；没有再次运行全部回归。

最终命令：

```sh
xcodebuild -project Compositor.xcodeproj -scheme Compositor \
  -configuration Debug -destination 'platform=macOS' \
  -derivedDataPath /tmp/compositor-windows-192b-derived \
  -disableAutomaticPackageResolution -parallel-testing-enabled NO \
  -testLanguage en -testRegion US -only-testing:CompositorTests/ProjectOperationStateTests \
  -resultBundlePath /tmp/compositor-windows-192b-state-matrix-final.xcresult \
  CODE_SIGNING_ALLOWED=NO test
```

日志 `/tmp/compositor-windows-192b-state-matrix-final.log`；摘要 `evidence/w001-state-matrix-summary.json`。first-responder 测试使用真实但未显示的 NSWindow；busy 分支由测试设标志。未执行菜单/面板、真实输入、应用退出、Windows 焦点/DPI，不能把本轮称作完整窗口验收。

基线矩阵已按实测更新，保留未验证项：关闭/退出确认、外部文件请求排队、floating transform、调整层编辑、跨窗口输入。W-001/M0 尚未关闭；Windows 实机与 D 决策的阻碍仍在，但本轮有新的可移交状态语义证据，目标保持进行中。
