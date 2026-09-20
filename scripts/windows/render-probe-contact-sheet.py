#!/usr/bin/env python3
"""Optional visual diagnostics; requires Pillow 11.3.0. Inputs remain unchanged."""
import argparse
from pathlib import Path

from PIL import Image, ImageDraw

parser = argparse.ArgumentParser()
parser.add_argument("fixtures", type=Path)
parser.add_argument("probe_output", type=Path)
parser.add_argument("destination", type=Path)
args = parser.parse_args()
if args.destination.exists():
    parser.error("destination must not exist")

sheet = Image.new("RGB", (1664, 1696), (24, 26, 31))
labels = ImageDraw.Draw(sheet)
for row, name in enumerate(["F04", "F05", "F06", "F07"]):
    inputs = [
        ("Mac reference", args.fixtures / f"{name}-mac.png"),
        ("Avalonia CPU", args.probe_output / f"{name}-export.png"),
        ("Difference x32 (RGB red, alpha blue)", args.probe_output / f"{name}-diff.png"),
    ]
    for column, (label, path) in enumerate(inputs):
        with Image.open(path) as source:
            image = source.convert("RGBA").resize((512, 384), Image.Resampling.NEAREST)
        background = Image.new("RGBA", image.size, (212, 212, 212, 255))
        checker = ImageDraw.Draw(background)
        for y in range(0, 384, 16):
            for x in range(0, 512, 16):
                if (x // 16 + y // 16) % 2:
                    checker.rectangle((x, y, x + 15, y + 15), fill=(242, 242, 242, 255))
        background.alpha_composite(image)
        x, y = 16 + column * 552, 8 + row * 424
        labels.text((x, y), name + " / " + label, fill=(244, 245, 250))
        sheet.paste(background.convert("RGB"), (x, y + 22))
sheet.save(args.destination)
