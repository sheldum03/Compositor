# Avalonia specimen prototype — W-008 preparation

This is an isolated experiment, not a Windows editor or a GUI framework decision. It exercises the first portion of the compositor/round-trip paths in `docs/windows/technical-design.md`. Windows execution and all four complete M1 paths remain required.

## Reproduce

Use .NET SDK **10.0.401** from `global.json`. First build the C library using [the native experiment instructions](../native/README.md). From this directory:

```sh
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet run -c Release --no-build -- ../../../docs/windows/fixtures <NEW_OUTPUT_DIRECTORY> <ABSOLUTE_NATIVE_LIBRARY_PATH>
```

The native file is `compositor_native.dll` on Windows or `libcompositor_native.dylib` on Mac. Its SHA-256 is recorded. The output directory must not exist. The command verifies all 91 hashes in the base corpus before and after execution. It writes 20 renamed `.comp` packages, preview/export/reopened/difference PNGs and `report.json`. Failed assertions return a nonzero process exit; partially generated diagnostic output remains for inspection. A successful run means the stated preparation checks passed, not that the observed Mac differences were accepted.

Dependencies are limited to Avalonia.Headless and Avalonia.Skia **11.3.22**, with the full transitive graph/content hashes in `packages.lock.json`. SkiaSharp resolves to **2.88.9**; its assembly reports **2.88.0.0**. The report records assembly versions, not NuGet versions. `NuGet.Config` explicitly selects the public NuGet feed. These are fixed experiment versions, not a claim about the latest release or an approved production dependency set.

The headless application uses `UseHeadlessDrawing = false` and `UseSkia()`: the default headless drawing backend would not establish real pixel output. `SceneControl` obtains a Skia lease through an `ICustomDrawOperation`; that callback and the offscreen exporter both call `FixtureScene.Paint`. Each specimen asserts that the callback ran and that its rendered PNG matches export exactly at 96 dpi. This proves an actual Avalonia drawing path without proving native-window behavior, display ICC, HiDPI, GPU, input or performance. See [Avalonia headless setup](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform) and the [pinned Skia lease API](https://github.com/AvaloniaUI/Avalonia/blob/627ae9ef921621e27e7aa58df2796fbadb50af88/src/Skia/Avalonia.Skia/ISkiaSharpApiLeaseFeature.cs).

## Deliberate scope

- Fixed F01–F07 and B01–B13: PNG assets, sRGB, 13 blend modes, opacity, integer origins, scaling, pass-through groups and inherited visibility. F04–F07 add layer masks, contiguous same-parent clipping stacks, group masks and a clipped master saturation adjustment. The 64×48 specimens include a source scaled to 44×32. Native Skia high-quality sampling is a candidate, not presumed equivalent to Core Graphics sampling for arbitrary images.
- Decode and comparison use explicit sRGB premultiplied RGBA8. The report records differing pixel count, maximum/mean channel error, maximum alpha error and pixels with error greater than one. Difference PNGs encode maximum RGB error in red and alpha error in blue, amplified 32× for inspection. No tolerance is silently introduced; Mac comparisons are measurements, not pass/fail compatibility claims.
- The sole edit is renaming the active layer in a **new** package. Existing destinations are refused. A temporary sibling is written, reread and moved into place. Original JSON values and original PNG bytes survive; old supported specimens are upgraded to v8 with default 72 dpi where absent. Source data is never overwritten.
- The loaded scene owns and disposes decoded `SKBitmap` objects. Compressed image/mask PNG bytes are retained for lossless copying, including disabled masks. The control borrows the scene for the synchronous render. The C functions borrow stable Skia RGBA memory and a pinned managed alpha array; they allocate nothing and retain no pointers. Skia canvases flush before pixel access and are notified after changes. The native module remains loaded for the process lifetime. Worker threads and GPU resources are not used.
- Unknown fields, text/shape, independent mask placement, non-contiguous clipping, visible children of hidden clip bases, rotation, mirrors and fractional origins are rejected. F08 remains refused. The adjustment subset is deliberately limited to normal-blended, clipped legacy master desaturation (saturation −100…0, hue/lightness zero, colorize false, no adjustment-local mask). Other settings and newer range-specific HSV are rejected. Inactive levels/curves JSON is preserved; this is not validation of all adjustment parameters.

`FixtureScene` is a restricted corpus reader, **not** the production v1–8 validator. It checks identities, earlier-group parents, expected asset names, basic dimensions/budgets, PNG decode, enums and numeric bounds. It does not implement every version-field rule, arbitrary tree normalization, hostile filesystem races, complete path ancestry validation, safe replacement/recovery, document transactions or an active-project UI. Do not expose it as the general project-open path. The runner's pinned corpus is the supported input contract.

## Composition order and numeric checks

`Composite` follows the Mac `LiveMaskRenderer` order: draw the base with its own mask/opacity, call the existing C `layer_extract_alpha` and `layer_unpremultiply_opaque`, blend clipped children/adjustments at full alpha, then call `layer_restore_alpha` once. Apply group masks to that completed stack. A group mask does not enter the base-alpha dependency. Ordinary group members each receive their ancestor masks independently.

Mask PNGs must be 8-bit grayscale without alpha. Decode their coverage without color management, map it through the layer transform, then multiply all premultiplied RGBA channels using `(value * coverage + 127) / 255`. The first attempt used Skia `DstIn`; it produced 27 for `73×97/255`, where the rounded convention produces 28. The exact group-coverage regression fails with that implementation. Explicit multiplication removed the F05/F06 differences and all alpha differences in F04–F07.

The desaturation operation reproduces the relevant part of the Mac 33³ HSL cube with eight-corner interpolation and no color conversion. For decreasing master saturation, each corner is `L + (component − L) × (1 + saturation/100)`, with `L = (min + max)/2`; no full HSV library is introduced. The adjusted 8-bit value is then mixed by adjustment opacity. Identity, zero opacity, neutral-gray output at −100 and unchanged stack alpha are asserted. These checks do not establish arbitrary HSV/range support.

The reference compositor allocates full-document scratch images. This is appropriate for these small correctness specimens; it does not satisfy tiled brush, memory or latency gates. The native binding source is linked from `../dotnet-bridge/Native.cs`; C source and its ABI are unchanged.

## Mac reader verification

After generating outputs, run the existing Mac application reader/exporter against them from the repository root (replace the path):

```sh
TEST_RUNNER_AVALONIA_ROUNDTRIP_DIR=<ABSOLUTE_OUTPUT_DIRECTORY> \
xcodebuild -project Compositor.xcodeproj -scheme Compositor \
  -configuration Debug -destination 'platform=macOS' \
  -disableAutomaticPackageResolution -parallel-testing-enabled NO \
  -testLanguage en -testRegion US \
  -only-testing:CompositorTests/WindowsFixtureTests \
  CODE_SIGNING_ALLOWED=NO test
```

`avaloniaRenamedCopiesReopenWithOriginalPixels` is explicitly skipped without `AVALONIA_ROUNDTRIP_DIR` in the test host environment. Xcode's `TEST_RUNNER_` prefix forwards that variable. It verifies all 20 output package names, the complete decoded manifest with only the intended changes, unchanged image/mask PNG bytes and pixel-identical **Mac rendering of original versus renamed projects**. The C# probe separately rereads its own outputs. These checks establish Mac → C# → Mac schema/pixel preservation for this subset; running both implementations on this Mac does not establish Windows interoperability.

## Observed on 2026-09-21

macOS 26.5.1 arm64, .NET 10.0.12. Locked restore and Release build succeeded with zero warnings/errors. All 20 actual custom-control previews equal their exports; renamed copies preserve pixels and data. Existing reader/destination checks and 11 mask/clip/adjustment checks passed. F08 remains refused.

Against Mac references, F01/F02/F03/F05/F06/B02/B05/B08 are exact. The other twelve specimens differ by at most **1/255 per channel**; maximum mean absolute error remains **0.2496744792/255** (B11). F04 differs in 14 pixels (mean 0.0011393229), F07 in 256 pixels (mean 0.0403645833); F04–F07 alpha is exact. Broader sampling/quantization analysis and Windows results remain necessary before accepting tolerances. Mac `WindowsFixtureTests` ran **3 passed / 0 failed / 0 skipped**, including all 20 returned packages.

Latest evidence: `docs/windows/evidence/avalonia-combination-macos.json`, `avalonia-combination-summary.json`, `avalonia-combination-preparation.json` and `avalonia-combination-contact-sheet.png`. The older `avalonia-macos.json` and associated preparation/roundtrip files preserve the initial 16-specimen result. Raw outputs/xcresult are temporary local diagnostics; commands above regenerate them. The optional contact sheet can be regenerated with Pillow 11.3.0 from the repository root:

```sh
python3 scripts/windows/render-probe-contact-sheet.py docs/windows/fixtures <PROBE_OUTPUT_DIRECTORY> <NEW_CONTACT_SHEET.png>
```

No Windows run, native UI, clipboard, IME, brush commit/undo, general clipping/adjustment implementation, memory/latency measurement, packaging or release acceptance has occurred. W-008 and M1 remain incomplete.

Avalonia and SkiaSharp NuGet metadata declare MIT; native packages also include third-party notices. A production distribution must inventory its actual native artifacts and notices under D-07/V-12, which this source experiment does not close.
