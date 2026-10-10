"""Run the pinned font-loading experiment in a fresh directory on the test Windows host."""
import hashlib
import json
import shutil
import subprocess
import sys
import time
import zipfile
from pathlib import Path


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    if sys.platform != "win32":
        raise SystemExit("Actual Windows required")
    kit = Path(__file__).resolve().parent
    root = kit.parent
    manifest = json.loads((kit / "manifest.json").read_text())
    base = root / manifest["baseApp"]
    runtime = root / "pinvoke-test/runtime"
    for directory, entries in [(kit, manifest["payload"]), (base, manifest["dependencies"]),
                               (runtime, manifest["runtimeFiles"])]:
        for name, digest in entries.items():
            if sha(directory / name) != digest:
                raise ValueError("File identity differs: " + str(directory / name))
    out = root / ("font-loading-" + time.strftime("%Y%m%d-%H%M%S"))
    out.mkdir(exist_ok=False)
    app = out / "app"
    app.mkdir()
    identity = {"manifestSha256": sha(kit / "manifest.json"), "processes": [],
                "windowsExecuted": True, "fontLoadingPassed": False, "productionFontImportAccepted": False}
    try:
        for name in manifest["dependencies"]:
            shutil.copyfile(base / name, app / name)
        for name in manifest["appPayload"]:
            shutil.copyfile(kit / name, app / name)
        for mode, extra, expected_exit in [("raw", ["--raw"], 1), ("adapted", [], 0)]:
            with (out / (mode + ".json")).open("wb") as stdout, (out / (mode + ".stderr.log")).open("wb") as stderr:
                p = subprocess.Popen([str(runtime / "dotnet.exe"), str(app / "FontLoadingProbe.dll"),
                                      str(kit / "fonts"), *extra], cwd=app, stdout=stdout, stderr=stderr)
                exit_code = p.wait()
            identity["processes"].append({"mode": mode, "pid": p.pid, "exitCode": exit_code})
            if exit_code != expected_exit:
                raise RuntimeError(mode + " exit differs; preserve raw output")
            report = json.loads((out / (mode + ".json")).read_text(encoding="utf-8-sig"))
            if report["processId"] != p.pid or not report["windowsExecuted"]:
                raise ValueError("Process identity differs")
            if [row["passed"] for row in report["results"]] != [True, True, True, mode == "adapted"]:
                raise ValueError("Unexpected font results")
            if report["rejectedFiles"] != ["empty.otf", "damaged.ttf", "truncated.ttc"]:
                raise ValueError("Invalid-font rejection differs")
        identity["fontLoadingPassed"] = True
    finally:
        (out / "identity.json").write_text(json.dumps(identity, indent=2) + "\n")
        files = [p for p in out.iterdir() if p.is_file()]
        (out / "files.json").write_text(json.dumps({p.name: sha(p) for p in files}, indent=2) + "\n")
        archive = out.with_suffix(".zip")
        with zipfile.ZipFile(archive, "x", zipfile.ZIP_DEFLATED) as z:
            for path in [*files, out / "files.json"]:
                z.write(path, path.name)
        print(json.dumps({"archive": str(archive), "sha256": sha(archive), **identity}), flush=True)


if __name__ == "__main__":
    main()
