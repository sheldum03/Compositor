#!/usr/bin/env python3
"""Validate a local Windows portable package without extracting or executing it."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import zipfile


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("package", type=Path)
parser.add_argument("--entry-point", default="Compositor.App.exe")
parser.add_argument("--expected-sha256", required=True)
args = parser.parse_args()

package = args.package.resolve(strict=True)
expected = args.expected_sha256.lower()
if len(expected) != 64 or any(character not in "0123456789abcdef" for character in expected):
    raise SystemExit("expected SHA-256 must contain 64 hexadecimal characters")
actual = hashlib.sha256(package.read_bytes()).hexdigest()
if actual != expected:
    raise SystemExit("package SHA-256 does not match expected value")

entry = PurePosixPath(args.entry_point.replace("\\", "/"))
if entry.is_absolute() or not entry.parts or any(part in ("", ".", "..") for part in entry.parts):
    raise SystemExit("entry point must be a safe relative path")

with zipfile.ZipFile(package) as archive:
    names = [PurePosixPath(name.replace("\\", "/")) for name in archive.namelist()]
    for name in names:
        if name.is_absolute() or any(part in ("", ".", "..") for part in name.parts):
            raise SystemExit(f"unsafe archive path: {name}")
    files = [name for name, info in zip(names, archive.infolist()) if not info.is_dir()]
    if entry not in files:
        raise SystemExit(f"missing entry point: {entry}")
    duplicate_files = sorted({name for name in files if files.count(name) > 1})
    if duplicate_files:
        raise SystemExit(f"duplicate archive paths: {duplicate_files}")

report = {
    "status": "valid",
    "package": str(package),
    "sha256": actual,
    "entryPoint": entry.as_posix(),
    "fileCount": len(files),
}
print(json.dumps(report, ensure_ascii=False))
