import hashlib
import json
import zipfile
from pathlib import Path
import numpy as np
from PIL import Image

base = Path(__file__).resolve().parent
raw = base / 'windows-final-raw'
native = raw / 'native-20260923-185914'
archive = base / 'avalonia-ime-matrix-20260923-185914.zip'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
assert sha(archive) == '2a4e476c2f7c42f451a99f2293ba4b6a2759cd6d3bb99af5e620c4b507ec3e8b'
with zipfile.ZipFile(archive) as zipped:
    assert zipped.testzip() is None
    manifest = json.loads(zipped.read('files.json'))
    assert len(manifest) == 579 and len(zipped.namelist()) == 580
    for name, digest in manifest.items():
        assert hashlib.sha256(zipped.read(name)).hexdigest() == digest, name
        assert sha(raw / name) == digest, name
identity = json.loads((raw / 'identity.json').read_text())
report = json.loads((native / 'window-report.json').read_text())
observations = json.loads((base / 'native-observations.json').read_text())
assert identity['executionCompleted'] and all(p['exitCode'] == 0 for p in identity['processes'])
assert report['processId'] == identity['processes'][-1]['pid'] == 40532
assert report['windowsExecuted'] and report['nativeWindow'] and report['injectedInputMethodCalls'] == 0
assert report['title'].endswith('native-20260923-185914')
assert not any(e['name'] == 'action-error' for e in report['events'])
assert report['events'][-1]['name'] == 'native-window-closed'
assert all(p.stat().st_size == 0 for p in raw.glob('*.stderr.log'))
original = '中文 English 🙂\n请切换微软拼音，在这里输入、取消和确认。'
replaced = '测试' + original[2:]
states, pixels = {}, {}
for index in range(1, 59):
    folder = native / f'{index:03}-text'
    states[index] = json.loads((folder / 'text-state.json').read_text())
    assert states[index]['preedit'] in [None, '']
    pixels[index] = np.asarray(Image.open(folder / 'export.png').convert('RGBA'))
    preview = np.asarray(Image.open(folder / 'preview.png').convert('RGBA'))
    assert np.array_equal(pixels[index], preview), index
expected_transforms = [
    (13, 1, False, False, 1, 1.5), (13, 1, False, True, .75, 1.5),
    (13, 1.5, True, True, .75, 1.5), (13, .5, True, True, .75, 1.5),
    (0, 1, False, False, 1, 1.5), (13, 1, True, False, 1, 1.5),
    (0, 1, False, False, 1, 1), (13, 1.5, True, True, .75, 1),
    (0, 1, False, False, 1, 2), (13, .5, True, True, .75, 2)]
keys = ['angle', 'zoom', 'flipX', 'flipY', 'stretchX', 'renderScaling']
checks = []
for case, transform in zip(observations['cases'], expected_transforms, strict=True):
    a, b, c, d, e = [case[k] for k in ['baseline', 'cancel', 'commit', 'undo', 'redo']]
    for i in [a, b, c, d, e]:
        assert tuple(states[i][k] for k in keys) == transform, (i, transform)
    for i in [a, b, d]:
        assert states[i]['text'] == original, i
        assert np.array_equal(pixels[a], pixels[i]), i
    for i in [c, e]:
        assert states[i]['text'] == replaced, i
        assert np.array_equal(pixels[c], pixels[i]), i
    assert states[b]['selectedText'] == '中文'
    assert (states[b]['selectionStart'], states[b]['selectionEnd']) == (0, 2)
    assert states[b]['nativePreeditChanges'] > states[a]['nativePreeditChanges']
    assert states[b]['textInputEvents'] == states[a]['textInputEvents']
    assert states[c]['textInputEvents'] == states[a]['textInputEvents'] + 1
    checks.append(case | {'transform': dict(zip(keys, transform)), 'passed': True})
assert states[22]['text'] == original and states[22]['selectionStart'] == states[22]['selectionEnd'] == 0
assert states[27]['text'] == original and states[27]['selectedText'] == original
assert (states[27]['selectionStart'], states[27]['selectionEnd']) == (0, 34)
assert np.array_equal(pixels[21], pixels[27])
assert states[28]['text'] == replaced and np.array_equal(pixels[24], pixels[28])
assert states[29]['text'] == original and np.array_equal(pixels[21], pixels[29])
for index, reference in [(30, 21), (32, 31), (37, 21), (58, 21)]:
    assert states[index]['text'] == original and np.array_equal(pixels[index], pixels[reference]), index
scales = [e['data'] for e in report['events'] if e['name'] == 'display-scaling']
assert scales == [1, 2, 1.5] and states[58]['renderScaling'] == 1.5
assert report['textState']['text'] == original
review = {
    'status': '10 executed native Pinyin cases passed within recorded scope; full M1 matrix remains open',
    'sourceCommit': identity['sourceCommit'], 'dllSha256': identity['dllSha256'],
    'windowsExecuted': True, 'nativeWindowExecuted': True, 'nativeExitCode': 0,
    'nativeProcessId': 40532, 'nativePreeditChanges': report['nativePreeditChanges'],
    'textInputEvents': report['textInputEvents'], 'injectedInputMethodCalls': 0,
    'nativePreviewExportPairsExact': 58, 'nativeCases': checks,
    'tabReturnSelectAllCancelPreservedTextSelectionAndRedo': True,
    'wrappedPreeditCancelPreservedTextAndPixels': [30, 32],
    'candidatePosition': 'Observed near current input line in CUA conversation screenshots; no independently archived screen-video measurements',
    'systemScalingEvents': scales, 'lastLiveExportScaling': 1.5,
    'postCloseReportScaling': report['renderScaling'],
    'postCloseScalingCaveat': 'Closed window reports 1; use last live export and scaling events for restored system DPI',
    'remoteDragAttempt': {'export': 22, 'selectedRange': [0, 0], 'accepted': False,
                          'reason': 'No selection highlight or selection range; delivery versus application cause not isolated'},
    'fullNativeMatrixAccepted': False,
    'remaining': ['Full transform-by-DPI Cartesian coverage not executed', 'Physical mouse/pen coverage',
                  'Cross-monitor DPI transition', 'Second Chinese IME', 'Additional device and clean-user deployment matrix'],
    'archive': str(archive), 'archiveBytes': archive.stat().st_size, 'archiveSha256': sha(archive),
    'verifiedFiles': len(manifest), 'restoredOriginalText': True, 'restoredSystemScaling150': True,
    'reviewerSha256': sha(Path(__file__))}
(base / 'native-review.json').write_text(json.dumps(review, indent=2, ensure_ascii=False) + '\n')
print(json.dumps({k:v for k,v in review.items() if k != 'nativeCases'}, indent=2, ensure_ascii=False))
