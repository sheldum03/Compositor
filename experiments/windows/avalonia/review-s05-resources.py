"""Evaluate a proposed S05 resource budget; never grant retrospective acceptance."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

MIB = 1024 * 1024


def evaluate(r):
    post = [row['afterClose'] for row in r['rounds']]
    samples = [r['baseline']]
    samples.extend(t['resources'] for t in r['trials'])
    samples.extend(s for row in r['rounds'] for s in (row['beforeClose'], row['afterClose']))
    samples.extend(s['Resources'] for s in r['idleSamples'])
    for sample in samples:
        for key in ('PrivateBytes', 'Handles'):
            if type(sample[key]) is not int or sample[key] <= 0:
                raise ValueError('Missing positive integer resource: ' + key)
    blocks = []
    for start in (0, 9, 18):
        rows = post[start:start + 9]
        blocks.append({key: {'min': min(s[key] for s in rows), 'max': max(s[key] for s in rows)}
                       for key in ('PrivateBytes', 'Handles')})
    delta = {key: {edge: blocks[2][key][edge] - blocks[1][key][edge] for edge in ('min', 'max')}
             for key in ('PrivateBytes', 'Handles')}
    peak = max(s['PrivateBytes'] for s in samples)
    final = r['rounds'][-1]['afterDiagnosticCollection']
    checks = {
        'sampledPrivateAtMost1GiB': peak <= 1024 * MIB,
        'postWarmupPrivateEnvelopeGrowthAtMost32MiB': max(delta['PrivateBytes'].values()) <= 32 * MIB,
        'postWarmupHandleEnvelopeGrowthAtMost8': max(delta['Handles'].values()) <= 8,
        'idlePrivateGrowthAtMost32MiB': max(s['Resources']['PrivateBytes'] for s in r['idleSamples']) <= post[-1]['PrivateBytes'] + 32 * MIB,
        'noRetainedDocumentsAfterDiagnosticGc': r['rounds'][-1]['retainedDocuments'] == 0,
        'diagnosticManagedAtMost64MiB': type(final['ManagedBytes']) is int and 0 <= final['ManagedBytes'] <= 64 * MIB,
    }
    return dict(policy='S05-resource-proposal-v1', candidateChecksPassed=all(checks.values()),
                resourceAccepted=False, checks=checks, sampledPrivatePeakBytes=peak,
                postCloseBlocks=blocks, postWarmupEnvelopeDelta=delta,
                scope='Calibration only. Requires correctness review and prospective Windows runs; not continuous peak, VRAM, other workloads or release acceptance.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('report', type=Path)
    args = parser.parse_args()
    # Reuse the complete 27-round verifier, including frame order and GC timing.
    validation = subprocess.run([sys.executable, str(Path(__file__).with_name('review-s05.py')),
                                 str(args.report), '--extended'], capture_output=True, text=True)
    if validation.returncode:
        sys.stderr.write(validation.stderr)
        raise SystemExit(validation.returncode)
    raw = args.report.read_bytes()
    result = evaluate(json.loads(raw.decode('utf-8-sig')))
    result['reportSha256'] = hashlib.sha256(raw).hexdigest()
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result['candidateChecksPassed'] else 1)
