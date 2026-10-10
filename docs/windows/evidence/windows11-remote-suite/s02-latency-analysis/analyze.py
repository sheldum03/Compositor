"""Read existing Windows timing records; do not run a benchmark or change a gate."""
import hashlib
import json
import math
import statistics
from pathlib import Path

source = Path('/Users/admin/.codex/visualizations/2026/09/20/01a0bf7d-45c7-7403-8411-eb985b4d9ff8/windows11-remote-suite/s02-visible-20260921-190545/results/report.json')
raw = source.read_bytes()
assert hashlib.sha256(raw).hexdigest() == 'd858c477eaf7289921d15d34a00f11774dde927f871750750a0ecf5ac3628c76'
report = json.loads(raw)
assert report['completed'] and report['error'] is None and report['nativeWindow'] and report['windowsExecuted']

def distribution(values):
    ordered = sorted(values)
    return dict(mean=statistics.mean(values), p50=ordered[math.ceil(.5*len(values))-1],
                p95=ordered[math.ceil(.95*len(values))-1], p99=ordered[math.ceil(.99*len(values))-1], maximum=max(values))

result = {'source': str(source), 'sourceSha256': hashlib.sha256(raw).hexdigest(),
          'method': 'nearest-rank percentiles; warmup and pointer-down excluded; no new benchmark',
          'scope': 'CPU append and paint are wall-clock sections, not sampled CPU stacks. Residual includes scheduling, canvas acquisition/release and other uninstrumented elapsed time; not pure queue time. Individual P95 values must not be summed.',
          'performanceAccepted': False, 'scenarios': []}
for scenario in ('empty', 'existing'):
    trials = [t for t in report['trials'] if t['scenario'] == scenario and not t['warmup']]
    frames = [f for t in trials for f in t['updates']]
    assert len(trials) == 30 and len(frames) == 3600
    append = [f['AppendMilliseconds'] for f in frames]
    paint = [f['PaintMilliseconds'] for f in frames]
    total = [f['UpdateToCanvasLeaseReleasedMilliseconds'] for f in frames]
    work = [a+p for a,p in zip(append,paint)]
    residual = [t-w for t,w in zip(total,work)]
    assert min(residual) >= 0
    result['scenarios'].append({'name': scenario, 'frames': len(frames),
        'appendMilliseconds': distribution(append), 'paintMilliseconds': distribution(paint),
        'appendPlusPaintMilliseconds': distribution(work), 'totalMilliseconds': distribution(total),
        'residualMilliseconds': distribution(residual),
        'framesOver16_7Milliseconds': sum(t>16.7 for t in total),
        'appendPlusPaintOver16_7Milliseconds': sum(w>16.7 for w in work),
        'meanNativePixelCopyMiBPerFrame': statistics.mean(f['NativePixelCopyBytes'] for f in frames)/2**20,
        'gcCollectionsAcrossMeasuredTrials': [sum(t['gcCollections'][g] for t in trials) for g in range(3)],
        'renderThreadIds': sorted({f['RenderThread'] for f in frames})})
Path(__file__).with_name('analysis.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result,indent=2))
