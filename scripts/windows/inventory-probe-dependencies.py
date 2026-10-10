#!/usr/bin/env python3
"""Inventory the existing win-x64 probe publish; do not alter or approve it."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET
import zipfile


def sha(data):
    return hashlib.sha256(data).hexdigest()


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("publish", type=Path)
parser.add_argument("nuget_cache", type=Path)
parser.add_argument("output", type=Path, help="New evidence directory")
args = parser.parse_args()
repo = Path(__file__).resolve().parents[2]
lock_path = repo / "experiments/windows/avalonia/packages.lock.json"
lock = json.loads(lock_path.read_text())["dependencies"]["net10.0"]
deps_path = args.publish / "Compositor.AvaloniaProbe.deps.json"
deps = json.loads(deps_path.read_text())
target = deps["runtimeTarget"]["name"]
if target != ".NETCoreApp,Version=v10.0/win-x64":
    raise SystemExit("Expected the existing net10.0/win-x64 probe publish")
args.output.mkdir(parents=True, exist_ok=False)
records = []
for name, entry in sorted(lock.items()):
    version = entry["resolved"]
    folder = args.nuget_cache / name.lower() / version
    archive = folder / f"{name.lower()}.{version}.nupkg"
    identity = f"{name}/{version}"
    selected = deps["targets"][target].get(identity, {})
    record = {"name": name, "version": version, "dependencyType": entry["type"],
              "lockContentHash": entry["contentHash"],
              "archiveSha256": sha(archive.read_bytes()), "assets": [], "notices": []}
    with zipfile.ZipFile(archive) as package:
        nuspecs = [n for n in package.namelist() if n.endswith(".nuspec")]
        if len(nuspecs) != 1:
            raise SystemExit(f"Expected one nuspec: {identity}")
        nuspec = package.read(nuspecs[0])
        metadata = ET.fromstring(nuspec).find("{*}metadata")
        if metadata.findtext("{*}id").lower() != name.lower() or metadata.findtext("{*}version") != version:
            raise SystemExit(f"Package identity mismatch: {identity}")
        license_node = metadata.find("{*}license")
        record["licenseMetadata"] = None if license_node is None else {
            "type": license_node.get("type"), "value": license_node.text}
        record["repository"] = getattr(metadata.find("{*}repository"), "attrib", {})
        destination = args.output / "packages" / name / version
        destination.mkdir(parents=True)
        (destination / "package.nuspec").write_bytes(nuspec)
        record["nuspecSha256"] = sha(nuspec)
        for kind in ("runtime", "native"):
            for asset in selected.get(kind, {}):
                if Path(asset).name == "_._":
                    continue
                published = args.publish / Path(asset).name
                data = package.read(asset)
                if published.read_bytes() != data:
                    raise SystemExit(f"Published asset differs from NuGet package: {published.name}")
                record["assets"].append({"kind": kind, "packagePath": asset,
                                         "publishedPath": published.name, "sha256": sha(data)})
        for item in package.namelist():
            path = Path(item)
            if item.endswith("/") or not any(s in path.name.lower() for s in ("license", "notice", "copying")):
                continue
            if path.is_absolute() or ".." in path.parts:
                raise SystemExit("Unsafe notice path")
            data = package.read(item)
            output = destination / "notices" / path
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(data)
            record["notices"].append({"packagePath": item, "bytes": len(data), "sha256": sha(data)})
    records.append(record)

published_notices = []
for file in sorted((args.publish / "Licenses").rglob("*")):
    if file.is_file():
        relative = file.relative_to(args.publish)
        output = args.output / "published" / relative
        output.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(file, output)
        published_notices.append({"path": relative.as_posix(), "sha256": sha(file.read_bytes())})
result = {
    "status": "local artifact inventory; D-07 and redistribution acceptance remain open",
    "publishPath": str(args.publish.resolve()), "runtimeTarget": target,
    "lockSha256": sha(lock_path.read_bytes()), "depsSha256": sha(deps_path.read_bytes()),
    "packageCount": len(records), "matchedAssetCount": sum(len(r["assets"]) for r in records),
    "packages": records, "publishedLicenseDirectory": published_notices,
    "limits": ["NuGet signatures and lock content hashes are not independently authenticated by this script",
               "Package license metadata is not an audit of all embedded native third-party components",
               "External .NET runtime, compositor_native.dll, Qt, HEIC and model weights are outside this publish inventory",
               "No Windows process was executed and no test package was changed"]}
(args.output / "inventory.json").write_text(json.dumps(result, indent=2) + "\n")
print(json.dumps({k: result[k] for k in ("status", "packageCount", "matchedAssetCount")}))
