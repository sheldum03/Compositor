"""Run the read-only native IME control comparison on the existing Windows test host."""
import datetime
import hashlib
import json
import os
import platform
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

if sys.platform != 'win32':
    raise SystemExit('Actual Windows required')
kit = Path(__file__).resolve().parent
root = kit.parent
base = root / 'qt-remote-suite/window-app'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
manifest = json.loads((kit / 'manifest.json').read_text())
for directory, entries in ((kit, manifest['payload']), (base, manifest['dependencies'])):
    for name, digest in entries.items():
        if sha(directory / name) != digest:
            raise RuntimeError('File identity mismatch: ' + name)
out = root / ('qt-ime-control-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
out.mkdir(exist_ok=False)
app = out / 'app'
shutil.copytree(base, app)
shutil.copyfile(kit / 'qt_ime_control.exe', app / 'qt_ime_control.exe')
shutil.copyfile(kit / 'libcompositor_native.dll', app / 'libcompositor_native.dll')
env = os.environ.copy()
env.update(QT_QPA_PLATFORM='windows', QT_PLUGIN_PATH=str(app), QT_LOGGING_RULES='qt.qpa.input.methods.debug=true')
for key in ('QT_SCALE_FACTOR', 'QT_WIDGETS_RHI', 'QT_WIDGETS_RHI_BACKEND'):
    env.pop(key, None)
identity = {'platform': platform.platform(), 'manifest': manifest, 'processes': [], 'candidatePositionAccepted': False}
def save():
    (out / 'identity.json').write_text(json.dumps(identity, indent=2) + '\n')
save()
try:
    for control in ('textedit', 'windowtext'):
        with (out / (control + '-stdout.log')).open('wb') as stdout, (out / (control + '-stderr.log')).open('wb') as stderr:
            process = subprocess.Popen([str(app / 'qt_ime_control.exe'), control, str(out / (control + '.jsonl'))], cwd=app, env=env, stdout=stdout, stderr=stderr)
            record = {'control': control, 'pid': process.pid, 'exitCode': None}
            identity['processes'].append(record)
            save()
            print('RUNNING', control, 'PID', process.pid, 'OUTPUT', out, flush=True)
            record['exitCode'] = process.wait()
            save()
            if record['exitCode']:
                raise RuntimeError(control + ' exited nonzero')
finally:
    files = {p.relative_to(out).as_posix(): sha(p) for p in sorted(out.rglob('*')) if p.is_file() and app not in p.parents}
    (out / 'files.json').write_text(json.dumps(files, indent=2) + '\n')
    archive = out.with_suffix('.zip')
    with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED) as zipped:
        for name in [*files, 'files.json']:
            zipped.write(out / name, name)
    print(json.dumps({'archive': str(archive), 'bytes': archive.stat().st_size, 'sha256': sha(archive)}), flush=True)
