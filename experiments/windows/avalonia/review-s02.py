"""Recompute the proposed S02 gates from a completed native Windows report."""
import json
import math
import sys
from pathlib import Path

report = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
assert report['completed'] and report['error'] is None, 'Run did not complete correctly'
assert report['nativeWindow'] and report['windowsExecuted'], 'Requires native Windows evidence'
assert report['measuredCountPerScenario'] >= 30
assert report['diameter'] == 800 and report['hardness'] == 0 and report['opacity'] == 1
assert report['viewport'] == '1000x1000 logical; 4000x4000 document; scale .25'

def p95(values):
    return sorted(values)[math.ceil(len(values) * .95) - 1]

rows = []
for scenario in ('empty', 'existing'):
    trials = [t for t in report['trials'] if t['scenario'] == scenario and not t['warmup']]
    assert len(trials) == report['measuredCountPerScenario']
    assert all(len(t['updates']) == 120 for t in trials)
    assert all(t['correctness'] == 'settled replay/immutable source/undo/redo exact' for t in trials)
    frames = [f for t in trials for f in t['updates']]
    update = p95([f['UpdateToCanvasLeaseReleasedMilliseconds'] for f in frames])
    commit = p95([t['commitMilliseconds'] for t in trials])
    row = dict(scenario=scenario, measuredFrames=len(frames), updateP95=update, commitP95=commit,
               updatePassed=update <= 16.7, commitPassed=commit <= 100)
    for field in ('AppendMilliseconds', 'PaintMilliseconds', 'RequestToRenderMilliseconds',
                  'CanvasAcquireMilliseconds', 'CanvasReleaseMilliseconds',
                  'RequestToSceneMilliseconds', 'SceneToRenderMilliseconds'):
        if all(field in f for f in frames):
            assert all(f[field] >= 0 for f in frames), field
            row[field + 'P95'] = p95([f[field] for f in frames])
    rows.append(row)
peak = report['sampledPrivatePeak']
passed = all(r['updatePassed'] and r['commitPassed'] for r in rows) and peak is not None and peak <= 2 * 1024**3
print(json.dumps(dict(scenarios=rows, sampledPrivatePeak=peak, proposedS02GatesPassed=passed,
    scope='Nearest rank; excludes warmup and pointer-down. Native canvas lease release, not physical presentation. Unobscured viewport and image comparisons require separate evidence.'), indent=2))
sys.exit(0 if passed else 1)
