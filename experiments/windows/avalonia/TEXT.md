# Shared text layout — W-008 preparation

This fixed-corpus experiment uses a real Avalonia `TextBox`, its input-method client and Skia text rendering on macOS. It is not a native Windows input test or a completed text tool. It does not change `.comp` files, cached text PNGs, font substitution policy or D-11 scale behavior.

## Reproduce

Use the pinned SDK, locked restore, Release build and native library from [README.md](README.md), then run here:

```sh
dotnet run -c Release --no-build -- --text ../../../docs/windows/fixtures/extended \
  <NEW_OUTPUT_DIRECTORY> <ABSOLUTE_NATIVE_LIBRARY_PATH>
```

The common bootstrap loads the native module; text rendering itself uses Avalonia/Skia/HarfBuzz, not the C bridge. All 47 extended-corpus hashes are checked before and after the run. Only the twelve F11 styles are rendered: 72/300 document dpi × point/box text × left/center/right alignment, each with Chinese/English/emoji/combining accents, two explicit paragraphs, tracking, extra line spacing, opacity, 13° rotation and horizontal flip. A successful exit means the assertions below passed, not that Mac differences were accepted.

The existing Source Han Sans SC font is embedded by a project link, without duplicating it in source or installing it in the OS. Its SHA-256 is asserted, and its existing OFL notice is copied to the build output. Each layout reports the actual shaped font families. In the recorded Mac run these are **Source Han Sans SC and Apple Color Emoji**. Emoji is an OS fallback, not a bundled or portable font result.

## Shared layout and input

`SpacedTextPresenter` is a small plain-text adapter. Avalonia 11.3.22's default presenter forwards `LineHeight` and `LetterSpacing` but does not pass additive line spacing. The paragraph property for this spacing is internal. The adapter therefore uses the public `TextBlock.LineSpacing` API and inline annotations as a layout factory, taking a separately created layout and disposing the factory's temporary cached one. `TextPresenter` then owns the returned layout. No framework reflection, private API calls or fork is involved.

`TextBox` retains responsibility for committed text, selection, routed input and undo/redo. Its required `PART_TextPresenter` is connected with explicit bindings. Rendering, hit testing and input-client cursor geometry use `presenter.TextLayout`; a separate export control draws that **same instance**. Export also uses the editor's transform, opacity and clipping boundary. Each measured natural layout is fitted to the fixture's existing layer rectangle for this comparison. This is an experimental placement convention, not a choice of how an edited or scaled production layer should replace cached text.

Two concrete defects were found and corrected during this experiment:

- `TextBox` clips to its bounds by default. Export initially omitted that clipping, leaving one extra low-alpha emoji-edge pixel in the 300 dpi left box case. Matching the boundary restores exact equality; no pixel tolerance was added.
- Splitting unstyled runs at a collapsed selection changes shaping after preedit cancellation. Unstyled text now stays a single text source. Cancellation must restore the original pixels exactly, not merely restore the text string.

Input is driven through the real framework client obtained from `TextInputMethodClientRequestedEvent`, followed by synthetic `SetPreeditText` and routed `TextInputEvent` calls. This verifies the framework integration seam only. It does not invoke an OS input method, candidate UI, native keyboard event or desktop focus lifecycle.

## Recorded checks

macOS 26.5.1 arm64, SDK 10.0.401 / runtime 10.0.12, Avalonia 11.3.22, real headless Skia at 96 framebuffer dpi:

| Check | Result |
| --- | --- |
| Editor / shared-layout export | 12/12 exact premultiplied RGBA8 comparisons; nonempty output and actual export draw asserted |
| Transformed caret hits | 2,631 samples; same caret as local layout, no surrogate/combining-cluster splits |
| Coordinate round trip | Maximum error 0.00008545 local pixels; geometry threshold 0.001 pixels, separate from exact pixel comparisons |
| Additive spacing / tracking | +3 and −3 shift the second line by exactly that amount; 1.25 tracking changes advance; tracking updates invalidate layout |
| Synthetic preedit | 12/12 stays outside committed text, changes layout/pixels, exposes the expected preedit caret |
| Cancel / commit / undo / redo / selection replacement | 12/12; cancellation pixels exact; routed commit and selection replacement update the presenter |
| Input cursor placement | All four cursor-rectangle corners map through the visual tree to the same canvas positions as the layer matrix |
| Corpus integrity | 47/47 hashes unchanged before/after |

The inverse-transform diagnostic originally required `1e-8` pixels. Avalonia's inverse can take its float-based perspective path after matrix arithmetic; observed errors reached roughly `8.5e-5` pixels. The geometric assertion now uses an explicit 0.001-pixel bound while still requiring exactly the same text caret and valid grapheme boundaries. This is not an image-comparison tolerance.

Mac reference differences remain substantial: 4,760–61,446 differing pixels per fixture, maximum premultiplied channel error 133–166/255. The visual comparison shows changed line metrics, glyph placement and emoji appearance; it does not establish the cause of every difference. Point-text natural widths are 287/1193 px and heights 58.569/244.0375 px at 72/300 dpi. The 300 dpi 360-pixel box wraps to seven lines. No cross-platform text tolerance or font-fallback policy has been accepted.

Evidence: `docs/windows/evidence/avalonia-text-macos.json`, `avalonia-text-preparation.json` and `avalonia-text-contact-sheet.png`. The output also contains preview, export, preedit, cancellation and difference PNGs for every style. Regenerate the diagnostic contact sheet with Pillow 11.3.0:

```sh
python3 scripts/windows/render-probe-contact-sheet.py docs/windows/fixtures/extended \
  <PROBE_OUTPUT_DIRECTORY> <NEW_CONTACT_SHEET.png> --text
```

## Remaining gates

The user reported a Windows 11 x64 headless run with all 12 samples matching preview/export exactly, 12 synthetic-input checks passed and zero cancellation pixel mismatches. NativeImeExecuted=false is expected. See [Windows 11 text evidence](../../../docs/windows/evidence/avalonia-text-windows11.json). Only the pasted summary has been received; full reference differences, font resolution, caret diagnostics, images and logs remain uncollected.

Not exercised: Microsoft Pinyin candidate position at multiple display scales, keyboard/pointer dispatch, focus/activation/preedit interruption, live text-box resizing and draft geometry, clipboard, custom-font import/removal, missing-font cache behavior, document text transactions/save/reopen, selection painting, bidi scripts/complex emoji sequences, memory/latency stress or Qt comparison. The template deliberately omits application chrome, scrolling and a native window. No new Mac production code or Mac test was needed; the existing 20-scene compositor regression passes unchanged. W-008 and M1 remain open.

Pinned framework sources used for the adapter and checks: [TextPresenter](https://github.com/AvaloniaUI/Avalonia/blob/627ae9ef921621e27e7aa58df2796fbadb50af88/src/Avalonia.Controls/Presenters/TextPresenter.cs), [TextBlock](https://github.com/AvaloniaUI/Avalonia/blob/627ae9ef921621e27e7aa58df2796fbadb50af88/src/Avalonia.Controls/TextBlock.cs), [input client](https://github.com/AvaloniaUI/Avalonia/blob/627ae9ef921621e27e7aa58df2796fbadb50af88/src/Avalonia.Controls/TextBoxTextInputMethodClient.cs), [paragraph properties](https://github.com/AvaloniaUI/Avalonia/blob/627ae9ef921621e27e7aa58df2796fbadb50af88/src/Avalonia.Base/Media/TextFormatting/TextParagraphProperties.cs), [matrix](https://github.com/AvaloniaUI/Avalonia/blob/627ae9ef921621e27e7aa58df2796fbadb50af88/src/Avalonia.Base/Matrix.cs).
