#!/usr/bin/env python3
"""Validate the shipped per-theme TOML files without Unity or the game."""

from __future__ import annotations

import re
import sys
import tomllib
from pathlib import Path

from tools import ROOT

THEMES = ROOT / 'mod' / 'Themes'
COLOR = re.compile(r'^#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?$')
ID = re.compile(r'^[a-z0-9][a-z0-9-]*$')

UI_COLORS = (
    'accent',
    'destructive',
    'accentText',
    'destructiveText',
    'checkFace',
    'windowBg',
    'viewBg',
    'popoverBg',
    'panel',
    'offlineBg',
    'scrim',
    'lead',
    'name',
    'dim',
    'faint',
    'off',
    'bad',
    'warn',
    'yes',
    'global',
    'edge',
    'edgeLit',
    'scrollTrough',
    'scrollThumb',
    'scrollThumbHover',
    'scrollThumbHeld',
    'rowBg',
    'rowOn',
    'hover',
    'stateWorking',
    'stateWaiting',
    'stateIdle',
    'stateDown',
    'btnFace',
    'btnHover',
    'btnDown',
    'knob',
)
UI_KEYS = {'version', 'id', 'label', *UI_COLORS}
TERMINAL_KEYS = {
    'version',
    'id',
    'label',
    'fg',
    'bg',
    'cursor',
    'cursorText',
    'selection',
    'link',
    'ansi',
}


def fail(path: Path, message: str) -> None:
    raise ValueError(f'{path.relative_to(ROOT)}: {message}')


def read_file(path: Path, expected: set[str], colors: tuple[str, ...] = ()) -> dict:
    try:
        data = tomllib.loads(path.read_text(encoding='utf-8'))
    except (OSError, tomllib.TOMLDecodeError) as error:
        fail(path, f'invalid TOML: {error}')

    allowed = expected | {'order'}
    if not expected.issubset(data) or set(data) - allowed:
        missing = sorted(expected - set(data))
        extra = sorted(set(data) - allowed)
        details = []
        if missing:
            details.append('missing ' + ', '.join(missing))
        if extra:
            details.append('unknown ' + ', '.join(extra))
        fail(path, '; '.join(details))
    if type(data['version']) is not int or data['version'] != 1:
        fail(path, 'version must be integer 1')
    if 'order' in data and (type(data['order']) is not int or data['order'] < 0):
        fail(path, 'order must be a non-negative integer')
    if not isinstance(data['id'], str) or not ID.fullmatch(data['id']):
        fail(path, 'id must match [a-z0-9][a-z0-9-]*')
    if path.stem != data['id']:
        fail(path, 'filename stem must equal id')
    if not isinstance(data['label'], str) or not data['label']:
        fail(path, 'label must be a non-empty string')
    for key in colors:
        if not isinstance(data[key], str) or not COLOR.fullmatch(data[key]):
            fail(path, f'{key} must be #rrggbb or #rrggbbaa')
    if expected == UI_KEYS:
        for key in ('accent', 'destructive', 'checkFace'):
            if len(data[key]) == 9 and data[key][-2:].lower() != 'ff':
                fail(path, f'{key} must be opaque for contrast-derived text')
    return data


def read_catalog(kind: str, expected: set[str], colors: tuple[str, ...] = ()) -> list[dict]:
    directory = THEMES / kind
    paths = sorted(directory.glob('*.toml')) if directory.is_dir() else []
    if not paths:
        raise ValueError(f'mod/Themes/{kind}: no TOML theme files')
    records = [read_file(path, expected, colors) for path in paths]
    ids = [item['id'] for item in records]
    if len(set(ids)) != len(ids):
        raise ValueError(f'mod/Themes/{kind}: theme IDs must be unique')
    pinned = [item for item in records if 'order' in item]
    unpinned = [item for item in records if 'order' not in item]
    for item in pinned:
        if not item['id'].startswith('slopworld-'):
            raise ValueError(f'{kind} theme {item["id"]}: only SlopWorld themes may set order')
    for item in unpinned:
        if item['id'].startswith('slopworld-'):
            raise ValueError(f'{kind} theme {item["id"]}: SlopWorld themes require order')
    pinned.sort(key=lambda item: item['order'])
    orders = [item['order'] for item in pinned]
    if orders != list(range(len(pinned))):
        raise ValueError(f'mod/Themes/{kind}: pinned order values must be contiguous from 0')
    unpinned.sort(key=lambda item: item['id'])
    return pinned + unpinned


def load_catalog() -> tuple[list[dict], list[dict]]:
    ui = read_catalog('UI', UI_KEYS, UI_COLORS)
    terminal = read_catalog(
        'Terminal',
        TERMINAL_KEYS,
        ('fg', 'bg', 'cursor', 'cursorText', 'selection', 'link'),
    )
    for theme in terminal:
        path = THEMES / 'Terminal' / (theme['id'] + '.toml')
        ansi = theme['ansi']
        if not isinstance(ansi, list) or len(ansi) != 16:
            fail(path, 'ansi must contain exactly 16 colors')
        if any(not isinstance(value, str) or not COLOR.fullmatch(value) for value in ansi):
            fail(path, 'ansi entries must be #rrggbb or #rrggbbaa')

    terminal_ids = {theme['id'] for theme in terminal}
    for scheme in ui:
        if scheme['id'] not in terminal_ids:
            raise ValueError(f'UI scheme {scheme["id"]} has no terminal theme for Match UI')
    return ui, terminal


def main() -> int:
    try:
        ui, terminal = load_catalog()
    except (OSError, ValueError) as error:
        print(f'validate_themes: FAIL: {error}', file=sys.stderr)
        return 1
    print(f'validated {len(ui)} UI themes and {len(terminal)} terminal themes')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
