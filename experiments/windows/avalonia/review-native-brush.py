"""Review the archived three-path Windows OS-input sequence (saves 007–012)."""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument('root', type=Path, help='Extracted native-brush-input directory')
args = parser.parse_args()
root = args.root / 'max-window'
report = json.loads((root / 'window-report.json').read_text(encoding='utf-8-sig'))
assert report['windowsExecuted'] and report['nativeWindow']
events = report['events']
names = [e['name'] for e in events]
assert 'action-error' not in names and names[-1] == 'native-window-closed'
assert names.count('brush-pointer-update') == 68 and names.count('brush-commit') == 2
saves = [e['data'] for e in events if e['name'] == 'brush-save-reopen'][-6:]
assert all(s['passed'] for s in saves)
assert [s['undoCount'] for s in saves] == [0, 1, 1, 2, 1, 2]
pixels = []
for index in range(7, 13):
    directory = root / f'{index:03d}-brush'
    with Image.open(directory / 'final.png') as image:
        assert image.size == (4000, 4000)
        final = np.array(image.convert('RGBA'))
    with Image.open(directory / 'reopened.png') as image:
        assert np.array_equal(final, np.array(image.convert('RGBA')))
    pixels.append(final)
assert not pixels[0][:, :, 3].any(), 'Initial canvas must be transparent'
assert not np.array_equal(pixels[0], pixels[1]), 'First stroke made no change'
assert np.array_equal(pixels[1], pixels[2]), 'Cancel changed the first stroke'
assert not np.array_equal(pixels[2], pixels[3]), 'Next stroke made no change'
assert np.array_equal(pixels[1], pixels[4]), 'Undo did not restore the first stroke'
assert np.array_equal(pixels[3], pixels[5]), 'Redo did not restore both strokes'
print(json.dumps(dict(status='passed', windowsExecuted=True, pointerUpdates=68,
    committedStrokes=2, pngPairsCompared=6, differentPixels=0,
    cancelRestoredPixels=True, undoRestoredPixels=True, redoRestoredPixels=True,
    nextStrokeChangedPixels=True, windowClosedRecorded=True,
    inputBoundary='Windows SendInput, not physical hardware',
    physicalInputAccepted=False, qtImeAccepted=False), indent=2))
