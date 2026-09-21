# 4K CPU brush path — W-008 preparation

This experiment extends the fixed compositor probe with local brush updates, a provisional tail, immutable tile snapshots, immediately starting the next stroke, undo/redo and project/export round trips. It is not a finished brush tool or a Windows performance result. D-03 (CPU/Metal reference and tolerances) remains open.

## Reproduce

Build the native library and restore/build the .NET project as in [README.md](README.md), then run from this directory:

```sh
dotnet run -c Release --no-build -- --brush ../../../docs/windows/fixtures/brush \
  <NEW_OUTPUT_DIRECTORY> <ABSOLUTE_NATIVE_LIBRARY_PATH>
```

The shared bootstrap loads the existing native module; this new brush algorithm itself is C#, not one of the eight legacy C routines. No new native ABI or production Mac code is introduced. The runner verifies all nine brush-corpus hashes before and after running. Input is exactly `soft-crossing-4k.json`: 4000×4000, 800 px, hardness zero, opacity 0.4, two 121-point strokes. It does not substitute the Metal reference for the CPU reference.

Output includes `first.png`, `final.png`, corresponding full-resolution Avalonia previews, CPU difference images, `brush.comp`, reopened PNG and `brush-report.json`. The saved layer currently uses the full document coordinate grid with transparent margins; it is a valid v8 package, not a cropped-raster implementation. The report retains every append/preview time, each commit time, tile/byte counters, GC collections, sampled working set, and separate CPU/Metal differences. A successful exit proves the stated checks, not reference equivalence or performance acceptance.

## Implemented data flow

- `SoftBrushStroke` follows the Mac CPU dabs path: centripetal Catmull–Rom sampled at at most 2 px steps, 2.5%-diameter dab spacing, grid-snapped placement and a 24-stop normalized Gaussian tip. Coverage uses rounded integer Screen accumulation. It restores the previous provisional tail before settling the next curve segment, saving/restoring dab-spacing state around the tail.
- Per-stroke coverage and writable RGBA allocate only in touched 256×256 tiles. Each publish rebuilds the dirty rectangle from the original tile plus the **whole stroke's** coverage/opacity; overlapping dabs do not repeatedly apply the layer color. The first 40% stroke is checked against its 102/255 alpha cap.
- `TiledRaster` snapshots hold private immutable byte arrays. Commit clones changed tiles and shares untouched ones. Starting the second stroke copies only tiles it touches. No full-size backing array or image export occurs before both commits. This reference snapshot keeps a fixed document grid; general transforms, masks and cropped grids are later work.
- `BrushSession` makes one history entry per nonempty stroke, supports cancel/undo/redo, preserves redo after cancel, replaces redo on a new branch, and rejects writes to a committed stroke. Saved snapshot identities and hashes are checked after later edits and export.
- Every pointer update renders through the real Avalonia Skia custom-control callback into a 1000×1000 CPU bitmap at 25%. Mutable RGBA is copied into native images for drawing so no deferred draw borrows writable managed memory. This is safe for the prototype but expensive: the first and second stroke copy approximately 1.64 and 4.36 GiB respectively over their 121 previews. A cache/upload strategy must be measured before claiming a production data path.
- After both commits, 4000×4000 preview and offscreen export are compared exactly. Full raster allocation is permitted at this export boundary. The v8 package is reread in C# and checked against final pixels.

The provisional-tail check also renders each complete path **without ever drawing provisional tails**, using the same curve/dab primitives. Its sparse pixels must match the interactive path exactly after each stroke. This checks restoration/publication state independently of the Mac comparison; it does not prove the shared mathematical primitives are equivalent to Core Graphics.

## Observed on macOS 26.5.1 arm64

.NET 10.0.12 / SDK 10.0.401, Avalonia 11.3.22, Release. Final diagnostic run `avalonia-brush-02`:

| Observation | First stroke | Second stroke |
| --- | ---: | ---: |
| Touched tiles | 92 | 106 |
| Commit pixel-copy bytes | 24,117,248 | 27,787,264 |
| Mouse-up/commit, including final curve flush | 10.077 ms | 8.620 ms |
| Append + CPU preview P95 | 12.999 ms | 12.955 ms |
| Max channel / alpha difference vs Mac CPU | 3 / 3 | 4 / 4 |
| Pixels with CPU error >1 | 28,962 | 47,834 |
| Mean absolute RGBA error vs CPU (0–255 units) | 0.02829665625 | 0.046540328125 |

All 242 update draws occurred. The second snapshot shares 56 unchanged tiles with the first. The sampled working-set high-water mark during update/preview was **240,910,336 bytes**; this excludes the later correctness replays and full export/decoding, is not peak private memory, and is not the V-08 RAM result. Managed allocation and GC counts are retained in the report; native frame/image copies are substantial. These are only two 40%-opacity strokes with no native window, real pointer input, GPU or display presentation. S02 requires 100% opacity, blank/existing content, at least 30 strokes and the fixed Windows machine. Do not compare these Release timings directly to the existing Mac Debug fixture timings or use them to choose a framework.

CPU/Metal reference comparisons remain **observations without accepted tolerances**. The Metal differences reach 7/255. The contact sheet shows errors concentrated around soft falloff bands; model inspection found no obvious grid seams or stale straight tails, but is not human Windows acceptance.

## Tip rasterization diagnostic

The Mac private `BrushStroke.tip` uses `CGGradient` to rasterize the 24-stop tip. This standalone diagnostic repeats that construction without changing the application:

```sh
swift macos-tip-diagnostic.swift <NEW_800x800_GRAY8_FILE>
```

Two runs produced identical 640,000 bytes. Compared with analytic radial pixel-center sampling and nearest integer quantization, 143,468 tip samples differ by one unit. The Core Graphics output itself has 150,388 differing horizontally mirrored samples, at most two units apart, while the analytic radial tip is symmetric. This is consistent with backend quantization/dithering; it does **not** establish dithering as the sole cause of every final stroke difference. No Mac-generated tip is shipped to, or used as input by, the C# brush. Numeric calibration and D-03 remain pending.

## Mac readback and remaining scope

From the repository root after running the probe:

```sh
TEST_RUNNER_AVALONIA_BRUSH_DIR=<ABSOLUTE_OUTPUT_DIRECTORY> \
xcodebuild -project Compositor.xcodeproj -scheme Compositor \
  -configuration Debug -destination 'platform=macOS' \
  -disableAutomaticPackageResolution -parallel-testing-enabled NO \
  -testLanguage en -testRegion US \
  '-only-testing:CompositorTests/WindowsBrushFixtureTests/avaloniaBrushPackageReopensWithExportedPixels()' \
  CODE_SIGNING_ALLOWED=NO test
```

The gated test passed: actual Mac ProjectStore/ImageExporter reads the C# package, preserves dimensions/resolution and exported pixels, saves again and rereads with the same pixels. It explicitly skips when the output-directory environment variable is absent. The tested `brush-01` package and final `brush-02` package are byte-identical; their first/final PNGs are also identical. The existing 20-specimen compositor probe was rerun after sharing the drawing-control entry point and still passes unchanged.

Evidence lives in `docs/windows/evidence/avalonia-brush-{macos,summary,preparation}.json` and `avalonia-brush-contact-sheet.png`. Regenerate the visual diagnostic with Pillow 11.3.0:

```sh
python3 scripts/windows/render-probe-contact-sheet.py docs/windows/fixtures/brush \
  <PROBE_OUTPUT_DIRECTORY> <NEW_CONTACT_SHEET.png> --brush
```

Not implemented/verified: actual mouse/pen events, GPU uploads/presentation, transformed or selected strokes, hard tips, erasing/other brush tools, clipped/masked brush composition, cancellation under background work, bounded production history, S02/S04/S05 stress, global memory budget or process leak checks. No M1 selection or Windows 1.0 acceptance is claimed.

## User-reported Windows 11 run

On Windows 11 Pro x64 Build26200, the user reported preparation checks passed, 121 samples per stroke, 242 custom-control updates, 56 shared tiles and 13 session checks. Update plus preview P95 was 27.5388 / 29.166 ms; commit was 25.5688 / 24.0438 ms. These P95 observations exceed the planned 16.7 ms target, but this two-stroke, 40%-opacity headless workload is not the S02 acceptance workload. The configured RTX 4090D does not imply GPU rendering was tested.

See [Windows 11 brush evidence](../../../docs/windows/evidence/avalonia-brush-windows11.json). The original archive has since been received: independent PNG checks match all four CPU/Metal difference records, and both outputs exactly match the earlier Mac Avalonia output. The actual Mac reader/exporter opens the Windows brush project, then saves and reopens it with unchanged pixels. See the [artifact review](../../../docs/windows/windows11-results-review.md) for raw timings, scope and the passing readback test. Functional preparation passed; performance and reference equivalence are not accepted.
