# Qt tiled soft brush — W-007 preparation

This isolated CPU path replays the same fixed two-stroke workload as [Avalonia](../avalonia/BRUSH.md). It exercises local commits, the immediate next stroke, history, widget painting, export and project readback on Mac. It does not complete W-007, M1 or Windows performance acceptance.

## Reproduce

Build the pinned Release target using [README.md](README.md), then run from the repository root with a new output directory:

```sh
QT_QPA_PLATFORM=offscreen QT_SCALE_FACTOR=1 <BUILD_DIRECTORY>/qt_probe --brush \
  docs/windows/fixtures/brush <NEW_OUTPUT_DIRECTORY>
```

Input is the frozen `soft-crossing-4k.json`: 4000×4000 transparent canvas, two paths of 121 events each, diameter 800, hardness 0 and whole-stroke opacity 0.4. Nine fixture hashes are checked before and after the run. Every event invokes an actual QWidget paint callback into a 1000×1000 target at 25%; final correctness previews are 4000×4000. Pointer input is replayed, not native mouse/pen input.

## Algorithm and ownership

`brush.cpp` ports the existing C# experiment's 24-stop normalized Gaussian tip, 2.5% dab spacing and centripetal Catmull–Rom interpolation. A provisional straight tail backs up coverage and restores the spacing state before the next settled segment. Screen accumulation builds coverage; publication composites the entire stroke coverage against the original tile, capped by stroke opacity. Both strokes must match a replay that never creates provisional tails. This oracle shares curve/dab primitives and tests tail replacement, not an independent brush implementation.

Snapshots hold 256×256 premultiplied RGBA8 QImage tiles. A stroke owns coverage, original and mutable tiles. Commit copies changed tiles and implicitly shares unchanged QImages; history snapshots expose no mutable pixels. Widget drawing synchronously borrows tile storage. There are no explicit application pixel copies for preview; Qt/backend internal allocations are not counted. A full raster is allocated for export and full-size correctness checks, after both measured commits.

The runner checks one history entry per stroke, immutable prior snapshots, undo/redo identity, cancellation preserving redo, empty commit, branch replacement, repeated flush, active/finished-state guards, first-stroke alpha ≤102, a nonempty changed second result and exact full preview/export pixels. It writes a v8 JSON/PNG package and checks Qt reread pixels. This uses the restricted fixture adapter from the compositor probe, not production ProjectIO.

## Observed final Release run

Mac arm64, Qt 6.11.2, AppleClang 21, Release; final output `compositor-qt-brush-02`:

| Measurement | First stroke | Second stroke |
| --- | ---: | ---: |
| Real widget updates | 121 | 121 |
| Touched / stored tiles | 92 / 92 | 106 / 162 |
| Commit pixel bytes copied | 24,117,248 | 27,787,264 |
| Commit duration (ms) | 7.155042 | 8.219416 |
| Append + preview P95 (ms) | 9.740917 | 10.735667 |
| Cumulative pixels published (bytes) | 545,336,064 | 561,024,044 |
| Cumulative tail backups (bytes) | 130,023,424 | 133,496,832 |

The second snapshot shares 56 unchanged tiles. No full source raster was exported before its commit. These are two observations, not a framework ranking or S02: that gate requires 100% opacity, 30 strokes and both blank/existing layers on the reference Windows machine. GPU upload, history budgets, hard tips, erasing, transformed/clipped/masked brush editing and native input remain untested.

Whole-process `/usr/bin/time -l` reported 7.29 s wall and 452,689,920 bytes maximum RSS. It includes correctness replays, full exports, PNG decoding, difference images and project reread. Avalonia's earlier 240,910,336-byte sample covered updates only; these resource values are not comparable. Neither is Windows private RAM, VRAM or an S05 measurement.

Release and ASan/UBSan passed; own C++ and native C were instrumented, prebuilt Qt was not. Leak detection was disabled. All nine PNG/package files were byte-identical between the initial Release, final Release and sanitizer outputs; timing reports are excluded. The existing 20-case compositor report stayed identical after removing per-case timing, and the Release native contract CTest passed.

## Pixel comparisons and Mac readback

Both `first.png` and `final.png` match the Avalonia probe exactly in premultiplied RGBA8. Reproduce with Pillow and NumPy (arguments are the Qt and Avalonia output directories):

```sh
python3 - <QT_OUTPUT_DIRECTORY> <AVALONIA_OUTPUT_DIRECTORY> <<'PY'
import sys
from pathlib import Path
import numpy as np
from PIL import Image

def pixels(path):
    a = np.array(Image.open(path).convert('RGBA'), dtype=np.uint16)
    a[:, :, :3] = (a[:, :, :3] * a[:, :, 3:] + 127) // 255
    return a

for name in ('first.png', 'final.png'):
    a, b = [pixels(Path(root) / name) for root in sys.argv[1:]]
    assert a.shape == b.shape == (4000, 4000, 4)
    assert np.array_equal(a, b), name
    print(name, 'exact premultiplied RGBA8')
PY
```

Mac references still differ; no tolerance is accepted:

| Reference | First differing pixels / max channel error | Final differing pixels / max channel error |
| --- | ---: | ---: |
| Mac CPU | 1,122,405 / 3 | 1,875,766 / 4 |
| Mac Metal | 1,340,829 / 7 | 2,269,671 / 7 |

Maximum alpha errors equal the listed channel maxima. The contact sheet shows soft falloff difference bands without apparent grid seams or stale tails; model inspection is not human Windows acceptance.

Run the actual Mac reader/exporter checks with both generated directories:

```sh
TEST_RUNNER_QT_BRUSH_DIR=<ABSOLUTE_QT_OUTPUT> \
TEST_RUNNER_AVALONIA_BRUSH_DIR=<ABSOLUTE_AVALONIA_OUTPUT> \
xcodebuild -project Compositor.xcodeproj -scheme Compositor -configuration Debug \
  -destination 'platform=macOS' -disableAutomaticPackageResolution \
  -parallel-testing-enabled NO -testLanguage en -testRegion US \
  '-only-testing:CompositorTests/WindowsBrushFixtureTests/qtBrushPackageReopensWithExportedPixels()' \
  '-only-testing:CompositorTests/WindowsBrushFixtureTests/avaloniaBrushPackageReopensWithExportedPixels()' \
  CODE_SIGNING_ALLOWED=NO test
```

Both passed: **2 passed / 0 failed / 0 skipped**, 1.138 s test execution. The shared helper retains the existing manifest/dimension/resolution/transform/pixel/save/reopen assertions. Qt input was the initial Release output, whose nine artifacts match the final run. Tests explicitly skip without their output variable. Mac product and native C code were unchanged; the full Mac suite was not rerun.

Evidence: `docs/windows/evidence/qt-brush-{macos,summary,preparation}.json` and `qt-brush-contact-sheet.png`. Qt transformed text/shared layout/native IME and all Windows execution/deployment/acceptance remain open; D-03 pixel tolerance and production framework selection remain undecided.
