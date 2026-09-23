"""Run a verified private input test kit; native observations require separate review."""
import datetime
import hashlib
import json
import platform
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
    base = root / "s02-lifecycle-r9-app"
    fixtures = root / "remote-suite/fixtures"
    native = root / "native-run-20260921-111947/compositor_native.dll"
    runtime = root / "pinvoke-test/runtime/dotnet.exe"
    manifest = json.loads((kit / "manifest.json").read_text())
    for directory, entries in [(kit, manifest["payload"]), (base, manifest["baseFiles"]),
                               (fixtures, manifest["fixtures"])]:
        for name, digest in entries.items():
            if sha(directory / name) != digest:
                raise ValueError("File identity differs: " + str(directory / name))
    if sha(native) != manifest["nativeLibrarySha256"]:
        raise ValueError("Native library identity differs")
    stamp = time.strftime("%Y%m%d-%H%M%S")
    out = root / ("avalonia-ime-matrix-" + stamp)
    out.mkdir(exist_ok=False)
    app = out / "app"
    identity = {"sourceCommit": manifest["sourceCommit"], "platform": platform.platform(),
                "utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
                "manifestSha256": sha(kit / "manifest.json"), "processes": [],
                "nativeImeAccepted": False, "systemDpiAccepted": False}

    def save():
        (out / "identity.json").write_text(json.dumps(identity, indent=2) + "\n")

    def run(name, arguments):
        with (out / (name + ".stdout.log")).open("wb") as stdout, (out / (name + ".stderr.log")).open("wb") as stderr:
            process = subprocess.Popen([str(runtime), *map(str, arguments)], cwd=app, stdout=stdout, stderr=stderr)
            row = {"name": name, "pid": process.pid, "arguments": list(map(str, arguments)), "exitCode": None}
            identity["processes"].append(row)
            save()
            print("RUNNING " + name + " PID " + str(process.pid) + " OUTPUT " + str(out), flush=True)
            row["exitCode"] = process.wait()
            save()
            if row["exitCode"] != 0:
                raise RuntimeError(name + " failed; preserve the result archive")

    save()
    try:
        shutil.copytree(base, app)
        for name in manifest["appPayload"]:
            shutil.copyfile(kit / name, app / name)
        identity["dllSha256"] = sha(app / "Compositor.AvaloniaProbe.dll")
        save()
        run("runtime", ["--info"])
        regression = app / "Regression.dll"
        run("window-check", [regression, "--window-matrix", fixtures, out / "window-check", native])
        run("selection", [regression, out / "selection"])
        run("ime-cancel", [regression, "--ime-cancel"])
        run("ime-tabs", [regression, "--ime-tabs"])
        window = out / ("native-" + stamp)
        run("native", [app / "Compositor.AvaloniaProbe.dll", "--window", fixtures, window, native])
        report = json.loads((window / "window-report.json").read_text(encoding="utf-8-sig"))
        if report["processId"] != identity["processes"][-1]["pid"] or not report["nativeWindow"]:
            raise ValueError("Native window process identity differs")
        if any(row["name"] == "action-error" for row in report["events"]):
            raise ValueError("Native window reported an action error")
        identity["executionCompleted"] = True
        save()
    finally:
        files = [p for p in sorted(out.rglob("*")) if p.is_file() and app not in p.parents]
        (out / "files.json").write_text(json.dumps({p.relative_to(out).as_posix(): sha(p) for p in files}, indent=2) + "\n")
        archive = out.with_suffix(".zip")
        with zipfile.ZipFile(archive, "x", zipfile.ZIP_DEFLATED) as target:
            for path in [*files, out / "files.json"]:
                target.write(path, path.relative_to(out))
        print(json.dumps({"archive": str(archive), "bytes": archive.stat().st_size, "sha256": sha(archive),
                          "nativeImeAccepted": False, "systemDpiAccepted": False}), flush=True)


if __name__ == "__main__":
    main()
