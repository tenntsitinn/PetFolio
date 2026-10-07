"""Read-only discovery of native ChatGPT/Codex windows; no UI input."""
import ctypes
import json
from ctypes import wintypes as w

u = ctypes.WinDLL('user32', use_last_error=True)
k = ctypes.WinDLL('kernel32', use_last_error=True)
callback = ctypes.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
u.EnumWindows.argtypes = [callback, w.LPARAM]
u.GetWindowThreadProcessId.argtypes = [w.HWND, ctypes.POINTER(w.DWORD)]
u.GetWindowRect.argtypes = [w.HWND, ctypes.POINTER(w.RECT)]
u.IsWindowVisible.argtypes = [w.HWND]
u.IsIconic.argtypes = [w.HWND]
u.GetWindowLongPtrW.argtypes = [w.HWND, ctypes.c_int]
u.GetWindowLongPtrW.restype = ctypes.c_ssize_t
u.GetWindow.argtypes = [w.HWND, w.UINT]
u.GetWindow.restype = w.HWND
u.GetWindowTextW.argtypes = [w.HWND, w.LPWSTR, ctypes.c_int]
u.GetClassNameW.argtypes = [w.HWND, w.LPWSTR, ctypes.c_int]
k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
k.OpenProcess.restype = w.HANDLE
k.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, ctypes.POINTER(w.DWORD)]
k.CloseHandle.argtypes = [w.HANDLE]
u.SetProcessDpiAwarenessContext.argtypes = [w.HANDLE]
u.SetProcessDpiAwarenessContext(w.HANDLE(-4))
found = []

@callback
def visit(hwnd, _):
    pid = w.DWORD()
    u.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    h = k.OpenProcess(0x1000, False, pid.value)
    if not h:
        return True
    try:
        executable = ctypes.create_unicode_buffer(32768)
        size = w.DWORD(len(executable))
        if not k.QueryFullProcessImageNameW(h, 0, executable, ctypes.byref(size)):
            return True
        if executable.value.lower().split('\\')[-1] not in ('chatgpt.exe', 'codex.exe'):
            return True
        title, klass = ctypes.create_unicode_buffer(512), ctypes.create_unicode_buffer(256)
        rect = w.RECT()
        u.GetWindowTextW(hwnd, title, len(title))
        u.GetClassNameW(hwnd, klass, len(klass))
        u.GetWindowRect(hwnd, ctypes.byref(rect))
        found.append({'hwnd': int(hwnd), 'pid': pid.value, 'title': title.value,
                      'class': klass.value, 'visible': bool(u.IsWindowVisible(hwnd)),
                      'minimized': bool(u.IsIconic(hwnd)),
                      'exstyle': hex(u.GetWindowLongPtrW(hwnd, -20)),
                      'owner': u.GetWindow(hwnd, 4),
                      'rect': [rect.left, rect.top, rect.right, rect.bottom]})
    finally:
        k.CloseHandle(h)
    return True

u.EnumWindows(visit, 0)
print(json.dumps(found, ensure_ascii=True))
