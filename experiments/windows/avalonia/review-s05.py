"""Independently check S05 completeness; report resource observations without inventing a stability tolerance."""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('report', type=Path)
parser.add_argument('--allow-local', action='store_true', help='Correctness diagnostics only; never Windows acceptance')
parser.add_argument('--soak', action='store_true', help='Require the separate nine-round diagnostic, not the three-round S05 scenario')
parser.add_argument('--idle', action='store_true', help='Require nine rounds followed by 60 seconds of natural idle observations')
parser.add_argument('--extended', action='store_true', help='Require 27 rounds in one process followed by 60 seconds of natural idle observations')
parser.add_argument('--gc-diagnostics', action='store_true', help='Require allocation and GC samples from R5 or later')
args = parser.parse_args()
r = json.loads(args.report.read_text(encoding='utf-8-sig'))
assert sum([args.soak, args.idle, args.extended]) <= 1, 'Choose one diagnostic mode'
round_count = 27 if args.extended else 9 if args.soak or args.idle else 3
scenario = 'S05-extended-diagnostic' if args.extended else 'S05-idle-diagnostic' if args.idle else 'S05-soak-diagnostic' if args.soak else 'S05'
observe_idle = args.idle or args.extended
assert r['scenario'] == scenario and r['completed'] and r['error'] is None
assert r.get('expectedRounds', 3) == round_count
assert args.allow_local or (r['nativeWindow'] and r['windowsExecuted']), 'Requires native Windows evidence'
assert len(r['rounds']) == round_count and len(r['trials']) == 100 * round_count
frames = []
for index, row in enumerate(r['rounds']):
    assert row['round'] == index and row['edits'] == row['undoChecks'] == row['redoChecks'] == 100
    assert row['saveReopenPassed']
    edits = [t for t in r['trials'] if t['round'] == index]
    assert [t['edit'] for t in edits] == list(range(100))
    assert all(len(t['frames']) == 21 and t['commitMilliseconds'] >= 0 for t in edits)
    frames.extend(f for t in edits for f in t['frames'])
assert [f['Sequence'] for f in frames] == list(range(1, 2100 * round_count + 1))
assert len({v['finalDigest'] for v in r['rounds']}) == 1, 'Identical rounds produced different pixels'
assert all(v['afterDiagnosticCollection'] is None for v in r['rounds'][:-1]), 'Unexpected forced GC between rounds'
assert r['rounds'][-1]['afterDiagnosticCollection'] is not None
assert r['rounds'][-1]['retainedDocuments'] == 0
if observe_idle:
    assert r.get('resourceDiagnosticsVersion') == 3 and r['idleObservationSeconds'] == 60
    idle = r['idleSamples']
    assert len(idle) == 7 and idle[0]['ElapsedMilliseconds'] == 0, 'Missing idle samples'
    assert idle[0]['Resources'] == r['rounds'][-1]['afterClose'], 'Idle baseline is not the natural post-close sample'
    times = [s['ElapsedMilliseconds'] for s in idle]
    assert all(b - a >= 9900 for a, b in zip(times, times[1:])) and times[-1] >= 60000, 'Idle observation too short'
    assert all(isinstance(s['RetainedDocuments'], int) and 0 <= s['RetainedDocuments'] <= round_count for s in idle)
if args.gc_diagnostics or observe_idle:
    assert r.get('resourceDiagnosticsVersion') in (2, 3), 'Requires allocation/GC diagnostics, not earlier evidence'
    samples = [r['baseline']]
    for index, row in enumerate(r['rounds']):
        samples.extend(t['resources'] for t in r['trials'] if t['round'] == index)
        samples.extend([row['beforeClose'], row['afterClose']])
    if observe_idle:
        samples.extend(s['Resources'] for s in idle[1:])
    samples.append(r['rounds'][-1]['afterDiagnosticCollection'])
    cumulative = ['TotalAllocatedBytesEstimate', 'Gen0Collections', 'Gen1Collections', 'Gen2Collections', 'LastGcIndex']
    for key in cumulative:
        values = [s[key] for s in samples]
        assert all(isinstance(v, int) and v >= 0 for v in values), key
        assert all(a <= b for a, b in zip(values, values[1:])), key + ' decreased'
    for sample in samples:
        assert sample['LastGcGeneration'] in (0, 1, 2)
        assert all(sample[key] >= 0 for key in ['LastGcHeapSizeBytes', 'LastGcPromotedBytes', 'LastGcLohSizeAfterBytes'])
    assert samples[-1]['Gen2Collections'] > samples[-2]['Gen2Collections'], 'Final diagnostic GC not observed'
private = [v['afterClose']['PrivateBytes'] for v in r['rounds']]
valid_private = all(v is not None and v > 0 for v in private)
print(json.dumps(dict(correctnessPassed=True, windowsExecuted=r['windowsExecuted'], nativeWindow=r['nativeWindow'],
    diagnosticOnly=args.soak or observe_idle, rounds=round_count, gcDiagnosticsChecked=args.gc_diagnostics or observe_idle,
    idleSamples=r['idleSamples'] if observe_idle else [],
    edits=100 * round_count, undoChecks=100 * round_count, redoChecks=100 * round_count, previewCallbacks=len(frames), elapsedMilliseconds=r['elapsedMilliseconds'],
    postClosePrivateBytes=private, postCloseHandles=[v['afterClose']['Handles'] for v in r['rounds']],
    monotonicPrivateGrowthObserved=all(a < b for a, b in zip(private, private[1:])) if valid_private else None,
    finalDiagnosticCollection=r['rounds'][-1]['afterDiagnosticCollection'],
    retainedDocumentsAfterCollection=0, resourceAccepted=False,
    scope='Correctness complete. Resource stability needs separate review; managed reachability alone does not prove no native leak. No physical input or VRAM acceptance.'), indent=2))
