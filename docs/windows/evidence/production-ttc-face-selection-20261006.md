# Production TTC face-index selection — 2026-10-06

This evidence closes the bounded import-dialog slice of W-024. It does not close M4 or Windows real-machine acceptance.

## Implemented contract

- `FontLibrary.EnumerateFaces` probes the selected OTF/TTF/TTC file through Skia and exposes each valid face index with its family identity.
- A multi-face file opens an explicit Avalonia dialog. The user can select one face or cancel; cancel leaves the catalog and current selection unchanged.
- Only the selected face index is passed to `FontLibrary.Import`. Existing temporary-file validation, same-family/face conflict rejection, hash-based recovery, deduplication, and imported `family / face N` selection tokens remain in force.
- The formal window selects the imported token after a successful import, so the selected face survives subsequent text editing and save/reopen. The macOS Headless runtime used for local checks did not expose a second face for the bundled or system TTC candidates; that branch is recorded as skipped locally while the Windows App job above exercised it.

## Regression evidence

`Compositor.Workflow.Checks` verifies that the repository TTC fixture exposes distinct face indexes and selection names. `Compositor.App.Checks` opens the formal dialog, verifies the explicit face selector and cancel button, checks that cancellation does not add a catalog entry, selects face index 1, and verifies the resulting token and catalog entry.

Persisted missing-file, hash-mismatch, unavailable-face, and duplicate-catalog cases now surface in the formal startup status; bounded evidence is in [font recovery diagnostics](production-font-recovery-diagnostics-20261006.md).

The Windows production core matrix passed on the final evidence commit [`53ba7ba`](https://github.com/sheldum03/Compositor/commit/53ba7ba19f8b64769ee23e983a18b11c8a564e1c): [run 37430512440](https://github.com/sheldum03/Compositor/actions/runs/37430512440) passed Smoke, Imaging, Workflow, SaveCrash, and App. The prior implementation run [`37427886072`](https://github.com/sheldum03/Compositor/actions/runs/37427886072) also passed the full matrix. The first attempt [`3819246`](https://github.com/sheldum03/Compositor/commit/38192468ae664cdfd3b0a577abf2d365b3e100f6) exposed a missing test helper; `0bd53f0` supplied that helper and moved the App fixture lookup to the repository TTC fixture so the check is deterministic on Windows.

## Deliberately not covered

- No Windows real-machine launch, DPI/multi-monitor, native file dialog, or Windows font service acceptance.
- No IME, candidate-window, full shaping/reflow, or GUI/IME hit-test acceptance.
- No complete damaged-font quarantine UX beyond the startup diagnostic and catalog cleanup, or complex v8 text writeback.
