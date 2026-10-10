"""Replay explicit UI actions into one observed Compositor window using Windows SendInput.
This exercises the OS input route, not physical mouse/keyboard hardware or pen pressure.
Usage: python replay-native-input.py <pid> <new-log.json> <actions.json>
Coordinates are logical client pixels; refuses another foreground application.
"""
import ctypes as c
from ctypes import wintypes as w
import json, sys, time
from pathlib import Path

pid, output, actions = int(sys.argv[1]), Path(sys.argv[2]), json.loads(Path(sys.argv[3]).read_text(encoding='utf-8-sig'))
assert not output.exists(), 'Output must be new'
u = c.WinDLL('user32', use_last_error=True)
u.SetProcessDpiAwarenessContext.argtypes = [c.c_void_p]
u.SetProcessDpiAwarenessContext(c.c_void_p(-4))
u.GetForegroundWindow.restype = w.HWND
u.GetWindowThreadProcessId.argtypes = [w.HWND, c.POINTER(w.DWORD)]
u.GetWindowTextW.argtypes = [w.HWND, w.LPWSTR, c.c_int]
u.SetForegroundWindow.argtypes = [w.HWND]
u.ClientToScreen.argtypes = [w.HWND, c.POINTER(w.POINT)]
u.GetClientRect.argtypes = [w.HWND, c.POINTER(w.RECT)]
u.GetDpiForWindow.argtypes = [w.HWND]
callback = c.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
windows = []
@callback
def visit(hwnd, unused):
    owner = w.DWORD(); u.GetWindowThreadProcessId(hwnd, c.byref(owner))
    title = c.create_unicode_buffer(512); u.GetWindowTextW(hwnd, title, len(title))
    if owner.value == pid and title.value.startswith('Compositor'):
        windows.append((hwnd, title.value))
    return True
u.EnumWindows(visit, 0)
assert len(windows) == 1, windows
hwnd, title = windows[0]
u.SetForegroundWindow(hwnd); time.sleep(.3)
origin = w.POINT(); rect = w.RECT(); u.ClientToScreen(hwnd, c.byref(origin)); u.GetClientRect(hwnd, c.byref(rect))
scale = u.GetDpiForWindow(hwnd) / 96
class Mouse(c.Structure):
    _fields_ = [('dx',w.LONG),('dy',w.LONG),('data',w.DWORD),('flags',w.DWORD),('time',w.DWORD),('extra',c.c_size_t)]
class Keyboard(c.Structure):
    _fields_ = [('vk',w.WORD),('scan',w.WORD),('flags',w.DWORD),('time',w.DWORD),('extra',c.c_size_t)]
class Payload(c.Union):
    _fields_ = [('mouse',Mouse),('key',Keyboard)]
class Input(c.Structure):
    _fields_ = [('kind',w.DWORD),('payload',Payload)]
u.SendInput.argtypes = [w.UINT,c.POINTER(Input),c.c_int]
events = 0
held_keys = set(); held_mouse = False

def send(value):
    global events
    assert u.GetForegroundWindow() == hwnd, 'Target lost foreground; stopping replay'
    assert u.SendInput(1,c.byref(value),c.sizeof(Input)) == 1, c.get_last_error()
    events += 1

def mouse(flags, x=0, y=0):
    send(Input(0, Payload(mouse=Mouse(x,y,0,flags,0,0))))

def move(x,y):
    assert 0 <= x*scale < rect.right and 0 <= y*scale < rect.bottom, 'Point outside client'
    sx,sy=origin.x+x*scale,origin.y+y*scale
    vx,vy,vw,vh=[u.GetSystemMetrics(i) for i in (76,77,78,79)]
    mouse(0xe001,round((sx-vx)*65535/(vw-1)),round((sy-vy)*65535/(vh-1)))

def key(vk,up=False):
    send(Input(1,Payload(key=Keyboard(vk,0,2 if up else 0,0,0))))
    if up: held_keys.discard(vk)
    else: held_keys.add(vk)

def chord(keys):
    for vk in keys: key(vk)
    for vk in reversed(keys): key(vk,True)

try:
    for action in actions:
        if action[0]=='click':
            move(*action[1:]);mouse(2);held_mouse=True;time.sleep(.05);mouse(4);held_mouse=False
        elif action[0]=='path':
            move(*action[1][0]);mouse(2);held_mouse=True
            for point in action[1][1:]: move(*point);time.sleep(.04)
            if len(action)>2 and action[2]=='cancel': chord([27]);time.sleep(.1)
            mouse(4);held_mouse=False
        elif action[0]=='keys': chord(action[1])
        elif action[0]=='text':
            assert action[1].isascii() and action[1].isalpha()
            for letter in action[1]: chord([ord(letter.upper())]);time.sleep(.12)
        else: raise ValueError(action[0])
        time.sleep(.25)
finally:
    # Release only keys/buttons held by this replay, including on focus failure.
    for vk in held_keys:
        value=Input(1,Payload(key=Keyboard(vk,0,2,0,0)));u.SendInput(1,c.byref(value),c.sizeof(Input))
    if held_mouse:
        value=Input(0,Payload(mouse=Mouse(0,0,0,4,0,0)));u.SendInput(1,c.byref(value),c.sizeof(Input))
output.write_text(json.dumps(dict(pid=pid,title=title,scale=scale,client=[rect.right,rect.bottom],actions=actions,
    sentInputEvents=events,inputBoundary='Windows SendInput; not physical hardware'),indent=2),encoding='utf-8')
print('INPUT_REPLAY_COMPLETED', events)
