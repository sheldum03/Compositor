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
if sys.argv[1:] not in ([], ['--font-comparison']):
    raise SystemExit('Usage: run.py [--font-comparison]')
font_comparison = bool(sys.argv[1:])
kit = Path(__file__).resolve().parent
root = kit.parent
base = root / 'qt-remote-suite/window-app'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
manifest = json.loads((kit / 'manifest.json').read_text())
for directory, entries in ((kit, manifest['payload']), (base, manifest['dependencies'])):
    for name, digest in entries.items():
        if sha(directory / name) != digest:
            raise RuntimeError('File identity mismatch: ' + name)
out = root / (('qt-ime-font-' if font_comparison else 'qt-ime-control-') + datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
out.mkdir(exist_ok=False)
app = out / 'app'
shutil.copytree(base, app)
shutil.copyfile(kit / 'qt_ime_control.exe', app / 'qt_ime_control.exe')
shutil.copyfile(kit / 'libcompositor_native.dll', app / 'libcompositor_native.dll')
env = os.environ.copy()
env.update(QT_QPA_PLATFORM='windows', QT_PLUGIN_PATH=str(app), QT_LOGGING_RULES='qt.qpa.input.methods.debug=true')
for key in ('QT_SCALE_FACTOR', 'QT_WIDGETS_RHI', 'QT_WIDGETS_RHI_BACKEND'):
    env.pop(key, None)
identity = {'platform': platform.platform(), 'manifest': manifest, 'processes': [], 'candidatePositionAccepted': False,
            'comparison': 'font' if font_comparison else 'control'}
def save():
    (out / 'identity.json').write_text(json.dumps(identity, indent=2) + '\n')
save()
try:
    cases = [('textedit', 'Source Han Sans SC'), ('textedit', 'Microsoft YaHei UI')] if font_comparison else [('textedit', None), ('windowtext', None)]
    for index, (control, font) in enumerate(cases):
        name = str(index) + '-' + control if font_comparison else control
        command = [str(app / 'qt_ime_control.exe'), control, str(out / (name + '.jsonl'))]
        if font:
            command.append(font)
        with (out / (name + '-stdout.log')).open('wb') as stdout, (out / (name + '-stderr.log')).open('wb') as stderr:
            process = subprocess.Popen(command, cwd=app, env=env, stdout=stdout, stderr=stderr)
            record = {'control': control, 'font': font, 'pid': process.pid, 'exitCode': None}
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
