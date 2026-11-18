"""Persistent Linux desktop input transports. Connecting does not inject events."""
import json
import os
import shutil
import socket
import struct
import subprocess
import tempfile
import time
from pathlib import Path

TEXT = 'Az漢字かな한글🙂🚀e\u0301 '


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
        self.held = set()

    def preflight(self, text=True):
        if not os.environ.get('WAYLAND_DISPLAY'):
            raise RuntimeError('A native Wayland session is required')
        if text and not all(shutil.which(cmd) for cmd in ('wl-copy', 'wl-paste')):
            raise RuntimeError('Install wl-clipboard for Unicode clipboard input')
        if '--wheel' not in run_command(['ydotool', 'mousemove', '--help']):
            raise RuntimeError('ydotool 1.x with mousemove --wheel is required')

    def prepare_text(self, text=TEXT):
        # Fixed public fixture, no newline/Enter and no destructive editing.
        # Replace the clipboard with the public fixture. Do not read or save its old contents.
        command = ['wl-copy']
        # Clipboard owners fork and stay alive. Their inherited stderr/stdout must
        # not keep communicate() waiting for EOF after the parent has exited.
        with tempfile.TemporaryFile() as errors:
            try:
                result = subprocess.run(command, input=text.encode(),
                                        stdout=subprocess.DEVNULL, stderr=errors, timeout=10)
            except subprocess.TimeoutExpired as error:
                raise RuntimeError('Clipboard setup did not finish. Check compositor clipboard access. '
                                   'The focus countdown has not started.') from error
            if result.returncode:
                errors.seek(0)
                detail = errors.read().decode(errors='replace').strip()
                raise RuntimeError(f'Clipboard setup exited {result.returncode}: {detail}')

        verify = ['wl-paste', '--no-newline']
        for _ in range(2):
            if run_command(verify) != text.strip():
                raise RuntimeError('The clipboard did not return the benchmark fixture twice. No input was sent.')

    def connect(self):
        self.close()
        if struct.calcsize('@llHHi') != 24:
            raise RuntimeError('Persistent ydotool backend currently requires 64-bit Linux')
        self.sock = socket.socket(socket.AF_UNIX, socket.SOCK_DGRAM)
        self.sock.settimeout(0.1)
        path = os.environ.get('YDOTOOL_SOCKET') or os.path.join(os.environ.get('XDG_RUNTIME_DIR', '/tmp'), '.ydotool_socket')
        try:
            self.sock.connect(path)
            self.verify_target()
        except Exception:
            self.close()
            raise

    def verify_target(self):
        self.window = self.focus()
        pid = self.window[1]
        if not pid or not (Path('/proc') / str(pid) / 'exe').resolve().name.startswith('RimWorld'):
            self.close()
            raise RuntimeError('Focus the RimWorld window before starting input')

    def focus(self):
        if os.environ.get('HYPRLAND_INSTANCE_SIGNATURE'):
            window = json.loads(run_command(['hyprctl', '-j', 'activewindow']))
            return window.get('address'), window.get('pid')
        if os.environ.get('NIRI_SOCKET'):
            window = json.loads(run_command(['niri', 'msg', '--json', 'focused-window'])) or {}
            return window.get('id'), window.get('pid')
        if os.environ.get('SWAYSOCK'):
            nodes = [json.loads(run_command(['swaymsg', '-t', 'get_tree']))]
            while nodes:
                node = nodes.pop()
                if node.get('focused'):
                    return node.get('id'), node.get('pid')
                nodes.extend(node.get('nodes', []) + node.get('floating_nodes', []))
            return None, None
        if shutil.which('kdotool'):
            window = run_command(['kdotool', 'getactivewindow'])
            return window, run_command(['kdotool', 'getwindowpid', window])
        raise RuntimeError('Cannot verify Wayland focus. Use Sway, Hyprland, niri, or KWin with kdotool.')

    def check_focus(self):
        observed = self.focus()
        if observed != self.window:
            raise RuntimeError(f'RimWorld focus changed: expected Wayland window {self.window}, '
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

    def close(self):
        # Release only this injector's interrupted key-downs, including Ctrl.
        for key in list(self.held):
            try:
                if self.sock:
                    self.sock.send(struct.pack('@llHHi', 0, 0, 1, key, 0))
                    self.sock.send(struct.pack('@llHHi', 0, 0, 0, 0, 0))
            except OSError:
                pass
        self.held.clear()
        if self.sock:
            self.sock.close()
            self.sock = None
