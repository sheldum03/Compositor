# Production font recovery diagnostics — 2026-10-06

This evidence covers the bounded W-024 recovery UX slice. It does not close M4 or Windows real-machine acceptance.

## Implemented contract

- `FontLibrary.Restore` still removes catalog entries whose file is missing, whose SHA-256 no longer matches, whose face cannot be loaded, or whose catalog identity is duplicated.
- Each removed entry now produces a `FontRecoveryIssue` with the persisted file name and a stable reason: `文件缺失`, `文件哈希不匹配`, `字体面不可用`, or `catalog 重复条目`.
- `FontRecoveryReport` exposes the issue list, count, and a user-facing message containing the count, file names, and reasons. A clean library returns an empty report.
- `MainWindow` keeps the existing normal startup status when the report is clean. When recovery removed entries, the startup status shows the report instead of silently presenting a normal startup message.

## Regression evidence

`Compositor.Workflow.Checks` builds one catalog containing a missing file, a copied font with a wrong hash, and a damaged font with a matching hash. It verifies that all three entries are removed from the persisted catalog and that the report identifies each file and reason.

`Compositor.App.Checks` constructs a window with a missing catalog entry and verifies the formal startup status contains the count, file name, and `文件缺失` reason after catalog cleanup.

The Windows production core matrix for implementation commit [`53ba7ba`](https://github.com/sheldum03/Compositor/commit/53ba7ba19f8b64769ee23e983a18b11c8a564e1c) passed all five jobs in [run 37430507670](https://github.com/sheldum03/Compositor/actions/runs/37430507670). The paired pull-request run [37430512440](https://github.com/sheldum03/Compositor/actions/runs/37430512440) also passed Smoke, Imaging, Workflow, SaveCrash, and App.

## Deliberately not covered

- No Windows real-machine launch, native file-dialog behavior, DPI/multi-monitor behavior, or Windows font-service acceptance.
- No IME, candidate-window, complete shaping/reflow, or GUI/IME hit-test acceptance.
- No complete damaged-font quarantine UX beyond the startup diagnostic and catalog cleanup, and no complex v8 text writeback.
