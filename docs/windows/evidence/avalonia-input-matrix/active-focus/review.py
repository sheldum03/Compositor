"""Review the fixed Windows active-composition archive; requires Pillow."""
import hashlib
import io
import json
import sys
import zipfile
from pathlib import Path
from PIL import Image

archive, destination = map(Path, sys.argv[1:3])
sha = lambda data: hashlib.sha256(data).hexdigest()
expected_sha = 'a9e6f0f3e4cbbe6860101d86882d1112e3b26cd1145defc7deb3ac6d390902ea'
assert archive.stat().st_size == 642137
assert sha(archive.read_bytes()) == expected_sha
original = '中文 English 🙂\n请切换微软拼音，在这里输入、取消和确认。'
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    files = json.loads(z.read('files.json'))
    assert len(files) == 51 and len(z.namelist()) == 52
    assert set(z.namelist()) == set(files) | {'files.json'}
    for name, digest in files.items():
        assert sha(z.read(name)) == digest, name
    identity = json.loads(z.read('identity.json'))
    prefix = 'native-20260924-174404/'
    report = json.loads(z.read(prefix + 'window-report.json'))
    assert identity['platform'].startswith('Windows-11-10.0.26200')
    assert identity['executionCompleted'] and identity['runMode'] == 'native-only'
    assert [(p['name'], p['pid'], p['exitCode']) for p in identity['processes']] == [
        ('runtime', 36340, 0), ('native', 23668, 0)]
    assert identity['dllSha256'] == '55909cba56a66a4a2d38eafa28351d2739a3882aadcd39c747d473a6e970aa1a'
    assert not identity['nativeImeAccepted'] and not identity['systemDpiAccepted']
    assert report['processId'] == 23668 and report['title'].endswith(prefix[:-1])
    assert report['windowsExecuted'] and report['nativeWindow']
    assert report['injectedInputMethodCalls'] == 0
    assert report['events'][-1]['name'] == 'native-window-closed'
    assert not any(e['name'] == 'action-error' for e in report['events'])
    assert all(not z.read(n) for n in files if n.endswith('.stderr.log'))
    states, pixels = {}, {}
    preedits = [6, 12, 18, 18, 24, 24, 24, 24, 30, 30, 30, 30, 42, 42, 42]
    commits = [0, 0, 1, 1] + [2] * 11
    for index in range(1, 16):
        folder = prefix + f'{index:03}-text/'
        s = states[index] = json.loads(z.read(folder + 'text-state.json'))
        expected_text = ('测试' + original[2:] if index == 3 else
                         'ceshi' + original[2:] if index in (5, 7, 11, 14) else original)
        assert s['text'] == expected_text and s['preedit'] in (None, ''), index
        assert tuple(s[k] for k in ('angle', 'zoom', 'flipX', 'flipY', 'stretchX', 'renderScaling')) == (13, 1, False, False, 1, 1.5)
        assert (s['nativePreeditChanges'], s['textInputEvents']) == (preedits[index-1], commits[index-1])
        images = [Image.open(io.BytesIO(z.read(folder + n))).convert('RGBA')
                  for n in ('preview.png', 'export.png')]
        assert images[0].size == images[1].size
        assert images[0].tobytes() == images[1].tobytes(), index
        pixels[index] = (images[1].size, images[1].tobytes())
    for group in ((1, 2, 4, 6, 8, 9, 10, 12, 13, 15), (5, 7, 11, 14)):
        assert all(pixels[i] == pixels[group[0]] for i in group)
    for i in (1, 2, 9, 10):
        assert (states[i]['selectionStart'], states[i]['selectionEnd'], states[i]['selectedText']) == (2, 0, '中文')
    assert (states[13]['selectionStart'], states[13]['selectionEnd'], states[13]['selectedText']) == (0, 2, '中文')
    assert report['textState']['text'] == original
    assert (report['nativePreeditChanges'], report['textInputEvents']) == (42, 2)
    assert [e['data']['length'] for e in report['events'] if e['name'] == 'text-input'] == [2, 5]

review = {
    'status': 'fixed active-composition observations verified; full M1 remains open',
    'archive': str(archive.resolve()), 'archiveBytes': 642137, 'archiveSha256': expected_sha,
    'verifiedFiles': 51, 'nativeProcessId': 23668, 'nativeExitCode': 0,
    'windowsExecuted': True, 'nativePreviewExportPairsExact': 15,
    'nativePreeditChanges': 42, 'textInputEvents': 2, 'injectedInputMethodCalls': 0,
    'tabCancelThenCommitUndo': [1, 2, 3, 4],
    'systemRunLiteralPinyinCommitUndoRedoRestore': [4, 5, 6, 7, 8],
    'exportCancelExistingRedoRestore': [8, 9, 10, 11, 12],
    'delayedRemoteTabCancelExistingRedoRestore': [12, 13, 14, 15],
    'lastLiveRenderScaling': 1.5, 'postCloseRenderScaling': report['renderScaling'],
    'restoredOriginalText': True, 'fullNativeMatrixAccepted': False,
    'limitations': ['Actions/candidates observed in conversation screenshots, no independent video.',
                    'First export follows an unbaselined cancellation.',
                    'Last tab case includes an extra Esc cancel and delayed input; preedit selection direction is not accepted.',
                    'Idle interval is not a continuous editing or resource test.',
                    'Physical drag, pen, second IME, cross-monitor and full device/release matrix remain open.'],
    'reviewerSha256': sha(Path(__file__).read_bytes())}
destination.write_text(json.dumps(review, ensure_ascii=False, indent=2) + '\n')
print(json.dumps(review, ensure_ascii=False, indent=2))
