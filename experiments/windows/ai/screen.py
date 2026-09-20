#!/usr/bin/env python3
"""Run the fixed U2NetP feasibility experiment; no downloads or user-image upload."""
import argparse
from collections import Counter
import hashlib
import json
import platform
from pathlib import Path
import shutil
import subprocess

import numpy as np
import onnx
from PIL import Image, ImageDraw

parser = argparse.ArgumentParser()
parser.add_argument("assets", type=Path)
parser.add_argument("probe", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
assets, output = args.assets.resolve(), args.output.resolve()
probe = args.probe.resolve()
model, photograph = assets / "u2netp.onnx", assets / "astronaut.png"
expected = {"u2netp.onnx": "309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8",
            "astronaut.png": "88431cd9653ccd539741b555fb0a46b61558b301d4110412b5bc28b5e3ea6cb5"}
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

for name, digest in expected.items():
    assert sha(assets / name) == digest, name
assert not output.exists(), "Output must be new"
graph = onnx.load(model, load_external_data=False)
onnx.checker.check_model(graph, full_check=True)
assert not graph.functions and all(not t.external_data for t in graph.graph.initializer)
assert all(n.domain in ("", "ai.onnx") for n in graph.graph.node), "No custom operators"
assert [(x.domain, x.version) for x in graph.opset_import] == [("", 11)]
output.mkdir()
source = Image.open(photograph).convert("RGB")
assert source.size == (512, 512)
# Match the pinned rembg U2NetP adapter: Lanczos resize, image max, mean/std, NCHW.
rgb = np.asarray(source.resize((320, 320), Image.Resampling.LANCZOS))
normalized = rgb / max(float(rgb.max()), 1e-6)
normalized = (normalized - (0.485, 0.456, 0.406)) / (0.229, 0.224, 0.225)
tensor = normalized.transpose(2, 0, 1)[None].astype("<f4")
assert tensor.shape == (1, 3, 320, 320) and np.isfinite(tensor).all()
tensor.tofile(output / "input.f32")
assert np.isfinite((np.zeros((1, 1, 3)) / 1e-6 - (0.485, 0.456, 0.406)) / (0.229, 0.224, 0.225)).all()
with (output / "native.log").open("w") as log:
    subprocess.run([str(probe), str(model), str(output / "input.f32"), str(output / "native")], stdout=log, stderr=subprocess.STDOUT, check=True)
profile_files = list((output / "native").glob("cpu-profile*.json"))
assert len(profile_files) == 1
profile = json.loads(profile_files[0].read_text())
providers = Counter(e.get("args", {}).get("provider") for e in profile if e.get("cat") == "Node" and e.get("args", {}).get("provider"))
assert set(providers) == {"CPUExecutionProvider"} and sum(providers.values()) > 0, providers
prediction = np.fromfile(output / "native/mask.f32", dtype="<f4").reshape(320, 320)
assert np.isfinite(prediction).all() and prediction.min() >= 0 and prediction.max() <= 1
spread = float(prediction.max() - prediction.min())
assert spread > 0.5
mask320 = Image.fromarray(((prediction - prediction.min()) / spread * 255).astype(np.uint8))
mask = mask320.resize(source.size, Image.Resampling.LANCZOS)
mask.save(output / "mask.png")
rgba = source.convert("RGBA"); rgba.putalpha(mask); rgba.save(output / "cutout.png")
shutil.copyfile(photograph, output / "source.png")
# A real .comp stores the image and editable Gray8 mask separately.
identity = "00000000-0000-4000-9000-000000000020"
package = output / "subject.comp"; (package / "images").mkdir(parents=True)
shutil.copyfile(photograph, package / "images" / (identity + ".png"))
shutil.copyfile(output / "mask.png", package / "images" / (identity + ".mask.png"))
manifest = {"format": "com.compositor.project", "version": 8, "colorSpace": "sRGB", "resolution": 72,
            "documentID": "00000000-0000-4000-9000-000000000120", "width": 512, "height": 512,
            "activeLayerID": identity, "layers": [{"id": identity, "name": "NASA public-domain sample / U2NetP mask",
            "isVisible": True, "maskEnabled": True, "imageFile": identity + ".png", "maskFile": identity + ".mask.png",
            "transform": {"origin": [0, 0], "size": [512, 512], "rotation": 0, "flipX": False, "flipY": False, "sampling": "Nearest"}}]}
(package / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
# Exercise failures against the actual executable; no final mask may be published.
bad = output / "invalid.f32"; bad.write_bytes(b"short")
nan = tensor.copy(); nan.flat[0] = np.nan; nan.tofile(output / "nan.f32")
(output / "invalid.onnx").write_bytes(b"not an ONNX graph")
failures = []
for name, model_path, input_path in (("short-input", model, bad), ("nan-input", model, output / "nan.f32"),
                                     ("missing-model", output / "missing.onnx", output / "input.f32"),
                                     ("invalid-model", output / "invalid.onnx", output / "input.f32")):
    destination = output / name
    result = subprocess.run([str(probe), str(model_path), str(input_path), str(destination)], capture_output=True, text=True)
    assert result.returncode != 0 and not (destination / "mask.f32").exists(), name
    failures.append({"case": name, "exitCode": result.returncode, "noMaskPublished": True})
original = sha(output / "native/mask.f32")
result = subprocess.run([str(probe), str(model), str(output / "input.f32"), str(output / "native")], capture_output=True)
assert result.returncode != 0 and sha(output / "native/mask.f32") == original
# Exercise native path handling through model loading, tensor IO and profiling.
unicode_root = output / "路径 空格 🧪"; unicode_root.mkdir()
unicode_model, unicode_input, unicode_output = (unicode_root / name for name in ("模型 🧠.onnx", "输入 张量.f32", "推理 结果 🚀"))
shutil.copyfile(model, unicode_model); shutil.copyfile(output / "input.f32", unicode_input)
with (unicode_root / "native.log").open("w") as log:
    subprocess.run([str(probe), str(unicode_model), str(unicode_input), str(unicode_output)], stdout=log, stderr=subprocess.STDOUT, check=True)
assert sha(unicode_output / "mask.f32") == original
unicode_profiles = list(unicode_output.glob("cpu-profile*.json"))
assert len(unicode_profiles) == 1
unicode_profile = json.loads(unicode_profiles[0].read_text())
unicode_providers = Counter(e.get("args", {}).get("provider") for e in unicode_profile if e.get("cat") == "Node" and e.get("args", {}).get("provider"))
assert unicode_providers == providers
result = subprocess.run([str(probe), str(unicode_model), str(unicode_input), str(unicode_output)], capture_output=True)
assert result.returncode != 0 and sha(unicode_output / "mask.f32") == original
assert sha(unicode_model) == expected["u2netp.onnx"] and sha(unicode_input) == sha(output / "input.f32")
for name, digest in expected.items():
    assert sha(assets / name) == digest
sheet = Image.new("RGB", (1568, 554), (24, 26, 31)); draw = ImageDraw.Draw(sheet)
checker = Image.new("RGBA", (512, 512), (215, 215, 215, 255)); tiles = ImageDraw.Draw(checker)
for y in range(0, 512, 16):
    for x in range(0, 512, 16):
        if (x // 16 + y // 16) % 2: tiles.rectangle((x, y, x + 15, y + 15), fill=(242, 242, 242, 255))
checker.alpha_composite(rgba)
for index, (label, image) in enumerate((("NASA source", source), ("U2NetP Gray8 mask", mask.convert("RGB")), ("Local masked preview", checker.convert("RGB")))):
    x = 8 + index * 520; draw.text((x, 8), label, fill=(245, 245, 250)); sheet.paste(image, (x, 28))
sheet.save(output / "contact-sheet.png")
values = np.asarray(mask)
report = {"status": "local feasibility only; no Windows/quality/distribution acceptance", "modelSha256": expected["u2netp.onnx"],
          "inputImageSha256": expected["astronaut.png"], "inputTensorSha256": sha(output / "input.f32"),
          "onnx": {"irVersion": graph.ir_version, "producer": graph.producer_name, "producerVersion": graph.producer_version,
                   "opset": 11, "nodeCount": len(graph.graph.node), "operators": dict(sorted(Counter(n.op_type for n in graph.graph.node).items())),
                   "externalData": False, "customOperators": False},
          "profileKernelProviders": dict(providers), "inference": json.loads((output / "native/inference.json").read_text()),
          "mask": {"mode": mask.mode, "size": list(mask.size), "zeroPixels": int(np.count_nonzero(values == 0)),
                   "fullPixels": int(np.count_nonzero(values == 255)), "intermediatePixels": int(np.count_nonzero((values > 0) & (values < 255))),
                   "sha256": sha(output / "mask.png"), "rawPredictionSha256": original},
          "failureChecks": failures, "existingOutputRefusedAndPreserved": True, "sourceFilesUnchanged": True,
          "unicodePaths": {"chineseSpacesAndNonBmp": True, "rawPredictionExact": True, "profileKernelProviders": dict(unicode_providers),
                           "existingOutputRefusedAndPreserved": True},
          "host": platform.platform(), "windowsExecuted": platform.system() == "Windows", "weightsRedistributionApproved": False,
          "activeInferenceCancellationTested": False}
(output / "screening.json").write_text(json.dumps(report, indent=2) + "\n")
print(json.dumps(report, indent=2))
