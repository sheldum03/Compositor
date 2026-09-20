# Qt shared text document — W-007 preparation

This fixed-corpus Qt Widgets experiment uses an editable `QGraphicsTextItem` and its own `QTextDocument`. Preview, export, caret hits and preedit use that same document layout. It exercises twelve F11 styles on macOS, not native Windows IME or production text transactions. The original manifest and cached PNG are never written; D-11's editing/scale/missing-font policy remains open.

## Reproduce

Build using [README.md](README.md), then run from the repository root into a new directory:

```sh
QT_QPA_PLATFORM=offscreen QT_SCALE_FACTOR=1 <BUILD_DIRECTORY>/qt_probe --text \
  docs/windows/fixtures/extended <NEW_OUTPUT_DIRECTORY>
```

The build now embeds the existing Source Han Sans SC resource through a CMake reference and copies its existing OFL notice into `Licenses/`. It does not duplicate the source font or install it in the OS. Runtime SHA-256 and Chinese glyph checks confirm the requested face. Actual shaped fonts are recorded as `Source Han Sans SC` and `.Apple Color Emoji UI` on this host. The latter is an OS fallback, not a portable/bundled result. An observed Qt diagnostic about populating the initial generic Sans Serif family aliases is retained in the log; actual runs are verified separately.

All 47 extended fixture hashes are checked before and after running. Cases cover 72/300 document dpi, point/box text and left/center/right alignment, with Chinese/English/emoji/combining accents, explicit paragraphs, tracking, additive line spacing, text/layer opacity, 13° rotation and horizontal flip. These font sizes are integral 18/75 pixels; the fixture adapter rounds pixel sizes and makes no general fractional-font-size claim.

## Shared layout and input path

`QGraphicsTextItem` retains Qt's text editing control; no custom shaping, cursor engine or framework fork is added. A plain-text format applies pixel font size, absolute tracking, `LineDistanceHeight` and color. Point text measures its unwrapped ideal width; box text uses its supplied width. The resulting layout is fitted to the fixture's existing layer rectangle for comparison. This is not the chosen production geometry policy.

`QGraphicsScene::render` invokes the actual item's paint method. Export calls `drawContents` on the **same QTextDocument**, with the item's scene transform, opacity and clipping rectangle. It does not load a PNG or create a second text layout. Export intentionally contains text/preedit glyphs without editor cursor or selection decorations.

Mouse press/release events are synthesized through `QGraphicsScene::sendEvent`. For each sampled layout line, the canvas point is mapped back through the transformed item, then the actual editor caret is compared with the document layout's hit result and Unicode grapheme boundaries. This exercises the item event handler, not native pointer dispatch.

Synthetic `QInputMethodEvent` preedit stays outside committed text/history. Cancellation must restore the original pixels exactly; commit, undo, redo and selection replacement use Qt's existing document/edit control. A second focused preedit is queried through an activated **offscreen** QGraphicsView. Four view scales include view scroll offset and the layer's transform in the returned input cursor rectangle. These are logical view zoom values, not OS monitor DPI. No native input method or candidate window runs.

Three issues found by added assertions were resolved in the probe:

- A document-backed layout returned no glyph runs when using the default implicit text length. Passing the actual block length records the shaped fonts; no cache modification is needed.
- An inactive view returned an empty input-method query. Showing/activating the offscreen view and processing Qt events establishes the real focus path before querying.
- Preedit in the 300 dpi left box differed by four edge pixels when export omitted the item's clip. Applying the same bounding rectangle restored exact pixels. No image tolerance was added.

## Final local observations

Qt 6.11.2, AppleClang 21, Release, macOS 26.5.1 arm64; output `compositor-qt-text-08`:

| Check | Result |
| --- | --- |
| Initial item / shared-document export | 12/12 exact and nonempty; actual item paint asserted |
| Preedit item / export | 12/12 exact, with visible preedit glyphs and underline; cursor attribute hidden |
| Cancel / commit / undo / redo / selection replacement | 12/12 passed; cancel restores exact baseline PNG, preedit does not create undo entries |
| Transformed synthetic mouse hits | 2,631; editor caret equals layout hit; no grapheme splits |
| Coordinate round trip | Maximum 2.97e−13 local pixels; explicit bound 0.001 pixels, separate from image equality |
| Focused view input cursor | 48 queries: 0.75×, 1×, 1.5×, 2× for every style; valid rectangles match scene/view/scroll mapping |
| Box resize | 6/6 narrower boxes rewrap, shared preview/export remain exact, restored width restores pixels |
| Spacing / tracking | +3/−3 moves second paragraph by that amount; tracking 1.25 changes advance, changing it to 2 relayouts |
| ASan/UBSan | Own C++ and native C instrumented, passed; prebuilt Qt uninstrumented and leak detection disabled |
| Stable artifacts | 72 PNGs byte-identical between final Release, preceding Release and sanitizer output |
| Existing paths | 20 compositor specimens passed with unchanged nontiming report; nine brush PNG/package files unchanged; native CTest 1 passed |

Point text measures 286.046875×60 at 72 dpi and 1192.328125×243 at 300 dpi. The 360-pixel 300 dpi box wraps to seven lines and measures 850.5 pixels high. Actual fallback fonts and per-case export/preview nanoseconds are in the report. These small timings do not rank frameworks or establish text performance acceptance.

Mac reference comparisons remain **nonexact in all twelve cases**: 4,720–51,784 different pixels; maximum premultiplied channel error 133–166/255. The contact sheet shows differences in line metrics, glyph placement and emoji; it does not prove a single cause. No cross-platform text tolerance is accepted.

Whole-process `/usr/bin/time -l` reported 1.50 s wall and 127,680,512 bytes maximum RSS, including font resource loading, all cases, PNG/difference generation, synthetic input and offscreen view activation. This is not Windows private RAM/VRAM or a long-running resource test. Mac product/native C code was unchanged, and no Mac XCTest/full-suite rerun was performed for this isolated path.

Evidence: `docs/windows/evidence/qt-text-{macos,preparation}.json` and `qt-text-contact-sheet.png`. Regenerate the sheet with the existing Pillow script:

```sh
python3 scripts/windows/render-probe-contact-sheet.py docs/windows/fixtures/extended \
  <QT_OUTPUT_DIRECTORY> <NEW_SHEET.png> --text --candidate-label 'Qt CPU'
```

Still required: native Windows Pinyin/candidate geometry at multiple monitor DPIs, focus/activation/composition interruption, real keyboard/pointer/clipboard, text transaction/save/reopen, draft sizing policy, missing-font/cache behavior, font import/removal/conflicts, complex bidi/emoji coverage and matched resource/latency measurements. Both framework prototypes remain preparation only; W-007/W-008 and M1 are unpassed.

Public API references: [editable graphics text item](https://doc.qt.io/qt-6/qgraphicstextitem.html), [input method events](https://doc.qt.io/qt-6/qinputmethodevent.html), [additive line height](https://doc.qt.io/qt-6/qtextblockformat.html). The pinned 6.11.2 sources were checked for [document glyph-run retrieval](https://github.com/qt/qtbase/blob/v6.11.2/src/gui/text/qtextlayout.cpp), [scene input query](https://github.com/qt/qtbase/blob/v6.11.2/src/widgets/graphicsview/qgraphicsscene.cpp), [view query mapping](https://github.com/qt/qtbase/blob/v6.11.2/src/widgets/graphicsview/qgraphicsview.cpp) and [item painting](https://github.com/qt/qtbase/blob/v6.11.2/src/widgets/graphicsview/qgraphicsitem.cpp).
