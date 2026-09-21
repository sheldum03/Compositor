来源：父任务 `01a0beb1-da38-7b00-9183-d65bff121d78` 实际执行并评审；本任务重新核对 ZIP SHA-256、CRC 和全部 213 个文件身份，并查看文字接触图确认 Emoji 缺字。原始包、PNG 和 xcresult 保存在父任务 artifact 目录：`/Users/admin/.codex/visualizations/2026/09/20/01a0beb1-da38-7b00-9183-d65bff121d78/qt-windows-cross-build`。以下保留其执行范围与结论，不把 Server 证据改记为用户的 Windows 11 实机结果。

# Qt Windows Server execution review

2026-09-21. W-005/W-007 gained a real Windows build and CPU execution path. **M1 remains incomplete, and no production framework has been selected.** The server is the previously authorized `tencent-cpu-01`, Windows Server 2022 x64 / 10.0.20348 with four logical CPUs and 4 GiB RAM; it is not the Windows 11 reference PC or a clean installation/GPU test machine.

## Verified execution and data receipt

The 49,731,077-byte kit was uploaded via the existing Chrome Tencent file manager to `C:\CompositorValidation`, with no new network exposure or credentials. The server command required its exact size/SHA-256 before extraction to the new `qt-cbc28f5` directory. The kit's manifest then checked all 350 recorded files before executing the four processes. There were no system installations, global PATH changes, font installations, firewall/password changes, restarts or RDP operations.

PowerShell **5.1.20348.3932** ran all four processes with exit 0: native C++ contract, twenty composition/round-trip cases, two 4K brush strokes and twelve transformed-text cases. Raw `summary.json` explicitly records `NativeImeExecuted=false` and `ProductAcceptance=false`. `native.log` reports eight C algorithms, pointer/size_t width 8 and long width 4. The Qt reports identify Windows, x86_64 and Qt 6.11.2; composition explicitly identifies the `offscreen` platform.

The first wrapper invocation ran 14:29:13–14:30:01 (console local time) and returned **1** because PowerShell `Compress-Archive` failed while disposing its output with a user-mapped-file error. This was an archive failure, not a missing native result. Original data and the failed archive were preserved. After confirming no probe process remained, a separate command used `.NET ZipFile.CreateFromDirectory` to create a fresh result archive, without rerunning the tests. It succeeded at 14:32:15. The cause of the transient file mapping has not been established; this observation does not prove that the wrapper has been fixed generally.

The result archive downloaded through Chrome is `qt-server-results-cbc28f5-20260921.zip`, **9,499,306 bytes**, SHA-256 **`4d3eda09a42df5a7edc636c3e667ecdcd2cbdd552278ceea9228e9a7c2df7d1b`**, matching the server's independently displayed hash. CRC validation passed for all entries. The 213 files and their hashes are recorded in `evidence/qt-windows-server/server-result-receipt.json`; Windows path separators were normalized during extraction into `server-results`.

## Independent assessment

| Requirement checked | Evidence and limit |
| --- | --- |
| Image reports correspond to actual files | Pillow 12.2.0 / NumPy 2.3.5 independently reproduced all 36 reference comparisons, including premultiplication, exactly matching the Qt reports. |
| Preview/export and cancel preserve pixels | All 32 composition/text preview/export pairs and all 12 text cancellation pairs have identical decoded RGBA. This alone does not establish correct rendering. |
| Composition compared with original Mac | F01/F02/F04/F05/F06 are exact; the other 15 have maximum channel error 1/255, with alpha exact. No tolerance was accepted. |
| Brush compared with Mac Qt | Both strokes have identical premultiplied RGBA pixels to the retained Mac Qt outputs. Straight-alpha PNG RGB differs after encoding/unpremultiplication, so byte equality is not claimed. |
| Brush compared with original Mac algorithms | CPU reference maximum alpha/channel difference is 3/255 then 4/255; Metal reference maximum is 7/255 for both. These remain observations with unaccepted tolerance. |
| Actual Windows-written projects reopen on Mac | Existing gated tests opened and resaved twenty Qt composition projects and one brush project: **2 passed / 0 failed / 0 skipped**. `mac-server-readback.xcresult` and the command/log retain the evidence. Mac source/tests had no changes from frozen `cbc28f5`. |
| Text | Twelve ordinary blue-text ink ratios are 0.892–0.949 of original Mac references, exceeding the catastrophic-loss guard. Visual inspection finds missing-glyph rectangles where Emoji should be; all twelve have zero warm Emoji-colored pixels, versus nonzero original references. **Text visual acceptance fails.** |
| Input | 2,631 transformed hit samples and the existing synthetic preedit/cancel/commit/undo checks passed. No native Windows IME, interactive window or DPI-transition acceptance occurred. |

The two brush update-plus-preview P95 values were **53.6642 / 61.5534 ms**, with commits **58.8498 / 69.4234 ms**. This is the fixed two-stroke, 40% opacity, headless CPU workload on a small server. It is not S02's 30-stroke/100%-opacity/real-window workload, not a comparison against the different Windows 11 hardware, and not a performance pass. This run did not sample Windows private RAM or GPU memory.

## Concrete text diagnosis lead

`server-results/text.log` reports that `app/lib/fonts` cannot be found; the recorded resolved font is only Source Han Sans SC. The pinned [Qt offscreen implementation](https://github.com/qt/qtbase/blob/v6.11.2/src/plugins/platforms/offscreen/qoffscreenintegration.cpp) selects FreeType on Windows and CoreText on Mac. Its [FreeType database](https://github.com/qt/qtbase/blob/v6.11.2/src/gui/text/freetype/qfreetypefontdatabase.cpp) populates from one font directory, while [fontDir](https://github.com/qt/qtbase/blob/v6.11.2/src/gui/text/qplatformfontdatabase.cpp) uses `QT_QPA_FONTDIR` or the SDK library/fonts path.

This supports the hypothesis that the Windows offscreen run did not discover a system Emoji fallback. It does **not** establish that Windows Server lacks the font, that Windows 11 has the same failure, or that the native Windows Qt text path fails. The next bounded diagnostic is to check installed Emoji-font availability and compare the same executable/fixtures under an explicit system font directory and the `qwindows` plugin, saving separate reports/images. Keep Source Han Sans and text metadata unchanged. Neither follow-up has run: Chrome switched to the user's unrelated browsing session, so UI operations were paused after the completed download.

The `libpng Read Error` text in the composition log corresponds to the existing deliberately truncated-PNG rejection case. The native process still returned 0 after all guard checks; that message is not by itself an unexpected composition failure.

## Scope still open

Windows 11 same-device Qt/Avalonia comparison, native window/IME/DPI paths, full performance/resource gates, original-Mac tolerance decisions, AI/HEIC Windows feasibility and the remaining M0–M7 requirements remain open. The kit and original result package remain immutable; this review adds evidence and corrects the interpretation of preparation checks without changing the product scope.
