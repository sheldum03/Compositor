#!/usr/bin/env python3
"""Exercise the native HEIC decoder against the frozen, self-generated corpus."""
import argparse
import hashlib
import json
from pathlib import Path
import platform
import shutil
import subprocess

import numpy as np
from PIL import Image, ImageDraw

parser = argparse.ArgumentParser()
parser.add_argument("fixtures", type=Path)
parser.add_argument("probe", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()
fixtures, probe, output = args.fixtures.resolve(), args.probe.resolve(), args.output.resolve()
def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def verify():
    records = json.loads((fixtures / "checksums.json").read_text())
    assert len(records) == 33
    for r in records:
        p = fixtures / r["path"]
        assert p.stat().st_size == r["bytes"] and sha(p) == r["sha256"]

verify()
assert not output.exists(), "Output must be new"
output.mkdir()
results = []
for case in json.loads((fixtures / "cases.json").read_text()):
    name = case["name"]
    subprocess.run([str(probe), str(fixtures / (name + ".heic")), str(output / name)], check=True)
    report = json.loads((output / name / "decode.json").read_text())
    w, h = report["width"], report["height"]
    assert (w, h) == (case["referenceWidth"], case["referenceHeight"]), name
    assert report["hasAlpha"] == case["macHasAlpha"] == case["requestedAlpha"]
    assert report["premultiplied"] is False, "Fixed fixtures must decode to straight RGBA"
    rgba = np.fromfile(output / name / "rgba.bin", dtype=np.uint8).reshape(h, w, 4)
    reference = np.asarray(Image.open(fixtures / (name + "-mac.png")).convert("RGBA"))
    assert np.array_equal(rgba[:, :, 3], reference[:, :, 3]), "Exact alpha: " + name
    Image.fromarray(rgba).save(output / (name + ".png"))
    def premultiplied(a):
        a = a.astype(np.uint16)
        a[:, :, :3] = (a[:, :, :3] * a[:, :, 3:] + 127) // 255
        return a.astype(np.int16)
    error = np.abs(premultiplied(rgba) - premultiplied(reference))
    maximum = error.max(axis=2)
    heat = np.zeros((h, w, 4), dtype=np.uint8)
    heat[:, :, 0] = np.clip(error[:, :, :3].max(axis=2) * 32, 0, 255)
    heat[:, :, 2] = np.clip(error[:, :, 3] * 32, 0, 255)
    heat[:, :, 3] = 255
    Image.fromarray(heat).save(output / (name + "-diff.png"))
    results.append({"case": name, "decode": report, "rgbaSha256": sha(output / name / "rgba.bin"),
                    "comparison": {"differentPixels": int(np.count_nonzero(maximum)), "maximumChannelError": int(error.max()),
                                   "maximumAlphaError": int(error[:, :, 3].max()), "meanAbsoluteChannelError": float(error.mean())}})
# Exercise input and output paths including characters outside legacy code pages.
unicode_root = output / "路径 空格 🧪"; unicode_root.mkdir()
unicode_input, unicode_output = unicode_root / "方向与透明度 🌈.heic", unicode_root / "解码 结果 🚀"
shutil.copyfile(fixtures / "orientation-6-alpha.heic", unicode_input)
subprocess.run([str(probe), str(unicode_input), str(unicode_output), "100000000"], check=True)
assert sha(unicode_output / "rgba.bin") == sha(output / "orientation-6-alpha/rgba.bin")
p = subprocess.run([str(probe), str(unicode_input), str(unicode_output)], capture_output=True)
assert p.returncode != 0 and sha(unicode_output / "rgba.bin") == sha(output / "orientation-6-alpha/rgba.bin")
assert sha(unicode_input) == sha(fixtures / "orientation-6-alpha.heic")
truncated = output / "truncated.heic"
truncated.write_bytes((fixtures / "orientation-1-opaque.heic").read_bytes()[:64])
failures = []
for name, path, extra in (("truncated", truncated, []), ("wrong-format", fixtures / "orientation-1-opaque-mac.png", []),
                           ("missing", output / "missing.heic", []), ("budget", fixtures / "orientation-1-opaque.heic", ["100"])):
    destination = output / (name + "-rejected")
    p = subprocess.run([str(probe), str(path), str(destination)] + extra, capture_output=True, text=True)
    assert p.returncode != 0 and not destination.exists(), name
    failures.append({"case": name, "exitCode": p.returncode, "noOutputPublished": True})
old = sha(output / "orientation-1-opaque/rgba.bin")
p = subprocess.run([str(probe), str(fixtures / "orientation-1-opaque.heic"), str(output / "orientation-1-opaque")], capture_output=True)
assert p.returncode != 0 and sha(output / "orientation-1-opaque/rgba.bin") == old
verify()
sheet = Image.new("RGB", (920, len(results) * 214), (24, 26, 31)); labels = ImageDraw.Draw(sheet)
for row, result in enumerate(results):
    name = result["case"]
    paths = [("Mac ImageIO", fixtures / (name + "-mac.png")), ("libheif/de265", output / (name + ".png")), ("Difference x32", output / (name + "-diff.png"))]
    for column, (label, path) in enumerate(paths):
        image = Image.open(path).convert("RGBA"); image.thumbnail((288, 180), Image.Resampling.NEAREST)
        # Enlarge the small specimens with nearest sampling for visible pixel differences.
        factor = min(288 / image.width, 180 / image.height)
        image = image.resize((round(image.width * factor), round(image.height * factor)), Image.Resampling.NEAREST)
        tile = Image.new("RGBA", (288, 180), (220, 220, 220, 255)); draw = ImageDraw.Draw(tile)
        for y in range(0, 180, 12):
            for x in range(0, 288, 12):
                if (x // 12 + y // 12) % 2: draw.rectangle((x, y, x + 11, y + 11), fill=(245, 245, 245, 255))
        tile.alpha_composite(image, ((288 - image.width) // 2, (180 - image.height) // 2))
        x, y = 8 + column * 304, 8 + row * 214
        labels.text((x, y), name + " / " + label, fill=(245, 245, 250)); sheet.paste(tile.convert("RGB"), (x, y + 20))
sheet.save(output / "contact-sheet.png")
report = {"status": "local HEIC screening only; not Windows or full IO/distribution acceptance", "host": platform.platform(),
          "windowsExecuted": platform.system() == "Windows", "corpusHashesVerified": 33, "results": results,
          "nonAsciiFilenamePixelsExact": True, "failureChecks": failures, "existingOutputPreserved": True,
          "unicodePaths": {"chineseSpacesAndNonBmp": True, "inputAndOutput": True, "existingOutputRefusedAndPreserved": True},
          "imageToleranceAccepted": False, "generalIccAndHdrValidated": False}
(output / "screening.json").write_text(json.dumps(report, indent=2) + "\n")
print(json.dumps(report, indent=2))
