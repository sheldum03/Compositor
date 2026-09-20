# Avalonia specimen prototype — initial W-008 preparation

This is an isolated experiment, not a Windows editor or a GUI framework decision. It exercises the first portion of the compositor/round-trip paths in `docs/windows/technical-design.md`. Windows execution and all four complete M1 paths remain required.

## Reproduce

Use .NET SDK **10.0.401** from `global.json`. From this directory:

```sh
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet run -c Release --no-build -- ../../../docs/windows/fixtures <NEW_OUTPUT_DIRECTORY>
```

The output directory must not exist. The command verifies all 91 hashes in the base corpus before and after execution. It writes 16 renamed `.comp` packages, preview/export/reopened PNGs and `report.json`. Failed assertions return a nonzero process exit; partially generated diagnostic output remains for inspection. A successful run means the stated preparation checks passed, not that the observed Mac differences were accepted.

Dependencies are limited to Avalonia.Headless and Avalonia.Skia **11.3.22**, with the full transitive graph/content hashes in `packages.lock.json`. SkiaSharp resolves to **2.88.9**; its assembly reports **2.88.0.0**. The report records assembly versions, not NuGet versions. `NuGet.Config` explicitly selects the public NuGet feed. These are fixed experiment versions, not a claim about the latest release or an approved production dependency set.

The headless application uses `UseHeadlessDrawing = false` and `UseSkia()`: the default headless drawing backend would not establish real pixel output. `SceneControl` obtains a Skia lease through an `ICustomDrawOperation`; that callback and the offscreen exporter both call `FixtureScene.Paint`. Each specimen asserts that the callback ran and that its rendered PNG matches export exactly at 96 dpi. This proves an actual Avalonia drawing path without proving native-window behavior, display ICC, HiDPI, GPU, input or performance. See [Avalonia headless setup](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform) and the [pinned Skia lease API](https://github.com/AvaloniaUI/Avalonia/blob/627ae9ef921621e27e7aa58df2796fbadb50af88/src/Skia/Avalonia.Skia/ISkiaSharpApiLeaseFeature.cs).

## Deliberate scope

- Fixed F01–F03 and B01–B13: PNG assets, sRGB, 13 blend modes, opacity, integer origins, scaling, pass-through groups and inherited visibility. The 64×48 specimens include a source scaled to 44×32. Native Skia high-quality sampling is a candidate, not presumed equivalent to Core Graphics sampling for arbitrary images.
- Decode and comparison use explicit sRGB premultiplied RGBA8. The report records differing pixel count, maximum absolute channel error and mean absolute channel error over all RGBA channels. No tolerance is silently introduced; Mac comparisons are measurements, not pass/fail compatibility claims.
- The sole edit is renaming the active layer in a **new** package. Existing destinations are refused. A temporary sibling is written, reread and moved into place. Original JSON values and original PNG bytes survive; old supported specimens are upgraded to v8 with default 72 dpi where absent. Source data is never overwritten.
- The loaded scene owns and disposes decoded `SKBitmap` objects. Compressed PNG bytes are retained for lossless copying. The control borrows the scene for the synchronous render, and the caller keeps it alive until the control draw/bitmap export is finished. No C DLL buffers, worker threads or GPU resources are involved yet.
- Unknown fields, masks, clipping, adjustment, text and shape fields are rejected before a scene can be edited/saved. F04–F08 are explicitly refused. Rotation, mirrors and fractional origins are also outside this first step.

`FixtureScene` is a restricted corpus reader, **not** the production v1–8 validator. It checks identities, earlier-group parents, expected asset names, basic dimensions/budgets, PNG decode, enums and numeric bounds. It does not implement every version-field rule, arbitrary tree normalization, hostile filesystem races, complete path ancestry validation, safe replacement/recovery, document transactions or an active-project UI. Do not expose it as the general project-open path. The runner's pinned corpus is the supported input contract.

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

`avaloniaRenamedCopiesReopenWithOriginalPixels` is explicitly skipped without `AVALONIA_ROUNDTRIP_DIR` in the test host environment. Xcode's `TEST_RUNNER_` prefix forwards that variable. It verifies all 16 output package names, the complete decoded manifest with only the intended changes, unchanged PNG bytes and pixel-identical **Mac rendering of original versus renamed projects**. The C# probe separately rereads its own outputs. These checks establish Mac → C# → Mac schema/pixel preservation for this subset; running both implementations on this Mac does not establish Windows interoperability.

## Observed on 2026-09-21

macOS 26.5.1 arm64, .NET 10.0.12. Locked restore and Release build succeeded with zero warnings/errors. All 16 actual custom-control previews equal their exports; renamed copies preserve pixels and data. Hidden-ancestor, duplicate-ID, missing-parent, escaped-asset-name, future-version, invalid-opacity, unsupported-rotation/unknown-field, truncated-PNG and destination-preservation checks passed. Five unsupported fixtures were refused.

Against Mac references, F01/F02/F03/B02/B05/B08 are exact. The other ten B specimens differ by at most **1/255 per channel**; maximum mean absolute error is **0.2496744792/255** (B11). Quantization, sampling and nonseparable blend rounding need broader analysis before a tolerance or equivalence claim. Mac `WindowsFixtureTests` ran **3 passed / 0 failed / 0 skipped**, including all 16 returned packages.

Evidence: `docs/windows/evidence/avalonia-macos.json`, `avalonia-roundtrip-summary.json`, `avalonia-preparation.json`. Raw outputs/xcresult are temporary local diagnostics; commands above regenerate them. No Windows run, native UI, clipboard, IME, brush commit/undo, complete clipping/adjustment path, memory/latency measurement, packaging or release acceptance has occurred. W-008 and M1 remain incomplete.

Avalonia and SkiaSharp NuGet metadata declare MIT; native packages also include third-party notices. A production distribution must inventory its actual native artifacts and notices under D-07/V-12, which this source experiment does not close.
