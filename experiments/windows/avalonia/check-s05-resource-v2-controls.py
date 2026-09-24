"""Synthetic rule controls, not Windows runtime or resource-acceptance evidence."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
script = Path(__file__).with_name('review-s05-resources-v2.py')
spec = importlib.util.spec_from_file_location('resources_v2', script)
resources = importlib.util.module_from_spec(spec)
spec.loader.exec_module(resources)
source = Path(sys.argv[1])
raw = source.read_bytes()
report = json.loads(raw.decode('utf-8-sig'))
MIB = 1024 * 1024
cases = []


def series(values):
    changed = copy.deepcopy(report)
    for row, value in zip(changed['rounds'], values, strict=True):
        row['afterClose']['PrivateBytes'] = value
    return changed


# Same bounded cycle at all 18 starting phases. There is no accumulated term.
cycle = [350, 512] + [350] * 7 + [350, 448] + [350] * 7
for phase in range(18):
    changed = series([cycle[(n + phase) % 18] * MIB for n in range(27)])
    result = resources.evaluate(changed)
    assert result['candidateChecksPassed'] and not result['resourceAccepted'], phase
    cases.append(dict(case='bounded-cycle-phase-' + str(phase), v2Passed=True,
                      v1EnvelopePassed=result['v1EnvelopeCheckPassed']))
assert sum(not c['v1EnvelopePassed'] for c in cases) == 9


def reject(name, changed, check):
    result = resources.evaluate(changed)
    assert not result['checks'][check] and not result['candidateChecksPassed'], name
    assert not result['resourceAccepted'], name
    cases.append(dict(case=name, rejected=True))


floor = 'postWarmupPrivateFloorGrowthAtMost32MiB'
upper = 'newPrivateHighWaterGrowthAtMost32MiB'
reject('4 MiB per round accumulation', series([(350 + 4*n)*MIB for n in range(27)]), floor)
reject('early spike cannot hide later accumulation',
       series([900*MIB] + [(350 + 4*n)*MIB for n in range(1, 27)]), floor)
reject('one retained 4K RGBA plane', series([350*MIB]*18 + [350*MIB + 4000*4000*4]*9), floor)
reject('new high-water excursion', series([350*MIB]*26 + [383*MIB]), upper)
reject('warmup spike cannot escape budget', series([1024*MIB+1] + [350*MIB]*26), 'sampledPrivateAtMost1GiB')
boundary = series([350*MIB]*18 + [382*MIB]*9)
assert resources.evaluate(boundary)['candidateChecksPassed']
cases.append(dict(case='inclusive 32 MiB growth boundary', passed=True))
reject('one byte above floor boundary', series([350*MIB]*18 + [382*MIB+1]*9), floor)
reject('one byte above upper boundary', series([350*MIB]*26 + [382*MIB+1]), upper)

for name, mutate, check in (
    ('trial budget overflow', lambda r: r['trials'][0]['resources'].update(PrivateBytes=1024*MIB+1), 'sampledPrivateAtMost1GiB'),
    ('handle accumulation', lambda r: r['rounds'][20]['afterClose'].update(Handles=9999), 'postWarmupHandleEnvelopeGrowthAtMost8'),
    ('idle delayed growth', lambda r: r['idleSamples'][-1]['Resources'].update(PrivateBytes=383*MIB), 'idlePrivateGrowthAtMost32MiB'),
    ('retained document', lambda r: r['rounds'][-1].update(retainedDocuments=1), 'noRetainedDocumentsAfterDiagnosticGc'),
    ('managed survivor budget', lambda r: r['rounds'][-1]['afterDiagnosticCollection'].update(ManagedBytes=64*MIB+1), 'diagnosticManagedAtMost64MiB'),
):
    changed = series([350*MIB]*27)
    mutate(changed)
    reject(name, changed, check)

with tempfile.TemporaryDirectory() as directory:
    for name, mutate in (
        ('not Windows', lambda r: r.update(windowsExecuted=False)),
        ('incomplete', lambda r: r.update(completed=False)),
        ('early forced GC', lambda r: r['rounds'][0].update(afterDiagnosticCollection=r['rounds'][-1]['afterDiagnosticCollection'])),
        ('missing private bytes', lambda r: r['trials'][0]['resources'].update(PrivateBytes=None)),
    ):
        changed = copy.deepcopy(report)
        mutate(changed)
        path = Path(directory)/'report.json'
        path.write_text(json.dumps(changed))
        result = subprocess.run([sys.executable, str(script), str(path)], capture_output=True, text=True)
        assert result.returncode != 0 and not result.stdout, name
        cases.append(dict(case=name, rejected=True))

print(json.dumps(dict(sourceReportSha256=hashlib.sha256(raw).hexdigest(),
    evaluatorSha256=hashlib.sha256(script.read_bytes()).hexdigest(),
    controlsSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    scope=__doc__, resourceAccepted=False, cases=cases), indent=2))
