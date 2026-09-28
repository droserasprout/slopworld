"""Bounded history fixture, executed only in the human-prepared empty host shell."""
import base64
import inspect
import re
import shlex
from pathlib import Path


def history_limit():
    source = Path(__file__).resolve().parents[2] / 'slopd/src/tmux.rs'
    match = re.search(r'SCROLLBACK_LINES:\s*u32\s*=\s*([\d_]+)', source.read_text())
    if not match:
        raise RuntimeError('Cannot determine the daemon scrollback limit')
    return int(match[1].replace('_', ''))


def fill_history(status_path, expected_limit):
    # This function is copied into a standalone command. Keep its imports local.
    import json
    import os
    import subprocess
    import sys
    from pathlib import Path
    status = Path(status_path)
    result = {'status': 'invalid'}
    try:
        pane = os.environ.get('TMUX_PANE')
        if not pane or not os.environ.get('TMUX'):
            raise RuntimeError('History setup requires an empty local host shell inside SlopWorld tmux')
        def query(format_string):
            return subprocess.check_output(
                ['tmux', 'display-message', '-p', '-t', pane, format_string],
                text=True, timeout=3).strip()
        limit, cols, rows, alternate = map(int, query(
            '#{history_limit} #{pane_width} #{pane_height} #{alternate_on}').split())
        if alternate:
            raise RuntimeError('History setup requires a shell, not an alternate-screen application')
        if limit != expected_limit or not 1 <= limit <= 100000:
            raise RuntimeError(f'Pane history limit {limit} differs from supported target {expected_limit}; open a new host terminal')
        if not 2 <= cols <= 4096 or not 1 <= rows <= 4096:
            raise RuntimeError('Unsupported terminal geometry')
        width = min(cols - 1, 120)
        lines = limit + rows + 1
        alphabet = b'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
        table = bytes(alphabet[index % len(alphabet)] for index in range(256))
        # Bound each read. Never emit raw random bytes, escapes, or controls
        # other than line separators. Modulo bias is irrelevant to a render fixture.
        with open('/dev/urandom', 'rb') as random:
            for first in range(0, lines, 128):
                count = min(128, lines - first)
                data = random.read(count * width).translate(table)
                if len(data) != count * width:
                    raise RuntimeError('Short random-device read')
                sys.stdout.buffer.write(b''.join(data[i:i + width] + b'\n'
                                                for i in range(0, len(data), width)))
        sys.stdout.buffer.flush()
        result.update(status='complete', target_history_lines=limit,
                      generated_lines=lines, line_width=width, pane_columns=cols,
                      pane_rows=rows, pane=pane, bytes_written=lines * (width + 1),
                      random_source='/dev/urandom', alphabet='ASCII A-Z a-z 0-9')
    except Exception as error:
        result['error'] = str(error)
    temporary = status.with_suffix('.pending')
    temporary.write_text(json.dumps(result) + '\n')
    temporary.replace(status)


def command(status_path, limit):
    program = inspect.getsource(fill_history) + f'\nfill_history({str(status_path)!r}, {limit!r})\n'
    encoded = base64.b64encode(program.encode()).decode()
    return 'python3 -c ' + shlex.quote(f'import base64;exec(base64.b64decode("{encoded}"))')
