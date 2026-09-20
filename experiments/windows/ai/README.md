# Native model inference — W-009 preparation

A real U2NetP ONNX model now runs locally through ONNX Runtime's C++ CPU API and produces a separate editable Gray8 layer mask. This is one Mac-host feasibility sample, not a model selection, Windows execution, quality gate or redistribution approval. HEIC screening remains a separate W-009 item. No Qt/Avalonia production direction is selected by this experiment.

## Sources and acquisition

Exact URLs, byte counts and SHA-256 values are in [assets.json](assets.json). Downloads stay outside the repository:

| Artifact | Fixed source | Verification / scope |
| --- | --- | --- |
| Native runtime | ONNX Runtime 1.30.0, official Mac arm64 SDK | 42,373,116-byte archive SHA-256 matches GitHub's published digest; SDK source commit recorded |
| Model | rembg `v0.0.0/u2netp.onnx` | 4,574,861 bytes; SHA-256 frozen; MD5 also matches the pinned adapter's expected value |
| Input | scikit-image v0.25.2 `astronaut.png` | 512×512 NASA photograph; [public-domain provenance](https://scikit-image.org/docs/0.25.x/api/skimage.data.html#skimage.data.astronaut), frozen bytes/hash |
| Windows SDK candidate | Official ONNX Runtime 1.30.0 win-x64 zip | Published URL/size/digest recorded only; **not downloaded or executed** |

The original [U²-Net repository](https://github.com/xuebinqin/U-2-Net/tree/ac7e1c817ecab7c7dff5ce6b1abba61cd213ff29) has an Apache-2.0 license. The ONNX conversion is obtained through [rembg's pinned adapter](https://github.com/danielgatis/rembg/blob/202e42649a8492a7c49f808de36608a7d1cbbfe3/rembg/sessions/u2netp.py). A standalone authorization/license statement for this exact converted weight asset has not been verified. Its existence/download hash and the architecture's license do not settle weight redistribution. D-08 remains open; no weights or runtime binaries are committed.

The native runtime includes MIT `LICENSE`, `ThirdPartyNotices.txt` and `Privacy.md`; hashes are retained. The notices cover multiple third-party components and are not a build-specific Windows dependency inventory. Future packaging must inspect the actual Windows DLLs, retain relevant notices, and resolve the model acquisition/authorization path. An explicit verified model download can be considered only after that decision; the current runner never downloads automatically or uploads an image. Runtime telemetry is explicitly disabled.

Pre/postprocessing follows the pinned [rembg base normalization](https://github.com/danielgatis/rembg/blob/202e42649a8492a7c49f808de36608a7d1cbbfe3/rembg/sessions/base.py) and U2NetP adapter; its MIT notice is retained in [licenses/rembg-MIT.txt](licenses/rembg-MIT.txt). Python is an experiment inspection/image-preparation tool, not a proposed desktop runtime dependency.

## Build and run

Download the Mac SDK, model and sample URLs from `assets.json` into `<ASSETS>`, verify their SHA-256 values, then extract the SDK there. The following assumes CMake 3.31.6 and the observed Python 3.9 host:

```sh
python3 -m venv <INSPECTION_VENV>
<INSPECTION_VENV>/bin/python -m pip install -r experiments/windows/ai/requirements.txt
cmake -S experiments/windows/ai -B <BUILD> -DCMAKE_BUILD_TYPE=Release \
  -DORT_ROOT=<ASSETS>/onnxruntime-osx-arm64-1.30.0
cmake --build <BUILD> --parallel 6
<INSPECTION_VENV>/bin/python experiments/windows/ai/screen.py \
  <ASSETS> <BUILD>/ai_probe <NEW_OUTPUT>
```

`screen.py` independently verifies model/photo SHA-256, validates the ONNX graph, writes a float32 input tensor, invokes the actual executable, verifies its CPU profile and writes mask/cutout PNGs plus `subject.comp`. Every destination must be new. The SDK path is a CMake cache parameter; no system installation is modified. On Windows, the candidate build would use the official x64 SDK with Visual Studio x64, and its `lib` directory must expose `onnxruntime.dll` to the loader. Those Windows steps are **unexecuted**, including clean-machine redistributable dependencies and non-ASCII command-line paths.

The native program itself takes only a preverified local model, raw tensor and output directory. It depends on the C++ standard library and ONNX Runtime, not Qt, Skia or Python. The Mac SDK links Apple frameworks including CoreML/Metal, but the actual node profile shows CPU execution only; no acceleration provider is appended.

## Tensor, mask and lifecycle contracts

The model is PyTorch 1.9-produced ONNX IR 6 / opset 11, with 1,055 nodes and 13 standard operator types: Add, Cast, Concat, Constant, Conv, Gather, MaxPool, Relu, Resize, Shape, Sigmoid, Slice, Unsqueeze. There are no external weight files, custom domains or local functions in this verified graph. This inspection is not a general untrusted-model validator.

Input is RGB Lanczos-resized to 320×320, divided by the image maximum (minimum divisor 1e−6), normalized by mean `(0.485,0.456,0.406)` and std `(0.229,0.224,0.225)`, then converted to little-endian float32 NCHW `[1,3,320,320]`. Native code rejects wrong-sized/nonfinite input. All seven actual outputs must be finite float32 `[1,1,320,320]` sigmoid values in `[0,1]`.

The first output is the saliency mask. The pinned adapter's min/max normalization, truncating uint8 conversion and Lanczos resize restore 512×512 Gray8 coverage. White retains foreground. The original RGB PNG remains separate from this mask in a v8 project; the cutout PNG is only a preview. No subject-removal postprocessing, hole cleanup or existing-mask combination is implemented in this screening path.

One sequential CPU session uses one intra-op and one inter-op thread. Three successful predictions must match bitwise; a pre-terminated `RunOptions` must reject a run, then resetting it must restore successful identical output. This checks **cancellation before inference**, not interruption of an active run or a UI cancellation lifecycle. RAII owns session/tensors; input storage stays alive until synchronous runs return. Profiling attributes every executed kernel to `CPUExecutionProvider`.

## Observed results and limits

Final screening output `/tmp/compositor-ai-run-02` passed; the standalone resource observation `/tmp/compositor-ai-native-metrics` used the same model/input and emitted the identical raw prediction:

| Check / observation | Result |
| --- | --- |
| Graph validation and actual runtime IO | Passed; seven mask tensors validated |
| Repeated output / pre-termination / session recovery | Passed; four successful outputs including recovery, 1,344 CPU kernel profile events |
| Invalid paths/data | Short tensor, NaN tensor, missing model and corrupt model refused without a final mask; existing output refused and preserved |
| Gray8 output | 108,712 zero, 395 full-coverage and 153,037 intermediate pixels |
| Actual Mac reader/exporter | 1 test passed, 0 failed, 0 skipped; 0.090 seconds |
| Mac editable mask | Export alpha equals inference coverage exactly; disable restores original pixels; save/reopen preserves mask and separate original image |
| ASan/UBSan | Own C++ instrumented and full harness passed; prebuilt ONNX Runtime uninstrumented; leak detection disabled |
| Stable outputs | Input tensor, raw mask, PNGs and project files byte-identical across initial/final Release and sanitizer |
| Native resource run | Session load 45.439125 ms; three inference samples 160.088750 / 163.463125 / 102.949459 ms |
| Native whole process | 0.66 s wall; maximum RSS 665,567,232 bytes (~635 MiB), including session, profiling, four successful inferences and pre-termination check |

The resource run excludes Python preprocessing/postprocessing, GUI presentation and the Mac reader test. This is not Windows private memory/VRAM, a throughput benchmark or a long-running leak/stress result. A ~4.4 MiB weight file does not imply small inference memory. These measurements do not choose a thread budget or establish a performance threshold.

Visual inspection shows the main astronaut/helmet retained, with **flag/background residue at the left boundary and imperfect hair/edge separation**. One public image with no labeled ground truth cannot establish segmentation quality or equivalence to Apple's foreground model. No quality tolerance is accepted.

Mac readback can be reproduced with the generated directory:

```sh
TEST_RUNNER_AI_SCREENING_DIR=<ABSOLUTE_OUTPUT> \
xcodebuild -project Compositor.xcodeproj -scheme Compositor -configuration Debug \
  -destination 'platform=macOS' -disableAutomaticPackageResolution \
  -parallel-testing-enabled NO -testLanguage en -testRegion US \
  '-only-testing:CompositorTests/WindowsFixtureTests/aiMaskPackageRetainsEditableCoverage()' \
  CODE_SIGNING_ALLOWED=NO test
```

The test explicitly skips without the environment variable. Mac product/native C code was unchanged; no full Mac suite was rerun. Evidence is under `docs/windows/evidence/ai-u2netp-*`, including the source/mask/cutout sheet. The sheet and source image derive from the credited public-domain NASA photograph.

Remaining W-009/M6 work: Windows x64 runtime/deployment, exact weight acquisition/license decision, HEIC decode/distribution screening, diverse labeled quality corpus, BiRefNet/other candidates, active cancellation, Basic/Advanced postprocessing, existing-mask preservation, preview/commit/history integration, failure recovery and final resource/performance/package validation. W-009 and M1 remain unpassed.
