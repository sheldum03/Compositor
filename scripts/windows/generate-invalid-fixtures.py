#!/usr/bin/env python3
"""Derive small, deliberately damaged v8 packages from the checked-in F08 input."""
import argparse
import copy
import json
from pathlib import Path
import shutil

REPOSITORY = Path(__file__).resolve().parents[2]
SOURCE = REPOSITORY / "docs/windows/fixtures/F08.comp"


def generate(destination):
    if destination.exists():
        raise SystemExit(f"Refusing to overwrite {destination}")
    source = json.loads((SOURCE / "manifest.json").read_text())
    cases = []

    def add(name, failure, change=None, asset_action=None):
        package = destination / (name + ".comp")
        shutil.copytree(SOURCE, package)
        manifest = copy.deepcopy(source)
        if change:
            change(manifest)
        (package / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
        if asset_action:
            asset_action(package, manifest)
        cases.append({"name": name, "failure": failure})

    add("future-version", "version", lambda m: m.update(version=99))
    add("text-in-v7", "invalid", lambda m: m.update(version=7))
    add("duplicate-id", "invalid", lambda m: m["layers"].append(copy.deepcopy(m["layers"][1])))
    add("missing-parent", "invalid", lambda m: m["layers"][1].update(parentID="00000000-0000-4000-8000-999999999999"))
    add("group-cycle", "invalid", lambda m: m["layers"][0].update(parentID=m["layers"][0]["id"]))
    add("mask-cycle", "invalid", lambda m: m["layers"][1].update(maskSourceID=m["layers"][2]["id"]))
    add("path-escape", "invalid", lambda m: m["layers"][1].update(imageFile="../../outside.png"))
    add("oversize-canvas", "tooLarge", lambda m: m.update(width=30001))
    add("unknown-blend", "invalid", lambda m: m["layers"][2].update(blendMode="Unimplemented Blend"))
    add("invalid-opacity", "invalid", lambda m: m["layers"][2].update(opacity=-0.1))
    add("missing-image", "fileMissing", asset_action=lambda p, m: (p / "images" / m["layers"][1]["imageFile"]).unlink())
    add("damaged-image", "missingImage", asset_action=lambda p, m: (p / "images" / m["layers"][1]["imageFile"]).write_bytes(b"not a PNG\n"))
    add("nonfinite-transform", "invalid", lambda m: m["layers"][1]["transform"].update(rotation="NaN"))
    # Valid JSON with a numeric value that overflows Double, distinct from a string type mismatch.
    overflow = destination / "numeric-overflow.comp"
    shutil.copytree(SOURCE, overflow)
    data = (overflow / "manifest.json").read_text().replace('"rotation" : 0', '"rotation" : 1e999', 1)
    assert "1e999" in data
    (overflow / "manifest.json").write_text(data)
    cases.append({"name": "numeric-overflow", "failure": "invalid"})
    (destination / "cases.json").write_text(json.dumps(cases, indent=2) + "\n")
    print(f"Generated {len(cases)} invalid fixtures at {destination}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", type=Path)
    generate(parser.parse_args().destination)
