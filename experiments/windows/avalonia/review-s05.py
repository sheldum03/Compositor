"""Independently check S05 completeness; report resource observations without inventing a stability tolerance."""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('report', type=Path)
parser.add_argument('--allow-local', action='store_true', help='Correctness diagnostics only; never Windows acceptance')
args = parser.parse_args()
r = json.loads(args.report.read_text(encoding='utf-8-sig'))
assert r['scenario'] == 'S05' and r['completed'] and r['error'] is None
assert args.allow_local or (r['nativeWindow'] and r['windowsExecuted']), 'Requires native Windows evidence'
assert len(r['rounds']) == 3 and len(r['trials']) == 300
frames = []
for index, row in enumerate(r['rounds']):
    assert row['round'] == index and row['edits'] == row['undoChecks'] == row['redoChecks'] == 100
    assert row['saveReopenPassed']
    edits = [t for t in r['trials'] if t['round'] == index]
    assert [t['edit'] for t in edits] == list(range(100))
    assert all(len(t['frames']) == 21 and t['commitMilliseconds'] >= 0 for t in edits)
    frames.extend(f for t in edits for f in t['frames'])
assert [f['Sequence'] for f in frames] == list(range(1, 6301))
assert len({v['finalDigest'] for v in r['rounds']}) == 1, 'Identical rounds produced different pixels'
assert all(v['afterDiagnosticCollection'] is None for v in r['rounds'][:2]), 'Unexpected forced GC between rounds'
assert r['rounds'][-1]['retainedDocuments'] == 0
private = [v['afterClose']['PrivateBytes'] for v in r['rounds']]
valid_private = all(v is not None and v > 0 for v in private)
print(json.dumps(dict(correctnessPassed=True, windowsExecuted=r['windowsExecuted'], nativeWindow=r['nativeWindow'],
    edits=300, undoChecks=300, redoChecks=300, previewCallbacks=len(frames), elapsedMilliseconds=r['elapsedMilliseconds'],
    postClosePrivateBytes=private, postCloseHandles=[v['afterClose']['Handles'] for v in r['rounds']],
    monotonicPrivateGrowthObserved=all(a < b for a, b in zip(private, private[1:])) if valid_private else None,
    finalDiagnosticCollection=r['rounds'][-1]['afterDiagnosticCollection'],
    retainedDocumentsAfterCollection=0, resourceAccepted=False,
    scope='Correctness complete. Resource stability needs separate review; managed reachability alone does not prove no native leak. No physical input or VRAM acceptance.'), indent=2))
