#!/usr/bin/env python3
"""Validate a local Windows portable package without extracting or executing it."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import zipfile


def safe_path(value: str) -> PurePosixPath:
    normalized = value.replace("\\", "/")
    parts = normalized.split("/")
    if (not normalized or normalized.startswith("/") or ":" in normalized or
            any(part in ("", ".", "..") for part in parts)):
        raise SystemExit(f"unsafe package path: {value}")
    path = PurePosixPath(normalized)
    if path.is_absolute() or not path.parts:
        raise SystemExit(f"unsafe package path: {value}")
    return path


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("package", type=Path)
parser.add_argument("--entry-point", default="Compositor.App.exe")
parser.add_argument("--manifest", default="Compositor.Portable.json")
parser.add_argument("--expected-sha256", required=True)
args = parser.parse_args()

package = args.package.resolve(strict=True)
expected = args.expected_sha256.lower()
if len(expected) != 64 or any(character not in "0123456789abcdef" for character in expected):
    raise SystemExit("expected SHA-256 must contain 64 hexadecimal characters")
actual = hashlib.sha256(package.read_bytes()).hexdigest()
if actual != expected:
    raise SystemExit("package SHA-256 does not match expected value")

entry = safe_path(args.entry_point)
manifest_name = safe_path(args.manifest)

with zipfile.ZipFile(package) as archive:
    files = {}
    archive_paths = set()
    for info in archive.infolist():
        raw_name = info.filename.replace("\\", "/")
        archive_name = safe_path(raw_name.rstrip("/") if info.is_dir() else raw_name)
        key = archive_name.as_posix().casefold()
        if key in archive_paths:
            raise SystemExit(f"duplicate archive path: {archive_name}")
        archive_paths.add(key)
        if info.is_dir():
            directory = raw_name.rstrip("/")
            if directory:
                safe_path(directory)
            continue
        name = safe_path(raw_name)
        if name in files:
            raise SystemExit(f"duplicate archive path: {name}")
        files[name] = info

    if entry not in files:
        raise SystemExit(f"missing entry point: {entry}")
    if manifest_name not in files:
        raise SystemExit(f"missing package manifest: {manifest_name}")

    try:
        with archive.open(files[manifest_name]) as stream:
            manifest = json.load(stream)
    except (OSError, json.JSONDecodeError) as error:
        raise SystemExit(f"invalid package manifest: {error}") from error

    if manifest.get("schema") != "com.compositor.windows-portable" or manifest.get("schemaVersion") != 1:
        raise SystemExit("unsupported package manifest schema")
    if not isinstance(manifest.get("version"), str) or not manifest["version"]:
        raise SystemExit("package manifest has no version")
    if manifest.get("runtimeIdentifier") != "win-x64":
        raise SystemExit("package manifest is not a win-x64 package")
    manifest_entry = safe_path(str(manifest.get("entryPoint", "")))
    if manifest_entry != entry:
        raise SystemExit("manifest entry point does not match the requested entry point")

    manifest_files = set()
    file_records = {}
    for record in manifest.get("files", []):
        name = safe_path(str(record.get("path", "")))
        if name in manifest_files or name == manifest_name:
            raise SystemExit(f"duplicate or reserved manifest path: {name}")
        if name not in files:
            raise SystemExit(f"manifest file is missing from archive: {name}")
        if not isinstance(record.get("bytes"), int) or record["bytes"] < 0:
            raise SystemExit(f"invalid byte count for: {name}")
        digest = str(record.get("sha256", "")).lower()
        if len(digest) != 64 or any(character not in "0123456789abcdef" for character in digest):
            raise SystemExit(f"invalid SHA-256 for: {name}")
        with archive.open(files[name]) as stream:
            actual_bytes = stream.read()
            actual_digest = hashlib.sha256(actual_bytes).hexdigest()
        if len(actual_bytes) != record["bytes"] or actual_digest != digest:
            raise SystemExit(f"manifest hash mismatch: {name}")
        manifest_files.add(name)
        file_records[name] = record

    entry_record = file_records.get(entry)
    if entry_record is None or str(manifest.get("entryPointSha256", "")).lower() != str(entry_record.get("sha256", "")).lower():
        raise SystemExit("manifest entry point hash is inconsistent")

    expected_files = manifest_files | {manifest_name}
    if set(files) != expected_files:
        unexpected = sorted(str(name) for name in set(files) - expected_files)
        missing = sorted(str(name) for name in expected_files - set(files))
        raise SystemExit(f"archive files differ from manifest; unexpected={unexpected}, missing={missing}")
    if manifest.get("fileCount") != len(expected_files):
        raise SystemExit("manifest file count does not match archive")

    native_libraries = []
    native_names = set()
    for record in manifest.get("nativeLibraries", []):
        name = safe_path(str(record.get("path", "")))
        if not str(name).lower().endswith(".dll") or name not in manifest_files:
            raise SystemExit(f"native library is not in the manifest file list: {name}")
        if record.get("architecture") != "x64" or record.get("required") is not True:
            raise SystemExit(f"native library metadata is incomplete: {name}")
        file_record = file_records[name]
        if record.get("bytes") != file_record["bytes"] or str(record.get("sha256", "")).lower() != str(file_record["sha256"]).lower():
            raise SystemExit(f"native library hash is inconsistent: {name}")
        if name in native_names:
            raise SystemExit(f"duplicate native library: {name}")
        native_names.add(name)
        native_libraries.append(name.as_posix())
    if not native_libraries:
        raise SystemExit("package manifest contains no native DLL")

report = {
    "status": "valid",
    "package": str(package),
    "sha256": actual,
    "version": manifest["version"],
    "entryPoint": entry.as_posix(),
    "nativeLibraries": native_libraries,
    "fileCount": len(files),
}
print(json.dumps(report, ensure_ascii=False))
