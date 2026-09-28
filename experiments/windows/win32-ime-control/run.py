"""Temporary native EDIT comparison. UI input is manual; no injected IME events."""
import ctypes as C
from ctypes import wintypes as W
import datetime
import hashlib
import json
import os
from pathlib import Path
import platform
import sys
import zipfile

if sys.platform != 'win32':
    raise SystemExit('Actual Windows required')
kit = Path(__file__).resolve().parent
manifest = json.loads((kit / 'manifest.json').read_text())
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
for name, digest in manifest.items():
    if sha(kit / name) != digest:
        raise RuntimeError('Payload identity mismatch: ' + name)
out = kit.parent / ('win32-ime-control-' + datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
out.mkdir(exist_ok=False)
trace = (out / 'trace.jsonl').open('x', encoding='utf-8')
u = C.WinDLL('user32', use_last_error=True)
g = C.WinDLL('gdi32', use_last_error=True)
i = C.WinDLL('imm32', use_last_error=True)
k = C.WinDLL('kernel32', use_last_error=True)
LRESULT = C.c_ssize_t
WPARAM = C.c_size_t
LPARAM = C.c_ssize_t
PROC = C.WINFUNCTYPE(LRESULT, W.HWND, W.UINT, WPARAM, LPARAM)

def api(lib, name, result, *args):
    f = getattr(lib, name)
    f.restype, f.argtypes = result, list(args)
    return f

create = api(u, 'CreateWindowExW', W.HWND, W.DWORD, W.LPCWSTR, W.LPCWSTR, W.DWORD,
             C.c_int, C.c_int, C.c_int, C.c_int, W.HWND, W.HMENU, W.HINSTANCE, W.LPVOID)
send = api(u, 'SendMessageW', LRESULT, W.HWND, W.UINT, WPARAM, LPARAM)
default = api(u, 'DefWindowProcW', LRESULT, W.HWND, W.UINT, WPARAM, LPARAM)
api(u, 'SetProcessDpiAwarenessContext', W.BOOL, W.HANDLE)(C.c_void_p(-4))
module = api(k, 'GetModuleHandleW', W.HMODULE, W.LPCWSTR)(None)
add_font = api(g, 'AddFontResourceExW', C.c_int, W.LPCWSTR, W.DWORD, W.LPVOID)
remove_font = api(g, 'RemoveFontResourceExW', W.BOOL, W.LPCWSTR, W.DWORD, W.LPVOID)
font_path = str(kit / 'SourceHanSansSC-Regular.otf')
if not add_font(font_path, 0x10, None):
    raise C.WinError(C.get_last_error())

class WNDCLASS(C.Structure):
    _fields_ = [('style', W.UINT), ('proc', PROC), ('extra', C.c_int), ('windowExtra', C.c_int),
                ('instance', W.HINSTANCE), ('icon', W.HICON), ('cursor', W.HANDLE),
                ('background', W.HBRUSH), ('menu', W.LPCWSTR), ('name', W.LPCWSTR)]

class Candidate(C.Structure):
    _fields_ = [('index', W.DWORD), ('style', W.DWORD), ('point', W.POINT), ('area', W.RECT)]

@PROC
def procedure(hwnd, message, wp, lp):
    if message == 2:  # WM_DESTROY
        u.PostQuitMessage(0)
        return 0
    return default(hwnd, message, wp, lp)

wc = WNDCLASS(0, procedure, 0, 0, module, None, None, 6, None, 'CompositorImeControl')
if not api(u, 'RegisterClassW', W.ATOM, C.POINTER(WNDCLASS))(C.byref(wc)):
    raise C.WinError(C.get_last_error())
window = create(0, wc.name, 'Win32 EDIT IME control — PID ' + str(os.getpid()),
                0x10CF0000, 850, 350, 1400, 900, None, None, module, None)
if not window:
    raise C.WinError(C.get_last_error())
dpi = api(u, 'GetDpiForWindow', W.UINT, W.HWND)(window)
scale = dpi / 96
px = lambda value: round(value * scale)
create(0, 'STATIC', 'Click text, Ctrl+End, slowly type zhongwen, observe candidate after wrap; Esc then close.',
       0x50000000, 12, 12, 1330, 36, window, None, module, None)
original = '中文输入 / Windows IME\r\nSelect, replace, undo, redo. 😀'
edit = create(0x200, 'EDIT', original, 0x50201044, 12, 60, 1300, 750, window, None, module, None)
if not edit:
    raise C.WinError(C.get_last_error())
font = api(g, 'CreateFontW', W.HFONT, *([C.c_int] * 5), *([W.DWORD] * 8), W.LPCWSTR)(
    -px(32), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, 'Source Han Sans SC')
if not font:
    raise C.WinError(C.get_last_error())
send(edit, 0x30, font, 1)  # WM_SETFONT
hdc = api(u, 'GetDC', W.HDC, W.HWND)(edit)
select_object = api(g, 'SelectObject', W.HGDIOBJ, W.HDC, W.HGDIOBJ)
previous_font = select_object(hdc, font)
face = C.create_unicode_buffer(128)
if not api(g, 'GetTextFaceW', C.c_int, W.HDC, C.c_int, W.LPWSTR)(hdc, len(face), face):
    raise C.WinError(C.get_last_error())
select_object(hdc, previous_font)
api(u, 'ReleaseDC', C.c_int, W.HWND, W.HDC)(edit, hdc)
rect = W.RECT(0, 0, px(540), 700)
send(edit, 0xB3, 0, C.addressof(rect))  # EM_SETRECT
api(u, 'SetFocus', W.HWND, W.HWND)(edit)
send(edit, 0xB1, len(original.encode('utf-16-le')) // 2, len(original.encode('utf-16-le')) // 2)
get_caret = api(u, 'GetCaretPos', W.BOOL, C.POINTER(W.POINT))
get_text = api(u, 'GetWindowTextW', C.c_int, W.HWND, W.LPWSTR, C.c_int)
get_context = api(i, 'ImmGetContext', W.HANDLE, W.HWND)
release = api(i, 'ImmReleaseContext', W.BOOL, W.HWND, W.HANDLE)
get_comp = api(i, 'ImmGetCompositionStringW', C.c_long, W.HANDLE, W.DWORD, W.LPVOID, W.DWORD)
get_candidate = api(i, 'ImmGetCandidateWindow', W.BOOL, W.HANDLE, W.DWORD, C.POINTER(Candidate))
api(u, 'SetTimer', C.c_size_t, W.HWND, C.c_size_t, W.UINT, W.LPVOID)(window, 1, 100, None)
identity = {'platform': platform.platform(), 'pid': os.getpid(), 'dpi': dpi,
            'control': 'Win32 EDIT', 'fontRequested': 'Source Han Sans SC', 'fontResolved': face.value, 'fontPixels': px(32),
            'formatWidthPixels': px(540), 'payload': manifest, 'candidatePositionAccepted': False}
(out / 'identity.json').write_text(json.dumps(identity, indent=2) + '\n')
last = None
last_text = None

def record():
    global last, last_text
    buf = C.create_unicode_buffer(4096)
    get_text(edit, buf, len(buf))
    last_text = buf.value
    point = W.POINT()
    state = {'text': buf.value, 'caretOk': bool(get_caret(C.byref(point))), 'caret': [point.x, point.y]}
    context = get_context(edit)
    state['hasImmContext'] = bool(context)
    if context:
        data = C.create_string_buffer(4096)
        count = get_comp(context, 8, data, len(data))
        state['preeditBytes'] = count
        state['preedit'] = data.raw[:count].decode('utf-16-le') if count > 0 else ''
        candidate = Candidate()
        state['candidateQueryOk'] = bool(get_candidate(context, 0, C.byref(candidate)))
        if state['candidateQueryOk']:
            state['candidateStyle'] = candidate.style
            state['candidatePoint'] = [candidate.point.x, candidate.point.y]
        release(edit, context)
    if state != last:
        trace.write(json.dumps(state | {'utc': datetime.datetime.now(datetime.timezone.utc).isoformat()}, ensure_ascii=False) + '\n')
        trace.flush()
        last = state

print('RUNNING', os.getpid(), out, flush=True)
message = W.MSG()
get_message = api(u, 'GetMessageW', W.BOOL, C.POINTER(W.MSG), W.HWND, W.UINT, W.UINT)
api(u, 'TranslateMessage', W.BOOL, C.POINTER(W.MSG))
api(u, 'DispatchMessageW', LRESULT, C.POINTER(W.MSG))
record()
while (result := get_message(C.byref(message), None, 0, 0)) > 0:
    u.TranslateMessage(C.byref(message))
    u.DispatchMessageW(C.byref(message))
    if message.message == 0x113:
        record()
trace.close()
identity.update(exitCode=0 if result == 0 else 1, finalOriginalPreserved=last_text == original)
(out / 'identity.json').write_text(json.dumps(identity, indent=2) + '\n')
api(g, 'DeleteObject', W.BOOL, W.HGDIOBJ)(font)
remove_font(font_path, 0x10, None)
files = {p.name: sha(p) for p in sorted(out.iterdir())}
(out / 'files.json').write_text(json.dumps(files, indent=2) + '\n')
archive = out.with_suffix('.zip')
with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED) as z:
    for p in sorted(out.iterdir()):
        z.write(p, p.name)
print(json.dumps({'archive': str(archive), 'bytes': archive.stat().st_size, 'sha256': sha(archive)}), flush=True)
raise SystemExit(identity['exitCode'])
