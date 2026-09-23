"""Private fixed-input Windows experiment; no installation or network access."""
from pathlib import Path, PurePosixPath
import argparse
import datetime
import hashlib
import json
import platform
import shutil
import subprocess
import sys
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--prepare-only', type=Path, help='Validate/extract into a new directory; do not execute Windows binaries')
args = parser.parse_args()
kit = Path(__file__).resolve().parent
root = kit.parent
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
manifest = json.loads((kit / 'manifest.json').read_text())
for name, expected in manifest['files'].items():
    p = kit / name
    if p.stat().st_size != expected['bytes'] or sha(p) != expected['sha256']:
        raise RuntimeError('Package identity mismatch: ' + name)
if args.prepare_only is None and platform.system() != 'Windows':
    raise SystemExit('Windows required; --prepare-only is extraction validation only')
out = args.prepare_only or root / ('ai-image-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
out.mkdir()
runtime = out / 'python'
runtime.mkdir()

def extract(source, destination):
    with zipfile.ZipFile(source) as z:
        if z.testzip() is not None:
            raise RuntimeError('Bad archive CRC: ' + str(source))
        for entry in z.infolist():
            path = PurePosixPath(entry.filename)
            if path.is_absolute() or '..' in path.parts or '\\' in entry.filename:
                raise RuntimeError('Unsafe archive entry')
            target = destination / str(path)
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with target.open('xb') as stream:
                    stream.write(z.read(entry))

extract(kit / 'downloads/python-3.11.9-embed-amd64.zip', runtime)
for wheel in sorted((kit / 'downloads').glob('*.whl')):
    extract(wheel, runtime / 'site-packages')
# Explicit package directory; user/system site and environment paths stay disabled.
(runtime / 'python311._pth').write_text('python311.zip\n.\nsite-packages\n', encoding='ascii')
if args.prepare_only:
    print(json.dumps({'prepared': str(out), 'windowsExecuted': False}))
    sys.exit(0)

execution = {'windowsExecuted': True, 'host': platform.platform(), 'manifest': manifest,
             'scope': 'One fixed public image, existing CPU probe; no GUI/quality/distribution acceptance',
             'exitCode': None, 'error': None}
try:
    model = root / 'remote-suite/private-model/u2netp.onnx'
    if sha(model) != manifest['modelSha256']:
        raise RuntimeError('Existing local model identity mismatch')
    assets = out / 'assets'
    assets.mkdir()
    shutil.copyfile(model, assets / 'u2netp.onnx')
    shutil.copyfile(kit / 'astronaut.png', assets / 'astronaut.png')
    python = runtime / 'python.exe'
    command = [str(python), '-B', str(kit / 'screen.py'), str(assets),
               str(kit / 'native/app/ai_probe.exe'), str(out / 'screening')]
    execution['command'] = command
    with (out / 'stdout.log').open('w', encoding='utf-8') as log, (out / 'stderr.log').open('w', encoding='utf-8') as errors:
        process = subprocess.Popen(command, stdout=log, stderr=errors)
        execution['pid'] = process.pid
        (out / 'launch.json').write_text(json.dumps(execution, indent=2) + '\n')
        print('RUNNING', process.pid, 'OUTPUT', out, flush=True)
        try:
            execution['exitCode'] = process.wait(timeout=600)
        except subprocess.TimeoutExpired:
            subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], check=False, capture_output=True)
            execution['exitCode'] = process.wait()
            raise RuntimeError('Harness exceeded 600 seconds; own child stopped')
    if execution['exitCode'] != 0:
        raise RuntimeError('Full image harness failed; inspect retained logs')
    report = json.loads((out / 'screening/screening.json').read_text())
    if not report['windowsExecuted'] or report['inference']['onnxruntime'] != '1.30.0':
        raise RuntimeError('Expected Windows harness and pinned native runtime')
    if sha(model) != manifest['modelSha256']:
        raise RuntimeError('Original model changed')
except Exception as error:
    execution['error'] = str(error)
finally:
    (out / 'execution.json').write_text(json.dumps(execution, indent=2) + '\n')
    archive = out.with_suffix('.zip')
    with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED) as z:
        for p in sorted(out.rglob('*')):
            rel = p.relative_to(out)
            if p.is_file() and rel.parts[0] not in ('python', 'assets') and p.suffix in ('.json', '.png', '.f32', '.log', '.txt'):
                z.write(p, rel.as_posix())
    print(json.dumps({'archive': str(archive), 'bytes': archive.stat().st_size, 'sha256': sha(archive), 'error': execution['error']}))
sys.exit(1 if execution['error'] else 0)
