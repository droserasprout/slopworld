"""Compare installed diff pagers in disposable tmux sessions, without a desktop.

Capture polling measures send-to-observed-terminal-grid latency, including tmux RPC
overhead; it does not measure SlopWorld rendering or physical display latency.
"""

import argparse
import json
import math
import os
import pathlib
import platform
import re
import shlex
import shutil
import statistics
import subprocess
import tempfile
import time
from collections import Counter
from typing import Any

from tools import ROOT
from tools.terminal.pager_geometry import parse_capture


def summary(values: list[float]) -> dict[str, float]:
    ordered = sorted(values)
    return {
        'median_ms': round(statistics.median(values), 3),
        'p95_ms': round(ordered[math.ceil(len(values) * 0.95) - 1], 3),
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=pathlib.Path, required=True)
    parser.add_argument('--trials', type=int, default=5)
    parser.add_argument('--scrolls', type=int, default=30)
    parser.add_argument('--lines', type=int, default=10000)
    parser.add_argument('--only', choices=['less', 'bat-less', 'bat-builtin', 'moor', 'most', 'ov'])
    parser.add_argument('--plain-headers', action='store_true', help='Avoid delta Unicode box decorations')
    parser.add_argument(
        '--ascii-fixture', action='store_true', help='Use ASCII source text to isolate Unicode failures'
    )
    args = parser.parse_args()
    if min(args.trials, args.scrolls, args.lines) < 1:
        parser.error('--trials, --scrolls and --lines must be positive')
    bat = shutil.which('bat') or shutil.which('batcat')
    pagers = {'less': 'less -RS --shift=1 --wheel-lines=1'}
    bat_options = (
        '--paging=always --style=plain --decorations=never --color=never --language=txt --strip-ansi=never --wrap=never'
    )
    if bat:
        pagers['bat-less'] = f'{shlex.quote(bat)} {bat_options} --pager {shlex.quote(pagers["less"])}'
        if 'builtin' in subprocess.check_output([bat, '--help'], text=True):
            pagers['bat-builtin'] = f'{shlex.quote(bat)} {bat_options} --pager=builtin'
    if shutil.which('moor'):
        pagers['moor'] = 'moor -no-linenumbers -shift=1'
    if shutil.which('most'):
        pagers['most'] = 'most'
    if shutil.which('ov'):
        pagers['ov'] = 'ov'
    if args.only:
        pagers = {key: value for key, value in pagers.items() if key == args.only}
    report: dict[str, Any] = {
        'commands': pagers,
        'results': {},
        'unavailable': [p for p in ['ov'] if not shutil.which(p)],
        'parameters': vars(args) | {'output': str(args.output)},
        'host': platform.platform(),
    }
    report['versions'] = {}
    for command in ['tmux', 'delta', bat or 'bat', 'less', 'moor', 'most', 'ov']:
        if shutil.which(command):
            version = subprocess.run(
                [command, '-V' if command == 'tmux' else '--version'], text=True, capture_output=True, check=False
            )
            report['versions'][command] = (version.stdout + version.stderr).splitlines()[0]
    with tempfile.TemporaryDirectory(prefix='slopworld-pager-compare-') as directory:
        root = pathlib.Path(directory)
        socket = str(root / 'tmux')

        def tmux(*words: str) -> str:
            return subprocess.check_output(['tmux', '-f', '/dev/null', '-S', socket, *words], text=True)

        def capture(name: str) -> str:
            return tmux('capture-pane', '-p', '-e', '-N', '-t', name + ':')

        def plain(output: str) -> str:
            return '\n'.join(''.join(cell.text for cell in row) for row in parse_capture(output))

        def changed(name: str, before: str, timeout: float = 3) -> str:
            deadline = time.monotonic() + timeout
            while time.monotonic() < deadline:
                output = capture(name)
                if 'panicked at' in output or 'Pane is dead' in output:
                    raise RuntimeError(f'{name}: pager exited or panicked')
                if output != before:
                    return output
                time.sleep(0.001)
            raise TimeoutError(f'{name}: no grid change')

        def start(name: str, command: str) -> float:
            begin = time.perf_counter()
            tmux('new-session', '-d', '-s', name, '-x', '80', '-y', '24', command)
            tmux('set-option', '-w', '-t', name + ':', 'window-size', 'manual')
            tmux('set-option', '-w', '-t', name + ':', 'remain-on-exit', 'on')
            deadline = time.monotonic() + 8
            while time.monotonic() < deadline:
                if 'ROW000' in plain(capture(name)):
                    return (time.perf_counter() - begin) * 1000
                time.sleep(0.001)
            raise TimeoutError(f'{name}: startup marker missing: {plain(capture(name))[:400]}')

        def colored_rows(output: str) -> list[dict[str, Any]]:
            return [
                {
                    'row': i,
                    'text': ''.join(c.text for c in row).rstrip(),
                    'length': len(row),
                    'backgrounds': sorted({str(c.background) for c in row}),
                    'background_counts': dict(Counter(str(c.background) for c in row)),
                    'foregrounds': sorted({str(c.foreground) for c in row}),
                }
                for i, row in enumerate(parse_capture(output))
            ]

        # Long rows have a changing visible prefix at every horizontal offset.
        long = ''.join(f'{i:04d}|' for i in range(100))
        removed = [
            'ROW000 removed short',
            '',
            'let old = "old value";',
            'let minified = "' + 'x' * 3200 + '";',
            'struct AfterTruncatedLine;',
        ]
        added = [
            'ROW000 added short',
            '',
            'let new = "new value";',
            'let long = "' + long + '";',
            'struct AfterLongString;',
            '界 e\u0301 👍🏻 linked',
        ]
        added.extend(f'ROW{i:05d} {long}' for i in range(1, args.lines + 1))
        if args.ascii_fixture:
            added = [line.encode('ascii', errors='replace').decode('ascii') for line in added]
        patch = root / 'fixture.patch'
        patch.write_text(
            'diff --git a/sample.rs b/sample.rs\n--- a/sample.rs\n+++ b/sample.rs\n'
            f'@@ -1,{len(removed)} +1,{len(added)} @@\n'
            + ''.join('-' + line + '\n' for line in removed)
            + ''.join('+' + line + '\n' for line in added)
        )
        delta = (
            'delta --no-gitconfig --paging=never --syntax-theme=ansi --width=80 --true-color=always '
            '--max-line-length=0 --max-syntax-highlighting-length=0 '
            '--minus-style="syntax #3f0001" --plus-style="syntax #002800"'
        )
        if args.plain_headers:
            delta += ' --file-decoration-style=none --hunk-header-decoration-style=none'
        rendered = root / 'rendered.ansi'
        timings = []
        for _ in range(args.trials):
            begin = time.perf_counter()
            with rendered.open('wb') as stream:
                subprocess.run(f'{delta} < {shlex.quote(str(patch))}', shell=True, stdout=stream, check=True)
            timings.append((time.perf_counter() - begin) * 1000)
        with rendered.open('a') as tail_stream:
            tail_stream.write('\nSLOPWORLD_INPUT_END\n')
        report['fixture'] = {
            'patch_bytes': patch.stat().st_size,
            'rendered_bytes': rendered.stat().st_size,
            'delta_command': delta,
            'delta_render': summary(timings),
        }
        env = 'env COLORTERM=truecolor TERM=tmux-256color LESS=-RSc LESSANSIENDCHARS=mK '
        sandbox = (ROOT / 'slopd/src/sandbox/mod.rs').read_text()
        # Match the daemon's joined-emoji policy rather than the host's less defaults.
        char_def = re.search(r'const PANE_LESS_UTFCHARDEF: &str = "([^"]+)";', sandbox)
        assert char_def is not None
        env += 'LESSUTFCHARDEF=' + shlex.quote(char_def[1]) + ' '
        try:
            for index, (label, pager) in enumerate(pagers.items()):
                result: dict[str, Any] = {}
                report['results'][label] = result
                try:
                    for mode in ['rendered', 'pipeline']:
                        samples = []
                        for trial in range(args.trials):
                            name = f'p{index}-{mode}-{trial}'
                            if mode == 'rendered':
                                command = env + pager + ' < ' + shlex.quote(str(rendered))
                            else:
                                command = (
                                    env
                                    + 'DELTA_PAGER='
                                    + shlex.quote(pager)
                                    + ' '
                                    + delta.replace('--paging=never', '--paging=always')
                                    + ' < '
                                    + shlex.quote(str(patch))
                                )
                            samples.append(start(name, command))
                            tmux('kill-session', '-t', name)
                        result[mode + '_startup'] = summary(samples)
                    name = f'p{index}-scroll'
                    start(name, env + pager + ' < ' + shlex.quote(str(rendered)))
                    load_begin = time.perf_counter()
                    deadline = time.monotonic() + 30
                    while 'SLOPWORLD_INPUT_END' not in plain(capture(name)):
                        if time.monotonic() > deadline:
                            raise TimeoutError(f'{label}: input not fully consumed')
                        tmux('send-keys', '-t', name + ':', 'B' if label == 'most' else 'G')
                        time.sleep(0.1)
                    result['load_to_end_ms'] = round((time.perf_counter() - load_begin) * 1000, 3)
                    top_key = 'T' if label == 'most' else ('g' if label == 'bat-builtin' else 'Home')
                    tmux('send-keys', '-t', name + ':', top_key)
                    deadline = time.monotonic() + 3
                    while 'ROW000' not in plain(capture(name)):
                        if time.monotonic() > deadline:
                            raise TimeoutError(f'{label}: cannot return to start')
                        time.sleep(0.01)
                    time.sleep(0.2)
                    tmux('set-option', '-s', 'codepoint-widths[0]', 'U+1F3FB-U+1F3FF=0')
                    if label == 'bat-builtin':
                        before = capture(name)
                        tmux('send-keys', '-t', name + ':', 'C-h')
                        result['builtin_unwrap_rows'] = colored_rows(changed(name, before))
                        time.sleep(0.1)
                    initial = capture(name)
                    result['initial_rows'] = colored_rows(initial)
                    samples = []
                    for _ in range(args.scrolls):
                        begin = time.perf_counter()
                        capture(name)
                        samples.append((time.perf_counter() - begin) * 1000)
                    result['capture_rpc'] = summary(samples)
                    result['vertical'] = {}
                    for direction in ['Down', 'Up']:
                        samples = []
                        for _ in range(args.scrolls):
                            before = capture(name)
                            begin = time.perf_counter()
                            tmux('send-keys', '-t', name + ':', direction)
                            changed(name, before)
                            samples.append((time.perf_counter() - begin) * 1000)
                        result['vertical'][direction] = summary(samples)
                    result['horizontal'] = {}
                    for direction in ['Right', 'Left']:
                        samples = []
                        for _ in range(args.scrolls):
                            before = capture(name)
                            begin = time.perf_counter()
                            tmux('send-keys', '-t', name + ':', direction)
                            changed(name, before)
                            samples.append((time.perf_counter() - begin) * 1000)
                        result['horizontal'][direction] = summary(samples)
                        if direction == 'Right':
                            result['scrolled_rows'] = colored_rows(capture(name))
                    tmux('resize-window', '-t', name + ':', '-x', '100', '-y', '30')
                    time.sleep(0.1)
                    result['resized_rows'] = colored_rows(capture(name))
                    tmux('kill-session', '-t', name)
                    # Log terminal bytes, not images: tmux capture does not export OSC 8 targets.
                    probe = root / 'probe.ansi'
                    probe.write_text(
                        'ROW000 Unicode: 界 e\u0301 👍🏻 tail\n'
                        '\x1b]8;;https://example.invalid/slopworld-probe\x1b\\linked text\x1b]8;;\x1b\\\n'
                        '\x1b[48;2;0;40;0mshort\x1b[K\x1b[0m\n' + ''.join(f'probe {i}\n' for i in range(100))
                    )
                    name = f'p{index}-probe'
                    log = root / (label + '-output')
                    tmux(
                        'new-session',
                        '-d',
                        '-s',
                        name,
                        '-x',
                        '80',
                        '-y',
                        '24',
                        'read gate; ' + env + pager + ' < ' + shlex.quote(str(probe)),
                    )
                    tmux('pipe-pane', '-O', '-t', name + ':', 'cat > ' + shlex.quote(str(log)))
                    tmux('send-keys', '-t', name + ':', 'Enter')
                    time.sleep(0.4)
                    result['probe_rows'] = colored_rows(capture(name))
                    output = log.read_bytes()
                    result['osc8_target_emitted'] = b'https://example.invalid/slopworld-probe' in output
                    result['osc8_control_emitted'] = b'\x1b]8;' in output
                    tmux('kill-session', '-t', name)
                except (TimeoutError, RuntimeError, subprocess.CalledProcessError) as error:
                    result['error'] = str(error)
                    result['failure_capture'] = capture(name)
                    subprocess.run(['tmux', '-S', socket, 'kill-session', '-t', name], check=False, capture_output=True)
            report['environment'] = {key: os.environ.get(key) for key in ['TERM', 'COLORTERM', 'LESS', 'BAT_PAGER']}
        finally:
            subprocess.run(['tmux', '-S', socket, 'kill-server'], check=False, capture_output=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    print(args.output)
    for label, result in report['results'].items():
        print(
            label, json.dumps({k: v for k, v in result.items() if not k.endswith('_rows') and k != 'failure_capture'})
        )


if __name__ == '__main__':
    main()
