"""Review the fixed native follow-up archive; requires Pillow, no Windows execution."""
import hashlib
import io
import json
import sys
import zipfile
from pathlib import Path
from PIL import Image

archive = Path(sys.argv[1])
destination = Path(sys.argv[2])
digest = lambda data: hashlib.sha256(data).hexdigest()
expected_sha = '56c1c08aa62822324fc5eab1bbbde1ffb29069ec97c7bdefbbaae120170ba36b'
assert archive.stat().st_size == 716069
assert digest(archive.read_bytes()) == expected_sha
with zipfile.ZipFile(archive) as zipped:
    assert zipped.testzip() is None
    manifest = json.loads(zipped.read('files.json'))
    assert len(manifest) == 63 and len(zipped.namelist()) == 64
    assert set(zipped.namelist()) == set(manifest) | {'files.json'}
    for name, expected in manifest.items():
        assert digest(zipped.read(name)) == expected, name
    identity = json.loads(zipped.read('identity.json'))
    prefix = 'native-20260923-224035/'
    report = json.loads(zipped.read(prefix + 'window-report.json'))
    assert identity['platform'].startswith('Windows-11-10.0.26200')
    assert identity['executionCompleted'] and identity['runMode'] == 'native-only'
    assert [(p['name'], p['pid'], p['exitCode']) for p in identity['processes']] == [
        ('runtime', 49608, 0), ('native', 42996, 0)]
    assert identity['dllSha256'] == '55909cba56a66a4a2d38eafa28351d2739a3882aadcd39c747d473a6e970aa1a'
    assert report['processId'] == 42996 and report['title'].endswith(prefix[:-1])
    assert report['windowsExecuted'] and report['nativeWindow']
    assert report['injectedInputMethodCalls'] == 0
    assert not any(e['name'] == 'action-error' for e in report['events'])
    assert report['events'][-1]['name'] == 'native-window-closed'
    assert all(not zipped.read(n) for n in manifest if n.endswith('.stderr.log'))
    states, pixels = {}, {}
    for index in range(1, 20):
        folder = prefix + f'{index:03}-text/'
        states[index] = json.loads(zipped.read(folder + 'text-state.json'))
        assert states[index]['preedit'] in (None, '')
        images = [Image.open(io.BytesIO(zipped.read(folder + name))).convert('RGBA')
                  for name in ('preview.png', 'export.png')]
        assert images[0].size == images[1].size
        assert images[0].tobytes() == images[1].tobytes(), index
        pixels[index] = (images[1].size, images[1].tobytes())

original = '中文 English 🙂\n请切换微软拼音，在这里输入、取消和确认。'
replacement = '测试' + original[2:]
for index, state in states.items():
    expected_text = replacement if index in (5, 7, 10) else '测试' if index in (16, 18) else original
    assert state['text'] == expected_text, index
    keys = ('angle', 'zoom', 'flipX', 'flipY', 'stretchX', 'renderScaling')
    transform = (13, 1, False, False, 1, 1.5) if index < 14 else (13, 1, True, True, .75, 1.5)
    assert tuple(state[k] for k in keys) == transform, index
for indexes in ((1, 2, 3, 4, 6, 8, 9, 11, 12, 13), (5, 7, 10), (14, 15, 17, 19), (16, 18)):
    assert all(pixels[index] == pixels[indexes[0]] for index in indexes), indexes
for index, start, end, selected in ((3, 2, 0, '中文'), (4, 2, 0, '中文'),
                                    (9, 34, 0, original), (12, 34, 32, '认。'),
                                    (15, 34, 0, original)):
    assert (states[index]['selectionStart'], states[index]['selectionEnd'], states[index]['selectedText']) == (start, end, selected)
for baseline, cancelled in ((3, 4), (8, 9), (11, 12), (14, 15)):
    assert states[cancelled]['nativePreeditChanges'] > states[baseline]['nativePreeditChanges']
    assert states[cancelled]['textInputEvents'] == states[baseline]['textInputEvents']
for before, committed in ((4, 5), (15, 16)):
    assert states[committed]['nativePreeditChanges'] > states[before]['nativePreeditChanges']
    assert states[committed]['textInputEvents'] == states[before]['textInputEvents'] + 1
assert report['textState']['text'] == original
assert report['nativePreeditChanges'] == 36 and report['textInputEvents'] == 2
review = {
    'status': 'recorded native follow-up checks passed; full M1 matrix remains open',
    'windowsExecuted': True, 'nativeProcessId': 42996, 'nativeExitCode': 0,
    'archive': str(archive.resolve()), 'archiveBytes': 716069, 'archiveSha256': expected_sha,
    'verifiedFiles': 63, 'nativePreviewExportPairsExact': 19,
    'nativePreeditChanges': 36, 'textInputEvents': 2, 'injectedInputMethodCalls': 0,
    'reversePrefixCancelCommitUndoRedo': [3, 4, 5, 6, 7],
    'tabReturnReverseSelectAllCancelAndExistingRedo': [8, 9, 10, 11],
    'otherWindowReturnReverseSuffixCancel': [11, 12],
    'smallScrolledViewportDoubleFlipStretchCancelCommitUndoRedo': [14, 15, 16, 17, 18, 19],
    'restoredOriginalText': True, 'lastLiveRenderScaling': 1.5,
    'viewportAndCandidateEvidence': 'CUA screenshots in conversation; no independent geometry measurement',
    'remoteDragAccepted': False, 'fullNativeMatrixAccepted': False,
    'remaining': ['Physical drag and pen', 'Second Chinese IME', 'Cross-monitor DPI',
                  'Full transform/DPI combinations', 'Additional device and clean-user release matrix'],
    'reviewerSha256': digest(Path(__file__).read_bytes())}
destination.write_text(json.dumps(review, ensure_ascii=False, indent=2) + '\n')
print(json.dumps(review, ensure_ascii=False, indent=2))
