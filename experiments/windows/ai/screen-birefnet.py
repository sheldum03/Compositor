"""Screen one pinned BiRefNet Lite candidate through the native 1.30.0 probe."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import platform
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('model', type=Path)
parser.add_argument('photograph', type=Path)
parser.add_argument('probe', type=Path)
parser.add_argument('output', type=Path)
args = parser.parse_args()
model, photograph, probe, output = (p.resolve() for p in (args.model, args.photograph, args.probe, args.output))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
expected_model = '5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333'
expected_photo = '88431cd9653ccd539741b555fb0a46b61558b301d4110412b5bc28b5e3ea6cb5'
assert sha(model) == expected_model and sha(photograph) == expected_photo
output.mkdir(exist_ok=False)
source = Image.open(photograph).convert('RGB')
assert source.size == (512, 512)
# The author's pinned ONNX notebook uses bilinear PIL resize, ImageNet normalization,
# and sigmoid on the single logits output. Do not reuse U2NetP's max normalization.
rgb = np.asarray(source.resize((1024, 1024), Image.Resampling.BILINEAR), dtype=np.float32) / np.float32(255)
normalized = (rgb - np.array([.485, .456, .406], dtype=np.float32)) / np.array([.229, .224, .225], dtype=np.float32)
tensor = normalized.transpose(2, 0, 1)[None].astype('<f4')
tensor.tofile(output / 'input.f32')
with (output / 'native.log').open('w') as log:
    subprocess.run([str(probe), str(model), str(output / 'input.f32'), str(output / 'native'), '--active-cancel'],
                   stdout=log, stderr=subprocess.STDOUT, check=True)
native = json.loads((output / 'native/inference.json').read_text())
assert native['onnxruntime'] == '1.30.0' and native['imageSide'] == 1024
assert native['outputCount'] == 1 and native['outputSemantics'] == 'logits'
review = subprocess.run([sys.executable, str(Path(__file__).with_name('review-active-cancel.py')),
                         str(output / 'native'), '--birefnet'], capture_output=True, text=True, check=True)
(output / 'cancellation-review.json').write_text(review.stdout)
profiles = list((output / 'native').glob('cpu-profile*.json'))
assert len(profiles) == 1
profile = json.loads(profiles[0].read_text())
providers = Counter(e.get('args', {}).get('provider') for e in profile
                    if e.get('cat') == 'Node' and e.get('args', {}).get('provider'))
assert set(providers) == {'CPUExecutionProvider'} and sum(providers.values()) > 0
prediction = np.fromfile(output / 'native/logits.f32', dtype='<f4').reshape(1024, 1024)
assert np.isfinite(prediction).all()
probability = 1 / (1 + np.exp(-np.clip(prediction, -80, 80)))
mask = Image.fromarray((probability * 255).astype(np.uint8)).resize(source.size, Image.Resampling.BICUBIC)
mask.save(output / 'mask.png')
rgba = source.convert('RGBA'); rgba.putalpha(mask); rgba.save(output / 'cutout.png')
identity = '00000000-0000-4000-9000-000000000021'
package = output / 'subject.comp'; (package / 'images').mkdir(parents=True)
shutil.copyfile(photograph, package / 'images' / (identity + '.png'))
shutil.copyfile(output / 'mask.png', package / 'images' / (identity + '.mask.png'))
manifest = dict(format='com.compositor.project', version=8, colorSpace='sRGB', resolution=72,
                documentID='00000000-0000-4000-9000-000000000121', width=512, height=512,
                activeLayerID=identity, layers=[dict(id=identity, name='NASA sample / BiRefNet Lite mask',
                    isVisible=True, maskEnabled=True, imageFile=identity + '.png', maskFile=identity + '.mask.png',
                    transform=dict(origin=[0, 0], size=[512, 512], rotation=0, flipX=False, flipY=False, sampling='Nearest'))])
(package / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')

bad = output / 'short.f32'; bad.write_bytes(b'short')
nan = tensor.copy(); nan.flat[0] = np.nan; nan.tofile(output / 'nan.f32')
invalid_model = output / 'invalid.onnx'; invalid_model.write_bytes(b'not an ONNX graph')
failures = []
for name, model_path, input_path in (
    ('short-input', model, bad), ('nan-input', model, output / 'nan.f32'),
    ('invalid-model', invalid_model, output / 'input.f32'),
):
    destination = output / name
    result = subprocess.run([str(probe), str(model_path), str(input_path), str(destination)], capture_output=True, text=True)
    assert result.returncode != 0 and not (destination / 'logits.f32').exists(), name
    failures.append(dict(case=name, exitCode=result.returncode, noPredictionPublished=True, error=result.stderr))
before = sha(output / 'native/logits.f32')
result = subprocess.run([str(probe), str(model), str(output / 'input.f32'), str(output / 'native')], capture_output=True)
assert result.returncode != 0 and sha(output / 'native/logits.f32') == before
assert sha(model) == expected_model and sha(photograph) == expected_photo
report = dict(windowsExecuted=platform.system() == 'Windows', host=platform.platform(),
    modelSha256=expected_model, inputImageSha256=expected_photo, inputTensorSha256=sha(output / 'input.f32'),
    probeSha256=sha(probe), native=native, profileKernelProviders=dict(providers),
    cancellation=json.loads(review.stdout), failureChecks=failures, existingOutputPreserved=True,
    maskSha256=sha(output / 'mask.png'), logitsSha256=before,
    scope='Fixed-image native feasibility; no quality, production transaction or release acceptance')
(output / 'screening.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report, indent=2))
