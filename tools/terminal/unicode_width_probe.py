"""Measure every entry in the pinned emoji-test file in an isolated tmux.

The probe uses cursor-position replies from an isolated tmux terminal, so widths
come from the terminal itself rather than a second Unicode-width implementation.
"""

import argparse
import json
import os
import select
import subprocess
import sys
import tempfile
import time
import tty
from pathlib import Path

from tools import ROOT


def cases() -> list[dict[str, str | int]]:
    rows: list[dict[str, str | int]] = []
    for number, line in enumerate((ROOT / 'assets/unicode/emoji-test.txt').read_text().splitlines(), 1):
        if not line or line.startswith('#'):
            continue
        points, rest = line.split(';', 1)
        status = rest.split('#', 1)[0].strip()
        text = ''.join(chr(int(point, 16)) for point in points.split())
        rows.append({'line': number, 'status': status, 'text': text})
    return rows


def probe(output: Path) -> None:
    tty.setraw(0)
    rows = cases()
    for row in rows:
        os.write(1, ('\x1b[H\x1b[2J' + str(row['text']) + 'X\x1b[6n').encode())
        reply = b''
        while not reply.endswith(b'R'):
            if not select.select([0], [], [], 5)[0]:
                raise TimeoutError(f'cursor reply missing for {row}')
            reply += os.read(0, 100)
        # DSR is one-based and the trailing X occupies one additional column.
        row['width'] = int(reply.rsplit(b';', 1)[1][:-1]) - 2
    pending = output.with_suffix('.pending')
    pending.write_text(json.dumps(rows))
    pending.replace(output)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--probe', type=Path)
    args = parser.parse_args()
    if args.probe:
        probe(args.probe)
        return
    with tempfile.TemporaryDirectory(prefix='slopworld-unicode-') as directory:
        socket = str(Path(directory) / 'tmux')
        output = Path(directory) / 'widths.json'

        def tmux(*command: str) -> str:
            return subprocess.check_output(['tmux', '-f', '/dev/null', '-S', socket, *command], text=True)

        try:
            tmux('new-session', '-d', '-s', 'bootstrap', 'sleep', '60')
            tmux('set-option', '-s', 'codepoint-widths[0]', 'U+1F3FB-U+1F3FF=0')
            tmux(
                'new-session',
                '-d',
                '-s',
                'probe',
                '-x',
                '200',
                '-y',
                '3',
                sys.executable,
                '-m',
                'tools.terminal.unicode_width_probe',
                '--probe',
                str(output),
            )
            deadline = time.monotonic() + 45
            while not output.exists():
                if time.monotonic() > deadline:
                    raise TimeoutError('Unicode width probe did not complete')
                time.sleep(0.05)
            print(output.read_text())
        finally:
            subprocess.run(['tmux', '-S', socket, 'kill-server'], check=False, capture_output=True)


if __name__ == '__main__':
    main()
