"""Run the fixed r4 source snapshot on the existing Windows test machine.
Copy this file beside CompositorProductionCore-r4.zip; run with Python 3.
"""
from datetime import datetime, timezone
from pathlib import Path
import hashlib
import json
import os
import platform
import subprocess
import sys
import zipfile

BASE = Path(__file__).resolve().parent
SOURCE = BASE / 'production-core-r4'
ARCHIVE = BASE / 'CompositorProductionCore-r4.zip'
EXPECTED_SHA256 = '0f8ba9c207d0210b6e087807fc92aa75055a9a4b1015a8041d0e631bf7ea9c0f'
TOOLS = BASE / 'tools/llvm-mingw-20260908-ucrt-x86_64/bin'
PREVIOUS = BASE / 'clean-source-build-20260924-103752'
DOTNET = PREVIOUS / 'sdk/dotnet.exe'
OUT = BASE / ('production-core-r4-results-' + datetime.now().strftime('%Y%m%d-%H%M%S-%f'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    if sys.platform != 'win32':
        raise RuntimeError('This runner requires actual Windows.')
    OUT.mkdir()
    env = dict(os.environ, NUGET_PACKAGES=str(OUT / 'packages'),
               DOTNET_CLI_HOME=str(OUT / 'cli'), DOTNET_CLI_TELEMETRY_OPTOUT='1',
               DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_NOLOGO='1')
    env['PATH'] = str(TOOLS) + os.pathsep + env.get('PATH', '')
    report = {'passed': False, 'sourceCommit': '1e41027',
              'utc': datetime.now(timezone.utc).isoformat(),
              'platform': platform.platform(), 'machine': platform.machine(),
              'runnerSha256': digest(Path(__file__)), 'steps': []}

    def run(name, argv, marker=None, timeout=600):
        argv = [str(arg) for arg in argv]
        print('RUN ' + name, flush=True)
        step = {'name': name, 'argv': argv, 'cwd': str(SOURCE / 'windows')}
        report['steps'].append(step)
        with (OUT / (name + '.log')).open('wb') as log:
            process = subprocess.run(argv, cwd=SOURCE / 'windows', env=env,
                                     stdout=log, stderr=subprocess.STDOUT, timeout=timeout)
        step['exitCode'] = process.returncode
        if process.returncode != 0:
            raise RuntimeError(f'{name} exited {process.returncode}; see its raw log.')
        text = (OUT / (name + '.log')).read_text(encoding='utf-8', errors='replace')
        if marker is not None and not any(line.startswith(marker) for line in text.splitlines()):
            raise RuntimeError(f'{name} did not produce the required PASS marker.')
        print('OK ' + name, flush=True)
        return text

    try:
        if digest(ARCHIVE) != EXPECTED_SHA256:
            raise RuntimeError('Source ZIP SHA-256 mismatch.')
        report['sourceZipSha256'] = EXPECTED_SHA256
        source_hashes = {}
        with zipfile.ZipFile(ARCHIVE) as bundle:
            if bundle.testzip() is not None:
                raise RuntimeError('Source ZIP CRC mismatch.')
            for item in bundle.infolist():
                if item.is_dir():
                    continue
                expected = hashlib.sha256(bundle.read(item)).hexdigest()
                if digest(SOURCE / item.filename) != expected:
                    raise RuntimeError('Extracted source mismatch: ' + item.filename)
                source_hashes[item.filename] = expected
        if len(source_hashes) != 130:
            raise RuntimeError('Unexpected source file count.')
        (OUT / 'source-sha256.json').write_text(json.dumps(source_hashes, indent=2), encoding='utf-8')
        (OUT / 'runner.py').write_bytes(Path(__file__).read_bytes())
        version = run('sdk-version', [DOTNET, '--version']).strip()
        if version != '10.0.401':
            raise RuntimeError('Expected .NET SDK 10.0.401, got ' + version)
        run('sdk-info', [DOTNET, '--info'])
        run('compiler-version', [TOOLS / 'clang.exe', '--version'])
        rendering = SOURCE / 'Compositor/Rendering'
        sources = [rendering / (name + '.c') for name in
                   ('AdjustPixels', 'BrushPixels', 'ContentFill', 'HealPixels',
                    'LensPixels', 'LevelsPixels', 'NoisePixels', 'WandPixels')]
        native = OUT / 'compositor_native.dll'
        run('native-build', [TOOLS / 'clang.exe', '-std=c17', '-O2', '-D_USE_MATH_DEFINES',
                            '-shared', '-Wl,--export-all-symbols', '-I' + str(rendering),
                            SOURCE / 'windows/native/bridge.c', *sources, '-o', native])
        run('native-symbols', [TOOLS / 'llvm-readobj.exe', '--coff-exports', '--coff-imports', native])
        for project in ('Compositor.Smoke', 'Compositor.Imaging.Checks', 'Compositor.Workflow.Checks'):
            csproj = SOURCE / 'windows' / project / (project + '.csproj')
            run(project + '-restore', [DOTNET, 'restore', csproj, '--locked-mode',
                                      '--source', PREVIOUS / 'feed', '-p:NuGetAudit=false'])
            run(project + '-build', [DOTNET, 'build', csproj, '-c', 'Release', '--no-restore'])
        def dll(project):
            return SOURCE / 'windows' / project / 'bin/Release/net10.0' / (project + '.dll')
        image_fixtures = SOURCE / 'windows/Compositor.Imaging.Checks/fixtures'
        run('core', [DOTNET, dll('Compositor.Smoke'), SOURCE / 'docs/windows/fixtures',
                     OUT / 'core', native], 'PASS: edit, undo, redo')
        core_log = (OUT / 'core.log').read_text(encoding='utf-8', errors='replace')
        if ', native C pixels' not in core_log:
            raise RuntimeError('Core did not confirm the native C path.')
        run('imaging', [DOTNET, dll('Compositor.Imaging.Checks'), image_fixtures,
                        OUT / 'imaging'], 'PASS: PNG pixels/alpha/tiles')
        image_result = json.loads((OUT / 'imaging/results.json').read_text(encoding='utf-8'))
        if image_result.get('passed') is not True or len(image_result['cases']) != 14 or image_result['rejectionChecks'] != 23:
            raise RuntimeError('Imaging result count or success mismatch.')
        run('workflow', [DOTNET, dll('Compositor.Workflow.Checks'), image_fixtures,
                         OUT / 'workflow'], 'PASS: v8 PNG/JPEG import')
        for artifact in ('core/Edited.comp/manifest.json', 'core/export.png',
                         'workflow/Image.comp/manifest.json', 'workflow/Edited.comp/manifest.json',
                         'workflow/Oriented.comp/manifest.json', 'workflow/export.png',
                         'workflow/export.jpg', 'workflow/oriented-export.png'):
            if not (OUT / artifact).is_file():
                raise RuntimeError('Missing expected output: ' + artifact)
        for name, expected in source_hashes.items():
            if digest(SOURCE / name) != expected:
                raise RuntimeError('Tracked source or lockfile changed during run: ' + name)
        report['passed'] = True
    except Exception as error:
        report['error'] = str(error)
        print('FAIL: ' + str(error), flush=True)
    finally:
        (OUT / 'result.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        included = [p for p in OUT.rglob('*') if p.is_file() and p.relative_to(OUT).parts[0] not in ('packages', 'cli')]
        (OUT / 'sha256.json').write_text(json.dumps({p.relative_to(OUT).as_posix(): digest(p)
                                                   for p in included}, indent=2), encoding='utf-8')
        with zipfile.ZipFile(str(OUT) + '.zip', 'x', zipfile.ZIP_DEFLATED) as archive:
            for path in included + [OUT / 'sha256.json']:
                archive.write(path, path.relative_to(OUT).as_posix())
        print('RESULT ' + str(OUT / 'result.json'), flush=True)
        print('ARCHIVE ' + str(OUT) + '.zip', flush=True)
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
