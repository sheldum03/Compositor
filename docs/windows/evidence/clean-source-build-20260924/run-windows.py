"""Build the pinned Avalonia source with a private SDK and empty offline cache."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import zipfile

if sys.platform != 'win32':
    raise SystemExit('Actual Windows required')
root = Path(__file__).resolve().parent
archives = {
    'AvaloniaSource.zip': '15eada3dac9e020ded7decff6674239c9af77049459ea4679ef44443caf1cd12',
    'AvaloniaOfflineFeed.zip': '2f547b6c0c30f5b9a1ccfc8d196b39cc74f17b1bef112f9e060c539a04decf4b',
    'dotnet-sdk-10.0.401-win-x64.zip': 'c1b96dea223e1ab2af8a51187e0cc96338d14c68834be1656136be18953cea09',
}
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
for name, digest in archives.items():
    if sha(root/name) != digest:
        raise SystemExit('Archive identity mismatch: ' + name)
native = root/'native-run-20260921-111947/compositor_native.dll'
if sha(native) != '046ad66d8da01625985f29f59fd5b39035adc0483dc43eb141d678b4e931320f':
    raise SystemExit('Native DLL identity mismatch')
out = root/('clean-source-build-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
out.mkdir(exist_ok=False)
identity = dict(sourceCommit='58f2ca79fc501356a5fb668be1a7b29b9e6a99f4', archives=archives,
    runnerSha256=sha(Path(__file__)), platform=platform.platform(), windowsExecuted=True,
    startedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(), emptyPackageCache=True,
    steps=[], completed=False, error=None, nativeDllSha256=sha(native),
    scope='Managed source build and headless probes; not native IME, S02, installation or M1 acceptance')
save = lambda: (out/'identity.json').write_text(json.dumps(identity, indent=2)+'\n')
save()
try:
    for name in archives:
        destination = out/'sdk' if name.startswith('dotnet-sdk') else out
        with zipfile.ZipFile(root/name) as z:
            z.extractall(destination)
    project = out/'source/experiments/windows/avalonia'
    lock = project/'packages.lock.json'
    identity['lockBeforeSha256'] = sha(lock)
    dotnet = out/'sdk/dotnet.exe'
    env = dict(os.environ, NUGET_PACKAGES=str(out/'packages'), DOTNET_CLI_HOME=str(out/'cli'),
               DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1')
    dll = project/'bin/Release/net10.0/Compositor.AvaloniaProbe.dll'
    fixtures = root/'remote-suite/fixtures'
    commands = [
        ('sdk-info', [str(dotnet), '--info']),
        ('restore', [str(dotnet), 'restore', '--locked-mode', '--source', str(out/'feed'), '-p:NuGetAudit=false', '-p:UseAppHost=false']),
        ('build', [str(dotnet), 'build', '-c', 'Release', '--no-restore', '-p:UseAppHost=false']),
        ('composite', [str(dotnet), str(dll), str(fixtures), str(out/'composite'), str(native)]),
        ('brush', [str(dotnet), str(dll), '--brush', str(fixtures/'brush'), str(out/'brush'), str(native)]),
        ('text', [str(dotnet), str(dll), '--text', str(fixtures/'extended'), str(out/'text'), str(native)]),
    ]
    for name, args in commands:
        row = dict(name=name, command=args, pid=None, exitCode=None)
        identity['steps'].append(row)
        with (out/(name+'.stdout.log')).open('wb') as stdout, (out/(name+'.stderr.log')).open('wb') as stderr:
            p = subprocess.Popen(args, cwd=project, env=env, stdout=stdout, stderr=stderr)
            row['pid'] = p.pid
            save()
            print('RUNNING '+name+' PID '+str(p.pid)+' OUTPUT '+str(out), flush=True)
            row['exitCode'] = p.wait()
        save()
        if row['exitCode'] != 0:
            raise RuntimeError(name + ' failed: ' + str(row['exitCode']))
    identity['lockAfterSha256'] = sha(lock)
    if identity['lockAfterSha256'] != identity['lockBeforeSha256']:
        raise RuntimeError('Locked dependency file changed')
    identity['buildAssets'] = {str(p.relative_to(dll.parent)): sha(p) for p in sorted(dll.parent.rglob('*')) if p.is_file()}
    identity['completed'] = True
except Exception as error:
    identity['error'] = str(error)
finally:
    save()
    excluded = {'sdk', 'source', 'packages', 'feed', 'cli'}
    files = [p for p in sorted(out.rglob('*')) if p.is_file() and p.relative_to(out).parts[0] not in excluded]
    (out/'files.json').write_text(json.dumps({p.relative_to(out).as_posix(): sha(p) for p in files}, indent=2)+'\n')
    archive = out.with_suffix('.zip')
    with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED) as z:
        for p in [*files, out/'files.json']:
            z.write(p, p.relative_to(out))
    print(json.dumps(dict(archive=str(archive), sha256=sha(archive), completed=identity['completed'], error=identity['error'])), flush=True)
sys.exit(0 if identity['completed'] else 1)
