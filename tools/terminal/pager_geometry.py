"""Exercise pager startup and line navigation in an isolated tmux server without the game."""

import pathlib
import re
import shlex
import shutil
import subprocess
import tempfile
import time
from typing import NamedTuple

from tools import ROOT


class Cell(NamedTuple):
    text: str
    background: tuple[int, ...] | None
    foreground: tuple[int, ...] | None


def parse_capture(output: str) -> list[list[Cell]]:
    """Decode the SGR colors emitted by tmux for our ASCII diff fixture."""
    # capture-pane shares SGR state across rows. Inspect cell backgrounds,
    # including trailing blanks; plain text alone cannot expose lost EL.
    background: tuple[int, ...] | None = None
    foreground: tuple[int, ...] | None = None
    rows: list[list[Cell]] = []
    for line in output.splitlines():
        cells: list[Cell] = []
        # tmux -e can include OSC 8 link metadata. It occupies no grid cells and
        # must not be mistaken for visible text or affect the active SGR colors.
        for match in re.finditer(r'\x1b\[([0-9;]*)m|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)|([^\x1b]+)', line):
            if match[2] is not None:
                cells.extend(Cell(char, background, foreground) for char in match[2])
                continue
            if match[1] is None:
                continue
            codes = [int(code or 0) for code in match[1].split(';')]
            at = 0
            while at < len(codes):
                code = codes[at]
                if code == 0:
                    background = foreground = None
                elif code == 49:
                    background = None
                elif code == 39:
                    foreground = None
                elif 30 <= code <= 37 or 90 <= code <= 97:
                    foreground = (code,)
                elif code in (38, 48) and at + 1 < len(codes):
                    count = 3 if codes[at + 1] == 2 else 1
                    color = tuple(codes[at + 2 : at + 2 + count])
                    if code == 48:
                        background = color
                    else:
                        foreground = color
                    at += 1 + count
                at += 1
        rows.append(cells)
    return rows


def main() -> None:
    with tempfile.TemporaryDirectory(prefix='slopworld-pager-') as directory:
        root = pathlib.Path(directory)
        socket = str(root / 'tmux')

        def tmux(*args: str) -> str:
            return subprocess.check_output(['tmux', '-f', '/dev/null', '-S', socket, *args], text=True)

        def wait_for(name: str, expected: str, *, styled: bool = False) -> list[str]:
            """Wait for a first row, or a marker anywhere in a styled capture."""
            deadline = time.monotonic() + 5
            lines: list[str] = []
            while time.monotonic() < deadline:
                options = ('-e', '-N') if styled else ()
                lines = tmux('capture-pane', '-p', *options, '-t', name + ':').splitlines()
                if styled:
                    ready = any(
                        expected in ''.join(cell.text for cell in row) for row in parse_capture('\n'.join(lines))
                    )
                else:
                    ready = bool(lines) and lines[0] == expected
                if ready:
                    return lines
                time.sleep(0.05)
            location = 'in capture' if styled else 'at row 1'
            raise AssertionError(f'{name}: expected {expected!r} {location}, got {lines!r}')

        # Delay preprocessing to exercise pager startup at the supplied geometry.
        preprocessor = root / 'preprocess'
        preprocessor.write_text('#!/bin/sh\nsleep 0.1\ncat -- "$1"\n')
        preprocessor.chmod(0o700)
        short = root / 'short.txt'
        short.write_text('one\ntwo\nthree\n')
        long = root / 'long.txt'
        long.write_text(''.join(f'line {i}\n' for i in range(1, 101)))
        emoji = root / 'emoji.txt'
        emoji.write_text('👍🏻 light skin\n🧎‍♂️ kneeling\n🤼‍♂️ wrestling\n🧑🏻‍🫯‍🧑🏼 mixed tones\n')
        unicode = ROOT / 'assets/unicode/emoji-test.txt'
        long_emoji = root / 'long-emoji.txt'
        source_rows = [
            line for line in unicode.read_text().splitlines() if line.startswith('1F46F') and 'bunny ears' in line
        ][:18]
        assert len(source_rows) == 18
        long_emoji.write_text('\n'.join(source_rows) + '\n')

        def start(name: str, path: pathlib.Path) -> None:
            # Match the mod's LessEnv and the daemon's supplied startup geometry.
            tmux(
                'new-session',
                '-d',
                '-s',
                name,
                '-x',
                '147',
                '-y',
                '44',
                'env',
                f'LESSOPEN=|{preprocessor} %s',
                'LESS=-Rc',
                'less',
                '--',
                str(path),
            )
            tmux('set-option', '-w', '-t', name + ':', 'window-size', 'manual')

        try:
            start('short', short)
            wait_for('short', 'one')
            tmux('send-keys', '-t', 'short:', 'Down', 'Down', 'Down')
            time.sleep(0.1)
            wait_for('short', 'one')
            start('long', long)
            lines = wait_for('long', 'line 1')
            assert lines[42] == 'line 43', lines
            assert tmux('display-message', '-p', '-t', 'long:', '#{alternate_on}').strip() == '1'
            tmux('send-keys', '-t', 'long:', 'Down', 'Down', 'Down')
            lines = wait_for('long', 'line 4')
            assert lines[42] == 'line 46', lines
            sandbox = (ROOT / 'slopd/src/sandbox/mod.rs').read_text()
            match = re.search(r'const PANE_LESS_UTFCHARDEF: &str = "([^"]+)";', sandbox)
            assert match is not None
            char_def = match.group(1)
            tmux('set-option', '-s', 'codepoint-widths[0]', 'U+1F3FB-U+1F3FF=0')
            tmux(
                'new-session',
                '-d',
                '-s',
                'emoji',
                '-x',
                '80',
                '-y',
                '12',
                'env',
                f'LESSUTFCHARDEF={char_def}',
                'LESS=-Rc',
                'less',
                '--',
                str(emoji),
            )
            lines = wait_for('emoji', '👍🏻 light skin')
            assert lines[1] == '🧎‍♂️ kneeling', lines
            assert lines[2] == '🤼‍♂️ wrestling', lines
            assert lines[3] == '🧑🏻‍🫯‍🧑🏼 mixed tones', lines
            tmux(
                'new-session',
                '-d',
                '-s',
                'long-emoji',
                '-x',
                '155',
                '-y',
                '49',
                'env',
                f'LESSUTFCHARDEF={char_def}',
                'LESS=-RSc',
                'less',
                '--',
                str(long_emoji),
            )
            lines = wait_for('long-emoji', source_rows[0].rstrip())
            assert lines[:18] == [row.rstrip() for row in source_rows], lines[:18]
            print('Pager geometry and emoji sequences passed')
            bat = shutil.which('bat') or shutil.which('batcat')
            if bat:
                # bat receives the file; LESS carries the diagnostic line selector to its pager.
                tmux(
                    'new-session',
                    '-d',
                    '-s',
                    'bat-line',
                    '-x',
                    '80',
                    '-y',
                    '12',
                    'env',
                    'LESS=-RSc +19',
                    bat,
                    '--paging=always',
                    '--style=plain',
                    '--wrap=never',
                    '--pager',
                    'less -RS --shift=1 --wheel-lines=1',
                    '--',
                    str(long),
                )
                wait_for('bat-line', 'line 19')
                print('Bat line navigation passed')
            else:
                print('SKIP: bat line navigation requires bat or batcat')
            if shutil.which('moor'):
                # Match MoorCommand's positional pager handoff: moor does not
                # consume LESSOPEN, so the configured highlighter streams to stdin.
                tmux(
                    'new-session',
                    '-d',
                    '-s',
                    'moor-line',
                    '-x',
                    '80',
                    '-y',
                    '12',
                    'env PAGER_LABEL='
                    + shlex.quote(str(long))
                    + ' sh -c '
                    + shlex.quote(shlex.quote(str(preprocessor)) + ' ' + shlex.quote(str(long)) + ' | exec "$@"')
                    + ' slopworld-pager moor --no-linenumbers=true --shift=1 --terminal-fg --lang=text --mousemode=select +19',
                )
                wait_for('moor-line', 'line 19')
                assert (
                    tmux(
                        'display-message',
                        '-p',
                        '-t',
                        'moor-line:',
                        '#{mouse_any_flag}#{mouse_button_flag}#{mouse_standard_flag}',
                    ).strip()
                    == '000'
                ), 'moor must leave wheel translation to the client'
                print('Moor preprocessed source line navigation passed')
            if bat and shutil.which('delta'):
                diff = root / 'diff.patch'
                diff.write_text(
                    'diff --git a/sample.rs b/sample.rs\n'
                    '--- a/sample.rs\n+++ b/sample.rs\n@@ -1 +1,46 @@\n'
                    '-let old = 0;\n'
                    + '+let description = "'
                    + 'x' * 440
                    + '";\n'
                    + '+struct AfterLongString;\n'
                    + '+let minified = "'
                    + 'x' * 3200
                    + '";\n'
                    + '+struct AfterTruncatedLine;\n'
                    + '+let short = 1;\n+\n'
                    + ''.join(f'+let value_{i} = {i};\n' for i in range(40))
                )
                pager = (
                    f'{shlex.quote(bat)} --paging=always --style=plain --decorations=never '
                    '--color=never --language=txt --strip-ansi=never --wrap=never '
                    "--pager 'less -RS --shift=1 --wheel-lines=1'"
                )
                tmux(
                    'new-session',
                    '-d',
                    '-s',
                    'diff',
                    '-x',
                    '80',
                    '-y',
                    '24',
                    'env COLORTERM=truecolor LESS=-RSc LESSANSIENDCHARS=mK DELTA_PAGER='
                    + shlex.quote(pager)
                    + ' delta --no-gitconfig --paging=always --syntax-theme=ansi'
                    + ' --max-line-length=0 --max-syntax-highlighting-length=0'
                    + ' --minus-style="syntax #3f0001" --plus-style="syntax #002800" < '
                    + shlex.quote(str(diff)),
                )
                output = '\n'.join(wait_for('diff', 'let value_5', styled=True))
                rows = parse_capture(output)
                short_row = next(i for i, row in enumerate(rows) if 'let short' in ''.join(cell.text for cell in row))
                removed = next(row for row in rows if 'let old' in ''.join(cell.text for cell in row))
                for row, color in [
                    (removed, (63, 0, 1)),
                    (rows[short_row], (0, 40, 0)),
                    (rows[short_row + 1], (0, 40, 0)),
                ]:
                    assert len(row) == 80 and row[-1].background == color, row
                for name in ['AfterLongString', 'AfterTruncatedLine']:
                    row = next(row for row in rows if name in ''.join(cell.text for cell in row))
                    column = ''.join(cell.text for cell in row).index('struct')
                    assert row[column].foreground == (35,), (name, row)
                print('Diff backgrounds and long-line syntax passed')
                if shutil.which('moor'):
                    tmux(
                        'new-session',
                        '-d',
                        '-s',
                        'moor-diff',
                        '-x',
                        '80',
                        '-y',
                        '24',
                        'env COLORTERM=truecolor DELTA_PAGER='
                        + shlex.quote('moor --no-linenumbers --shift=1 --terminal-fg --lang=text --mousemode=select')
                        + ' delta --no-gitconfig --paging=always --syntax-theme=ansi'
                        + ' --max-line-length=0 --max-syntax-highlighting-length=0'
                        + ' --minus-style="syntax #3f0001" --plus-style="syntax #002800" < '
                        + shlex.quote(str(diff)),
                    )
                    initial = parse_capture('\n'.join(wait_for('moor-diff', 'let value_5', styled=True)))
                    short_index = next(
                        i for i, row in enumerate(initial) if 'let short' in ''.join(cell.text for cell in row)
                    )
                    removed_index = next(
                        i for i, row in enumerate(initial) if 'let old' in ''.join(cell.text for cell in row)
                    )
                    tmux('send-keys', '-t', 'moor-diff:', *(['Right'] * 30))
                    deadline = time.monotonic() + 5
                    while True:
                        shifted = parse_capture(tmux('capture-pane', '-p', '-e', '-N', '-t', 'moor-diff:'))
                        if 'let short' not in ''.join(cell.text for cell in shifted[short_index]):
                            break
                        assert time.monotonic() < deadline, 'moor did not scroll horizontally'
                        time.sleep(0.05)
                    for index, color in [
                        (removed_index, (63, 0, 1)),
                        (short_index, (0, 40, 0)),
                        (short_index + 1, (0, 40, 0)),
                    ]:
                        row = shifted[index]
                        assert len(row) == 80 and all(cell.background == color for cell in row[1:]), row
                    print('Moor retains horizontally hidden diff backgrounds')
            else:
                print('SKIP: diff backgrounds and long-line syntax require bat/batcat and delta')
        finally:
            subprocess.run(['tmux', '-S', socket, 'kill-server'], check=False)


if __name__ == '__main__':
    main()
