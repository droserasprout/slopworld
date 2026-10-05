#!/usr/bin/env python3
"""Measure the complete UI scheme tables without loading Unity.

Usage: uv run --locked python -m tools.themes.analyze_ui_schemes [--check-warm]

The complete tables are parsed from the shipped per-theme TOML files rather
than copied into this script. ``#rrggbbaa`` values are composited over
``ViewBg`` before their contrast is measured, as a view-surface estimate. Other UI backing surfaces are not measured.
The analysis requires opaque surface colors.
"""

from __future__ import annotations

import argparse
import math
import sys

from tools.themes.validate_themes import load_catalog

SCHEME_IDS = ('slopworld-warm', 'slopworld-cold', 'slopworld-calm', 'onedark')

SURFACES = ('windowBg', 'viewBg', 'popoverBg')
TEXT = ('lead', 'name', 'dim', 'faint', 'off')
SIGNALS = (
    'accent',
    'destructive',
    'bad',
    'warn',
    'yes',
    'global',
    'stateWorking',
    'stateWaiting',
    'stateIdle',
    'stateDown',
)
STRUCTURE = ('edge', 'edgeLit', 'rowBg', 'rowOn', 'hover')
CHECK_ROLES = SURFACES + TEXT + SIGNALS + STRUCTURE
BUTTON_TEXT = ('accentText', 'destructiveText')


def parse_schemes() -> dict[str, dict[str, str | int]]:
    """Load the validated UI catalog while retaining the report's old shape."""

    schemes = {scheme['id']: scheme for scheme in load_catalog()[0]}
    for scheme_id, scheme in schemes.items():
        if any(rgba(scheme[role])[3] != 1 for role in SURFACES):
            raise ValueError(f'{scheme_id}: view-surface analysis requires opaque backgrounds')
    return schemes


def rgba(value: str) -> tuple[float, float, float, float]:
    value = value.removeprefix('#')
    if len(value) == 6:
        value += 'ff'
    if len(value) != 8:
        raise ValueError(f'invalid color: #{value}')
    return tuple(int(value[i : i + 2], 16) / 255 for i in range(0, 8, 2))


def over(foreground: tuple[float, ...], background: tuple[float, ...]) -> tuple[float, ...]:
    alpha = foreground[3]
    return tuple(foreground[i] * alpha + background[i] * (1 - alpha) for i in range(3)) + (1.0,)


def luminance(color: tuple[float, ...]) -> float:
    def linear(channel: float) -> float:
        return channel / 12.92 if channel <= 0.04045 else ((channel + 0.055) / 1.055) ** 2.4

    return 0.2126 * linear(color[0]) + 0.7152 * linear(color[1]) + 0.0722 * linear(color[2])


def contrast(first: tuple[float, ...], second: tuple[float, ...]) -> float:
    high, low = sorted((luminance(first), luminance(second)), reverse=True)
    return (high + 0.05) / (low + 0.05)


def color(scheme: dict[str, str | int], role: str) -> tuple[float, ...]:
    return rgba(scheme[role])


def rendered(scheme: dict[str, str | int], role: str) -> tuple[float, ...]:
    value = color(scheme, role)
    return over(value, color(scheme, 'viewBg')) if value[3] < 1 else value


def metric_name(role: str) -> str:
    return f'{role}.luminance' if role in SURFACES else f'{role}.contrast'


def metrics(scheme: dict[str, str | int]) -> dict[str, float]:
    view = color(scheme, 'viewBg')
    result = {metric_name(role): luminance(color(scheme, role)) for role in SURFACES}
    result.update({metric_name(role): contrast(rendered(scheme, role), view) for role in TEXT + SIGNALS + STRUCTURE})
    result['accentButtonText.contrast'] = contrast(color(scheme, 'accent'), color(scheme, 'accentText'))
    result['destructiveButtonText.contrast'] = contrast(color(scheme, 'destructive'), color(scheme, 'destructiveText'))
    return result


def print_report(schemes: dict[str, dict[str, str | int]]) -> None:
    print('UI schemes: ' + ', '.join(schemes[scheme]['label'] for scheme in SCHEME_IDS))
    print()
    print('scheme                 View L   Accent   EdgeLit     Lead      Dim    Faint')
    print('---------------------  -------  -------  --------  --------  -------  -------')
    for scheme_id in SCHEME_IDS:
        scheme = schemes[scheme_id]
        values = metrics(scheme)
        print(
            f'{scheme["label"]:<21}  {values["viewBg.luminance"]:7.4f}  '
            f'{values["accent.contrast"]:6.2f}:1  '
            f'{values["edgeLit.contrast"]:7.2f}:1  '
            f'{values["lead.contrast"]:7.2f}:1  '
            f'{values["dim.contrast"]:6.2f}:1  '
            f'{values["faint.contrast"]:6.2f}:1'
        )

    print()
    print('Warm vs Cold (relative metric delta)')
    regular = metrics(schemes['slopworld-cold'])
    warm = metrics(schemes['slopworld-warm'])
    for role in CHECK_ROLES:
        metric = metric_name(role)
        delta = (warm[metric] / regular[metric] - 1) * 100
        print(f'  {role:<14} {delta:+6.1f}%')
    for metric, label in (
        ('accentButtonText.contrast', 'accent button text'),
        ('destructiveButtonText.contrast', 'destructive button text'),
    ):
        delta = (warm[metric] / regular[metric] - 1) * 100
        print(f'  {label:<14} {delta:+6.1f}%')


def check_warm(schemes: dict[str, dict[str, str | int]], tolerance: float) -> int:
    regular = metrics(schemes['slopworld-cold'])
    warm = metrics(schemes['slopworld-warm'])
    failures = []
    for role in CHECK_ROLES:
        metric = metric_name(role)
        delta = abs(warm[metric] / regular[metric] - 1)
        if delta > tolerance:
            failures.append(f'{role} {delta:.1%} > {tolerance:.1%}')
    for metric, label in (
        ('accentButtonText.contrast', 'accent button text'),
        ('destructiveButtonText.contrast', 'destructive button text'),
    ):
        delta = abs(warm[metric] / regular[metric] - 1)
        if delta > tolerance:
            failures.append(f'{label} {delta:.1%} > {tolerance:.1%}')

    if failures:
        print('Warm check: FAIL', file=sys.stderr)
        for failure in failures:
            print(f'  {failure}', file=sys.stderr)
        return 1

    print(f'Warm check: PASS (all measured roles within {tolerance:.1%} of SlopWorld Cold)')
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        '--check-warm',
        action='store_true',
        help='fail if Warm changes a measured luminance/contrast metric too much',
    )
    parser.add_argument(
        '--tolerance',
        type=float,
        default=0.05,
        help='relative tolerance for --check-warm (default: 0.05)',
    )
    args = parser.parse_args()
    if not math.isfinite(args.tolerance) or args.tolerance < 0:
        parser.error('--tolerance must be finite and non-negative')

    try:
        schemes = parse_schemes()
        missing = set(SCHEME_IDS) - schemes.keys()
        if missing:
            raise ValueError('missing explicit schemes: ' + ', '.join(sorted(missing)))
    except (OSError, ValueError) as error:
        print(f'analyze_ui_schemes: {error}', file=sys.stderr)
        return 2

    print_report(schemes)
    return check_warm(schemes, args.tolerance) if args.check_warm else 0


if __name__ == '__main__':
    raise SystemExit(main())
