"""Reproduce resource-review fault controls using a verified 27-round report."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
script = Path(__file__).with_name('review-s05-resources.py')
source = Path(sys.argv[1])
raw = source.read_bytes()
report = json.loads(raw.decode('utf-8-sig'))
spec = importlib.util.spec_from_file_location('resources', script)
resources = importlib.util.module_from_spec(spec)
spec.loader.exec_module(resources)
baseline = subprocess.run([sys.executable, str(script), str(source)], capture_output=True, text=True)
assert baseline.returncode == 0, baseline.stderr
assert json.loads(baseline.stdout)['resourceAccepted'] is False
blocks = resources.evaluate(report)['postCloseBlocks']
reference = blocks[1]
MIB = 1024 * 1024
cases = []


def reject(name, mutate, check=None):
    changed = copy.deepcopy(report)
    mutate(changed)
    try:
        result = resources.evaluate(changed)
    except ValueError:
        assert check is None, name
    else:
        assert check is not None and result['checks'][check] is False, name
        assert not result['candidateChecksPassed'] and not result['resourceAccepted'], name
    cases.append(dict(case=name, rejected=True))


reject('peak hidden in trial', lambda r: r['trials'][100]['resources'].update(PrivateBytes=1024*MIB+1), 'sampledPrivateAtMost1GiB')
reject('warmup peak cannot be excluded', lambda r: r['baseline'].update(PrivateBytes=1024*MIB+1), 'sampledPrivateAtMost1GiB')
reject('upper envelope growth', lambda r: r['rounds'][20]['afterClose'].update(PrivateBytes=reference['PrivateBytes']['max']+32*MIB+1), 'postWarmupPrivateEnvelopeGrowthAtMost32MiB')
reject('lower envelope growth', lambda r: [row['afterClose'].update(PrivateBytes=max(row['afterClose']['PrivateBytes'], reference['PrivateBytes']['min']+32*MIB+1)) for row in r['rounds'][18:]], 'postWarmupPrivateEnvelopeGrowthAtMost32MiB')
reject('handle growth', lambda r: r['rounds'][20]['afterClose'].update(Handles=reference['Handles']['max']+9), 'postWarmupHandleEnvelopeGrowthAtMost8')
reject('delayed idle growth', lambda r: r['idleSamples'][-1]['Resources'].update(PrivateBytes=r['rounds'][-1]['afterClose']['PrivateBytes']+32*MIB+1), 'idlePrivateGrowthAtMost32MiB')
reject('retained document', lambda r: r['rounds'][-1].update(retainedDocuments=1), 'noRetainedDocumentsAfterDiagnosticGc')
reject('managed survivor budget', lambda r: r['rounds'][-1]['afterDiagnosticCollection'].update(ManagedBytes=64*MIB+1), 'diagnosticManagedAtMost64MiB')
reject('missing private bytes', lambda r: r['trials'][0]['resources'].update(PrivateBytes=None))
reject('invalid boolean handles', lambda r: r['rounds'][0]['afterClose'].update(Handles=True))

boundary = copy.deepcopy(report)
boundary['baseline']['PrivateBytes'] = 1024*MIB
boundary['rounds'][20]['afterClose'].update(PrivateBytes=reference['PrivateBytes']['max']+32*MIB, Handles=reference['Handles']['max']+8)
boundary['rounds'][-1]['afterDiagnosticCollection']['ManagedBytes'] = 64*MIB
assert resources.evaluate(boundary)['candidateChecksPassed']
cases.append(dict(case='inclusive budget boundaries', passed=True, resourceAccepted=False))

with tempfile.TemporaryDirectory() as directory:
    for name, mutate in (
        ('not Windows', lambda r: r.update(windowsExecuted=False)),
        ('incomplete', lambda r: r.update(completed=False)),
        ('early forced GC', lambda r: r['rounds'][0].update(afterDiagnosticCollection=r['rounds'][-1]['afterDiagnosticCollection'])),
    ):
        changed = copy.deepcopy(report)
        mutate(changed)
        path = Path(directory)/'report.json'
        path.write_text(json.dumps(changed))
        result = subprocess.run([sys.executable, str(script), str(path)], capture_output=True, text=True)
        assert result.returncode != 0 and not result.stdout, name
        cases.append(dict(case=name, exitCode=result.returncode, rejected=True))

print(json.dumps(dict(sourceReportSha256=hashlib.sha256(raw).hexdigest(),
    evaluatorSha256=hashlib.sha256(script.read_bytes()).hexdigest(),
    controlsSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    calibrationExitCode=baseline.returncode, resourceAccepted=False, cases=cases), indent=2))
