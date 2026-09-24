# S05 资源提案 v2 的规则验证

本目录是规则控制和校准证据，**没有新的 Windows 前瞻通过记录**。规则见 [v2 提案](../../../s05-resource-policy-v2.md)。

- [控制记录](controls.json)：固定周期的 18 种相位，v1 上限检查九次失败；v2 均通过。额外覆盖持续累积、预热峰值掩盖累积、整幅像素残留、新高峰、预算边界及原有资源/完整性错误。合成修改仅用于测试判定器，不冒充新的 Windows 采样。
- [旧校准报告](earlier-calibration.json)及 [v1 失败报告的校准](v1-failure-calibration.json)：实际运行完整 CLI，均满足 v2 候选规则，`resourceAccepted=false`。原 v1 失败保留在 [run-1](../resource-policy-v1/run-1/README.md)。
- 应用源码、DLL、GC 参数没有修改。修改的是上限的参照区间，数值预算未变；不能据此称应用性能得到优化或泄漏已修复。

下一步在本规则提交之后执行两次独立 Windows 前瞻运行。每轮必须核验原始归档、完整性和全部 54 张图片；不能只保留绿色结果。

启动观察：在规则提交 `8a4d1d8` 后，UU session2 已核对旧 runner 摘要一致。发送启动指令时 CUA 报告 Mac 锁屏且自动解锁失败；新 PID/输出目录未确认，不判断启动成功或失败。见 [原会话检查点](launch-observation.json)。解锁后先检查现有会话及实际进程，再决定是否需要启动，避免重复运行。
