# M2 原生魔棒与选区轮廓接口（2026-10-02）

正式 Core 新增 `NativeSelections.Select` / `Trace`，调用现有 `WandPixels.c`，没有复制或修改共享 C 算法。采样半径限定为 Mac 产品已有的 0/1/2（点、3×3、5×5）；容差夹取至 0–255，连通模式保留四邻域语义，图外种子返回空蒙版。RGBA 输入支持行填充，返回蒙版为紧密单通道字节。轮廓返回托管 x/y 顶点和各闭合环的长度，保留外环/孔洞的非零环绕填充规则。

跨平台边界使用 `compositor_wand_mask` 的 `int64_t` 返回值，而非把 C 的 `long` 直接映射为 C# `long`；`size_t` 使用 `nuint`。轮廓的原生分配在复制成功、失败或异常时均在 `finally` 中调用同一 DLL 的 `compositor_free`，不由 .NET 分配器释放。输入尺寸、stride、数组长度和半径在调用前校验。过密轮廓保留可报告的失败，内存失败不返回半成品结果。

在 macOS arm64 上，以[固定提交加三份覆盖源码](production-native-selection-m2/baseline.json)建立独立源码目录；从全部八个共享 C 文件及 bridge 重新编译动态库，锁定恢复并 Release 构建 Core/Smoke，**0 警告、0 错误，运行退出 0**。见[命令/退出码](production-native-selection-m2/execution.json)、[源码摘要](production-native-selection-m2/source-sha256.json)、[运行日志](production-native-selection-m2/run.log)和[结果](production-native-selection-m2/results.json)。检查包括：

- 10 个固定选择案例：连通与全局差异、图外种子、边缘 3×3/5×5 平均、种子与平均值不匹配、alpha 匹配及容差上下限；同时核对计数与每个蒙版字节，源图与行填充不变。
- 5 个非法输入：短 stride、短缓冲、不支持的采样半径、尺寸越界及蒙版长度错误。
- 环形蒙版有外环和孔洞，角接触像素保留独立环；以独立的点在多边形非零环绕判定逐像素验证轮廓，涵盖空蒙版及 100 次不规则蒙版追踪。
- 2002×2002 棋盘格超过 C 算法的 800 万边界限制，按预期拒绝；随后普通环形蒙版仍能成功追踪。

该检查已接入 Smoke 的原生分支，现有 Windows CI 源码将构建 DLL 并执行它，但本轮未推送或触发 CI。复现：在 `windows/` 运行 `dotnet run --project Compositor.Smoke -c Release -- ../docs/windows/fixtures <不存在的输出目录> <本平台原生库绝对路径>`。不传第三参数会跳过原生检查。固定运行目录为 `/Users/admin/.codex/visualizations/2026/10/02/production-native-selection-m2`，[产物摘要](production-native-selection-m2/output-sha256.json)和[Core 样本摘要](production-native-selection-m2/fixture-sha256.json)随证据归档。

本项只完成正式原生 API 与边界检查。100 次调用不证明无泄漏或内存峰值合格；Windows LLP64 实际执行、选区历史/取消/组合、活动层或合成取样、画布交互及性能仍待完成。不能据此关闭 W-006/W-012/W-018；固定 r4 包不含这些新增接口。
