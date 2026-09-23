"""Validate Qt S02 raw samples; local check mode never accepts Windows performance."""
import argparse
import json
import math
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("report", type=Path)
parser.add_argument("--local-check", action="store_true")
args = parser.parse_args()
report = json.loads(args.report.read_text(encoding="utf-8-sig"))
assert report["completed"] is True and report["error"] is None and report["windowClosed"] is False
assert report["performanceAccepted"] is False
assert report["diameter"] == 800 and report["hardness"] == 0 and report["opacity"] == 1
assert report["color"] == [1, .3, .1]
assert report["viewport"] == "1000x1000 logical; 4000x4000 document; scale .25"
assert report["clientWidth"] == report["clientHeight"] == 1000
count = report["measuredCountPerScenario"]
if args.local_check:
    assert report["nativeWindow"] is False and count == 1
else:
    assert report["nativeWindow"] is True and report["windowsExecuted"] is True
    assert report["platformPlugin"] == "windows" and count == 30
checks = report["viewportChecks"]
assert [c["scenario"] for c in checks] == ["empty", "existing"]
assert all(c[field] == 0 for c in checks for field in ("DifferentPixels", "MaximumChannelError", "MaximumAlphaError", "MeanAbsoluteChannelError", "PixelsWithErrorAbove1"))
assert all(c["width"] == c["height"] == math.ceil(1000 * report["renderScaling"]) for c in checks)


def finite(value):
    assert type(value) in (int, float) and math.isfinite(value) and value >= 0
    return value


def p95(values):
    return sorted(values)[math.ceil(len(values) * .95) - 1]


trials = report["trials"]
assert len(trials) == 2 * (count + 1)
sequence = 0
private = []
rows = []
for scenario in ("empty", "existing"):
    group = trials[(count + 1) * len(rows):(count + 1) * (len(rows) + 1)]
    digest = group[0]["digest"]
    assert len(digest) == 64 and all(c in "0123456789abcdef" for c in digest)
    for index, trial in enumerate(group):
        assert trial["scenario"] == scenario and trial["trial"] == index
        assert trial["warmup"] is (index == 0)
        assert trial["correctness"] == "settled replay/immutable source/undo/redo exact"
        assert trial["digest"] == digest
        assert len(trial["updates"]) == 120 and len(trial["privateBytes"]) == 122
        finite(trial["commitMilliseconds"])
        for frame in [trial["pointerDown"], *trial["updates"]]:
            sequence += 1
            assert frame["sequence"] == sequence
            total = finite(frame["UpdateToFrameCallbackMilliseconds"])
            for field in ("AppendMilliseconds", "PaintMilliseconds", "RequestToPaintMilliseconds", "UpdateToPainterReleasedMilliseconds", "ViewportCheckMilliseconds", "ChecksToCallbackMilliseconds"):
                assert finite(frame[field]) <= total
        for value in trial["privateBytes"]:
            if value is not None:
                assert finite(value) > 0
                private.append(value)
            elif not args.local_check:
                raise AssertionError("Windows PrivateUsage sampling unavailable")
    frames = [f for t in group[1:] for f in t["updates"]]
    update = p95([f["UpdateToFrameCallbackMilliseconds"] for f in frames])
    commit = p95([t["commitMilliseconds"] for t in group[1:]])
    rows.append({"scenario": scenario, "measuredFrames": len(frames), "updateP95": update,
                 "commitP95": commit, "updatePassed": update <= 16.7, "commitPassed": commit <= 100})
assert sequence == report["updatesCompleted"] == 2 * (count + 1) * 121
peak = max(private) if private else None
assert report["sampledPrivatePeak"] == peak
passed = not args.local_check and all(r["updatePassed"] and r["commitPassed"] for r in rows) and peak is not None and peak <= 2 * 1024**3
print(json.dumps({"rawSamplesValidated": True, "localCheckOnly": args.local_check,
                  "sampledPrivatePeak": peak, "scenarios": rows, "proposedS02NumbersPassed": passed,
                  "scope": "Queued callback after Qt raster paint/endPaint/flush returns, including scheduling; not physical presentation or a framework ranking. PNG files and unobscured native viewport require separate review."}, indent=2))
raise SystemExit(0 if args.local_check or passed else 1)
