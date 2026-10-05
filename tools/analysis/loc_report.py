#!/usr/bin/env python3
"""Measure tracked Python, C#, and Rust files and write a dated note."""

from __future__ import annotations

import argparse
import datetime as dt
import subprocess
import sys
from pathlib import Path

from tools import ROOT
from tools.analysis.loc import measure

LANGUAGES = ('Python', 'C#', 'Rust')


def collect() -> dict[str, list[int]]:
    """Return [files, lines, blank, comments, code] totals for each language."""
    measured = measure([], languages=set(LANGUAGES))
    return {language: measured.get(language, [0, 0, 0, 0, 0]) for language in LANGUAGES}


def commit_hash() -> str:
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    dirty = subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=no'], cwd=ROOT, text=True)
    return revision + (' (working tree modified)' if dirty else '')


def write_note(
    output_path: Path,
    revision: str,
    generated: dt.datetime,
    rows: dict[str, list[int]],
) -> None:
    lines = [
        '# Lines-of-code snapshot',
        '',
        f'- Generated: {generated.isoformat(timespec="seconds")}',
        f'- Commit: `{revision}`',
        '- Command: `uv run --locked python -m tools.analysis.loc_report`',
        '',
        'Counts cover tracked Python, C#, and Rust files. Build output, ignored files, and '
        'Markdown are excluded. A line containing both code and a trailing comment is counted '
        'as code. Blank lines are separate. Counts measure working-tree contents and are approximate; '
        'the non-Python scanner does not recognize raw strings or nested block comments.',
        '',
        '| Language | Files | Lines | Blank | Comments | Code |',
        '| --- | ---: | ---: | ---: | ---: | ---: |',
    ]
    total = [0, 0, 0, 0, 0]
    for language, row in rows.items():
        lines.append(f'| {language} | {row[0]} | {row[1]} | {row[2]} | {row[3]} | {row[4]} |')
        total = [left + right for left, right in zip(total, row)]
    lines.extend(
        [
            f'| **Total** | **{total[0]}** | **{total[1]}** | **{total[2]}** | **{total[3]}** | **{total[4]}** |',
            '',
            f'Code plus comment lines: **{total[3] + total[4]}**. Code-only lines: **{total[4]}**.',
            '',
        ]
    )
    with output_path.open('x', encoding='utf-8') as output:
        output.write('\n'.join(lines))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        '--output',
        type=Path,
        help='note path (default: dist/loc-YYYYMMDD-HHMMSS.md)',
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    generated = dt.datetime.now(dt.timezone.utc)
    output_path = args.output or ROOT / 'dist' / f'loc-{generated:%Y%m%d-%H%M%S}.md'
    if not output_path.is_absolute():
        output_path = ROOT / output_path
    if output_path.exists():
        print(f'refusing to overwrite existing note: {output_path}', file=sys.stderr)
        return 2

    try:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        rows = collect()
        write_note(output_path, commit_hash(), generated, rows)
    except (OSError, SyntaxError, subprocess.CalledProcessError) as error:
        print(f'loc-report: {error}', file=sys.stderr)
        return 1

    try:
        display_path = output_path.relative_to(ROOT)
    except ValueError:
        display_path = output_path
    print(f'Wrote lines-of-code note: {display_path}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
