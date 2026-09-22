"""Require actual CPU work in a canceled inference and exact same-session recovery."""
import argparse
import hashlib
import json
import platform
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('output', type=Path)
args = parser.parse_args()
root = args.output
inference = json.loads((root / 'inference.json').read_text())
assert inference['activeInferenceCancellationTested'], 'Only pre-termination was tested'
result = json.loads((root / 'active-cancellation.json').read_text())
assert result['terminationErrorObserved'] and result['workerJoined'] and result['sessionRecovered']
assert not result['cancelledCallReturnedOutput']
assert result['cancelToJoinMilliseconds'] >= 0
profiles = list(root.glob('active-profile*.json'))
assert len(profiles) == 1
profile = json.loads(profiles[0].read_text())
runs = sorted((e for e in profile if e.get('name') == 'model_run'), key=lambda e: e['ts'])
assert len(runs) == 2, 'Require canceled run followed by one successful recovery'
counts = []
for run in runs:
    kernels = [e for e in profile if e.get('cat') == 'Node' and e.get('name', '').endswith('_kernel_time')
               and run['ts'] <= e['ts'] and e['ts'] + e['dur'] <= run['ts'] + run['dur']]
    assert kernels, 'No actual kernel work observed within inference'
    assert all(e.get('args', {}).get('provider') == 'CPUExecutionProvider' for e in kernels)
    counts.append(len(kernels))
baseline = (root / 'mask.f32').read_bytes()
assert len(baseline) == 320 * 320 * 4
assert baseline == (root / 'active-recovered.f32').read_bytes(), 'Recovery changed output'
print(json.dumps(dict(activeCancellationEvidencePassed=True,
    host=platform.platform(), windowsExecuted=platform.system() == 'Windows',
    cancelledRunKernelEvents=counts[0], recoveredRunKernelEvents=counts[1],
    cancelToJoinMilliseconds=result['cancelToJoinMilliseconds'],
    recoveredPredictionSha256=hashlib.sha256(baseline).hexdigest(),
    scope='Native cooperative cancellation only; no UI transaction, Windows, quality or release acceptance.'), indent=2))
