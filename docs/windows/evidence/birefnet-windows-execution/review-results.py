"""Independently check the fixed BiRefNet Windows result archive."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path, PurePosixPath
import subprocess
import sys
import zipfile
import numpy as np
from PIL import Image

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('archive', type=Path)
parser.add_argument('expected_sha256')
parser.add_argument('kit', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
assert sha(args.archive) == args.expected_sha256
args.output.mkdir(exist_ok=False)
raw = args.output / 'raw'
with zipfile.ZipFile(args.archive) as z:
    assert z.testzip() is None
    names = z.namelist()
    assert len(names) == len(set(names)) and not any(n.endswith('.onnx') for n in names)
    for name in names:
        p = PurePosixPath(name)
        assert not p.is_absolute() and '..' not in p.parts and '\\' not in name
        dest = raw / name
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(z.read(name))
e = read(raw / 'execution.json')
manifest = read(args.kit / 'manifest.json')
assert e['manifest'] == manifest and e['windowsExecuted'] and e['host'].startswith('Windows')
assert e['exitCode'] == 0 and e['error'] is None and (raw / 'stderr.log').stat().st_size == 0
s = raw / 'screening'
r = read(s / 'screening.json')
assert r['windowsExecuted'] and r['host'].startswith('Windows')
assert r['modelSha256'] == manifest['modelSha256'] == '5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333'
assert r['probeSha256'] == manifest['files']['app/birefnet_probe.exe']['sha256']
m = read(s / 'subject.comp/manifest.json')
assert m['version'] == 8 and m['width'] == m['height'] == 512 and len(m['layers']) == 1
layer = m['layers'][0]
assert layer['maskEnabled'] and m['colorSpace'] == 'sRGB'
src = s / 'subject.comp/images' / layer['imageFile']
assert sha(src) == r['inputImageSha256'] == manifest['files']['astronaut.png']['sha256']
with Image.open(src) as photo:
    original = photo.convert('RGB')
    rgb = np.asarray(original.resize((1024, 1024), Image.Resampling.BILINEAR), dtype=np.float32) / np.float32(255)
    normalized = (rgb - np.array([.485, .456, .406], dtype=np.float32)) / np.array([.229, .224, .225], dtype=np.float32)
    expected_input = normalized.transpose(2, 0, 1)[None].astype('<f4').tobytes()
assert (s / 'input.f32').read_bytes() == expected_input and sha(s / 'input.f32') == r['inputTensorSha256']
native = read(s / 'native/inference.json')
assert native == r['native'] and native['onnxruntime'] == '1.30.0'
assert native['imageSide'] == 1024 and native['outputCount'] == 1 and native['outputSemantics'] == 'logits'
assert native['repeatedPredictionsExact'] and native['preTerminatedRunRejected'] and native['sessionRecovered']
prediction = np.fromfile(s / 'native/logits.f32', dtype='<f4').reshape(1024, 1024)
assert np.isfinite(prediction).all() and sha(s / 'native/logits.f32') == r['logitsSha256']
probability = 1 / (1 + np.exp(-np.clip(prediction, -80, 80)))
expected_mask = Image.fromarray((probability * 255).astype(np.uint8)).resize((512, 512), Image.Resampling.BICUBIC)
with Image.open(s / 'mask.png') as mask, Image.open(s / 'cutout.png') as cut:
    assert mask.mode == 'L' and mask.size == (512, 512) and mask.tobytes() == expected_mask.tobytes()
    gray = np.asarray(mask).copy()
    rgba = np.asarray(cut.convert('RGBA'))
    assert np.array_equal(rgba[:, :, :3], np.asarray(original)) and np.array_equal(rgba[:, :, 3], gray)
assert sha(s / 'mask.png') == r['maskSha256'] == sha(s / 'subject.comp/images' / layer['maskFile'])
profiles = list((s / 'native').glob('cpu-profile*.json'))
assert len(profiles) == 1
providers = Counter(x.get('args', {}).get('provider') for x in read(profiles[0]) if x.get('cat') == 'Node' and x.get('args', {}).get('provider'))
assert providers == r['profileKernelProviders'] and set(providers) == {'CPUExecutionProvider'}
review = subprocess.run([sys.executable, str(args.kit / 'source/review-active-cancel.py'), str(s / 'native'), '--birefnet'], capture_output=True, text=True)
(args.output / 'cancellation-review.json').write_text(review.stdout)
(args.output / 'cancellation-review.stderr.log').write_text(review.stderr)
assert review.returncode == 0
cancellation = json.loads(review.stdout)
for key in ['cancelledRunKernelEvents', 'recoveredRunKernelEvents', 'cancelToJoinMilliseconds', 'recoveredPredictionSha256']:
    assert cancellation[key] == r['cancellation'][key]
assert {x['case'] for x in r['failureChecks']} == {'short-input', 'nan-input', 'invalid-model'}
assert all(x['exitCode'] != 0 and x['noPredictionPublished'] and not (s / x['case'] / 'logits.f32').exists() for x in r['failureChecks'])
assert r['existingOutputPreserved']
result = dict(windowsExecutionRecorded=True, archiveSha256=sha(args.archive), archiveBytes=args.archive.stat().st_size,
    inputTensorRecomputedExact=True, maskRecomputedExact=True, cutoutRgbPreservedAlphaEqualsMask=True,
    separateImageAndMaskAssetsExact=True, cancellation=cancellation, providers=dict(providers),
    native=native, rawPredictionSha256=sha(s / 'native/logits.f32'),
    maskPixelsSha256=hashlib.sha256(gray.tobytes()).hexdigest(),
    scope='Independent fixed-image archive review; actual Mac reader, quality, product UI and release are separate.',
    rawFileHashes={p.relative_to(raw).as_posix():sha(p) for p in sorted(raw.rglob('*')) if p.is_file()})
(args.output / 'independent-review.json').write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps({k:v for k,v in result.items() if k != 'rawFileHashes'}, indent=2))
