"""Measure fixed brush differences; no acceptance threshold. Requires Pillow and NumPy."""
import hashlib
import io
import json
import sys
import zipfile
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

archive, output = map(Path, sys.argv[1:3])
assert hashlib.sha256(archive.read_bytes()).hexdigest() == 'b059e2b04fd7cfbd0f516468ca78d850608db83d6c65207a7252ed4b59f2e4c4'
assert archive.stat().st_size == 7291136
output.mkdir(exist_ok=False)
fixtures = Path(__file__).resolve().parents[2] / 'fixtures/brush'
sha = lambda data: hashlib.sha256(data).hexdigest()
for entry in json.loads((fixtures / 'checksums.json').read_text()):
    data = (fixtures / entry['path']).read_bytes()
    assert len(data) == entry['bytes'] and sha(data) == entry['sha256'], entry['path']
records, identities, final = [], {}, {}
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    for stage in ('first', 'final'):
        data = {'windows': z.read('brush-run-20260921-114232\\' + stage + '.png')}
        data.update({name: (fixtures / f'{stage}-{name}.png').read_bytes() for name in ('cpu', 'metal')})
        images = {name: np.asarray(Image.open(io.BytesIO(b)).convert('RGBA'), dtype=np.float64) for name, b in data.items()}
        assert all(a.shape == (4000, 4000, 4) for a in images.values())
        identities[stage] = {name: sha(b) for name, b in data.items()}
        for a_name, b_name in (('windows', 'cpu'), ('windows', 'metal'), ('cpu', 'metal')):
            a, b = images[a_name], images[b_name]
            support = (a[:, :, 3] > 0) | (b[:, :, 3] > 0)
            alpha_error = np.abs(a[:, :, 3] - b[:, :, 3])
            a_pm, b_pm = a[:, :, :3] * a[:, :, 3:4] / 255, b[:, :, :3] * b[:, :, 3:4] / 255
            pm_error = np.abs(a_pm - b_pm).max(axis=2)
            white_error = np.abs((a_pm + 255-a[:, :, 3:4]) - (b_pm + 255-b[:, :, 3:4])).max(axis=2)
            def stats(error):
                return {'max': float(error.max()), 'meanWithinUnionSupport': float(error[support].mean()),
                        'p95WithinUnionSupport': float(np.percentile(error[support], 95)),
                        'pixelsAbove1': int((error > 1+1e-9).sum()), 'pixelsAbove4': int((error > 4+1e-9).sum())}
            records.append({'stage': stage, 'a': a_name, 'b': b_name, 'supportPixels': int(support.sum()),
                'supportDisagreementPixels': int(((a[:, :, 3] > 0) != (b[:, :, 3] > 0)).sum()),
                'alpha': stats(alpha_error), 'premultipliedRGB': stats(pm_error), 'whiteCompositeRGB': stats(white_error)})
        if stage == 'final': final = images

# Display the same crop around the greatest Windows/CPU alpha difference for all three outputs.
y, x = np.unravel_index(np.abs(final['windows'][:, :, 3]-final['cpu'][:, :, 3]).argmax(), (4000, 4000))
x, y = min(3488, max(0, int(x)-256)), min(3488, max(0, int(y)-256))
canvas = Image.new('RGB', (1230, 1345), 'white'); draw = ImageDraw.Draw(canvas)
for col, name in enumerate(('windows', 'cpu', 'metal')):
    a = final[name]; white = np.rint(a[:, :, :3]*a[:, :, 3:4]/255 + 255-a[:, :, 3:4]).astype('uint8')
    image = Image.fromarray(white)
    draw.text((col*410+10, 8), name + ' / final on white', fill='black', font_size=20)
    canvas.paste(image.resize((400,400)), (col*410+5,40))
    draw.text((col*410+10, 455), f'crop ({x},{y}) 512x512', fill='black', font_size=18)
    canvas.paste(image.crop((x,y,x+512,y+512)).resize((400,400)), (col*410+5,485))
for col, (a_name,b_name) in enumerate((('windows','cpu'),('windows','metal'),('cpu','metal'))):
    error=np.abs(final[a_name][:,:,3]-final[b_name][:,:,3]); heat=np.zeros((4000,4000,3),dtype='uint8');heat[:,:,0]=np.minimum(255,error*32).astype('uint8')
    draw.text((col*410+5,905), f'alpha abs x32: {a_name}/{b_name}', fill='black',font_size=17)
    canvas.paste(Image.fromarray(heat).resize((400,400)), (col*410+5,935))
canvas.save(output/'comparison.png')
report = {'status':'observations only; no D-03 acceptance threshold or product acceptance',
          'inputSha256':identities, 'records':records, 'crop':[x,y,512,512],
          'reviewerSha256':sha(Path(__file__).read_bytes()),
          'limits':['Two fixed 800px soft, 40 percent strokes only; not S02 or a complete brush matrix.',
                    'Straight PNG values re-premultiplied mathematically; original renderer buffers not sampled.',
                    'White composite uses encoded channel arithmetic; not a color-management or perceptual metric.',
                    'Current test run is offline analysis of previously executed Windows output.']}
(output/'review.json').write_text(json.dumps(report,indent=2)+'\n')
for r in records:
    print(r['stage'], r['a'], r['b'], 'alpha max', r['alpha']['max'], 'white max', r['whiteCompositeRGB']['max'], 'support',r['supportPixels'])
