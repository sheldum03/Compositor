# Native lifecycle and S05 checks

`--s02-small-window <brush-fixtures> <new-output> <native-library>` opens a real 700×700 window. Expected: exit 1, `completed=false`, `updatesAttempted=0`, and the explicit 1000×1000 viewport requirement in `error`. It must not crash during framework startup. The normal `--s02-window` path is unchanged in workload; start its replay, then close that process's window to check cancellation, retained partial report and prompt termination. `windowClosed=true` plus cancellation is expected; a completed report is not.

The replay begins on the next UI dispatcher turn. Synchronous rejection directly inside `Opened` previously called `Shutdown` while `ClassicDesktopStyleApplicationLifetime.StartCore` was still starting, producing a NullReferenceException. The native macOS reproducer failed before the change and now exits 1 with an empty stderr and the expected rejection report. Windows R3 verification also passes at 150% DPI: exit 1, empty stderr, zero updates. Closing a running native replay after 4254 updates exits in about 316 ms with a retained cancellation report and empty stderr; the PowerShell exit-code observation was null and is not claimed verified.

`--s05-window <brush-fixtures> <new-output> <native-library>` uses the same native software-rendered viewport as S02. It executes the S05 sequence of 100 local edits, all 100 undos, all 100 redos, save/reopen and document release, repeated three times. Each edit is a 160px, 40%-opacity soft brush with 21 deterministic points over a 10×10 grid in the 4000×4000 document. This is a resource workload, not the 800px S02 performance gate. It retains the real history through each round and verifies every restored snapshot digest. Three saved projects must reopen with exact pixels.

Each point waits for a real canvas callback. Reports retain all 6300 callbacks, 300 commit times and resource samples. No forced collection occurs during editing or between rounds. One second after closing each document, natural resource measurements are recorded; only after the third sample does a diagnostic full GC check weak references to closed sessions. The edit method returns before this check, so JIT-lived temporaries in the test do not keep its last session alive. The initial one-edit reproducer caught that test-harness problem; the scoped version and full workload pass locally.

Private memory, working set, managed allocation estimates and Windows handle counts are observations. `LastGcCommittedBytes` and `LastGcFragmentedBytes` describe the last GC snapshot, not a simultaneous accounting of all current native allocations. Unsupported private memory/handle values are null. Diagnostic collection is not production memory management and does not prove native resources are bounded. `resourceAccepted` stays false until independent trend review; there is no invented S05 tolerance or VRAM claim.

`--s05-check` runs the same workload through the real Skia headless callback for local correctness. Review reports with:

```sh
python3 experiments/windows/avalonia/review-s05.py <windows-report.json>
# Local diagnostics only:
python3 experiments/windows/avalonia/review-s05.py <local-report.json> --allow-local
```

The reviewer requires all edits, sequential frame IDs, undo/redo checks, identical round digests and zero final retained documents. A missing-frame negative control fails. It reports natural post-close growth instead of marking resource stability passed from correctness alone.

`replay-native-input.py <pid> <new-log.json> <actions.json>` is a prepared Windows OS-input helper, not yet executed on Windows. It selects exactly one observed Compositor window, requires it to remain foreground, maps logical client coordinates using actual DPI, and releases any keys/buttons it held on failure. Actions are `click`, a multi-point `path` (optional `cancel` before release), `keys` with virtual-key codes, and ASCII `text`. It uses [Windows SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput), with [MOUSEINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput) and [KEYBDINPUT](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput). This can test native OS event delivery, but cannot replace physical hardware, pen-pressure or human candidate-position acceptance. Do not target unrelated applications or count successful injection as correct editing without inspecting the application's events and saved pixels.
