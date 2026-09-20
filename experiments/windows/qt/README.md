# Qt compositor and project round-trip — W-007 preparation

This isolated C++17 / Qt Widgets experiment renders the same F01–F07 and B01–B13 fixed specimens as the Avalonia compositor probe. It covers a portion of the composition and JSON/PNG round-trip paths. The companion [soft-brush probe](BRUSH.md) adds a fixed 4K tile/history path. Text/IME, Windows execution and framework selection remain open. No Windows product directory, installer or second GPU backend is introduced.

## Pinned build

Qt **6.11.2**, qtbase only, dynamically linked Core/Gui/Widgets; CMake **3.31.6**. The observed host uses macOS 26.5.1 arm64 and AppleClang 21. The native subdirectory builds the existing eight C files and bridge unchanged; its contract test is included once in CTest.

The local SDK was installed outside the repository using aqtinstall 3.3.0. Its Python installer dependencies are frozen in `installer-requirements.txt`; they are development tools, not application dependencies. Example Mac setup:

```sh
python3 -m venv <TEMP_INSTALLER_VENV>
<TEMP_INSTALLER_VENV>/bin/python -m pip install -r experiments/windows/qt/installer-requirements.txt
<TEMP_INSTALLER_VENV>/bin/aqt install-qt mac desktop 6.11.2 clang_64 \
  --archives qtbase -O <TEMP_QT_DIRECTORY> -k -d <TEMP_ARCHIVE_DIRECTORY>
cmake -S experiments/windows/qt -B <TEMP_BUILD_DIRECTORY> \
  -DCMAKE_PREFIX_PATH=<TEMP_QT_DIRECTORY>/6.11.2/macos \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES=arm64
cmake --build <TEMP_BUILD_DIRECTORY> --parallel 6
ctest --test-dir <TEMP_BUILD_DIRECTORY> --output-on-failure
QT_QPA_PLATFORM=offscreen QT_SCALE_FACTOR=1 <TEMP_BUILD_DIRECTORY>/qt_probe \
  docs/windows/fixtures <NEW_OUTPUT_DIRECTORY>
```

The output directory must not exist. Windows will require its own Qt `win64_msvc2022_64` package, Visual Studio 2022 x64 environment and runtime DLL paths (Qt's `bin` and the build's `native/Release`). These Windows steps have **not been executed**. The Mac universal archive does not provide Windows binaries.

The qtbase archive is 31,039,494 bytes. Its official SHA-1 sidecar was checked independently of aqt; SHA-256 was also recorded:

```text
SHA-1   898a61de33218d55538721ce35c61f973768cb07
SHA-256 9592f84f7e26d532c5c56824d1da7c9214a766cb0a17beb5af71022bcfbcd271
```

[Official release](https://www.qt.io/blog/qt-6.11.2-released), [exact Mac archive](https://download.qt.io/online/qtsdkrepository/mac_x64/desktop/qt6_6112/qt6_6112/qt.qt6.6112.clang_64/6.11.2-0-202608131016qtbase-MacOS-MacOS_15-Clang-MacOS-MacOS_15-X86_64-ARM64.7z). Qt's installed SBOM identifies Core, Gui, Widgets and the offscreen plugin as offering commercial/LGPL-3.0/GPL license alternatives. This is recorded metadata, not a completed redistribution review or D-07 choice. No Qt binaries are committed or distributed. The stock host Python 3.9.6 emitted an urllib3/LibreSSL compatibility warning during installation; download and independent hash verification succeeded.

## Rendering and ownership

- Each export and each real `QWidget::paintEvent` runs the CPU compositor afresh into an owned sRGB `QImage::Format_RGBA8888_Premultiplied`. The widget draws that image at 1:1 into a transparent offscreen target. It never loads the export PNG as its preview. This proves the shared CPU path and widget integration, not GPU/display presentation.
- QPainter handles nine separable modes. It has no Hue, Saturation, Color or Luminosity mode; those four use CPU implementations of the [W3C nonseparable equations and alpha composition](https://www.w3.org/TR/2024/CRD-compositing-1-20240321/#blendingnonseparable). Analytic red/green, neutral and transparent-source/backdrop cases are asserted. No Skia dependency is added to Qt.
- Coverage and opacity use rounded integer `/255` multiplication. Gray mask samples bypass RGB color conversion. A clipping stack saves base alpha, makes RGB opaque, blends children and restores alpha once through the actual C functions. Group masks are applied outside the stack. The adjustment subset matches C#: clipped legacy master desaturation, saturation −100…0, no hue/lightness/colorize or independent adjustment mask, using the same 33³ cube interpolation.
- Native calls synchronously borrow QImage storage and retain no pointers. All QPainters finish before raw pixel mutation. `QImage::bits()` detaches a shared image before writes; a retained-image assertion checks this. RAII owns images, vectors, painters and temporary directories.
- Nearest uses QPainter's fast path; **Smooth and High quality currently both use SmoothPixmapTransform**. This does not provide three distinct sampling algorithms. Scaling is exercised; rotation, flips, fractional placement, text, shapes and independent mask placement are rejected.

The compositor allocates full-canvas scratch images and masks. These 64×48 fixtures cannot establish a production tile/cache design, 4K performance, memory budget or leak behavior. Individual export/preview times are retained for diagnostics, not framework ranking.

The final whole-process Mac `/usr/bin/time -l` sample reported 0.24 s wall time and 20,791,296 bytes maximum resident set size, including all twenty cases and guard checks. This is not Windows private RAM, VRAM, a long-running resource test or a comparison with the Avalonia 4K workload.

## Files and validation

`Scene::read` is a fixed-corpus adapter, not the production v1–8 validator. It preserves the supported manifest, PNG bytes and inactive settings, and enforces the limited hierarchy/asset/transform/clipping contracts needed here. The runner checks all 91 corpus hashes before and after running. General hostile-input validation, filesystem race protection and replacement/recovery are not implemented.

Rename/save changes only the active layer name, current version 8 and default resolution where absent. It writes a new temporary sibling, verifies it with the Qt reader and publishes a new directory. Existing destinations are refused. No source package is edited.

Observed Release and ASan/UBSan runs:

| Check | Result |
| --- | --- |
| Widget / export | 20/20 exact; one real widget paint per sample; every result nonempty |
| PNG export decode | 20/20 exact premultiplied bytes |
| Rename / Qt reread / existing destination | 20/20; only expected manifest changes; all source PNG bytes preserved |
| Mac comparison | F01/F02/F04/F05/F06 exact; other 15 have maximum channel error 1/255; all alpha values exact |
| Guard cases | 11 rejected: future version, unknown field, duplicate ID, asset escape, missing parent, opacity, rotation, text, F08, truncated PNG, RGBA mask |
| Composition contracts | Disabled masks, clipping alpha, group coverage once, adjustment alpha, zero/identity adjustment, hidden layers, QImage copy-on-write, nonseparable analytic/neutral/transparent cases passed |
| Native contract | 1 CTest passed in each build; exercises the existing C bridge contract suite |
| ASan/UBSan | Own C++ and C instrumented; all scenarios passed; prebuilt Qt uninstrumented; leak detection disabled, so no leak claim |

B10–B13 have 1,149/1,408/1,156/1,408 differing Mac pixels, all by at most one channel unit. This is an observation without an accepted cross-platform tolerance, not evidence that the GUI candidates or their sampling algorithms are generally equivalent.

Run Mac reader/exporter checks from the repository root after generating outputs:

```sh
TEST_RUNNER_QT_ROUNDTRIP_DIR=<ABSOLUTE_QT_OUTPUT> \
TEST_RUNNER_AVALONIA_ROUNDTRIP_DIR=<ABSOLUTE_AVALONIA_OUTPUT> \
xcodebuild -project Compositor.xcodeproj -scheme Compositor \
  -configuration Debug -destination 'platform=macOS' \
  -disableAutomaticPackageResolution -parallel-testing-enabled NO \
  -testLanguage en -testRegion US -only-testing:CompositorTests/WindowsFixtureTests \
  CODE_SIGNING_ALLOWED=NO test
```

Both gated framework readbacks and the two existing fixture tests passed: **4 passed / 0 failed / 0 skipped**, 1.112 seconds of test execution. The helper was shared without weakening its original assertions. Tests explicitly skip when the corresponding output variable is absent. No full Mac suite rerun was needed; product code was unchanged.

Evidence lives under `docs/windows/evidence/qt-compositor-*`. Regenerate the contact sheet with Pillow 11.3.0:

```sh
python3 scripts/windows/render-probe-contact-sheet.py docs/windows/fixtures \
  <QT_OUTPUT_DIRECTORY> <NEW_CONTACT_SHEET.png> --candidate-label 'Qt CPU'
```

The fixed soft-brush transaction/next-stroke/undo path now has [separate preparation evidence](BRUSH.md). Remaining W-007 work: transformed text/shared layout/native IME, Windows deployment/execution, matched performance/resource measurements and resulting integration gaps. M0/M1 remain unpassed; this does not authorize production framework selection.
