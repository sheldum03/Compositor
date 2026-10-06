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
3. `e80ede0` makes rendering, hit testing, caret stops, and selection rectangles consume one `TextLayoutSnapshot`; checks cover wrapped box text, CRLF offsets, combining marks, and end-of-content caret placement.
4. The formal Avalonia window routes a left click on an editable text layer through the same transform-aware hit test and places the sidebar `TextBox` caret at the returned UTF-16 boundary. The canvas then draws the caret from that same layout result; changing the sidebar selection produces transformed canvas selection polygons. The App Headless check clicks the end of a rotated/flipped text layer and verifies the sidebar caret plus both overlays.
5. A missing-font cache project exposes an explicit font choice and keeps the content editor disabled. Selecting an installed font reloads the editable layer assets, redraws the text, and unlocks the editor; no fallback font is selected silently. Undo restores the missing-font read-only state and redo restores the explicit replacement.
6. `1a2f3a8` validates imported font bytes through a temporary file before moving them into the user directory, restores only catalog entries whose file hash still matches, and initializes the font library when the formal window starts. Workflow checks cover rejected damaged imports leaving no file and a mismatched persisted hash being removed from the catalog.
7. `50a3fa2` inspects an imported face before registration and rejects a different SHA-256 with the same normalized family and face index. The temporary file is removed, the catalog keeps the original entry, and the formal command path exposes the rejection as an operation error instead of silently replacing the registered font.
8. The font selector now exposes distinct system style choices and imported `family / face N` identities. Exact face selections are used for rendering and written through the existing v8 `fontPostScriptName` field; a legacy family-only value becomes unavailable when several faces share that family instead of silently choosing one. Workflow checks cover two TTC faces, ambiguous legacy identity, render, save/reopen, and catalog restore; App Headless covers selecting a second same-family choice in the formal window and preserving it through save/reopen.

Command on the development host:

```text
dotnet run --project windows/Compositor.Workflow.Checks -c Release --no-build -p:RuntimeIdentifier= -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-workflow-font-faces-20261006-2
```

Exit code: `0`.

The Windows production core matrix for `4356dde`: [run 37420433872](https://github.com/sheldum03/Compositor/actions/runs/37420433872) passed Smoke, Imaging, Workflow, SaveCrash, and App. The Workflow case includes the persistent font library, UTF-16 and transform-aware hit tests, shared caret/selection geometry, and the font import/recovery checks. The App case includes point/box creation, the formal text editor regressions, and the explicit missing-font resolver.

The local App Headless check was rerun after the face-selector integration:

```text
dotnet run --project windows/Compositor.App.Checks/Compositor.App.Checks.csproj -c Release --no-build -- windows/Compositor.Imaging.Checks/fixtures /tmp/compositor-app-font-faces-20261006-1
```

It passed the production soft-brush/pointer workflow, dialog/window workflow, text caret/selection overlay check, explicit missing-font resolver check, and resolver undo/redo read-only-state check. The local Workflow Checks Release build and run also passed the font import/recovery checks and all existing checks.

The Release build also succeeded with `RuntimeIdentifier=win-x64`; the executable was not run on this macOS host. Windows execution through the UU-connected Windows 11 machine remains a separate acceptance step.

## Deliberately not covered

- No Windows IME, candidate-window validation, or native Windows input result. The current window slice covers the Avalonia canvas caret/selection overlay and sidebar `TextBox` routing in Headless checks; it does not prove Windows text services, DPI behavior, or production-machine input.
- No full TextKit-equivalent shaping; the slice uses Skia line layout with explicit tracking and simple box wrapping.
- The import dialog still imports the default face of a font file; it does not yet expose a face-index chooser. Damaged-font recovery UX and full user-selected font persistence remain outside this slice.
- No complete M4 text history/transform/rotation/mirror workflow or cross-platform pixel tolerance decision.
- Text projects whose cache dimensions are local layer bounds remain on the cached/read-only project path; this slice does not widen general layer-raster editing.
