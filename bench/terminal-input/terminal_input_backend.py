"""Persistent Linux desktop input transports. Connecting does not inject events."""
import ctypes as C
import ctypes.util
import os
import shutil
import socket
import struct
import subprocess
import tempfile
import time
from pathlib import Path

TEXT = 'Az漢字かな한글🙂🚀e\u0301 '


def use_wayland_clipboard(backend):
    # GNOME's wl-clipboard fallback can map a surface and steal terminal focus.
    desktop = os.environ.get('XDG_CURRENT_DESKTOP', '').lower().split(':')
    return backend == 'ydotool' and not ('gnome' in desktop and os.environ.get('DISPLAY'))


def run_command(args, **kwargs):
    result = subprocess.run(args, capture_output=True, text=True, timeout=2, **kwargs)
    if result.returncode:
        detail = result.stderr.strip() or result.stdout.strip() or 'no diagnostic output'
        raise RuntimeError(f'{args[0]} exited {result.returncode}: {detail}')
    return result.stdout.strip()


def key_events(action):
    if action == 'paste':
        return [(29, 1), (47, 1), (47, 0), (29, 0)]  # Ctrl+V
    code = {'Up': 103, 'Down': 108, 'Enter': 28}[action]
    return [(code, 1), (code, 0)]


def packets(action):
    # ydotoold's local datagram protocol is native Linux struct input_event.
    # One event per datagram, each followed by SYN_REPORT, as in its CLI.
    events = [(2, 8, 1 if action == 'wheel_up' else -1)] if action.startswith('wheel_') else [
        (1, code, value) for code, value in key_events(action)]
    for kind, code, value in events:
        yield struct.pack('@llHHi', 0, 0, kind, code, value)
        yield struct.pack('@llHHi', 0, 0, 0, 0, 0)


class Backend:
    def __init__(self, name):
        self.name, self.window = name, None
        self.sock = None
        self.display = None
        self.held = set()

    def preflight(self, text=True):
        clipboard = 'wl-copy' if use_wayland_clipboard(self.name) else 'xclip'
        if not os.environ.get('DISPLAY') or not shutil.which('xdotool'):
            raise RuntimeError('An X11/XWayland display and xdotool are required to guard RimWorld focus')
        if text and not shutil.which(clipboard):
            raise RuntimeError(f'Install {clipboard} for Unicode clipboard input')
        if text and use_wayland_clipboard(self.name) and not shutil.which('wl-paste'):
            raise RuntimeError('Install wl-paste to verify the Unicode clipboard fixture')
        if self.name == 'ydotool':
            # Verify installed CLI/protocol family without sending events.
            if '--wheel' not in run_command(['ydotool', 'mousemove', '--help']):
                raise RuntimeError('ydotool 1.x with mousemove --wheel is required')
        else:
            run_command(['xdotool', 'getactivewindow'])
            for library in ('X11', 'Xtst'):
                if not ctypes.util.find_library(library):
                    raise RuntimeError(f'Install lib{library} for persistent XTest injection')

    def prepare_text(self, text=TEXT):
        # Fixed public fixture, no newline/Enter and no destructive editing.
        # Replace the clipboard with the public fixture. Do not read or save its old contents.
        wayland = use_wayland_clipboard(self.name)
        command = (['wl-copy', '--type', 'text/plain;charset=utf-8', text]
                   if wayland else ['xclip', '-selection', 'clipboard'])
        # Clipboard owners fork and stay alive. Their inherited stderr/stdout must
        # not keep communicate() waiting for EOF after the parent has exited.
        with tempfile.TemporaryFile() as errors:
            try:
                result = subprocess.run(command, input=None if wayland else text.encode(),
                                        stdout=subprocess.DEVNULL, stderr=errors, timeout=10)
            except subprocess.TimeoutExpired as error:
                raise RuntimeError('Clipboard setup did not finish. Check compositor clipboard access. '
                                   'The focus countdown has not started.') from error
            if result.returncode:
                errors.seek(0)
                detail = errors.read().decode(errors='replace').strip()
                raise RuntimeError(f'Clipboard setup exited {result.returncode}: {detail}')

        verify = (['wl-paste', '--no-newline'] if wayland
                  else ['xclip', '-selection', 'clipboard', '-out'])
        for _ in range(2):
            if run_command(verify) != text.strip():
                raise RuntimeError('The clipboard did not return the benchmark fixture twice. No input was sent.')

    def connect(self):
        self.close()
        if self.name == 'ydotool':
            if struct.calcsize('@llHHi') != 24:
                raise RuntimeError('Persistent ydotool backend currently requires 64-bit Linux')
            self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_DGRAM)
            self.sock.settimeout(0.1)
            path = os.environ.get('YDOTOOL_SOCKET') or os.path.join(os.environ.get('XDG_RUNTIME_DIR', '/tmp'), '.ydotool_socket')
            self.sock.connect(path)
        # The launcher uses X11/XWayland even on Wayland. Guard that window
        # for both transports. Stop if the target cannot be verified.
        self.open_display()
        self.verify_target()

    def verify_target(self):
        window = run_command(['xdotool', 'getactivewindow'])
        pid = run_command(['xdotool', 'getwindowpid', window])
        executable = Path('/proc') / pid / 'exe'
        if not executable.resolve().name.startswith('RimWorld'):
            self.close()
            raise RuntimeError('The focused window is not RimWorld. No input was sent.')

    def open_display(self):
        self.x = C.CDLL(ctypes.util.find_library('X11'))
        self.xt = C.CDLL(ctypes.util.find_library('Xtst'))
        self.x.XOpenDisplay.argtypes, self.x.XOpenDisplay.restype = [C.c_char_p], C.c_void_p
        self.x.XGetInputFocus.argtypes = [C.c_void_p, C.POINTER(C.c_ulong), C.POINTER(C.c_int)]
        self.x.XKeysymToKeycode.argtypes, self.x.XKeysymToKeycode.restype = [C.c_void_p, C.c_ulong], C.c_uint
        self.x.XFlush.argtypes = [C.c_void_p]
        self.x.XCloseDisplay.argtypes = [C.c_void_p]
        self.xt.XTestFakeKeyEvent.argtypes = [C.c_void_p, C.c_uint, C.c_int, C.c_ulong]
        self.xt.XTestFakeButtonEvent.argtypes = [C.c_void_p, C.c_uint, C.c_int, C.c_ulong]
        self.display = self.x.XOpenDisplay(None)
        if not self.display:
            raise RuntimeError('Cannot connect to X display')
        self.window = self.focus()
        self.codes = {key: self.x.XKeysymToKeycode(self.display, sym)
                      for key, sym in {29: 0xffe3, 47: 0x76, 103: 0xff52, 108: 0xff54, 28: 0xff0d}.items()}
        if not all(self.codes.values()):
            raise RuntimeError('X keyboard mapping lacks required keys')

    def focus(self):
        window, revert = C.c_ulong(), C.c_int()
        self.x.XGetInputFocus(self.display, C.byref(window), C.byref(revert))
        return window.value

    def check_focus(self):
        observed = self.focus()
        if observed != self.window:
            raise RuntimeError(f'RimWorld focus changed: expected X11 window {self.window}, '
                               f'observed {observed}; stopping input before injection')

    def send(self, action):
        self.check_focus()
        if self.sock:
            for packet in packets(action):
                self.sock.send(packet)
                _, _, kind, code, down = struct.unpack('@llHHi', packet)
                if kind == 1:
                    if down: self.held.add(code)
                    else: self.held.discard(code)
                elif action == 'paste':
                    # Give the compositor distinct modifier/key transitions. Do
                    # not deliver the entire chord in a microsecond burst.
                    time.sleep(0.001)
        else:
            if action.startswith('wheel_'):
                button = 4 if action == 'wheel_up' else 5
                for down in (1, 0):
                    if not self.xt.XTestFakeButtonEvent(self.display, button, down, 0):
                        raise RuntimeError('XTest button injection failed')
            else:
                for key, down in key_events(action):
                    if not self.xt.XTestFakeKeyEvent(self.display, self.codes[key], down, 0):
                        raise RuntimeError('XTest key injection failed')
                    if down: self.held.add(key)
                    else: self.held.discard(key)
                    if action == 'paste':
                        self.x.XFlush(self.display)
                        time.sleep(0.001)
            self.x.XFlush(self.display)

    def close(self):
        # Release only this injector's interrupted key-downs, including Ctrl.
        for key in list(self.held):
            try:
                if self.sock:
                    self.sock.send(struct.pack('@llHHi', 0, 0, 1, key, 0))
                    self.sock.send(struct.pack('@llHHi', 0, 0, 0, 0, 0))
                elif self.display:
                    self.xt.XTestFakeKeyEvent(self.display, self.codes[key], 0, 0)
                    self.x.XFlush(self.display)
            except OSError:
                pass
        self.held.clear()
        if self.sock:
            self.sock.close()
            self.sock = None
        if self.display:
            self.x.XCloseDisplay(self.display)
            self.display = None
