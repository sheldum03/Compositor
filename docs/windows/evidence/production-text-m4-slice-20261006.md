# Production text M4 slice — 2026-10-06

This evidence covers one bounded W-023 slice. It does not close M4 or the Windows text acceptance gate.

## Implemented contract

- v8 `text` metadata is accepted by the Windows Core manifest gate and exposed as `ProjectSession.TextLayers`.
- A text layer keeps its PNG as the authoritative cache.
- When the requested PostScript/family name matches an installed Skia font family, the Imaging path redraws the text from the v8 metadata at the document resolution before the normal layer transform.
- When the font is unavailable, the path returns the cached raster unchanged and reports that the user must choose a font. It never substitutes a fallback font silently.
- Save/reopen retains the text object and its content, style, layout, and font name.

## Fixed regression

`Compositor.Workflow.Checks` now covers:

1. `extended/F12-missing-font.comp`: metadata is present, the missing font is reported, the rendered text raster is byte-identical to the cache, and rendering does not modify the source manifest.
2. A generated full-canvas v8 text project using the first installed font family: the available-font path redraws visible pixels rather than returning the transparent cache, the normal preview uses that redraw, and save/reopen keeps the text metadata.
3. `ea1b035` makes the formal `EditorWorkspace` read-only when any text layer font is unavailable; cached preview and export remain available, while Save/Save As are rejected. This closes the regression found by Windows App Checks on `e42ea0d`.

Command on the development host:

```text
dotnet run --project windows/Compositor.Workflow.Checks -c Release --no-build -p:RuntimeIdentifier= -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-text-m4-1791259065
```

Exit code: `0`.

The Windows production core matrix passed on `12fabb4`: [run 37414986552](https://github.com/sheldum03/Compositor/actions/runs/37414986552) passed Smoke, Imaging, Workflow, SaveCrash, and App. The App case includes point-text and box-text creation, missing-font read-only preview, and the available-font text editor regressions; the Workflow case includes the persistent font library and UTF-16 hit-test contract checks.

## Editor and font library slice

- `819727d` adds a bounded editable-font transaction: content, font family, size, and alignment are validated, redrawn from v8 metadata, committed as one history step, and retained across undo/redo, save, and reopen. The formal Avalonia window exposes the controls only for editable text layers.
- `b88c8a4` adds a persistent `FontLibrary` under the user's local application data. It records hash-addressed `.otf`/`.ttf`/`.ttc` bytes in `fonts.json`, deduplicates repeated imports, restores registered faces on process start, supports explicit TTC face indexes, and exposes a formal-window import entry.
- `2c05bcd` adds a formal point-text creation path. It selects an available font, renders the initial full-canvas cache from v8 metadata, inserts a text layer with one history step, and exposes the layer in the existing editor panel. Headless checks cover creation, editing, save, reopen, and grouped-project button protection.
- `0fb2fcd` adds a second formal entry for box text, with a bounded width and the same initial redraw, editing, history, and save/reopen path. Headless checks cover both creation buttons and grouped-project protection.
- `12fabb4` adds a small Skia line-layout hit-test contract. It returns a stable UTF-16 content offset, line index, and inside/outside result for point and bounded box text; checks cover an English point layout plus Chinese, Emoji, combining-mark, and wrapped box content.
- Local App/Workflow checks passed after these changes. The Windows production run above passed all five jobs, including the editor UI and font-library regressions.

The Release build also succeeded with `RuntimeIdentifier=win-x64`; the executable was not run on this macOS host. Windows execution through the UU-connected Windows 11 machine remains a separate acceptance step.

## Deliberately not covered

- No Windows GUI text editor, IME, caret selection, or candidate-window validation. The new hit-test contract is an engine check, not a real Windows input result.
- No full TextKit-equivalent shaping; the slice uses Skia line layout with explicit tracking and simple box wrapping.
- No complete point/box text tool for cached projects, IME, full reflow, conflict-font visual resolver, damaged-font recovery policy, or complex v8 semantic writeback.
- No complete M4 text history/transform/rotation/mirror workflow or cross-platform pixel tolerance decision.
- Text projects whose cache dimensions are local layer bounds remain on the cached/read-only project path; this slice does not widen general layer-raster editing.
