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

## 2026-09-21：W-005/006 C# 桥接与 Windows CI 准备

起点 `d55ec9e`，工作区干净。前轮状态测试是实际进展。本轮重新核查平台条件：PATH 无 prlctl/qemu/VBoxManage/vmrun/dotnet/pwsh，未发现已安装的本地虚拟机应用；尚无用户提供的 Windows 设备连接。没有从远程控制应用的存在推定有可用设备。

新的可行路径：`gh repo view` 确认 origin 为公开仓库 `sheldum03/Compositor`、当前身份权限 ADMIN；Actions API 确认 enabled=true，现无工作流，远端无 `codex/windows-implementation` 分支。可通过标准 Windows Server 2025 x64 runner 验证原生桥接；它不满足 Windows 11 实机 GUI/IME/性能或无 SDK 干净安装机门槛。尚未推送、触发工作流、改主分支或创建 PR。

- `experiments/windows/dotnet-bridge`：固定 SDK 10.0.401，无 NuGet 包依赖，Cdecl 参数、size_t/nuint、固定 64 位输出、RGBA/Gray stride 与 packed coverage、托管数组固定及分配方释放；实际调用 8 个 C 文件的 17 个 FFI 入口。
- 官方 .NET release metadata 列出的 2026-09-08 SDK，归档 SHA-512 校验后解压至 `/tmp/compositor-dotnet-10.0.401`，不装系统 pkg。原生库复用此前 Release 构建，8 个 C 文件与 bridge 的内容未改。
- Mac Release `dotnet build` **0 warnings / 0 errors**；`dotnet run --no-build` 探测 **passed**，.NET 10.0.12、arm64，轮廓分配/库内释放 1,000 次。证据 `evidence/pinvoke-macos.json`，版本/命令/hash 与验证范围 `evidence/pinvoke-preparation.json`。
- `.github/workflows/windows-native-probe.yml`：限定隔离分支上相关路径的 push；固定 actions SHA、SDK、CMake；MSVC x64 构建、C++ CTest、DLL 导出表、ctypes、C# PInvoke；失败仍保留日志与已有产物，14 天留存，20 分钟任务时限，contents:read。另有 workflow_dispatch 声明，但首次不依赖尚未注册到默认分支的手动 dispatch。
- 下载并核对 actionlint 1.7.12 发布校验值后运行工作流检查，**退出 0，无诊断**。这只验证工作流静态结构，不代表 PowerShell/MSVC/Windows 步骤已经执行。

本机执行命令（工作目录 `experiments/windows/dotnet-bridge`）：

```sh
DOTNET_CLI_HOME=/tmp/compositor-dotnet-cli DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 /tmp/compositor-dotnet-10.0.401/dotnet build -c Release
DOTNET_CLI_HOME=/tmp/compositor-dotnet-cli DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  /tmp/compositor-dotnet-10.0.401/dotnet run -c Release --no-build -- \
  /tmp/compositor-windows-native-release/libcompositor_native.dylib
```

完整构建日志在 `/tmp/compositor-pinvoke-build.log`。没有运行 Mac 全套测试，因为本轮只增加隔离 C# 实验和工作流，未修改 Mac 产品源码。

下一步具体外部动作是推送当前隔离分支（包括既有未公开实施提交）并触发 Windows CI；仓库及上传的测试日志/二进制将公开。先完成本机验证和可审查提交，再请求此次公开推送/运行的确认。CI 若成功，只登记相应 Windows Server 桥接证据；W-004/W-006 全范围、双框架原型和 M1/M7 门槛仍逐项验证，不能扩大成 Windows 1.0 完成。

## 2026-09-21：M0 准备边界复核与可信回归统计

起点 `4a12ca0`，工作区干净。前轮 C# 探测通过并提交工作流，属于实际进展。公开推送确认未收到，本轮无远端写入。

按原 development-plan.md 的 W-001/W-002 退出条件复核后，确认此前把后续完整窗口/Windows 验收也持续计入了“Mac 准备未完成”。现纠正任务状态：W-001 基线盘点/回归/规则与 D-11 事实证据就绪，W-002 固定样本就绪；W-003/W-004 仍未满足，M0 继续未通过。没有删减任何 PRD/V 门槛，逐项依据在 [m0-readiness.md](m0-readiness.md)。

本次整合检查所有新增 fixture/font/state 测试，另核验五组固定文件共 **242 个 / 11,008,078 字节** 的 hash 和已声明大小，均一致。文件身份核验不等于 Windows 像素兼容，结果见 `evidence/m0-fixture-integrity.json`。

整合回归还纠正了测试报告的问题：三个手动诊断入口在未启用时直接 return，曾被计 passed。除之前已说明的 BrushPerformanceTests 外，还有 BrushIntersectionTests.exportCrossingExample、LevelsTests.panelPreview；此前只标明第一项不完整。三者现改为 `.enabled(if: …)` 条件，未启用时真正报告 skipped；功能测试和诊断主体未改。此行为依据 Swift Testing [ConditionTrait](https://developer.apple.com/documentation/testing/conditiontrait)，本机 xcresult 也已验证。

| 运行（/tmp/compositor-windows-192b-） | 范围与结果 |
| --- | --- |
| m0-integrated | 修改条件前完整 CompositorTests：框架 330 passed / 0 failed / 0 skipped；其中 3 个诊断提前返回，不计实际执行 |
| benchmark-disabled | 首个条件修正专项：0 passed / 0 failed / 1 skipped，确认工具不再假报性能执行 |
| m0-freeze | 三个条件全部修正后完整 CompositorTests：**327 passed / 0 failed / 3 skipped**；56 suites、330 声明；参数展开 361 passed / 3 skipped；52.532 s |

最终命令：

```sh
env -u BRUSH_BENCHMARK -u LEVELS_PREVIEW xcodebuild \
  -project Compositor.xcodeproj -scheme Compositor -configuration Debug \
  -destination 'platform=macOS' -derivedDataPath /tmp/compositor-windows-192b-derived \
  -disableAutomaticPackageResolution -parallel-testing-enabled NO \
  -testLanguage en -testRegion US -only-testing:CompositorTests \
  -resultBundlePath /tmp/compositor-windows-192b-m0-freeze.xcresult \
  CODE_SIGNING_ALLOWED=NO test
```

日志与 xcresult 同前缀，最终摘要 `evidence/m0-freeze-summary.json`。显式清除两个可选开关只作用于该命令，没有修改用户全局环境；没有执行性能或两项人工诊断，没有运行 CompositorUITests。

现不再无上限扩充 Mac 样本。依 technical-design.md“未决项不应阻止样本准备与可逆原型”，后续可做 W-007/W-008 的有限源码准备；选型和完成仍依赖 Windows 四路径实测。公开推送、D 决策及 Windows 环境尚未确认，自动续跑不构成授权，整体目标保持未完成。

## 2026-09-21：W-008 初始 Avalonia 绘制与工程往返准备

起点 `45799d7`。新增 `experiments/windows/avalonia`，未修改 Mac 产品源码或原生 C 算法，也未推送或触发 CI。本轮在已有固定样本上验证一部分实际跨实现数据路径，不扩大成 M1 通过。

- 固定 SDK 10.0.401、Avalonia.Headless/Skia 11.3.22、SkiaSharp 2.88.9 及完整 NuGet lock。官方 feed 还原；`--locked-mode` 重放成功，Release **0 warnings / 0 errors**。第一次编译发现 Skia feature 查询 API 的泛型扩展不可用，改用实际 `TryGetFeature(Type)` 后通过。
- `FixtureScene` 限定固定工程子集：13 混合、opacity、整数位移/缩放、穿透组及继承可见性；unsupported 字段拒绝。保存只允许复制到新目录，保留 JSON 其余值及 PNG 原字节，旧支持样本写成 v8。没有把它当完整安全 reader 或替换恢复实现。
- `SceneControl` 通过真实 `ICustomDrawOperation` 获取 Skia canvas，与离屏导出共用绘制函数。关闭 headless 的假绘制后端，断言实际回调发生。**16/16 控件预览与导出逐像素一致**；重命名→C# 重开→导出也全部一致。
- 两次核对固定基础 corpus 的 91 个 hash；F04–F08 拒绝。显隐继承、重复 ID、失效父节点、资源路径、未来版本、负透明度、未支持变换/未知字段、坏 PNG 和已存在目标保护检查通过。尚未验证完整 F10 拒绝类别或活动文档状态。
- Mac 参考差异按相同 sRGB 预乘 RGBA8 比较：F01/F02/F03/B02/B05/B08 完全一致；其余 10 个 B 样本最大通道差 **1/255**，最大平均绝对通道差 **0.2496744792/255**（B11）。不自行设定新容差或把这些观测计作兼容通过。
- 新增有显式环境条件的 `WindowsFixtureTests.avaloniaRenamedCopiesReopenWithOriginalPixels`。真实 Mac ProjectStore/ImageExporter 读回 C# 写出的 16 个包，完整 manifest 仅有预期 rename/version/resolution 改动，PNG 字节及 Mac 合成像素不变。该 suite **3 passed / 0 failed / 0 skipped**，0.753 s；没有重跑完整回归。无输出目录环境变量时，新测试明确 skipped，不改变此前 M0 完整回归记录。

本机命令与运行路径：

```sh
# cwd: experiments/windows/avalonia
DOTNET_CLI_HOME=/tmp/compositor-dotnet-cli DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  NUGET_PACKAGES=/tmp/compositor-nuget-packages \
  /tmp/compositor-dotnet-10.0.401/dotnet restore --locked-mode
DOTNET_CLI_HOME=/tmp/compositor-dotnet-cli DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  NUGET_PACKAGES=/tmp/compositor-nuget-packages \
  /tmp/compositor-dotnet-10.0.401/dotnet run -c Release --no-restore -- \
  ../../../docs/windows/fixtures /tmp/compositor-avalonia-probe-01
```

Mac 读回从仓库根运行 xcodebuild，设置 `AVALONIA_ROUNDTRIP_DIR` 和 `TEST_RUNNER_AVALONIA_ROUNDTRIP_DIR` 均为上述输出目录；其余使用 M0 的 Debug/macOS/en-US/关闭并行/禁签名参数，`-only-testing:CompositorTests/WindowsFixtureTests`。日志 `/tmp/compositor-windows-192b-avalonia-roundtrip.log`，结果包同名前缀 `.xcresult`。

入库证据 `evidence/avalonia-macos.json`、`avalonia-roundtrip-summary.json`（移除设备 ID）、`avalonia-preparation.json`（输入/源码及本机产出摘要 hash）。此次只证明 Mac → C# → Mac 的受限往返和 CPU 控件绘制，不证明 Windows、原生窗口、IME、GPU、全组合渲染、笔刷局部提交/撤销、资源和延迟门槛。W-007 尚待同样本准备；W-008/M1 仍未完成，D-02 未选型。

## 2026-09-21：W-008 蒙版、剪贴栈与调整组合

起点 `3b916cd`，工作区干净。上轮有已提交原型及可复现结果，属于实际进展。本轮继续原定受限路径，未推送、未运行 Windows、未进入生产目录。

核对 `ImageExporter`、`LiveMaskRenderer`、`FolderMaskClip`、`HueSaturationFilter` 和 `PixelAdjust` 后，增加 F04–F07：8-bit Gray 蒙版按 coverage 解码而非色彩转换；同父连续剪贴栈先保存 base alpha、不透明化后合成子层、恢复一次 alpha；组蒙版在栈外应用。C# 直接复用已有 `Native.cs`，调用原 `BrushPixels.c` 的 extract/unpremultiply/restore 三函数。借用 Skia 自有 RGBA 内存，managed alpha 数组使用 fixed；没有修改 C 源码或 ABI。

当前调整子集仅为 Normal、clipped、legacy master desaturation（hue/lightness=0、colorize=false、saturation −100…0，无调整层自有蒙版），用 33³ HSL cube 的八角插值和 opacity 混合。其余调整、非连续链接、隐藏 base 上的可见剪贴层、独立 maskPlacement、文本/形状继续拒绝，不能据 F07 宣称全调整实现。

首次 `combination-01` 控件/导出与保护检查通过，但 F06/F07 的 Mac 最大通道差为 2/255。逐通道追踪发现 Skia `DstIn` 的整数量化与基准不一致：`73×97/255` 得 27，rounded /255 应得 28。原输出有 **1,041 像素**不满足精确的组 alpha 乘法公式。改为 `(value * coverage + 127) / 255`，将此前允许一个量化单位的组 alpha 测试收紧为精确比较；没有放宽 Mac 容差。

最终 `combination-02` 的结果：

| 验证 | 结果 |
| --- | --- |
| locked NuGet restore / Release build | 成功；0 warnings / 0 errors |
| 真实 Avalonia control → 离屏 export | **20/20** 逐像素一致；每个 specimen 实际回调 1 次 |
| rename → 新目录 → C# 重读 | 20/20 完整 manifest 仅预期修改，所有 image/mask PNG 原字节与渲染不变 |
| 新增 11 个组合断言 | 禁用层/组蒙版、alpha 不变、组 mask 精确只乘一次、identity/零 opacity/全去饱和、拒绝自环/不支持调整/RGBA mask 均通过 |
| Mac 像素参考 | F01/F02/F03/F05/F06/B02/B05/B08 精确；其余 12 个最大差 1/255，无像素超过 1 |
| F04/F07 剩余误差 | 14/256 个像素，平均绝对通道误差 0.0011393229 / 0.0403645833；F04–F07 alpha 全部精确 |
| Mac reader/exporter 重开 | WindowsFixtureTests **3 passed / 0 failed / 0 skipped**，0.800 s，涵盖 20 个返回工程 |

Mac 测试输入是 `combination-01`；整数合成修正只影响临时渲染，不改保存数据。已逐字节核对 `combination-01` 与最终 `combination-02` 的 **64 个 package 文件完全一致**，因此读回证据适用于最终保存输出。没有重复全量 Mac 回归；新完整测试结果不替代既有 M0 整合记录。

命令沿用实验 README，第三参数现必须提供 native library：

```sh
# cwd: experiments/windows/avalonia
DOTNET_CLI_HOME=/tmp/compositor-dotnet-cli DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  NUGET_PACKAGES=/tmp/compositor-nuget-packages \
  /tmp/compositor-dotnet-10.0.401/dotnet run -c Release --no-restore -- \
  ../../../docs/windows/fixtures /tmp/compositor-avalonia-combination-02 \
  /tmp/compositor-windows-native-release/libcompositor_native.dylib
```

Mac 测试从仓库根运行 M0 的 xcodebuild 参数，`TEST_RUNNER_AVALONIA_ROUNDTRIP_DIR=/tmp/compositor-avalonia-combination-01`，限定 `WindowsFixtureTests`；日志/xcresult 前缀 `/tmp/compositor-windows-192b-avalonia-combination`。原始 probe 日志与目录同名加 `.log`。输入 corpus 91 个 hash 在运行前后均一致。

证据 `evidence/avalonia-combination-{macos,summary,preparation}.json`；新增每张参考的 RGB/alpha 差分 PNG。`scripts/windows/render-probe-contact-sheet.py`（Pillow 11.3.0）生成并实看 F04–F07 三列对照：Mac / Avalonia / 32 倍误差，RGB 红色、alpha 蓝色；入库 `avalonia-combination-contact-sheet.png`。未见新的透明边接缝，F05/F06 差分为空，F04/F07 仅稀疏 RGB 差异。这是模型的诊断检查，不是用户或 Windows 实机视觉验收。

本轮推进了小型蒙版/剪贴/调整组合及工程往返的源码准备。仍无同机 Qt 比较、真实 Windows GUI/IME、笔刷瓦片提交/下一笔/撤销、GC/内存/延迟记录；全画布 scratch 分配不满足后续笔刷性能约束。未确定新的兼容容差、未选型；W-008/M1 与完整 Windows 1.0 目标保持未完成。

## 2026-09-21：W-008 4K CPU 笔刷、局部提交与历史

起点 `6e992b8`，工作区干净。上轮组合渲染有实际提交与证据，本轮继续原计划第二条原型路径。没有推送或运行 Windows，也没有把未确认的 D-03 当作选择 CPU 算法。

新增 `SoftBrushStroke` / `TiledRaster` / `BrushProbe`：使用冻结的两笔各 121 点、800 px、0 硬度、40% opacity 输入，移植 Mac CPU 的 Catmull–Rom、2.5% spacing、24-stop Gaussian、临时 tail 恢复与整笔 opacity 上限。只支持本阶段未变换、无选区的彩色软笔；未扩展到全部画笔工具。每次更新复合脏区，每次提交只克隆改动瓦片，未触及瓦片共享。没有在 pointer update、mouse-up 或立即下一笔时调用全幅导出/物化。

`SceneControl` 现在接收实际绘制委托，合成和笔刷两条现有调用共用 Skia lease 入口；每个固定 pointer update 真正绘制一张 1000×1000 CPU frame，最终另做 4000×4000 preview/export 精确比较。Mutable managed tiles 通过 native image copy 保证绘制生命周期，不借出会继续写入的指针。副作用是复制量和 GC 成本显著，不能宣传零拷贝或生产就绪。

| 最终 `avalonia-brush-02` | 第一笔 | 第二笔 |
| --- | ---: | ---: |
| touched tiles / commit copy bytes | 92 / 24,117,248 | 106 / 27,787,264 |
| commit（含最终曲线 flush） | 10.0766 ms | 8.6196 ms |
| append + CPU preview P95 | 12.9989 ms | 12.9547 ms |
| vs Mac CPU 最大 RGBA / alpha 差 | 3 / 3 | 4 / 4 |
| vs CPU 差值 >1 的像素 | 28,962 | 47,834 |

共 **242 个实际 control draw**；第二笔共享 56 个旧瓦片。提交前 full-raster export 计数均为零。整笔 40% alpha cap、历史身份/字节不变、undo/redo、取消保留 redo、新编辑替换 redo、空笔不记历史、重复 flush、已提交对象拒绝写入均通过。额外用相同曲线/dab 原语直接重放完整路径，完全不画 provisional tail；两笔结果都与交互路径逐像素一致，验证 tail 恢复/发布状态。该对照不证明共同原语本身与 Core Graphics 等价。

两笔 preview 累计向 native images 复制 **6,442,713,088 bytes**；gen-2 GC 分别 36 / 11 次。更新/预览期间采样 working-set 高水位 **240,910,336 bytes**，不包括之后的独立正确性重放与全幅导出/解码，也不是 peak private RAM 或 VRAM。所有原始 append/preview 样本、copy/GC 计数保留在报告。两笔、40% opacity、headless CPU framebuffer 不是 S02：尚无 100% opacity/空层与已有层/30 笔/真实 Windows 输入到显示测量；不得用 Mac Release 数字对既有 Mac Debug 样本做选型排名。

与 Mac CPU 的 3–4/255、与 Metal 的最大 7/255 差异仍未接受容差。独立 `macos-tip-diagnostic.swift` 重建 private tip 的 Core Graphics 构造，两次 640,000-byte 输出一致；对比纯径向像素中心采样，143,468 个 tip 值差 1；CG tip 本身有 150,388 个水平镜像样本不相等、最大差 2。观察与 backend quantization/dithering 一致，但没有证明这是所有最终笔刷差异的唯一原因。C# 未使用任何 Mac 生成笔尖作为输入。差分图集中于软边衰减带，模型检查未见明显 tile 网格接缝或旧直尾；不算人类 Windows 视觉验收。

`WindowsBrushFixtureTests.avaloniaBrushPackageReopensWithExportedPixels()` 新增显式环境条件，用实际 Mac reader/exporter 读取 C# `brush.comp`、检查导出像素，再保存重开；**1 passed / 0 failed / 0 skipped，0.551 s**。未设置变量时明确 skipped。测试输入 `brush-01`，已证明最终 `brush-02` 的 manifest/asset/first/final PNG 共 4 文件逐字节一致。现阶段 package 使用全画布稀疏网格的 PNG，未实现收紧栅格边框。

共享绘制入口变动后重跑原 20 个 compositor 样本，结果报告与 `combination-02` 语义完全相同。最终 locked restore / Release build 0 warnings / 0 errors。没有重跑整个 Mac 测试套件，Mac 产品源码和原 8 个 C 文件未改。

复现命令见 `experiments/windows/avalonia/BRUSH.md`。本机运行路径：

```sh
# cwd: experiments/windows/avalonia
DOTNET_CLI_HOME=/tmp/compositor-dotnet-cli DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  NUGET_PACKAGES=/tmp/compositor-nuget-packages \
  /tmp/compositor-dotnet-10.0.401/dotnet run -c Release --no-restore -- --brush \
  ../../../docs/windows/fixtures/brush /tmp/compositor-avalonia-brush-02 \
  /tmp/compositor-windows-native-release/libcompositor_native.dylib
```

Mac 测试使用 `TEST_RUNNER_AVALONIA_BRUSH_DIR=/tmp/compositor-avalonia-brush-01`，结果日志/xcresult 前缀 `/tmp/compositor-windows-192b-avalonia-brush`。probe 原始日志与目录同名加 `.log`。入库 `evidence/avalonia-brush-{macos,summary,preparation}.json`、`avalonia-brush-contact-sheet.png`。通用 contact-sheet 脚本增加 `--brush`；其原 mask 模式重建输出与既有 PNG 字节一致。

本轮完成一条受限 4K CPU 工作流的本机准备，不是 Windows W-008 完成。仍需框文字共享布局/IME、同机 Qt 对照、完整 Windows 性能/资源测量及 D 决策，后续 M2–M7 范围保持不变。

## 2026-09-21：Avalonia 共享文字布局与合成输入准备

起点 `211fb2e`，工作区干净。继续 W-008 的第三条受限路径，没有推送、Windows 执行或生产 GUI 选型。新增 `--text`，读取固定 F11 的 12 种样式；原 Mac 产品代码、工程数据和文字缓存未改。按现有 layer rectangle 放置重新布局结果仅用于实验，不作 D-11 的缩放/缺字重绘策略决定。

核对 Avalonia 11.3.22 pinned source 后，确认默认 TextPresenter 不传附加行距、TextParagraphProperties.LineSpacing 为 internal。小型 `SpacedTextPresenter` 通过公开 TextBlock.LineSpacing 创建独立布局，TextPresenter 接管其生命周期；TextBox 保留输入、选择和撤销逻辑。导出控件绘制编辑器当前 TextLayout 同一实例；不是再次排版。使用项目链接嵌入仓库既有 Source Han Sans SC，并复制既有 OFL 通知；hash 和中文 glyph 显式校验。实际 emoji 回退为本机 Apple Color Emoji，未当作 Windows 字体可用性证据。

新增断言发现并修复两处实验问题：TextBox 默认裁剪而导出未裁剪，导致一个低 alpha 的 emoji 边缘像素不同；按折叠选择位置拆分无样式 runs，导致取消预编辑后整形/像素变化。分别统一裁剪边界、保留完整无样式文本源。编辑/导出和取消恢复仍要求精确像素一致，没有放宽图像误差。

最终 `text-13`：

| 检查 | 结果 |
| --- | --- |
| locked restore / Release build | 0 warnings / 0 errors |
| 共享布局的编辑画面/导出 | 12/12 精确且非空，真实导出 draw 已发生 |
| 旋转/翻转/缩放后的光标命中 | 2,631 个局部/文档坐标样本一致，无 surrogate/combining cluster 内部位置 |
| 附加行距与字距 | +3/−3 对第二行的位移精确；1.25 字距改变 advance；修改字距使布局失效 |
| 合成预编辑/取消/提交/撤销/重做/选择替换 | 12/12 通过；preedit 不写入 committed Text；取消后像素精确恢复 |
| 输入客户端光标 | preedit 中光标对应同一布局的正确位置，四角通过 visual tree 映射与 layer matrix 一致 |
| 既有合成回归 | 20 个样本通过；报告与 combination-02 的已存证据完全相同 |
| 输入文件冻结 | 242/242 固定文件 hash 不变；字体许可已复制到输出 |

逆变换最初 `1e-8` 像素的几何断言过严：6 个 box 样本的逆矩阵 M33 为 `0.9999999999999999`，框架判断为 perspective，走 float 路径；最终记录最大局部坐标误差 `0.00008544921865905053` px。几何阈值明确为 0.001 px，仍要求实际 caret index 精确且落在 grapheme boundary；这不是 Mac 图片容差。

12 份 Mac 参考均不精确：差异 4,760–61,446 像素，最大 premultiplied 通道差 133–166/255；未接受容差。接触图可见行度量/字形位置与 emoji 差异；不能据此认定唯一原因。原工程 PNG 保持不变，不拿重新排版覆盖缓存。原生 Windows IME 候选窗、焦点生命周期、DPI、字体安装/移除、文字 draft 尺寸与事务保存仍未验证。

本轮使用真实框架输入客户端，但输入是 `SetPreeditText` 和 routed TextInput 的合成调用，未创建原生窗口，也未调用系统中文输入法。没有新的 Mac product tests 或全量回归；不替代 M0 的既有回归记录，也不勾选 W-008/M1。

复现命令见 `experiments/windows/avalonia/TEXT.md`。本机最终命令为：

```sh
# cwd: experiments/windows/avalonia
DOTNET_CLI_HOME=/tmp/compositor-dotnet-cli DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  NUGET_PACKAGES=/tmp/compositor-nuget-packages \
  /tmp/compositor-dotnet-10.0.401/dotnet run -c Release --no-build -- --text \
  ../../../docs/windows/fixtures/extended /tmp/compositor-avalonia-text-13 \
  /tmp/compositor-windows-native-release/libcompositor_native.dylib
```

日志与输出目录同名加 `.log`；build log 为 `/tmp/compositor-avalonia-text-build.log`。最终 60 张 PNG 与已视觉检查的 `text-11` 输出逐字节相同。入库 `evidence/avalonia-text-macos.json`、`avalonia-text-preparation.json`、`avalonia-text-contact-sheet.png`；同一接触图脚本增加 `--text`，旧 mask 模式输出仍与已存图一致。公开推送与 Windows 环境问题尚未收到用户答复，未据自动继续视作授权。

## 2026-09-21：Qt 合成与工程往返对照准备

起点 `bafbcce`，工作区干净。上一轮提交和文字证据属于实际进展，本轮推进 W-007 对照，不改变 M0/M1 门槛，不推送。官方 Qt 6.11.2 的 qtbase-only Mac universal SDK 已通过 aqtinstall 3.3.0 安装在 `/tmp/compositor-qt-6.11.2`；未改系统 Qt 或 Homebrew。官方 archive SHA-1 已独立核验，另记录 SHA-256；工具依赖、SDK/SBOM 和实际链接清单见 preparation 证据。D-07 仍未作发行选择。

新增 `experiments/windows/qt`，C++17/Qt Widgets/CMake3.31.6，链接既有 native 子目录的原 8 个 C 文件与 bridge。每次导出与 QWidget paintEvent 都重新执行 CPU 合成，然后绘制新 QImage；不是读取导出 PNG 当预览。QPainter 实现 9 个分离混合；缺少的 Hue/Saturation/Color/Luminosity 用 W3C 方程补齐，包含 opaque red/green、neutral、transparent source/backdrop 的解析断言。

F04–F07 的 Gray mask、同父连续剪贴、组覆盖与有限 clipped desaturation 采用与 Avalonia 同样的规则；alpha extract/unpremultiply/restore 实际调用 C。所有 QPainter 在原始像素修改前结束；QImage 写时分离有保留图像断言。Smooth 与 High quality 暂都映射 QPainter 平滑采样，尚无三个独立采样实现。原型全幅临时缓冲仅用于 64×48 样本，不宣称完成局部瓦片或 4K 路径。

最终结果：

| 检查 | 结果 |
| --- | --- |
| Release 构建 | AppleClang21，`-Wall -Wextra -Werror`，通过 |
| 实际 QWidget 画面/离屏导出 | 20/20 精确、每份 paintEvent 1 次、输出非空 |
| PNG encode/decode | 20/20 premultiplied RGBA8 原值不变 |
| 最小重命名另存与 Qt 重读 | 20/20；完整 manifest 仅预期字段变动；64 个 package 文件包含的素材字节均保留；拒绝覆盖原目的地 |
| Mac 参考 | F01/F02/F04/F05/F06 精确；其余 15 最大通道差 1/255；全部 alpha 精确，未接受容差 |
| 11 个拒绝检查 | 未知版本/字段、重复 ID、路径越界、缺父节点、非法 opacity、rotation、text/F08、截断 PNG、RGBA mask 均拒绝 |
| 组合与算法断言 | 蒙版启停、剪贴 alpha、组覆盖精确只应用一次、调整 alpha/identity/零 opacity、隐藏层、copy-on-write、四个非分离混合的解析样例均通过 |
| native contract CTest | Release 与 sanitizer 各 1 test passed；不是重复计数两个相同 test |
| ASan/UBSan | 原型 C++ 和原 C 均插桩，全部场景通过；Qt 预编译库未插桩、leak detection 关闭，不能证明无泄漏 |
| 实际 Mac ProjectStore/ImageExporter | WindowsFixtureTests **4 passed / 0 failed / 0 skipped**，1.112 s；含 Qt 与 Avalonia 各 20 个返回工程 |

非分离 B10/B11/B12/B13 分别有 1,149/1,408/1,156/1,408 个差异像素，最大 1/255；并未将观察转成通过阈值。接触图中 F04–F06 完全相同、F07 少量 RGB 差异条带，无 alpha 差异；模型查看不替代人类 Windows 验收。

Mac 测试将原 Avalonia readback 函数提取为共享 helper，并增加 `QT_ROUNDTRIP_DIR` gated test；原 manifest/assets/pixels 断言保持。两个环境变量均实际设置，本次没有用 skipped 充数。生产 Mac 代码和 native C 未改，没有重跑完整 Mac suite。

本机 Release 目录 `/tmp/compositor-qt-probe-release`，ASan/UBSan 为 `/tmp/compositor-qt-probe-sanitized`。最终输出 `/tmp/compositor-qt-compositor-03`，日志同名 `.log`，整体资源日志为 `-resources.log`；Mac readback 的日志/xcresult 前缀 `/tmp/compositor-windows-192b-qt-roundtrip`。Mac 测试输入 `qt-compositor-02`；已核对最终 03、02 和 sanitizer 的 **124 个 PNG/package 文件逐字节一致**。Qt 报告中的 timing 不要求跨运行相同。

最后一次整进程 Mac `time -l` 记录 0.24 s wall、最大 RSS 20,791,296 bytes；包括所有微型样本和 guards，不是 Windows private RAM/VRAM 或 S04/S05。不得拿它与 Avalonia 4K 笔刷测量排名。复现命令和全部限制见 `experiments/windows/qt/README.md`；入库 `evidence/qt-compositor-{macos,preparation,summary}.json` 与 contact sheet。

Qt 尚缺软笔提交/下一笔/undo 工作流和变换文字/真实 IME；两个方案还都缺 Windows 同机验证与最终部署。W-007、W-008、M1 保持未通过，后续 M2–M7 及全部 PRD/发布目标不缩减。

## 2026-09-21：Qt 4K 软笔、局部提交与历史对照准备

起点 `97892a6`，工作区干净。新增 Qt `--brush`，复用已冻结的两笔 4K 事件（每笔 121 点、直径 800、hardness 0、opacity 0.4）。C++ 实现与 C# 相同的 Gaussian tip、Catmull–Rom、临时笔尾覆盖恢复与整笔透明度上限；未改 native C 或 Mac 产品代码。本轮是 W-007 可逆准备，不推送、不选型。

256×256 QImage 瓦片仅在改变时提交复制，未改瓦片通过 Qt implicit sharing 保留；笔中 coverage/original/pixels 独立拥有。真实 QWidget paintEvent 直接同步绘制瓦片，每笔 121 次、1000×1000 视口 25% 缩放；两笔提交后才执行全幅导出和 4K 预览精确对照。记录应用显式 preview 像素复制为 0，不将 Qt 内部分配算作已测量或不存在。

最终 Release `compositor-qt-brush-02`：

| 检查/观察 | 结果 |
| --- | --- |
| 局部 commit | 第一笔 92 tiles / 24,117,248 bytes，第二笔 106 tiles / 27,787,264 bytes；共享 56 个未改 tiles |
| commit 耗时 | 7.155042 / 8.219416 ms，仅两笔样本 |
| append + preview P95 | 9.740917 / 10.735667 ms；242 次真实离屏 widget 绘制 |
| 笔尾 oracle | 两笔均与不生成临时笔尾的 settled replay 像素一致；共享曲线/dab 原语，非独立算法证明 |
| 历史与状态 | 一笔一次 history、旧 snapshot 不变、undo/redo identity、cancel 保留 redo、分支替换 redo、空提交、重复 flush、active/finished guard 全通过 |
| 4K 预览/导出与 Qt package 读回 | 首笔/最终像素精确且非空，第二笔确实改变图像；首笔 alpha ≤102 |
| Qt / Avalonia | `first.png`、`final.png` 的 premultiplied RGBA8 均精确相同；复现公式与 canonical pixel hash 已记录 |
| Mac CPU 参考 | 第一笔差异 1,122,405 pixels / max 3，最终 1,875,766 / max 4；alpha max 同值，容差未接受 |
| Mac Metal 参考 | 第一笔差异 1,340,829 pixels / max 7，最终 2,269,671 / max 7；容差未接受 |
| Release / sanitizer | warnings-as-errors 构建通过；ASan/UBSan 自有 C++/C 插桩通过，Qt 未插桩、leak detection 关闭；9 个 PNG/package 文件与 Release 逐字节相同 |
| Mac 实际 reader/exporter | Qt 与 Avalonia 两个 gated readback tests：2 passed / 0 failed / 0 skipped，1.138 s；保留原 manifest/尺寸/分辨率/transform/pixels/save/reopen 断言 |
| 原合成回归 | 20 个样本通过，去除逐样本 timing 后报告与既有证据相同；Release native CTest 1 passed |
| 固定样本 | 242/242 文件 hash 不变 |

全进程 `time -l` 记录 7.29 s wall、452,689,920 bytes maximum RSS，包含正确性重放、全幅导出、PNG decode/diff 与工程读回。Avalonia 旧值只采样 updates 阶段，不能比较内存高低，也不是 Windows private RAM/VRAM 或 S05。本场景 0.4 opacity / 两笔不满足 S02 的 100% opacity / 30 笔及 blank/existing-layer 要求。模型检查接触图可见柔边差异带、未见明显网格接缝或旧笔尾；不替代 Windows 人工验收。

本机 Release/sanitizer build 目录沿用上轮；最终输出 `/tmp/compositor-qt-brush-02`，日志同名 `.log`，资源记录同名前缀 `-resources.log`。Mac xcodebuild 的日志/xcresult 前缀 `/tmp/compositor-windows-192b-qt-brush`，Qt 输入为 brush-01；已核验其 9 个产物与最终 brush-02/sanitizer 相同。Mac helper 提取复用未降低原断言，未重跑全量 Mac suite。入库 `evidence/qt-brush-{macos,summary,preparation}.json` 与 contact sheet；详细命令见 `experiments/windows/qt/BRUSH.md`。

Qt 仍缺变换文字/共享布局/native IME，对照双方都缺 Windows 同机输入、性能资源与部署验证。W-007/W-008、M0/M1 保持未通过，M2–M7 与全部 PRD/发布要求不缩减。未收到公开推送或 Windows 环境的用户答复，未把自动继续视作授权。

## 2026-09-21：Qt 变换文字、共享布局与合成输入准备

起点 `a5168f4`，工作区干净。上一轮 Qt 软笔提交属于实际进展，本轮继续 W-007 文字路径。新增 `--text`，使用可编辑 QGraphicsTextItem 内部的 QTextDocument；Scene paint 与 export.drawContents 使用同一布局，不重复排版。CMake 引用嵌入既有 Source Han Sans SC，复制原 OFL 通知；不安装字体，不改 Mac 产品、native C、工程 manifest 或文本 PNG 缓存。D-11 编辑/缩放策略未决。

12 个 F11 样式覆盖 72/300 dpi、点/框文字、三种对齐、中文/英文/emoji/combining accent、附加行距/字距、13° 旋转与水平翻转。自然布局按已有 layer rectangle 适配仅是实验放置约定。Qt 原有编辑控件处理合成鼠标 press/release 和 QInputMethodEvent；不自建输入引擎。

本轮断言发现并解决三个实验问题：默认长度读取 document-backed QTextLayout 的 glyphRuns 得到空列表，改传显式 block text length；未激活视图返回空输入光标查询，改走 offscreen view.show/activate/focus + event processing；300 dpi 左对齐框的 preedit 导出漏裁剪，造成 4 个边缘像素不同，给 drawContents 传入与 item 一样的 boundingRect 后精确通过。未加图片容差、未动 framework 私有 API。

最终 Release `/tmp/compositor-qt-text-08`：

| 检查 | 结果 |
| --- | --- |
| 构建 | Qt 6.11.2 / AppleClang21 / warnings-as-errors 通过；resource font hash/OFL 副本已核验 |
| 共享画面/导出 | 基线与 preedit 各 12/12 精确，实际 item paint 已执行，输出非空 |
| 变换鼠标命中 | 2,631 个合成事件样本；actual editor caret 与 document hit 相同，无 grapheme split；逆映射 max 2.97e−13 px（阈值 0.001 px） |
| 框宽变化 | 6/6 缩窄后增加换行、画面/导出仍精确，恢复宽度后原像素恢复 |
| 合成输入 | 12/12 preedit 不进入 committed text/undo；cancel 像素恢复；commit/undo/redo/selection replacement 通过 |
| 视图光标查询 | 激活的离屏 QGraphicsView 在 0.75×/1×/1.5×/2× 共 48 次，preedit query 包含图层变换、视图缩放与滚动偏移 |
| 实际字体 | Source Han Sans SC + .Apple Color Emoji UI；后者是本机回退，不视为 Windows 字体可用 |
| Mac 参考 | 全部非精确，差异 4,720–51,784 pixels，最大通道差 133–166/255；未接受容差 |
| ASan/UBSan | 自有 C++/C 插桩通过，预编译 Qt 未插桩、leak detection 关闭；72 PNG 与 Release 逐字节一致 |
| 已有路径回归 | 20-case 合成通过，去掉 timing 后报告与已存证据相同；brush 9 个 PNG/package 文件字节不变；native CTest 1 passed |
| 样本身份 | 242/242 文件 hash 不变 |

视图缩放不是操作系统 monitor DPI，offscreen activation 不是 Windows 激活/输入法候选窗验收。本次没有原生鼠标/键盘/中文输入法、剪贴板或多显示器。共享导出只含文字/preedit 内容，光标和选择装饰不属于导出；事务保存、缺字/缺字体缓存、字体冲突与导入仍在后续范围。

`time -l` 全进程观察 1.50 s wall、127,680,512 bytes maximum RSS，包含字体、所有案例、PNG/diff、合成输入与离屏视图激活；非 Windows private RAM/VRAM 或长期资源测试。Qt 在初始化 generic Sans Serif 别名时有诊断日志，实际 glyph run 字体已单独记录。接触图可见行度量、字形位置与 emoji 差异，不能以视觉相近代替像素验收。

详细命令与 pinned 官方源码链接见 `experiments/windows/qt/TEXT.md`。日志 `/tmp/compositor-qt-text-08.log`、`-resources.log`；sanitizer 前缀 `/tmp/compositor-qt-text-sanitized`。72 张 PNG 与已检查 Release 07 和 sanitizer 相同，入库 `evidence/qt-text-{macos,preparation}.json`、contact sheet。Mac 产品代码未改，无新增 XCTest/全量 suite 运行；不能更新旧全量通过数。

Qt 与 Avalonia 的四条受限路径目前都有本机准备，但 Windows 同机执行、真实 IME、性能/资源、部署和产品决策仍缺。W-007/W-008、M0/M1 保持未通过；M2–M7、全部 PRD/发布目标不缩减。未收到公开推送授权或 Windows 访问答复，未推送或触发远程 CI。

## 2026-09-21：W-009 原生 AI 推理与可编辑蒙版筛查

起点 `5fed5b7`，工作区干净。上轮 Qt 文字提交属于实际进展；本轮推进 W-009 的真实模型路径。新增 `experiments/windows/ai` 独立 C++17 / ONNX Runtime 1.30.0 CPU 原型，不依赖 Qt/Avalonia，不选 GUI 方向。Mac arm64 SDK 放在 `/tmp/compositor-ai-screening`，archive SHA-256 与 GitHub 官方 release digest 一致；native library、SDK source commit、MIT/第三方 notices/Privacy 文件 hash 均记录。Windows x64 SDK 的官方 URL/大小/digest 仅作候选元数据，未下载或运行。

模型为 rembg 官方 release 提供的 4,574,861-byte U2NetP ONNX；SHA-256 已冻结，MD5 与 pinned adapter 一致。图为 PyTorch1.9 / IR6 / opset11、1,055 nodes、13 类标准算子，没有 external data/custom domain/local function。原架构仓库 Apache-2.0、rembg MIT 不自动解决该转换权重的单独分发许可；尚未核实，不提交 SDK 或权重，不替用户作 D-08 决策。预处理参考代码的 MIT notice 已保留。

使用 scikit-image v0.25.2 的 NASA 公有领域 astronaut 样图（512×512），记录来源/原图 hash。Python 仅作模型检查、RGB Lanczos/归一化/NCHW float32 准备、PNG/工程诊断输出；真正推理通过 C++ API，关闭 telemetry，没有图片上传或自动模型下载。模型输出第一张 saliency 经 rembg 同样 min/max + Gray8 + Lanczos 还原为原尺寸，原图和蒙版分别存入真实 v8 `.comp`。

最终结果：

| 检查/观察 | 结果 |
| --- | --- |
| 原生 Release 构建 | CMake3.31.6 / AppleClang21 / warnings-as-errors 通过 |
| 实际 CPU 推理 | 一个 `[1,3,320,320]` float32 输入、七个实际 `[1,1,320,320]` float32 输出；全部 finite 且在 `[0,1]` |
| 稳定性/执行后端 | 三次预测 bitwise 相同；恢复运行同值；profile 中 1,344 kernel events 均为 CPUExecutionProvider |
| 取消范围 | 预先 SetTerminate 的 Run 被拒绝，UnsetTerminate 后 session 恢复；没有声称测过正在执行中的中断 |
| 拒绝路径 | 短 tensor、NaN、缺模型、损坏模型均无最终 mask；已有输出拒绝且保留 |
| 蒙版 | Gray8 512×512：108,712 零覆盖、395 满覆盖、153,037 中间值 |
| Mac 可编辑性 | 实际 ProjectStore/ImageExporter 的 gated test 验证 alpha 与 mask 一致、关闭 mask 恢复原图、保存重开保持独立图与 mask |
| ASan/UBSan | 自有 C++ 插桩、完整 harness 通过；预编译 ORT 未插桩，leak detection 关闭 |
| 稳定产物 | 初始/最终 Release 与 sanitizer 的 9 个 tensor/raw-mask/PNG/package 产物字节相同；资源运行 raw mask 同值 |
| 既有样本 | 242/242 固定文件 hash 不变；Mac 产品与原 native C 未改 |

独立原生资源运行 `/tmp/compositor-ai-native-metrics`：session load 45.439125 ms，三次推理 160.088750 / 163.463125 / 102.949459 ms，全进程 0.66 s wall、maximum RSS 665,567,232 bytes（约 635 MiB）。它包含 profiling、四次成功推理和 pre-termination，排除 Python 图像处理、GUI 与 Mac test，不是 Windows private RAM/VRAM 或长期压力指标。模型文件约 4.4 MiB 不代表推理内存很小。

接触图实看主体/头盔被保留，左边旗帜/背景仍残留，发丝和边缘不够精确。没有标注真值/多图质量集，不能选定模型或宣称与 Apple Vision 等价。Basic/Advanced 后处理、已有蒙版组合、活跃取消、事务/历史与 Windows 部署继续保留在 M6。

最终完整筛查目录 `/tmp/compositor-ai-run-02`，原生资源目录如上，sanitizer `/tmp/compositor-ai-run-sanitized`；各主日志同名前缀 `.log`。最终 Mac test 是 `WindowsFixtureTests/aiMaskPackageRetainsEditableCoverage()`：**1 passed / 0 failed / 0 skipped**，0.090 s，输入为 run-02；日志/xcresult 前缀 `/tmp/compositor-windows-192b-ai-mask-02`。测试先确认 mask 是 512×512 单通道 8-bit，再读取 coverage；未设 AI_SCREENING_DIR 时明确 skipped。未重跑全量 Mac suite。来源、命令与许可边界见 `experiments/windows/ai/README.md` / `assets.json`。入库 `evidence/ai-u2netp-{macos,native-metrics,summary,preparation}.json` 和 source/mask/cutout 接触图。

本轮证明一条本机原生推理与可编辑蒙版数据路径存在，未解决权重授权、HEIC、Windows 实机或最终发行门槛。W-009、M0/M1 保持未通过，后续 M2–M7 和 PRD 范围不变；未推送或启动远程 CI。

## 2026-09-21：W-009 HEIC 原生解码与独立目录搬迁准备

起点 `6531bd5`，工作区干净。上一轮 AI 推理/蒙版提交属于实际进展；本轮完成 HEIC 候选路径的本机准备。官方 libheif1.23.4、libde2651.1.1 源码包下载至 `/tmp/compositor-heic-screening`，SHA-256 均与 GitHub release published digest 独立核对一致。源码未修改；两库从源码编译成 shared library，不安装系统包，不依赖用户额外安装系统 HEVC codec。

新增 `experiments/windows/heic`：C++17 探针、libheif codec cache 配置、Mac-only 样本生成器和 Python 核验脚本。只启用 libde265 HEVC decoder，关闭 runtime plugin loading 和其他外部 encoder/decoder。运行枚举揭示 libheif 必留内部 mask encoder；探针精确断言一个 libde265 decoder、一个 mask encoder、零 HEVC encoder，没有把它记成“无编码器”。未用 x265。上游 heifio 构建发现的 host PNG/TIFF/zlib 等没有进入实际 staged linkage，未分发其静态 helper。

ImageIO 实际编码自有非方形颜色/透明度几何像素，CoreImage 按 ImageIO 报告的方向归一化并生成 sRGB 参考。初始 6 例通过后扩展为所有 EXIF orientation1–8 × opaque/alpha，共 16 例；不是用 PNG 改扩展名。新增冻结数据 33 份（16 HEIC + 16 参考 PNG + cases.json），此前 242 份 hash 全不变，合计 275 份数据。编码结果可能随 OS 变化，固定 hash 不因后续重生成而自动更新。

最终结果：

| 检查/观察 | 结果 |
| --- | --- |
| 原生构建 | CMake3.31.6 / AppleClang21；自有探针 warnings-as-errors；两库 Release 和 sanitizer 均成功 |
| 依赖诊断 | Debug 依赖构建有 4 条上游 sprintf deprecated warning；未屏蔽、未改上游源码 |
| 实际 HEVC 解码 | 16/16、原方向 96×64/旋转 64×96、镜像/旋转正确、严格模式无 warning、straight RGBA8/stride 已断言 |
| alpha | 16/16 与 Mac 参考精确；透明/不透明 metadata 一致 |
| RGB | 每个 opaque 样本 4,608 差异像素、alpha 样本 2,304；premultiplied max 1/255，alpha max0，未接受容差 |
| 文件名 | 中文路径重放与原旋转透明样本像素一致，仅证明 Mac 路径处理 |
| 错误路径 | 截断、PNG、缺文件、100-pixel budget 均拒绝且不发布输出目录；旧输出拒绝并保留 |
| 搬迁 | 独立目录包含探针、libheif、libde265、COPYING；probe RPATH 改 @loader_path、刷新本地 ad-hoc 签名后移动目录；DYLD trace 确认两库从搬迁目录加载，无 Homebrew/旧 prefix；完整 16 例通过 |
| ASan/UBSan | 自有探针和两库全部源码插桩；完整 harness 通过；system libraries 未插桩，leak detection 关闭 |
| Release/sanitizer 一致性 | 50 个 raw/PNG 产物字节一致（17 raw decodes、32 result/diff PNG、contact sheet） |

原生单样本资源观察（64×96 透明旋转图）read/decode/copy0.458833 ms、全进程0.06 s wall、maximum RSS3,424,256 bytes；这不代表大图性能、Windows private memory/VRAM 或长期资源行为。contact sheet 实看所有方向、镜像与透明带相符，RGB 为细小均匀差异；模型检查不代替 Windows 人工验收。

16 份样本的 raw ICC bytes 都为0，不能据此宣称 ICC 分支或任意 ICC 转换通过。HDR、10/12bit、NCLX 变体、只有 EXIF 没有 container transform 的方向、多主图、辅助图、相机 tiled HEIC、DPI、大图/恶意输入与取消继续属于 W-031/V-07。未跑 Mac app XCTest/全量回归，生成器的 ImageIO/CI 参考与应用测试明确区分；Mac 产品代码和旧 C 文件未改。

两库库代码头均为 LGPL-3.0-or-later，COPYING hash 与 staged 副本记录在证据。可用动态库+对应源码/构建说明作为分发候选，但最终包依赖/义务、codec 专利与 D-07 决策未完成；“能解码”或“动态链接”不等于发行放行。未提交第三方源码或二进制。

复现见 `experiments/windows/heic/README.md`。Release 目录 `/tmp/compositor-heic-run-02`、sanitizer `/tmp/compositor-heic-run-sanitized`；移动后 stage `/tmp/compositor-heic-relocated`；两源码 builds/logs 使用 `/tmp/compositor-heic-*` 前缀，单样本资源 `/tmp/compositor-heic-metrics-resources.log`。入库 `evidence/heic-{macos,preparation}.json` 与 `heic-contact-sheet.png`。

W-009 的 AI/HEIC 都已有真实本机路径，但 Windows clean-machine、发行选择/授权及 M1 其余门槛仍未满足。W-009/W-031、M0/M1 不勾选，M2–M7 与全部 PRD/发布目标不缩减；未推送或触发远程 CI。

## 2026-09-21：W-005/008 Windows CI 接入已有 Avalonia 三组探针

起点 `8cd2d0b`，工作区干净。上一轮 HEIC 提交属于实际进展。本轮修补已有 `.github/workflows/windows-native-probe.yml` 的执行缺口：此前只运行 C/C++、ctypes 和 C# P/Invoke，未包含后续完成的 Avalonia 原型。没有新增产品入口或作出框架选型。

工作流在 native/PInvoke 检查后执行固定 SDK 的 locked restore 和 Release build，再分步运行 20 个合成/工程往返样本、两笔 4K 笔刷和 12 个文字/合成输入样本，共用该作业生成的 Release DLL。Avalonia build 成功后，各探针使用显式 `!cancelled()` 条件，前一探针失败不阻止后续两组且 job 仍失败；取消时不启动后续检查。路径过滤补齐 Avalonia 源码、固定样本与嵌入字体/许可，30 分钟作业超时；失败时保留全部输出、日志、锁文件与 managed build 目录，沿用 14 天 artifact。构建目录是诊断包，不宣称可分发或可搬迁安装包。

验证：actionlint **1.7.12 exit0**；Mac 上相同 dotnet argv 的 locked restore / Release build / 三组执行均 exit0，build **0 warning / 0 error**，lock hash 未变。20 个合成、242 次笔刷 custom draw 和 12 个文字样本通过。与上次输出逐字节比较：合成 PNG/工程资产 **144** 文件、笔刷 **9** 文件、文字 PNG **60** 文件一致；不比较波动的计时/内存字段。目录 `/tmp/compositor-avalonia-ci-preflight-01` 保存逐步日志和 JSON；入库摘要 `evidence/avalonia-ci-preparation.json` 记录工作流/依赖锁/报告 hash。这是 CLI 预检，PowerShell 管道和 Windows 运行尚未执行；未改 probe 或 Mac 产品源码，未重跑 Mac app XCTest。

复现和 artifact 用法写入 `experiments/windows/avalonia/README.md`。将来 Server runner 的 headless 结果也不能代替 Windows 11 参考机、真实 IME/DPI/GPU 或干净机安装。Qt、AI、HEIC 尚未接入此工作流。本轮未推送、未调远程 CI；W-004/W-008、M0/M1 与发布目标保持未通过。

## 2026-09-21：W-005/007 Qt Windows SDK 阻断修正与 CI 接入

起点 `b00bc1f`，工作区干净。上一轮 Avalonia CI 接入是实际进展。本轮核对 Qt Windows 路径时发现真实问题：aqtinstall3.3.0 的架构列表与指定 MSVC dry-run 都请求不存在的 `qt6_6112/qt6_6112/Updates.xml`，返回404。官方6.11.2 Windows仓库已按 MSVC/MinGW 分目录；未升级/修改第三方安装器或降低 Qt 版本，改由新增 `experiments/windows/qt/windows-sdk.json` 固定官方 MSVC qtbase URL/大小/hash。

实际下载 **39,618,573 bytes** 官方 archive，SHA-1 与官方 sidecar 独立匹配，SHA-256 为 `fd984b7264361b4dd3fd2a417702ca1258e4086268f2ee6a69b9a393d9c3f6bb`。在 Mac 用已有 py7zr1.0.0 解压检查4731个 archive entries；Core/Gui/Widgets、qtpaths/rcc、qoffscreen 的 PE 头与 import table 实查为 x86-64，记录各自 hash。确认 CMake imports 使用相对安装前缀；Windows ICU/MSVC/系统图形依赖列入证据，尚未实际加载 Windows DLL。该 SDK 没有提交到仓库或作为发行包发布。

共享 Windows workflow 新增同一 job 内的 Qt SDK 校验/解压/qt.conf、qtpaths 前缀自检、MSVC Release build、native CTest 和合成/工程往返、4K笔刷、共享文字三组检查。Qt bin/native Release PATH、插件目录、offscreen/scale1 显式设置。Qt build 通过后，各检查不因前一探针失败而被跳过，失败仍使 job 失败；Avalonia 失败不压掉 Qt 的后续准备。作业上限40分钟，artifact新增 Qt PNG/工程、日志、CTest XML、exe/native DLL、字体notice、CMakeCache、SDK manifest/文件hash和官方SBOM；不包含 Qt SDK DLL，不宣称便携安装包。未引入另一个 GPU 后端或作出框架选型。

验证：actionlint1.7.12 exit0；全新 Mac build 目录 `/tmp/compositor-qt-ci-preflight-01/build` 配置/Release构建通过，native CTest **1 passed / 0 failed**，三组探针全部 exit0。20合成、242笔刷更新、12文字样本及既有断言实际执行。**205 个** PNG/工程文件与既有最终结果字节相同（124合成、9笔刷、72文字），计时/内存不纳入精确比较。`qt-ci-preparation.json` 保存步骤/文件hash/Windows SDK imports/限制；完整本机日志和SDK在 `/tmp/compositor-qt-ci-preflight-01`、`/tmp/compositor-qt-windows-sdk`。Mac 产品和探针源码不变，未跑 Mac app XCTest/全量套件。

PowerShell、Windows 7-Zip、qtpaths.exe、MSVC及Windows探针仍未执行。本机解压检查与Mac编译不能替代它们；未来Server runner的headless结果也不能代替Windows11参考机/IME/DPI/GPU/干净安装。W-004/W-007、M0/M1及M2–M7发布目标保持未通过。未推送或触发远程CI。

## 2026-09-21：W-009 AI/HEIC 原生 Windows 参数编码修正

起点 `a20bc89`，工作区干净。上一轮 Qt CI/SDK 修正属于实际进展。本轮在准备 AI/HEIC Windows 运行入口时发现窄参数编码假设：两支探针原用 `main(char**)` 后对路径调用 `fs::u8path`，不能保证 Windows 当前代码页给出的 argv 是 UTF-8。这是源码与 Microsoft CRT 文档核对得到的缺陷，不声称已在 Windows 复现。

两支探针在 `_WIN32` 下改为 `wmain(wchar_t**)`，直接构造 native `filesystem::path`，给文件流和 ORT 的 Windows wchar_t API 使用；POSIX 继续 main/native argv。ORT 自身返回的 profile 文件名有 UTF-8 契约，其 u8path 不变。未引入参数转换库、系统 locale 修改或生产公共模块。

AI harness 新增同一次执行中的中文/空格/non-BMP Emoji 模型文件、输入张量、输出目录重放：raw prediction 与普通路径精确相同，CPU profile 实际存在且 kernel provider 计数同为1344；二次运行拒绝旧输出并保持其 mask，复制的模型/张量 hash 不变。HEIC 将原中文输入用例扩展到同类字符的输入与输出目录，显式传入 pixel budget；方向6透明图像素精确，已有输出拒绝/保留、输入 hash 保持。

Release 和 ASan/UBSan 各执行两支完整 harness，**4 builds + 4 runs 全部 exit0**。AI 自有 C++ 插桩，预编译 ORT 未插桩；HEIC 自有 C++ 和此前构建的两库均插桩；leak detection关闭。与此前最终输出比较：AI原9个产物及新增Unicode raw mask精确；HEIC50个raw/PNG精确（旧unicode路径与新路径显式对应）。全部275份固定数据 hash 仍通过。未改图像算法、输入样本、模型或容差。

证据 `evidence/native-unicode-preparation.json`，日志/产物 `/tmp/compositor-native-unicode-01`。本轮使用HEIC build-prefix libs，未替换此前搬迁目录中的旧探针；旧证据作为历史记录保留。未改 Mac 产品，未跑 app XCTest/全量回归。

Windows wmain 分支尚未由 MSVC 编译或执行，不能用本机 UTF-8 成功冒充 Windows 路径验收。此入口修正在 AI/HEIC CI 接入之前完成；这两条 CI 路径仍待接入，未推送或触发远程运行。W-009、M0/M1 与全部 M2–M7/发布门槛保持未通过。

## 2026-09-21：W-005/009 AI/HEIC Windows 可行性 CI 接入

起点 `47b5d7d`，工作区干净。上一轮 Unicode 入口修正属于实际进展。本轮新增 `.github/workflows/windows-feasibility-probes.yml`，AI/HEIC 使用独立 Windows Server matrix job，fail-fast关闭、25分钟超时；固定 Python3.11.9 x64、CMake3.31.6 与已有检查依赖。仅同分支相关路径 push/manual dispatch 触发，未实际推送或调度。

AI setup 按既有 assets.json 核对 runtime/model/photo 三者的大小/SHA-256，再解压并构建 MSVC Release。实际 Windows ORT zip **82,645,522 bytes** 已在本机下载，SHA-256与既有官方published digest一致；头文件/import lib/notices/两支x64 PE DLL存在且已检查imports，**未运行Windows二进制**。assets.json 将原“未下载或执行”拆成“已下载并核验/未执行”，没有把下载记成Windows通过。Windows ONNX/NumPy/Pillow/Protobuf及CMake共5个兼容CPython3.11/x64 wheel实际下载成功，未安装到Windows。setup-python v5 SHA从官方Git ref核对并固定。

HEIC新增只含两份既有源码身份的assets.json，重新核验本地archive匹配。CI按同一codec cache从源码构建/安装libde2651.1.1与libheif1.23.4共享库到私有prefix，再构建probe；installed headers/library可直接由上游find模块定位，pkg-config可选。设置DLL搜索路径、禁用plugin目录；两项都执行既有完整harness及Unicode重放，并要求最终JSON的windowsExecuted为true。记录DLL hash/dumpbin imports、OS/runner/Python/PowerShell/依赖、configure/build/install与screen日志。

上传使用明确正向文件类型白名单：输出PNG/JSON/f32/bin/log，以及evidence和CMakeCache。模型`.onnx`（包括Unicode目录中复制的完整模型）、SDK二进制和下载源码不入artifact；AI生成的subject.comp manifest/PNG仍可下载供Mac读回。14天失败证据保留；这不是运行时安装包或模型/codec授权放行，hosted upload尚未执行。

验证：actionlint1.7.12对两条Windows工作流exit0；官方PowerShell7.6.0 Mac arm64 archive按release digest核验后在临时目录运行，实际解析两条工作流共**22段**shell脚本，无语法错误、未执行Windows命令。另实际验证PSNativeCommandUseErrorActionPreference+Stop+Tee-Object：原生exit0时PSexit0并进入后续步骤；原生exit7时PSexit1且后续标记未写，避免日志管道吞掉错误。各下载、wheel、runtime notices/imports与检查hash见 `evidence/feasibility-ci-preparation.json`，本机诊断目录 `/tmp/compositor-feasibility-ci-preflight-01`。未改原生算法/输入harness，沿用上一轮完整Release/sanitizer结果；未重复Mac app XCTest/全量套件。

Windows/MSVC构建、宽字符入口、实际DLL加载、数值结果与模型质量/资源门槛都仍未验证。Developer-equipped Server CI不能充当Windows11参考机或干净VM；M0产品决策/设备、M1选型以及M2–M7和全部发布目标仍待原定验收。AI/HEIC两条CI入口至此已备好，W-005/009/W-031不勾选。

## 2026-09-21：W-004 获得 Windows Server CPU 节点，完成源码传输校验

起点 `003ff71`，工作区干净。用户通过原任务授权使用已登录 Chrome 中的腾讯云 Windows 轻量服务器，并要求 Computer Use。全部服务器操作使用 Chrome 原生界面、腾讯云 TAT PowerShell 和文件管理；未推送公开源码、改端口/密码/安全策略、升级套餐或中断既有管理员 RDP 会话。入库证据用 `tencent-cpu-01` 别名，省略公网 IP、账号和实例标识。

实际环境为 Windows Server 2022 Datacenter x64 / 10.0.20348，Xeon Platinum 8255C、4 logical processors、4 GiB RAM。02:10:36 UTC 的只读命令 exit0，空闲内存 1,611 MiB、C 盘空闲 9.75 GiB；PATH 上有 Python/Node/Git，没有 cl/clang/clang-cl/GCC/Zig/CMake/dotnet，默认 dotnet 与 vswhere 路径不存在。显示适配器只有 Remote Display 与 Basic Display/SeaBIOS VBE，不能宣称硬件 GPU、Windows 11 或干净机验收。

为低资源节点新增 `experiments/windows/native/run-mingw.ps1`：逐个编译现有 8 个 C 文件和 bridge，链接 DLL 后执行原 C++/ctypes 检查并检查 19 个 PE 导出，保留日志。没有改原算法或原测试。PowerShell7.6.0 本机语法检查通过；非 Windows 调用按预期 exit1 且未创建输出目录。Windows PowerShell5.1/编译器执行仍未完成。

21 个源码/头文件/测试/构建脚本打包为 24,597 bytes，通过控制台文件选择器上传至 `C:\CompositorValidation\003ff71`。服务器在 02:25:52.0134209 UTC 实际解压并校验 archive 与每个文件 SHA-256，exit0、全部匹配；archive SHA-256 为 `0039d367e3288a6cc50a4a828b72f79809bcf8bdd3d6050c2bc43bf7d143a6fd`，逐文件身份记录在 `evidence/windows-server-environment.json`。

便携 LLVM-MinGW20260908 官方包190,677,197 bytes、展开749,497,736 bytes；本机完整下载匹配官方 SHA-256。服务器 WebClient 下载900秒超时，仅留下16,627,762 bytes；控制台大包上传显示21.8MB后返回 `access deny`，只读检查确认目标没有该上传文件，原因未确定。随后通过 TAT 调用系统 curl 对原官方文件作120秒断点续传，180秒任务上限；10:34:17控制台时间启动，结果尚未读取。用户切到另一 Chrome 标签页输入内容后，暂停界面操作，避免干扰。

另在本地用既有 SDK10.0.401 将原 C# 探针构建为 `UseAppHost=false` 的托管程序集，0 warning/error；同一程序集在Mac的.NET10.0.12/原Release动态库上通过17入口与1,000次释放检查。已下载并匹配官方SHA-512的.NET10.0.12 Windows x64 runtime zip为36,960,258 bytes，尚未上传/在Windows执行。托管包 `/tmp/compositor-pinvoke-managed-003ff71.zip`（9,006 bytes）和runtime均仅作后续准备，不是新的Windows运行证据。

W-004 获得部分环境资源，W-006 仍待实际编译/运行。等待浏览器可继续操作后先读回续传状态，再完成工具链准备与已有测试；不将下载、源码校验或脚本解析计入实机测试通过。M0/M1、W-007/008/009及全部M2–M7/PRD/发布门槛保持原范围，目标尚未完成。


## 2026-09-21：用户回传 Windows 11 原生 C++/ctypes 探针通过结果

用户提供 Windows 11 Pro x64 实机：Build26200 / 10.0.26200、i9-13900、64 GiB、4090D、驱动32.0.15.9186。按本任务逐步指导，在桌面 CompositorTest 目录解压测试源码及便携 LLVM-MinGW；系统已发现 Python313 和 dotnet 路径。实际编译器版本输出尚未回传，不把官方包标识当作已测工具版本。

用户运行 `run-mingw.ps1` 后贴出完整结果 JSON：UTC `2026-09-21T03:19:50.1402809Z`、status passed、windowsExecuted true、19个导出、8个C算法通过既有C++合约；pointer/size_t均8字节、long4字节。ctypes确认Windows/AMD64，1,000次轮廓分配/释放检查通过。DLL SHA-256 `046ad66d8da01625985f29f59fd5b39035adc0483dc43eb141d678b4e931320f`。证据见 `evidence/native-probe-windows11.json`，明确来源为用户回传；尚未独立取得原始result.json、编译日志、导出表或DLL。

没有修改原算法、桥接或测试断言。此为首份Windows11原生运行结果，不是MSVC/CMake、C# P/Invoke、完整固定样本跨平台比较、GUI/IME/DPI/GPU或性能验收；W-006仍部分完成。硬件配置也不等于S01–S05已测，干净虚拟机和集显参考设备仍待补充。

为下一步准备私有C#测试包CompositorPInvoke.zip（35,794,869 bytes，SHA-256 `edf335aa56e4cfed4249bd2d4a1e6053429a92abda6c1a918f53d79bcd8eef35`）。包含此前SDK10.0.401/Release/UseAppHost=false构建并已在Mac验证的原托管程序集、原测试源码身份、官方.NET10.0.12 win-x64完整runtime及LICENSE/ThirdPartyNotices。runtime原archive SHA-512再次匹配官方元数据，包内文件CRC与逐文件SHA-256检查通过；不包含Windows native DLL，将按明确路径加载用户刚生成的DLL。该包仅在本地交付目录准备，不入Git、不公开推送、不安装系统运行时。Windows C#执行仍待用户操作，不提前记为通过。


## 2026-09-21：用户回传 Windows 11 C# P/Invoke 通过；准备自动渲染包

用户继续运行上一轮私有测试包，回传 C# P/Invoke status passed：Microsoft Windows 10.0.26200 / X64 / .NET10.0.12，pointerBytes8、sizeTBytes8、fixedSignedOutputBytes8，8个源算法、17个入口与1,000次分配/释放检查通过。该平台版本与此前Windows11 Pro Build26200环境一致。执行命令在加载前校验同一原生DLL SHA-256；证据 `evidence/pinvoke-windows11.json` 保留原输出及来源限制，尚未独立取得原始结果文件。

为下一轮实机图像输出准备 CompositorRenderTest.zip：30,048,765 bytes、SHA-256 `d3dac08a54f8f34ae54019ea99a40d1b13faf8b5e70fdff734db75591edafef1`。既有Avalonia11.3.22原型以SDK10.0.401、locked restore、Release、UseAppHost=false重建，0 warning/error；在Mac运行同一程序集的20组合成预检，全部自定义控件预览/导出精确一致。未改原型算法、样本或断言；本机预检不是Windows结果。

包包含原托管输出、保留原runtimes/win-x64/native路径的SkiaSharp2.88.9及HarfBuzzSharp8.3.1.1 DLL、base/brush/extended固定样本、源码身份及字体/框架/原生依赖的license/notices/NuGet元数据。Windows两支DLL逐字节匹配对应NuGet archive成员，并记录PE imports（Skia：ole32/FONTSUB/USER32/KERNEL32，HarfBuzz：KERNEL32）。16份NuGet cache archive的实际SHA512匹配各自sidecar，cache metadata contentHash匹配locked graph；二者是分别记录的身份值，不将其当作同一个hash。包内CRC与逐文件SHA256检查通过，复用用户已有便携.NET10.0.12和已验证native DLL，不安装新系统组件。

该原型当前是headless CPU自动渲染入口，没有可手动操作的编辑器窗口。下一轮先执行20组PNG与最小重命名保存/重开检查，Windows结果仍待回传；真实窗口、IME、DPI、GPU、S01–S05、Qt对照和完整跨平台比较仍待原定验收。W-006的两类FFI探针已有用户回传实机证据，但不据此关闭完整W-006、M1或发布目标。二进制包留在本地交付目录，不入Git、不公开推送。


## 2026-09-21：用户回传 Windows 11 的20组合成与保存/重开检查通过

用户运行私有CompositorRenderTest包的既有headless CPU合成入口，回传PowerShell从report.json提取的摘要：WindowsExecuted True、SampleCount20、PreviewExportMismatches0、SavedProjects20，status仍明确Mac差异仅为观察、未验收。输出目录为桌面CompositorTest下 `render-run-20260921-113945`，见 `evidence/avalonia-composition-windows11.json`。

这是Windows11上真实图像输出路径的用户回传结果，不能扩大为原生窗口或GPU测试通过。完整逐样本report、PNG、保存工程与日志尚未独立收集；Mac差分数值未收到，Windows保存工程尚未在Mac端读回。原型算法、输入和容差未改；未因自动测试成功选择生产框架。

下一步复用同一包运行既有4K两笔软笔测试，无需重新下载。其固定输入是4000×4000、800px、0硬度、40%不透明度、每笔121个事件，验证连续下一笔、历史不变性、撤销/重做、预览/导出与保存重开。此输入不是S02的100%不透明度/至少30笔实机性能验收，W-008/M1仍未通过。

## 2026-09-21：用户回传 Windows 11 的4K两笔软笔检查通过

用户回传 PowerShell 摘要与两行时序：WindowsExecuted True、每笔121个事件、242次自定义控件更新、56个共享瓦片、13项session检查；status为 preparation checks passed，仍明确参考差异和时序只是观察。输出目录 `brush-run-20260921-114232`；证据见 `evidence/avalonia-brush-windows11.json`，来源为用户粘贴，尚未独立收到完整报告、PNG、工程和日志。

两笔更新加预览P95分别27.5388/29.166 ms，提交25.5688/24.0438 ms。P95高于计划16.7 ms目标，需后续定位；此为4000×4000、800px软笔、40%不透明度的两笔同步headless CPU测试，不是S02要求的100%不透明度/至少30笔性能验收，不能推定4090D已参与渲染。完整差分与内存数据未收到，Windows保存的brush.comp尚未在Mac读回。

未改算法、容差或性能实现；仅记录证据并检查文档差异。下一步复用同一包执行12组文字布局与合成输入检查；nativeImeExecuted=false为此探针的明确范围，真实Windows输入法仍需后续窗口测试。W-008/M1及正式性能门槛保持未通过。

## 2026-09-21：用户回传 Windows 11 的12组文字与模拟输入检查通过

用户回传 text-report.json 的 PowerShell 摘要：WindowsExecuted True、NativeImeExecuted False、SampleCount12、PreviewExportMismatches0、SyntheticInputPassed12、CancelPixelMismatches0。输出目录 `text-run-20260921-114831`；见 `evidence/avalonia-text-windows11.json`。status保留原文 local preparation only; synthetic input is not Windows IME acceptance，未将模拟输入通过扩大为微软拼音验收。

目前只收到摘要；完整Mac参考差异、字体回退、光标诊断、PNG和日志尚未独立收集。此次只更新证据和文档并检查差异，未修改算法或容差。下一步指导用户汇总既有原生、合成、笔刷和文字运行目录与日志，再检查原始报告、图像及Windows工程在Mac端的读回结果。真实窗口/IME/DPI/GPU、正式性能、完整文字工程事务与W-008/M1仍未通过。


## 2026-09-21：收到原始 Windows 结果，Mac 读回通过，发现文字视觉失败

用户上传 Windows11-TestResults-20260921-133437.zip，7,291,136 bytes / 247 files，SHA-256 b059e2b04fd7cfbd0f516468ca78d850608db83d6c65207a7252ed4b59f2e4c4，CRC通过。独立核对DLL hash与19个PE导出，compiler.txt确认clang23.1.1。完整结果、receipt manifest、独立像素核算、Mac读回日志与接触图见 evidence/windows11-artifact-review；分析见 windows11-results-review.md。C# P/Invoke独立结果文件未包含，仍保留用户回传来源限制。

Pillow11.3.0/NumPy独立核算全部36组参考差分，与原报告完全一致；32组预览导出及12组取消恢复的完整RGBA精确相同。合成只有F01/F02/B08对Mac参考精确，其余17组最大通道差2/255。两张笔刷图与此前Mac Avalonia输出RGBA一致；更新P95约24ms，预览P95约3.7/5.3ms，working-set采样高水位213,577,728bytes，不充当正式性能/内存验收。

使用原有两个gated Mac测试，设置用户实际输出目录并运行xcodebuild；2 passed / 0 skipped，覆盖20个合成工程的manifest/资产/渲染保持及1个brush.comp的打开/再保存/重开像素保持。没有修改测试或产品。

视觉检查纠正先前文字“通过”的范围：四个代表样本可见中英文笔画严重缺失、碎点，emoji仍可见；12组参考最大误差均255。shared preview/export equality和any-nonzero断言可在画面错误时通过。文字视觉验收明确失败；根因未确定，下一步需定位普通字形绘制并补独立覆盖检查，再发修订探针复测。未改算法、依赖或容差，不要求用户重复现有测试，不选择框架；W-008/M1保持未通过。


## 2026-09-21：收到 D-01/D-04/D-11 明确产品决定

父任务 01a0beb1-da38-7b00-9183-d65bff121d78 转达真实用户答复：对“首版 Windows11 x64、.comp 文件夹工程；原字体可用缩放重绘，缺字体保留原画面并提示选择，禁止静默替换”的明确问题回答“按建议实施”。据此关闭 D-01、D-04、D-11 待决定状态；首版容器为 v8。同步 technical-design、product-requirements、product-goals、m0-readiness、development-plan。

此决定不授权静默回退、丢失文字元数据、自动栅格化或单文件容器；不等于相关行为已经实现/验收。真实字体缺失及缩放行为测试仍需补齐。公开推送、签名、参考容差及性能门槛变更仍未获授权，M0/M1 不因三项产品决定自动通过。


## 2026-09-21：文字缺失回归检测与一次性 Windows 光栅诊断包

依据 diagnosing-bugs 流程，先用捕获的实际输出构造可重复反馈：F11-72-point-right 的普通蓝色文字 alpha 墨量为参考0.251，Mac Avalonia为0.877，50%严重缺失检查分别红/绿；emoji颜色被排除。随后将同一行为检查接入TextInkChecks：独立读取上传Windows的12张输出全部拒绝；原完整Mac文字流程12张全部通过（ratio0.877–0.980），取消/模拟输入仍通过。此阈值仅捕获严重字形丢失，不是跨平台像素容差，不能证明每个字形都正确。

新增 --verify-text-output 只读重验旧输出；--text-diagnostics 在原文字流程前加入14个无emoji变体，分别经过Avalonia TextBlock和直接Skia，在同字体下对抗锯齿、hinting、subpixel位置、翻转、旋转、透明度和outline绘制做对照。固定Avalonia上游源码确认antialias字形仍使用Full hinting/subpixel位置；这只是候选边界，未确定Windows根因。没有替换字体、依赖或正式渲染实现，没有凭灰度/outline对照宣称修复。

Release build 0warning/error，Mac完整文字/诊断与20组合成回归均成功；新guard的Windows捕获失败保留实际exit134日志。PowerShell7.6.0仅语法检查通过，未冒充Windows执行。证据 evidence/text-ink-diagnostic。

私有CompositorTextDiagnostic.zip为13,819,215bytes，SHA256 ee658c6f7db590f2918b557f257ecf9ab370d777f4c557a59d7782780af30722，CRC/patch文件hash核对。仅含新托管探针、源码、OFL和脚本，复用用户既有runtime/依赖；脚本复制新运行目录、不修改旧app/旧结果，检查同一DLL和patch身份，保留失败日志、14组图、原12样本与ordinary-ink.json并自动打包。已向用户交付一次运行命令；等待真实Windows诊断结果，不提前记为修复。父任务并行在独立/tmp副本诊断笔刷；本工作区不重复该工作、不操作服务器、不公开推送。


## 2026-09-21：整合像素等价的笔刷颜色缓存候选

父任务在固定8cf1253临时副本定位Publish占Append61.6%/68.5%，RemoveTail仅0.4%/0.3%。受控预热后同进程4对交替Append总时序原版→候选1423.62→1259.37、1243.75→1133.88、1651.71→1117.61、1245.60→1135.44ms，像素均相等，中位比例减少约10.19%。另一组完整新进程P95存在候选更慢反例，不能声称稳定E2E或Windows收益。

只整合SoftBrushStroke每笔256色预计算，增加1024字节，保留Round255及原底图叠加。正式工作区重新执行独立60-case/720-preview差分回归和完整13项会话检查通过；9个输出文件与旧Mac基线逐字节相同。对收到Windows包的初次9文件字节比较在manifest失败，检查仅CRLF/LF不同、JSON数据一致，8个PNG均字节相等；没有为此改工程格式或容差。回归harness固定从Git取8cf1253并核对原文件hash，参考副本仅存在临时生成目录。

Windows ABBA复测包CompositorBrushAB.zip已准备：13,814,837bytes，SHA256 2a91d12f7ece5b00699b5c164cd34f986eabf89ec0b9997f9385bbb6c4a1a7a8；原版程序集身份取自实际先前交付zip，候选复制到新目录，4轮结果/时序和9文件hash比较全部保留。脚本语法和包CRC检查通过，Windows尚未执行；先完成用户当前文字修复复测，避免同时要求两项操作。证据 evidence/brush-cache-candidate，S02/W-030/M1均未据此通过。


## 2026-09-21：Windows 实时诊断定位文字抗锯齿路径，提交灰度覆盖修复

用户回传 text-diagnostic-20260921-140002.zip，476,958bytes / 80entries，SHA256 9d3cb2851df6a505e555c312938ce9d0255d4bf1643cf070137e59d28342030b，CRC通过。原12样本普通字形墨量继续全红；无emoji的Avalonia默认变体alphaMass29，显式Antialias恢复920.475，Alias926.588。直接Skia默认Antialias929.42及no-hinting/no-subpixel/outline均正常。实际查看PNG确认默认碎点、显式灰度模式中英文完整，样本本来设置镜像。证据 evidence/text-grayscale-fix。

因此在TextProbe.Save仅增加显式TextRenderingMode.Antialias，使透明/变换文字画布使用灰度覆盖；不换字体、不改布局/颜色/透明度/变换、不选择outline作为产品渲染器。它修正可控的默认抗锯齿路径，未证明底层Skia/驱动内部缺陷。Mac重建0warning/error，12个墨量检查通过、全部60个PNG与此前Mac结果逐字节相同；完整Windows修复后F11仍待用户回传，不提前关闭视觉/IME门槛。

已交付CompositorTextFix.zip，13,822,931bytes，SHA256 f6110db3c9707611d3aaec54ee543a06541a4936347e5ba0e1347761dbfc7fab。复用旧runtime/依赖，复制新app并自动打包完整12样本、日志、ink及摘要。父任务随后发现包装脚本在native失败但成功打包后会exit0；在独立/tmp负例已复现。当前两个新wrapper均已改为打包后显式验证并exit1/0；6种真实尾部守卫条件在PowerShell7.6实际执行符合预期。

为保持已交付包可追溯，首包不修改，按其中summary.ExitCode/原始报告/图片判断，不要求用户仅为wrapper重跑。另准备-v2包：文字13,822,994bytes，SHA256 42610ad46067f240b29eb7c1117d210eab68b6975f541f221b1f7882a28d5dd4；笔刷13,814,853bytes，SHA256 3eb64e6e25207fd161b134a8757fa82ab55f220abbf4b06a82c4846f6a6da150。v2仅修正wrapper失败传播，托管payload未变化，CRC通过；Windows A/B仍未运行。

## 2026-09-21：父任务补充 Windows Server 便携运行时证据

父任务通过独立Chrome/TAT操作tencent-cpu-01，06:02:53.3038204Z下载官方.NET10.0.12 win-x64 runtime完成，curlExit0、36,960,258bytes、SHA512精确匹配微软元数据；06:05:11.5903914Z在C:\CompositorValidation\tools\dotnet-10.0.12运行--list-runtimes，exit0，剩余9.6GiB。证据 windows-server-runtime.json 来源为父任务界面读回转录，非原始日志下载。本任务未操作服务器，无系统安装/全局PATH/防火墙/密码/重启/RDP改动。

此前LLVM续传实际curlExit28，只获得21,395,506/190,677,197bytes，TAT包装退出0不能当下载完成。runtime成功只证明Server环境可执行，不是Windows11、产品、GUI/IME/GPU或性能验收；旧文字诊断包未上传，避免与新Win11回传重复。W-004仍部分完成。


## 2026-09-21：包装脚本失败传播的独立完整负例证据

父任务在macOS/PowerShell7.6.0隔离目录实际运行冻结的run-text-fix.ps1，以加载真实Windows DLL故意使探针exit134：旧wrapper返回0；加入完整性/退出码守卫的副本对同一故障返回1，两轮zip、summary和text-run.log均保留。归档 evidence/wrapper-failure-propagation 含两份冻结脚本及其hash、日志和摘要。此证据属于父任务执行的副本，不宣称当前v2包在Windows已测；本任务当前脚本的6种实际尾部guard检查另见 text-grayscale-fix/wrapper-exit-checks.json。

不要求用户为第一版wrapper单独重跑；其完整产物仍按native ExitCode、样本完整性、墨量断言及图片核验。Windows PowerShell5.1和完整修复后文字样本仍待实际回传，既定产品决定持续有效，M0–M7范围不变。


## 2026-09-21：Avalonia 真实窗口和 Mac 手动操作证据

新增 --window 原生桌面入口，固定 Desktop/SimpleTheme11.3.22；三页覆盖变换 TextBox 输入与导出、4K 真实鼠标笔刷、F04 合成和工程另存重开。Windows明确请求Software/RedirectionSurface，Mac为Avalonia.Native Software；不提前选型、不实现生产界面或D-11缺字体工作流。输入/保存与绘制共用锁，关闭释放受保护；SKImage持有像素副本。实际Mac事件UI线程1、brush/composition绘制线程4。

通过CUA实际操作Mac窗口，第一轮6组文字预览/导出精确、粘贴/撤销/重做和变换点击输入可见；2笔鼠标绘制、撤销/重做后保存及F04另存重开通过。初版原生笔刷可见方形背景擦除，独立真实WindowBrushView回归捕获不透明背景的32041个半透明像素，exit1；仅在原生绘制回调加SaveLayer后为0、exit0，真实窗口方块消失。原TiledRaster/算法未改。最终带滚动容器构建再实际检查三页并正常退出0；三轮共14对导出/重开PNG逐字节相等，原笔刷redo恢复及文字取消前后PNG相等。Mac原生预编辑事件不等于微软拼音验收。证据evidence/avalonia-native-window-macos，完整窗口产物在本任务artifact目录。

最终程序集6214da989f8d527d11a2bc78cce70c8ddc6b939d70e067bc5b73e094cd811bec。原20合成、12文字墨量及13笔刷会话检查全通过；并行运行只作正确性检查，不比较时序。新的窗口背景回归保留在experiments/windows/window-regression。

完整私有CompositorWindowTest.zip已准备，46859313bytes，SHA25663d489a89a1899a59cf238ae8bb6f90cd4f492c6d4023f30846ccb379137b163；402个清单文件身份及CRC逐项通过，包含完整新依赖、win-x64资产、固定样本、自有源码/字体及依赖许可；复用用户既有便携.NET和验证过的native DLL，不覆盖旧app。Windows原生资产PE机型均AMD64。PowerShell7.6语法通过，精确打包wrapper在Mac加载Windows DLL故意失败时native134、wrapper1，日志/ZIP均保留；Windows PowerShell5.1及GUI仍待实际运行。

先等待用户完整12样本文字修复ZIP，再安排Windows原生窗口/微软拼音测试，避免并行要求操作。开发计划已纠正Windows headless已执行和W-003决定已确认的过时段落；M0/M1及完整M0–M7范围不变。未公开推送。


## 2026-09-21：整合 Qt Windows Server 实跑及 Emoji 视觉失败证据

父任务以冻结cbc28f5、Qt6.11.2 LLVM-MinGW SDK、clang23.1.1与CMake3.31.6完成交叉构建，并在既已授权的tencent-cpu-01 Windows Server2022/PowerShell5.1实际执行native、20合成、两笔4K笔刷、12文字四项，进程退出均0。首次wrapper因Compress-Archive文件映射占用退出1，原数据保留，仅以新文件名重新归档，没有重跑测试；不声称归档根因已修。

父任务下载结果9499306bytes、SHA2564d3eda09a42df5a7edc636c3e667ecdcd2cbdd552278ceea9228e9a7c2df7d1b。本任务重新核对SHA/CRC/213项文件身份，并查看接触图确认Emoji缺字方框。父任务的36组独立像素核算、32组预览导出/12取消精确、两项Mac读回2passed/0failed/0skipped及原始报告/运行日志整合到evidence/qt-windows-server；评审qt-windows-server-review.md保留父任务执行来源，原始PNG和xcresult在其artifact目录。

文字12样本普通蓝字墨量0.892–0.949不能掩盖Emoji视觉失败；offscreen字体库未发现系统fallback为待验证假设，fontdir/qwindows对照未执行。笔刷对Mac Qt预乘RGBA相同但PNG字节不同，对原CPU/Metal参考仍存在未接受差异。Server P95 53.6642/61.5534ms、commit58.8498/69.4234ms不是S02或Win11同机比较。已纠正Qt文档中所有Windows未执行与D-11决定未定的过时表述，MSVC/CI未执行状态保留；不选框架、不关闭M1。


## 2026-09-21：整合 Qt 原生窗口并修复替换文本颜色继承

父任务基于605a610在独立/tmp提供6文件窗口补丁与Mac/Windows交叉构建、原20合成/12文字/笔刷回归和实际背景负例证据。本任务独立检查接口/生命周期后应用，在权威源码重新Mac Release构建并运行--window-check通过；随后以cocoa启动真实窗口，通过CUA实际完成文字替换/粘贴/撤销/重做、鼠标两笔和history保存、F04另存。

真实交互暴露原自动检查漏项：全选键盘/IME及粘贴后文字变黑，但预览/导出一致。新增真实TextItem全选QInputMethodEvent替换后的蓝色像素检查实际exit1；format同时设置段落默认setBlockCharFormat后exit0。重开真实窗口，键盘与纯文本粘贴保持蓝色，30°/翻转/125%和预编辑取消后的导出通过；同内容深色像素4619→0、蓝色0→4951。原12文字全部72PNG逐字节不变，鼠标历史往返和文字取消PNG恢复验证通过。两轮原生窗口均退出0，无报告错误；Mac原生预编辑事件不算微软拼音候选位置验收。

最终权威源码的Windows LLVM-MinGW交叉配置/构建均退出0，3个自有PE机型AMD64；EXE尚未在Windows运行，未修改此前Server Emoji结论。一次过早发起构建因配置尚未完成返回could not load cache，随后在配置完成后正确构建；一次覆盖旧Mac测试bundle的启动exit137，换新私有bundle正常启动/退出，原因未确定。全部原始证据与明确限制见evidence/qt-native-window-macos，完整图片/工程/二进制在本任务qt-native-window-integrated artifact。

用户仍优先回传完整12文字修复ZIP，未要求同时测试Qt/Avalonia新窗口。无Chrome/服务器操作；D11文字工程/字体缺失、Windows IME/DPI及M1/选型保持未完成。


## 2026-09-21：整合 HEIC MinGW Unicode 入口修正及交叉包证据

父任务在冻结605a610独立副本构建原libde2651.1.1/libheif1.23.4共享库；probe首次链接实际WinMain未定义，已有wmain分支无需改动，只对MINGW追加-municode后同目录链接成功。本任务核对probe.cpp与冻结提交逐字节一致，审查并应用该4行CMake修正；原生路径编码与解码逻辑未变。

父任务5个x64 PE/9条依赖边静态命名导入核对通过，不当作运行时证明。Windows私有包4466298bytes、SHA2563350ac1b5f29157e4d4d11a2b8aef464328b4cb7982c49873f5c1000088d9d45，本任务重新核对完整包SHA/CRC和75文件hash。原始构建日志/失败和修正退出码、源码身份、依赖汇总、wrapper适配正负例摘要保留evidence/heic-windows-cross-build；大PE表、源码包、raw及完整产物在父任务artifact。

脚本正例仅在独立Mac副本替换平台guard/exe后执行：16decode、23invoke、指标/raw与原独立Python一致，exit0；参考alpha翻转负例native0/wrapper1，日志和ZIP保留。原Windows包不变，WindowsPowerShell5.1未执行。父任务因用户在Chrome切标签停止上传，服务器没有上传/执行；本任务未操作CUA/服务器。W009/W031/D07/M1均保持未完成，用户仍优先回传12文字修复结果。


## 2026-09-21：用户 Windows 11 完整文字修复回传通过缺字复测

收到 text-fix-20260921-152945.zip，1560237bytes，SHA256 cafecf36e32bb646e4301863f070d08ed3939545704d38dbae8e3da58e155f55，64文件CRC通过。独立Pillow/NumPy重算12组墨量与Mac参考差分，全部与原报告一致；完整RGBA预览/导出12/12、取消恢复12/12精确。实际查看全部12图及四组Mac对照，普通中英文恢复、Emoji可见，墨量比0.8053–0.8623通过既有严重丢失防护阈值。未改变阈值或参考。

证据保存在evidence/text-grayscale-windows11；完整ZIP和60PNG在本任务同名artifact。NativeImeExecuted=false，最大参考误差161/166及Emoji外观差异仍未验收。原缺字阻断已修复，W008/M1仍未关闭。下一步只指导用户启动Avalonia真实窗口，再逐步执行微软拼音/焦点/变换检查；不同时要求笔刷AB或Qt操作。


## 2026-09-21：整合 AI MinGW Unicode 入口和部署准备证据

父任务冻结605a610的ONNX Runtime1.30.0原型首次链接WinMain未定义，仅对MINGW添加-municode后原目录链接成功，probe.cpp和模型均不改。本任务审查应用4行CMake补丁，核对probe源与冻结提交一致，重验7717659bytes私有包SHA25661d074d63aa28eba01cedc97730140a32f70d64fa3c80617624b4b5e0037c738、CRC及27文件身份，零onnx权重。独立解析原始PE字节确认probe按ordinal1导入，SDK DLL相同序号实际导出OrtGetApiBase；不把静态导入核对当作Windows加载成功。

父任务的5个AMD64 PE/88命名符号/ordinal1检查、构建红绿日志与Mac适配脚本8调用正例、NaN负例native1/wrapper1、默认下载分支证据保留evidence/ai-windows-cross-build。输入tensor与原Mac字节一致，正例1344个CPU kernel；Mac适配并非Windows5.1。MSVCP140、MSVCP140_1、VCRUNTIME140、VCRUNTIME140_1未随包，未安装或复制系统运行库。

Windows EXE/实际DLL加载/网络脚本均待执行。runner仅固定tensor至raw，不覆盖Windows图像前后处理、512px蒙版或.comp，不关闭D08/W009/W032/M1。本任务未操作服务器或浏览器，用户当前只安排原生窗口输入法下一步。补丁与文档单独提交，不混入文字复测提交。


## 2026-09-21：整合 HEIC Server 真实解码与 PS5.1 修正证据

父任务在授权Tencent Windows Server2022上核验原包后执行，首轮wrapper1/0native，原因是PS5.1的ConvertFrom-Json外围@()令16样本嵌成1个数组元素。只移除这层包装，在全新目录更新runner清单后重跑wrapper0：16样本、23原生调用，17成功6预期拒绝。原包、420字节失败ZIP和19585字节成功ZIP均保留。

本任务独立核对两轮ZIP SHA/CRC，以及4466303字节修正版包75文件身份；原包比较仅runner一行及files.json变化。从成功ZIP直接读取16raw，使用本仓库固定参考重新计算全部像素指标，均精确吻合；16raw与原Mac libheif逐字节相同，alpha对ImageIO精确、RGB最大差1未接受。实际查看全部方向/alpha联系图，证据evidence/heic-windows-server含原始60文件、首次失败ZIP、修正脚本及整合核验。WindowsServer不等于Windows11干净机，不关闭D07/W031/M1。

用户的UU Windows11输入法首轮仍等待本机Esc对照；本轮本任务没有再次操作前台或更改正在运行的窗口测试包。


## 2026-09-21：UU 远程 Windows 11 原生窗口首轮中间结果

用户授权直接通过UU操作Windows11。本任务用CUA文件传输冻结窗口ZIP和单独校验启动cmd；远端ZIP SHA通过，只向新window-test解压后启动原脚本。实际nativeWindow=true，10.0.26200，RenderScaling1.5，首次文字导出零差异；回收760×520PNG独立完整RGBA比较精确。输出window-run-20260921-154725仍运行。

发送a后出现拼音候选UI，但后续Esc/Backspace/点击无明显反应；Windows时钟和文件传输仍正常，先前PowerShell长命令也有字符丢失，未定位到UU或测试程序。已暂停远程输入，请用户本机Esc对照。保存evidence/avalonia-native-window-windows11-interim的截图、原始中间报告/PNG、独立复核和启动器；报告写入早于候选观察，不能把其0事件数字当作最终IME记录。未执行完整文字/笔刷/合成三页流程，不记为通过，未重启/结束现场。


## 2026-09-21：AI wrapper 下载整体时限修复与独立负例

父任务旧WindowsServer首轮TAT240秒下载超时，只有4058944/4574861字节part，后续诊断无剩余相关进程、无原生调用。为使失败在任务时限内归档，runner改用系统curl -q/连接20秒/总传输120秒，无重试；原模型身份、推理与-ModelPath不变。本任务核验7718022字节修正版ZIP SHA/CRC/27manifest，只有runner/README/manifest改变，零权重文件；将逐字节一致的runner源纳入experiments/windows/ai/run-ai.ps1。

在新Mac适配副本独立完整复跑正常/慢下载/错hash三项：正常wrapper0、8原生调用；慢服务发1024字节后阻塞，2秒测试时限实际curl28/2.092秒，wrapper1/2.919秒，失败ZIP保留；错hash在原生前拒绝。证据evidence/ai-download-timeout-fix保留适配脚本、原始ZIP、summary/log和包身份。本任务未操作CUA/服务器，修正版Windows分支尚未执行；旧包用本地模型的Server后续命令已由父任务提交但尚未读回，不提前记成功。
