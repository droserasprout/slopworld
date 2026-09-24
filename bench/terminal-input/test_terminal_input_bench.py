import importlib.util
import io
import json
import struct
import socket
import os
from terminal_input_backend import Backend, packets, TEXT, run_command, use_wayland_clipboard
from unittest.mock import patch, Mock
from test_latency_summary import record
from pathlib import Path
import tempfile
import unittest
from collections import Counter

spec = importlib.util.spec_from_file_location("bench", Path(__file__).with_name("terminal-input-bench.py"))
bench = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bench)


class Clock:
    now = 0.0

    def time(self):
        return self.now

    def sleep(self, seconds):
        self.now += seconds


class BenchmarkTests(unittest.TestCase):
    def test_gnome_wayland_injection_uses_x11_clipboard(self):
        with patch.dict(os.environ, {'XDG_CURRENT_DESKTOP': 'ubuntu:GNOME', 'DISPLAY': ':0'}):
            self.assertFalse(use_wayland_clipboard('ydotool'))
            with patch('terminal_input_backend.subprocess.run') as run, \
                    patch('terminal_input_backend.run_command', return_value=TEXT.strip()):
                run.return_value.returncode = 0
                Backend('ydotool').prepare_text()
                self.assertEqual(run.call_args.args[0], ['xclip', '-selection', 'clipboard'])
                self.assertEqual(run.call_args.kwargs['input'], TEXT.encode())
        with patch.dict(os.environ, {'XDG_CURRENT_DESKTOP': 'sway', 'DISPLAY': ':0'}):
            self.assertTrue(use_wayland_clipboard('ydotool'))
            self.assertFalse(use_wayland_clipboard('xdotool'))

    def test_exact_counts_and_timing(self):
        for phase, rate, _ in bench.PHASES:
            clock, rows, sent = Clock(), [], []
            bench.drive(phase, rate, sent.append, lambda: None, rows, clock.time, clock.sleep)
            self.assertEqual(clock.now, 60)
            self.assertEqual(len(sent), 60 * rate)
            self.assertEqual([r['issued_s'] for r in rows], [i / rate for i in range(60 * rate)])
            counts = Counter(sent)
            if phase == 'typing':
                self.assertEqual(counts, {'paste': 600})
            elif phase == 'history':
                self.assertEqual(counts, {'wheel_up': 1800, 'wheel_down': 1800})
            else:
                self.assertEqual(counts, {'wheel_up': 1200, 'wheel_down': 1200, 'Up': 600, 'Down': 600})

    def test_late_injector_aborts_without_catchup(self):
        clock, rows = Clock(), []
        with self.assertRaisesRegex(RuntimeError, 'Excessive scheduling delay'):
            bench.drive('typing', 10, lambda _: clock.sleep(.3), lambda: None, rows, clock.time, clock.sleep)
        self.assertEqual(len(rows), 2)
        self.assertEqual(rows[0]['status'], 'sent')
        self.assertEqual(rows[1]['status'], 'not_sent_deadline')

    def test_small_scheduler_stall_shifts_without_bursting(self):
        clock, rows = Clock(), []
        def send(_):
            if len(rows) == 1:
                clock.sleep(.004)
        result = bench.drive('history', 600, send, lambda: None, rows, clock.time, clock.sleep)
        self.assertEqual(len(rows), 36000)
        self.assertEqual(result['rebased_deadlines'], 1)
        self.assertGreater(result['actual_seconds'], 60)
        self.assertLess(result['actual_seconds'], 60.01)
        self.assertAlmostEqual(rows[2]['issued_s'] - rows[1]['issued_s'], 1 / 600)
        self.assertIn('schedule_shift_s', rows[1])

    def test_cumulative_slip_budget_is_bounded(self):
        clock, rows = Clock(), []
        with self.assertRaisesRegex(RuntimeError, 'Excessive scheduling delay'):
            bench.drive('history', 600, lambda _: clock.sleep(.005), lambda: None, rows, clock.time, clock.sleep)
        self.assertEqual(rows[-1]['status'], 'not_sent_deadline')
        self.assertLessEqual(sum(row.get('schedule_shift_s', 0) for row in rows), .6)
        self.assertLess(len(rows), 1000)

    def test_failed_delivery_recorded(self):
        clock, rows = Clock(), []
        def fail(_):
            raise RuntimeError('transport failed')
        with self.assertRaisesRegex(RuntimeError, 'transport failed'):
            bench.drive('typing', 10, fail, lambda: None, rows, clock.time, clock.sleep)
        self.assertEqual(rows[0]['status'], 'failed_or_delivery_unknown')

    def test_packets_release_keys_and_preserve_unicode(self):
        wheel = [struct.unpack('@llHHi', p)[2:] for p in packets('wheel_up')]
        self.assertEqual(wheel, [(2, 8, 1), (0, 0, 0)])
        paste = [struct.unpack('@llHHi', p)[2:] for p in packets('paste')]
        self.assertEqual(paste[::2], [(1, 29, 1), (1, 47, 1), (1, 47, 0), (1, 29, 0)])
        self.assertEqual(paste[1::2], [(0, 0, 0)] * 4)
        self.assertIn('漢字', TEXT)
        self.assertIn('🙂', TEXT)
        self.assertNotIn('\n', TEXT)
        self.assertNotIn('\b', TEXT)

    def test_persistent_socket_connect_is_inert_and_sends_complete_paste(self):
        with tempfile.TemporaryDirectory() as directory:
            path = str(Path(directory) / 'socket')
            with socket.socket(socket.AF_UNIX, socket.SOCK_DGRAM) as server:
                server.bind(path)
                server.settimeout(0.01)
                backend = Backend('ydotool')
                with patch.dict(os.environ, {'YDOTOOL_SOCKET': path}), patch.object(backend, 'open_display'), patch.object(backend, 'verify_target'):
                    backend.connect()
                backend.focus = lambda: None
                try:
                    with self.assertRaises(TimeoutError):
                        server.recv(1024)
                    backend.send('paste')
                    received = [server.recv(1024) for _ in range(8)]
                    self.assertEqual(received, list(packets('paste')))
                    self.assertFalse(backend.held)
                finally:
                    backend.close()

    def test_focus_loss_stops_wayland_before_injection(self):
        backend = Backend('ydotool')
        backend.window = 10
        backend.focus = lambda: 11
        backend.sock = Mock()
        with self.assertRaisesRegex(RuntimeError, 'expected X11 window 10, observed 11'):
            backend.send('paste')
        backend.sock.send.assert_not_called()

    def test_five_and_ten_times_schedules(self):
        for multiplier in (5, 10):
            for phase, rate, _ in bench.PHASES:
                clock, rows = Clock(), []
                bench.drive(phase, rate * multiplier, lambda _: None, lambda: None, rows, clock.time, clock.sleep)
                self.assertEqual(len(rows), rate * multiplier * 60)
                self.assertEqual(clock.now, 60)
                self.assertNotIn('BackSpace', {row['action'] for row in rows})

    def test_command_failure_preserves_backend_diagnostic(self):
        result = bench.subprocess.CompletedProcess(['ydotool'], 2, '', 'failed to connect socket: missing')
        with patch.object(bench.subprocess, 'run', return_value=result):
            with self.assertRaisesRegex(RuntimeError, 'ydotool exited 2: failed to connect socket: missing'):
                run_command(['ydotool', 'mousemove', '--help'])

    def test_clipboard_does_not_capture_daemon_output_pipes(self):
        result = bench.subprocess.CompletedProcess(['wl-copy'], 0)
        with patch.object(bench.subprocess, 'run', return_value=result) as run, \
                patch('terminal_input_backend.run_command', return_value=TEXT.strip()):
            Backend('ydotool').prepare_text()
            self.assertEqual(run.call_args.kwargs['stdout'], bench.subprocess.DEVNULL)
            self.assertNotEqual(run.call_args.kwargs['stderr'], bench.subprocess.PIPE)
            self.assertNotIn('capture_output', run.call_args.kwargs)

    def test_existing_phase_refused_before_backend_connection(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'typing.log').write_text('existing capture')
            with patch.object(bench.sys, 'argv', ['bench', '--phase', 'typing', '--log', 'unused', '--output', directory]), \
                    patch.object(bench, 'Backend') as backend, patch('sys.stderr', new_callable=io.StringIO):
                with self.assertRaises(SystemExit):
                    bench.main()
                backend.assert_not_called()
            self.assertEqual((root / 'typing.log').read_text(), 'existing capture')

    def test_stalled_paste_aborts_early_and_drains(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            log = root / 'trace.log'
            log.write_text('')
            clock = Clock()
            backend = Mock(window=10)
            backend.name = 'ydotool'
            with patch.object(bench, 'Backend', return_value=backend), \
                    patch.object(bench.time, 'monotonic', clock.time), patch.object(bench.time, 'sleep', clock.sleep), \
                    patch('builtins.input', return_value=''), patch('sys.stdout', new_callable=io.StringIO), \
                    patch.object(bench.sys, 'argv', ['bench', '--phase', 'typing', '--multiplier', '10',
                                                   '--log', str(log), '--output', str(root / 'out')]):
                with self.assertRaisesRegex(RuntimeError, 'No new SlopWorld paste requests'):
                    bench.main()
            data = json.loads((root / 'out' / 'typing-events.json').read_text())
            self.assertEqual(data['status'], 'invalid')
            self.assertEqual(data['observed_paste_requests'], 0)
            self.assertLessEqual(data['sent_events'], 103)
            self.assertGreaterEqual(clock.now, 22)  # countdown + watchdog + drain
            backend.close.assert_called_once()

    def test_guided_run_writes_all_phase_artifacts(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            log = root / 'trace.log'
            log.write_text('')
            clock = Clock()
            class FakeBackend:
                window = 'test-window'
                count = 0
                def __init__(self, name):
                    self.name = name
                def preflight(self, text=True):
                    pass
                def connect(self):
                    pass
                def close(self):
                    pass
                def prepare_text(self):
                    pass
                def send(self, action):
                    FakeBackend.count += 1
                    with log.open('a') as stream:
                        kind = 'paste' if action == 'paste' else 'keys'
                        stream.write(f'[SlopWorld] latency id={self.count:032x} status=start kind={kind}\n')
                        if self.count <= 18000:
                            stream.write(f'[SlopWorld] latency id={self.count:032x} kind=history_scroll status=frame_end elapsed_us=40 draw_us=35\n')
                        else:
                            stream.write(record(self.count).replace('kind=keys', f'kind={kind}') + '\n')
                        if self.count % 60 == 0:
                            stream.write('[SlopWorld] perf context eco=1 terminal=1 sessions=1 width=100 height=100 fps=60 gc0=0;\n')
            with patch.object(bench, 'Backend', FakeBackend), patch.object(bench.time, 'monotonic', clock.time), \
                    patch.object(bench.time, 'sleep', clock.sleep), patch('builtins.input', return_value=''), \
                    patch.object(bench.sys, 'argv', ['bench', '--log', str(log), '--output', str(root / 'out')]), \
                    patch('sys.stdout', new_callable=io.StringIO):
                for phase, _, _ in bench.PHASES:
                    with patch.object(bench.sys, 'argv', ['bench', '--phase', phase, '--prepare-seconds', '20',
                                                         '--log', str(log), '--output', str(root / 'out')]):
                        bench.main()
            self.assertGreaterEqual(clock.now, 3 * (20 + 60 + 11))
            for phase, rate, _ in bench.PHASES:
                events = json.loads((root / 'out' / f'{phase}-events.json').read_text())
                self.assertEqual(events['status'], 'complete')
                self.assertEqual(events['sent_events'], rate * 5 * 60)
                latency = json.loads((root / 'out' / f'{phase}-latency.json').read_text())
                self.assertEqual(latency['counts']['samples'], rate * 5 * 60)
                self.assertEqual(latency['counts']['unfinished'], 0)
                self.assertIn('mean reported FPS=60.00', (root / 'out' / f'{phase}-performance.txt').read_text())

    def test_capture_boundaries_partial_lines_and_drain(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'trace.log'
            path.write_text('[SlopWorld] latency old\n')
            output = io.StringIO()
            capture = bench.Capture(path, output)
            try:
                with path.open('a') as writer:
                    writer.write('[SlopWorld] latency id=abc'); writer.flush()
                    capture.pump()
                    self.assertEqual(output.getvalue(), '')
                    writer.write('\n[SlopWorld] perf first\n'); writer.flush()
                    capture.pump()
                    writer.write('[SlopWorld] perf drain\n[SlopWorld] latency last\n'); writer.flush()
                    capture.pump(perf=False)
                self.assertEqual(output.getvalue(), '[SlopWorld] latency id=abc\n[SlopWorld] perf first\n[SlopWorld] latency last\n')
                path.write_text('')
                with self.assertRaisesRegex(RuntimeError, 'truncated'):
                    capture.pump()
            finally:
                capture.source.close()


if __name__ == '__main__':
    unittest.main()
