#!/usr/bin/env python3
"""Report measured CPU/Metal differences, without defining a pass threshold (Pillow 11.3.0)."""
import json
from pathlib import Path
from PIL import Image, ImageChops, ImageStat

root = Path(__file__).resolve().parents[2] / "docs/windows/fixtures/brush"
for stage in ["first", "final"]:
    cpu = Image.open(root / f"{stage}-cpu.png").convert("RGBA")
    metal = Image.open(root / f"{stage}-metal.png").convert("RGBA")
    if cpu.size != metal.size:
        raise SystemExit(f"Incompatible image sizes: {stage}")
    difference = ImageChops.difference(cpu, metal)
    histogram = difference.getchannel("A").histogram()
    print(json.dumps({
        "stage": stage,
        "pixels": cpu.width * cpu.height,
        "meanAbsoluteStraightRGBA": ImageStat.Stat(difference).mean,
        "maxAbsoluteStraightRGBA": [extent[1] for extent in difference.getextrema()],
        "alphaDifferentPixels": sum(histogram[1:]),
        "alphaDifferenceOver2": sum(histogram[3:]),
        "alphaDifferenceOver8": sum(histogram[9:]),
        "note": "Mac CPU dabs vs Metal density; measured difference, not an accepted Windows tolerance",
    }, sort_keys=True))
