#!/usr/bin/env python3
"""Read-only comparison against the separately verified portable runtime archive."""
import argparse
import hashlib
import json
from pathlib import Path
import platform

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("manifest", type=Path)
parser.add_argument("runtime_directory", type=Path)
parser.add_argument("output", type=Path, help="New JSON report; existing files are refused")
args = parser.parse_args()
if args.output.exists():
    raise SystemExit("Refusing to overwrite existing report")
manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
root = args.runtime_directory.resolve(strict=True)
expected = {}
for entry in manifest["files"]:
    name = entry["path"]
    path = Path(name)
    if path.is_absolute() or ".." in path.parts or name in expected:
        raise SystemExit("Invalid or duplicate manifest path")
    expected[name] = entry
checks = []
for name, entry in sorted(expected.items()):
    path = root / name
    if path.is_file():
        data = path.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        matches = len(data) == entry["bytes"] and digest == entry["sha256"]
        checks.append({"path": name, "matches": matches, "bytes": len(data), "sha256": digest})
    else:
        checks.append({"path": name, "matches": False, "missing": True})
actual = {p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file()}
unexpected = sorted(actual - expected.keys())
passed = bool(checks) and all(c["matches"] for c in checks) and not unexpected
report = {
    "status": "matched" if passed else "different", "system": platform.system(),
    "runtimeLaunched": False, "cleanMachineAccepted": False,
    "runtimeDirectory": str(root), "manifestSha256": hashlib.sha256(args.manifest.read_bytes()).hexdigest(),
    "officialArchiveSha256": manifest["archive"]["sha256"],
    "checkedFiles": len(checks), "checks": checks, "unexpectedFiles": unexpected,
}
with args.output.open("x", encoding="utf-8") as output:
    json.dump(report, output, indent=2)
    output.write("\n")
print(json.dumps({"status": report["status"], "checkedFiles": len(checks), "unexpectedFiles": len(unexpected)}))
raise SystemExit(0 if passed else 1)
