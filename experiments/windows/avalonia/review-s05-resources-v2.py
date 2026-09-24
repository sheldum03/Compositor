"""Evaluate S05 proposal v2, distinguishing a prior high-water return from growth."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
previous = Path(__file__).with_name('review-s05-resources.py')
spec = importlib.util.spec_from_file_location('s05_resources_v1', previous)
v1 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(v1)


def evaluate(report):
    result = v1.evaluate(report)
    blocks = result['postCloseBlocks']
    reference = max(block['PrivateBytes']['max'] for block in blocks[:2])
    upper_growth = blocks[2]['PrivateBytes']['max'] - reference
    lower_growth = result['postWarmupEnvelopeDelta']['PrivateBytes']['min']
    old_key = 'postWarmupPrivateEnvelopeGrowthAtMost32MiB'
    result['v1EnvelopeCheckPassed'] = result['checks'].pop(old_key)
    result['checks']['postWarmupPrivateFloorGrowthAtMost32MiB'] = lower_growth <= 32 * v1.MIB
    result['checks']['newPrivateHighWaterGrowthAtMost32MiB'] = upper_growth <= 32 * v1.MIB
    result.update(policy='S05-resource-proposal-v2',
                  priorPostCloseHighWaterBytes=reference,
                  newPostCloseHighWaterGrowthBytes=upper_growth,
                  candidateChecksPassed=all(result['checks'].values()))
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('report', type=Path)
    args = parser.parse_args()
    validation = subprocess.run([sys.executable, str(previous.with_name('review-s05.py')),
                                 str(args.report), '--extended'], capture_output=True, text=True)
    if validation.returncode:
        sys.stderr.write(validation.stderr)
        raise SystemExit(validation.returncode)
    raw = args.report.read_bytes()
    result = evaluate(json.loads(raw.decode('utf-8-sig')))
    result.update(reportSha256=hashlib.sha256(raw).hexdigest(),
                  v1EvaluatorSha256=hashlib.sha256(previous.read_bytes()).hexdigest(),
                  evaluatorSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest())
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result['candidateChecksPassed'] else 1)
