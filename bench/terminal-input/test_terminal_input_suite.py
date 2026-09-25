"""Game-free tests for history preparation, unattended sequencing and reports."""
import io
import json
import os
from pathlib import Path
import shlex
import subprocess
import shutil
import time
import uuid
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch, Mock

from test_terminal_input_bench import bench, Clock
from test_latency_summary import record
from terminal_input_backend import TEXT, key_events
from terminal_input_history import fill_history, command
from terminal_input_metrics import measurement_status


class SuiteTests(unittest.TestCase):
    def test_generator_outputs_exact_finite_printable_lines(self):
        with tempfile.TemporaryDirectory() as directory:
            status = Path(directory) / 'done.json'
            output = io.BytesIO()
            with patch.dict(os.environ, {'TMUX': 'fixture', 'TMUX_PANE': '%1'}), \
                    patch('subprocess.check_output', return_value='5 9 2 0'), \
                    patch('sys.stdout', SimpleNamespace(buffer=output)):
                fill_history(status, 5)
            data = output.getvalue()
            self.assertEqual(len(data), 8 * 9)
            self.assertEqual(len(data.splitlines()), 8)
            self.assertTrue(all(len(line) == 8 and line.isalnum() for line in data.splitlines()))
            info = json.loads(status.read_text())
            self.assertEqual(info['status'], 'complete')
            self.assertEqual(info['bytes_written'], len(data))

    def test_generator_refuses_bad_context_without_output(self):
        for geometry in ('5 9 2 1', '6 9 2 0', '5 1 2 0'):
            with self.subTest(geometry=geometry), tempfile.TemporaryDirectory() as directory:
                status = Path(directory) / 'done.json'
                output = io.BytesIO()
                with patch.dict(os.environ, {'TMUX': 'fixture', 'TMUX_PANE': '%1'}), \
                        patch('subprocess.check_output', return_value=geometry), \
                        patch('sys.stdout', SimpleNamespace(buffer=output)):
                    fill_history(status, 5)
                self.assertEqual(output.getvalue(), b'')
                self.assertEqual(json.loads(status.read_text())['status'], 'invalid')

    def test_standalone_command_roundtrip_and_shell_quoting(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tmux = root / 'tmux'
            tmux.write_text("#!/bin/sh\nprintf '5 9 2 0\\n'\n")
            tmux.chmod(0o700)
            status = root / "quote ' dollar $ and spaces.json"
            argv = shlex.split(command(status, 5))
            self.assertNotIn('\n', command(status, 5))
            result = subprocess.run(argv, capture_output=True, timeout=5, check=True,
                                    env={**os.environ, 'PATH': str(root) + os.pathsep + os.environ['PATH'],
                                         'TMUX': 'fixture', 'TMUX_PANE': '%1'})
            self.assertEqual(len(result.stdout), 72)
            self.assertEqual(json.loads(status.read_text())['status'], 'complete')

    @unittest.skipUnless(shutil.which('tmux'), 'tmux is needed for the isolated fixture check')
    def test_real_tmux_history_fixture(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            status = root / 'done.json'
            config = root / 'tmux.conf'
            config.write_text('set-option -g history-limit 128\n')
            socket = 'slopworld-history-test-' + uuid.uuid4().hex
            def tmux(*args):
                return subprocess.check_output(['tmux', '-L', socket, *args], text=True, timeout=5)
            try:
                tmux('-f', str(config), 'new-session', '-d', '-s', 'fixture',
                     command(status, 128) + '; exec sleep 30')
                deadline = time.monotonic() + 5
                while not status.exists() and time.monotonic() < deadline:
                    time.sleep(.01)
                info = json.loads(status.read_text())
                self.assertEqual(info['status'], 'complete')
                self.assertEqual(info['target_history_lines'], 128)
                # tmux trims history in chunks when the limit is exceeded. The
                # emitter must displace the viewport and fill at least one limit.
                history = int(tmux('display-message', '-p', '-t', 'fixture', '#{history_size}').strip())
                self.assertGreaterEqual(history, 115)
                data = tmux('capture-pane', '-p', '-t', 'fixture', '-S', '-')
                lines = [line for line in data.splitlines() if line]
                self.assertGreaterEqual(len(lines), 128)
                self.assertTrue(all(line.isascii() and line.isalnum() for line in lines))
            finally:
                subprocess.run(['tmux', '-L', socket, 'kill-server'], capture_output=True, timeout=5)

    def test_setup_does_not_send_enter_without_confirmed_paste(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            log = root / 'trace.log'
            log.write_text('')
            args = SimpleNamespace(output=root, log=log, prepare_seconds=1)
            backend = Mock()
            clock = Clock()
            with patch.object(bench.time, 'monotonic', clock.time), \
                    patch.object(bench.time, 'sleep', clock.sleep), \
                    patch('builtins.input', return_value=''), patch('sys.stdout', new_callable=io.StringIO):
                with self.assertRaisesRegex(RuntimeError, 'no Enter sent'):
                    bench.prepare_history(args, backend)
            backend.send.assert_called_once_with('paste')
            self.assertEqual(json.loads((root / 'setup.json').read_text())['status'], 'invalid')

    def test_wait_stops_on_focus_loss(self):
        backend = Mock()
        backend.check_focus.side_effect = RuntimeError('focus changed')
        with self.assertRaisesRegex(RuntimeError, 'focus changed'):
            bench.wait_until(lambda: True, backend, 10, 'timeout')
        self.assertEqual(key_events('Enter'), [(28, 1), (28, 0)])

    def test_one_prompt_prepares_then_runs_history_and_typing(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            log = root / 'trace.log'
            log.write_text('')
            state = {'actions': [], 'texts': [], 'count': 0}
            def setup_command(path, limit):
                state['status'] = path
                return 'bounded setup command'
            class FakeBackend:
                window = 10
                def __init__(self, name): self.name = name
                def preflight(self, text=True): pass
                def connect(self): state['connected'] = True
                def close(self): state['closed'] = True
                def check_focus(self): pass
                def prepare_text(self, text=TEXT): state['texts'].append(text)
                def send(self, action):
                    state['actions'].append(action)
                    state['count'] += 1
                    count = state['count']
                    if action == 'Enter':
                        # Setup may only submit after a separately observed paste.
                        assert state['actions'] == ['paste', 'Enter']
                        state['status'].write_text(json.dumps({'status': 'complete', 'generated_lines': 10040}))
                        return
                    kind = 'history_scroll' if action.startswith('wheel') else 'paste'
                    with log.open('a') as stream:
                        stream.write(f'[SlopWorld] latency id={count:032x} status=start kind={kind}\n')
                        stream.write(record(count).replace('kind=keys', f'kind={kind}') + '\n')
                        if count % 10 == 0:
                            stream.write('[SlopWorld] perf context eco=1 terminal=1 sessions=1 width=100 height=100 fps=60 gc0=0;\n')
            clock = Clock()
            with patch.object(bench, 'Backend', FakeBackend), patch.object(bench, 'history_command', setup_command), \
                    patch.object(bench, 'SECONDS', 1), patch.object(bench.time, 'monotonic', clock.time), \
                    patch.object(bench.time, 'sleep', clock.sleep), \
                    patch('builtins.input', return_value='') as prompt, patch('sys.stdout', new_callable=io.StringIO), \
                    patch.object(bench.sys, 'argv', ['bench', '--multiplier', '10', '--log', str(log),
                                                   '--output', str(root / 'out')]):
                bench.main()
            prompt.assert_called_once()
            self.assertEqual(state['texts'], ['bounded setup command', TEXT])
            self.assertEqual(state['actions'][:2], ['paste', 'Enter'])
            self.assertEqual(state['actions'].count('Enter'), 1)
            self.assertEqual(len(state['actions']), 2 + 600 + 100)
            self.assertTrue(state['closed'])
            self.assertFalse((root / 'out' / 'htop.log').exists())
            history = json.loads((root / 'out' / 'history-events.json').read_text())
            typing = json.loads((root / 'out' / 'typing-events.json').read_text())
            self.assertEqual(history['sent_events'], 600)
            self.assertEqual(typing['observed_paste_requests'], 100)
            self.assertEqual(typing['measurement_status'], 'complete')
            report = (root / 'out.md').read_text()
            self.assertIn('history / history_scroll', report)
            self.assertIn('typing / paste', report)
            self.assertIn('same history-filled tab', report)
            self.assertTrue((root / 'out' / 'metrics.csv').exists())
            self.assertTrue((root / 'out' / 'outcomes.csv').exists())

    def test_censored_survivors_are_withheld_and_contexts_kept_separate(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'htop-events.json').write_text(json.dumps({'status': 'complete_with_slip',
                'sent_events': 36000, 'expected_events': 36000}))
            (root / 'htop-latency.json').write_text(json.dumps({'counts': {'samples': 48, 'overflow': 35803},
                'metrics': {'keys': {'input_to_frame_end': {'n': 48, 'p50': 99999999}}}}))
            (root / 'htop-performance.txt').write_text('eco=1 terminal=1 sessions=37 size=100x100 windows=4\n'
                'mean reported FPS=59.43; gen0 collections=24\n  terminal-window: 8.020\n'
                'eco=0 terminal=1 sessions=38 size=100x100 windows=2\n'
                'mean reported FPS=30.00; gen0 collections=5\n  terminal-window: 16.000\n')
            output = root / 'report.md'
            bench.record_phase(root, 'htop', json.loads((root / 'htop-events.json').read_text()),
                               json.loads((root / 'htop-latency.json').read_text()),
                               (root / 'htop-performance.txt').read_text())
            bench.data.write_report(root, output)
            report = output.read_text()
            self.assertIn('withheld', report)
            self.assertNotIn('99999.999', report)
            self.assertIn('| overflow | 35803 |', report)
            self.assertIn('59.43', report)
            self.assertIn('30.00', report)
            self.assertIn('eco=1 terminal=1 size=100x100', report)
            self.assertIn('eco=0 terminal=1 size=100x100', report)
            self.assertNotIn('sessions=', report)
            self.assertIn('sessions=37', (root / 'metrics.csv').read_text())
            self.assertIn('sessions=38', (root / 'metrics.csv').read_text())

    def test_preparation_failure_still_writes_report_without_injection(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            log = root / 'trace.log'
            log.write_text('')
            backend = Mock()
            backend.prepare_text.side_effect = RuntimeError('clipboard unavailable')
            with patch.object(bench, 'Backend', return_value=backend), \
                    patch('sys.stdout', new_callable=io.StringIO), \
                    patch.object(bench.sys, 'argv', ['bench', '--log', str(log), '--output', str(root / 'out')]):
                with self.assertRaisesRegex(RuntimeError, 'clipboard unavailable'):
                    bench.main()
            backend.send.assert_not_called()
            backend.close.assert_called_once()
            self.assertIn('clipboard unavailable', (root / 'out.md').read_text())

    def test_report_only_never_connects_backend(self):
        with tempfile.TemporaryDirectory() as directory:
            bench.data.add_metadata(Path(directory), 'terminal', 'setup', {'status': 'complete'})
            with patch.object(bench, 'Backend') as backend, patch('sys.stdout', new_callable=io.StringIO), \
                    patch.object(bench.sys, 'argv', ['bench', '--report-only', '--output', directory]):
                bench.main()
            backend.assert_not_called()
            Path(directory).with_suffix('.md').unlink()

    def test_history_only_can_fill_an_empty_shell(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            log = root / 'trace.log'
            log.write_text('')
            backend = Mock()
            with patch.object(bench, 'Backend', return_value=backend), \
                    patch.object(bench, 'prepare_history') as prepare, \
                    patch.object(bench, 'run_phase') as run_phase, \
                    patch.object(bench, 'show_report', return_value=root / 'out.md'), \
                    patch('builtins.input') as prompt, patch('sys.stdout', new_callable=io.StringIO), \
                    patch.object(bench.sys, 'argv', ['bench', '--phase', 'history', '--fill-history',
                                                    '--log', str(log), '--output', str(root / 'out')]):
                bench.main()
            prepare.assert_called_once()
            self.assertEqual(run_phase.call_args.args[1], 'history')
            self.assertTrue(run_phase.call_args.kwargs['close_backend'] is False)
            prompt.assert_not_called()
            backend.close.assert_called_once()
            self.assertFalse((root / 'out' / 'typing.log').exists())

    def test_typing_only_can_fill_an_empty_shell(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            log = root / 'trace.log'
            log.write_text('')
            backend = Mock()
            with patch.object(bench, 'Backend', return_value=backend), \
                    patch.object(bench, 'prepare_history') as prepare, \
                    patch.object(bench, 'run_phase') as run_phase, \
                    patch.object(bench, 'show_report', return_value=root / 'out.md'), \
                    patch('builtins.input') as prompt, patch('sys.stdout', new_callable=io.StringIO), \
                    patch.object(bench.sys, 'argv', ['bench', '--phase', 'typing', '--fill-history',
                                                    '--log', str(log), '--output', str(root / 'out')]):
                bench.main()
            prepare.assert_called_once()
            self.assertEqual(run_phase.call_args.args[1], 'typing')
            self.assertFalse(run_phase.call_args.kwargs['close_backend'])
            backend.prepare_text.assert_called_once_with()
            prompt.assert_not_called()
            backend.close.assert_called_once()
            self.assertFalse((root / 'out' / 'history.log').exists())

    def test_report_only_repairs_empty_performance_summary(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            line = ('[SlopWorld] perf context eco=1 terminal=1 sessions=1 width=800 '
                    'height=600 fps=60 gc0=0; root-update calls=60 work=60 ms=3; '
                    'terminal-window calls=60 work=60 ms=30;\n')
            (root / 'typing.log').write_text(line * 2)
            performance = root / 'typing-performance.txt'
            performance.write_text('')
            bench.data.add_metadata(root, 'terminal', 'typing', {'status': 'complete'})
            with patch.object(bench, 'Backend') as backend, patch('sys.stdout', new_callable=io.StringIO), \
                    patch.object(bench.sys, 'argv', ['bench', '--report-only', '--output', directory,
                                                    '--report', str(root / 'report.md')]):
                bench.main()
            backend.assert_not_called()
            self.assertIn('terminal-window: 0.500', performance.read_text())

    def test_paint_reason_counts_backfill_once_and_report_zeros(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            summary = ('eco=1 terminal=1 sessions=1 size=800x600 windows=58\n'
                       '  terminal-cache-broad-repaints: 501 calls\n'
                       '  terminal-cache-broad-rows: 501 calls\n'
                       '  terminal-cache-damage-50-74: 12 calls, 340 rows\n'
                       '  terminal-cache-damage-75-99: 0 calls, 0 rows\n'
                       '  terminal-cache-damage-100: 489 calls, 23961 rows\n')
            bench.record_paint_reasons(root, 'typing', summary)
            bench.record_paint_reasons(root, 'typing', summary)
            rows = bench.data.read(root / 'metrics.csv')
            self.assertEqual(len(rows), 10)
            self.assertEqual({row['metric']: row['value'] for row in rows}, {
                'paint/broad-repaints': '501', 'paint/broad-rows': '501',
                'paint/skipped-revisions': '0', 'paint/missing-damage': '0',
                'paint/damage-50-74-frames': '12', 'paint/damage-50-74-rows': '340',
                'paint/damage-75-99-frames': '0', 'paint/damage-75-99-rows': '0',
                'paint/damage-100-frames': '489', 'paint/damage-100-rows': '23961'})
            report = bench.data.render(root, latest=True)
            self.assertIn('terminal / typing / eco=1 terminal=1 size=800x600 / paint/broad-rows', report)
            self.assertIn('paint/skipped-revisions | total | 0 count', report)

    def test_invalid_phase_never_becomes_usable_from_survivors(self):
        self.assertEqual(measurement_status({'status': 'invalid'}, {'counts': {'samples': 100}}), 'invalid')
        self.assertEqual(measurement_status({'status': 'complete'}, {'counts': {'samples': 10, 'timeout': 1}}), 'censored')


if __name__ == '__main__':
    unittest.main()
