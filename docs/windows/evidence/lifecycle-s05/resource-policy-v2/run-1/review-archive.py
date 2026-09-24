"""Independent local review of a transferred S05 v2 prospective result."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import subprocess
import sys
import zipfile
from PIL import Image

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('archive', type=Path)
parser.add_argument('expected_sha256')
parser.add_argument('output', type=Path)
args = parser.parse_args()
sha = lambda data: hashlib.sha256(data).hexdigest()
assert sha(args.archive.read_bytes()) == args.expected_sha256
args.output.mkdir(exist_ok=False)
raw = args.output / 'raw'
with zipfile.ZipFile(args.archive) as archive:
    assert archive.testzip() is None
    manifest = json.loads(archive.read('files.json'))
    normalized = {key.replace('\\', '/'): value for key, value in manifest.items()}
    assert len(normalized) == len(manifest)
    assert set(archive.namelist()) == set(normalized) | {'files.json'}
    for key, digest in normalized.items():
        assert sha(archive.read(key)) == digest, key
    for key in archive.namelist():
        path = PurePosixPath(key)
        assert not path.is_absolute() and '..' not in path.parts and '\\' not in key
        destination = raw / key
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(archive.read(key))
identity = json.loads((raw / 'identity.json').read_text())
assert identity['nativeExit'] == identity['reviewExit'] == 0
assert identity['platform'].startswith('Windows') and identity['gcEnvironment'] == {}
assert identity['dllSha256'] == '557e793c2442122450ad994ea1e88aa3535295405b01e290caa40b47ad94dce6'
assert identity['diagnosticPatchSha256'] == 'e05d2901ef052ea873f16ee49e13119b3e5dc4d86c35858089d63f4e7d38ea63'
assert identity['baseSourceCommit'] == 'f1ac69a7657bc42245feeda6d494ba4f1cb19406'
assert (raw / 'stderr.log').stat().st_size == (raw / 'review-error.log').stat().st_size == 0
scripts = Path('/Users/admin/.codex/worktrees/192b/Compositor/experiments/windows/avalonia')
exits = {}
for name, script, extra in [('correctness', 'review-s05.py', ['--extended']),
                            ('resources-v2', 'review-s05-resources-v2.py', [])]:
    result = subprocess.run([sys.executable, str(scripts / script), str(raw / 'run/report.json'), *extra], capture_output=True, text=True)
    (args.output / (name + '.json')).write_text(result.stdout)
    (args.output / (name + '.stderr.log')).write_text(result.stderr)
    exits[name] = result.returncode
assert exits['correctness'] == 0
assert json.loads((args.output / 'correctness.json').read_text()) == json.loads((raw / 'review.json').read_text())
pixels = []
expected = 'a8a62c6c48e71e2ce419ee56802d7160c0beaeded89c386d903ac60deba58a8e'
for round_number in range(27):
    for name in ['final.png', 'reopened.png']:
        path = raw / 'run' / ('round-' + str(round_number)) / name
        with Image.open(path) as image:
            assert image.size == (4000, 4000)
            digest = sha(image.convert('RGBA').tobytes())
        assert digest == expected, str(path)
        pixels.append(dict(path=str(path.relative_to(raw)),rgbaSha256=digest))
review = dict(archive=str(args.archive.resolve()), bytes=args.archive.stat().st_size,
    sha256=args.expected_sha256, verifiedFiles=len(manifest), windowsIdentity=identity,
    correctnessPassed=True, imageCount=len(pixels), allImagesExactAgainstFrozenWindowsR9=True,
    resourceEvaluatorExit=exits['resources-v2'], resourceAccepted=False,
    scope='Archive correctness/resource evaluation; prospectivity and acceptance require separate evidence', images=pixels)
(args.output / 'archive-review.json').write_text(json.dumps(review,indent=2)+'\n')
print(json.dumps({key:value for key,value in review.items() if key != 'images'},indent=2))
sys.exit(exits['resources-v2'])
