"""Bake standalone emoji originals and silhouettes from the reviewed manifest.

Usage: just generate-emojis
For an ad hoc bake, pass --emoji/--name pairs, --size, --out, and optionally
--color. Without pairs, assets/emoji/manifest.toml owns the shipped outputs.
"""

import argparse
import sys
import tomllib
from pathlib import Path

from tools import ROOT
from tools.assets.artwork import fit
from tools.assets.artwork import save_png
from tools.assets.artwork import silhouette
from tools.assets.emoji_render import render

MANIFEST = ROOT / 'assets/emoji/manifest.toml'
DEFAULT_OUT = ROOT / 'mod/Textures/SlopWorld/FileIcons'
BREATHE = 1.08


def bake(emoji: str, name: str, size: int, out: str | Path, color: bool = False) -> bool:
    try:
        image = fit(render(emoji), size, breathe=BREATHE)
        save_png(image if color else silhouette(image), Path(out) / f'{name}.png')
    except Exception as error:  # noqa: BLE001 - Report backend failures per glyph.
        print(f'  {name}: could not bake {emoji!r}: {error}', file=sys.stderr)
        return False
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, default=MANIFEST)
    parser.add_argument('--emoji', action='append')
    parser.add_argument('--name', action='append')
    parser.add_argument('--size', type=int, default=32)
    parser.add_argument('--out', type=Path, default=DEFAULT_OUT)
    parser.add_argument('--color', action='store_true')
    args = parser.parse_args()
    if args.size <= 0:
        parser.error('--size must be positive')
    if args.emoji or args.name:
        if len(args.emoji or []) != len(args.name or []):
            parser.error('--emoji and --name must pair up one-to-one')
        results = [bake(e, n, args.size, args.out, args.color) for e, n in zip(args.emoji, args.name, strict=True)]
    else:
        with args.manifest.open('rb') as stream:
            entries = tomllib.load(stream)
        results = []
        for name, entry in entries.items():
            style = entry['style']
            if style not in {'original', 'silhouette'}:
                raise ValueError(f'{name}: unknown emoji style {style!r}')
            target = ROOT / entry['output']
            results.append(bake(entry['emoji'], target.stem, entry['size'], target.parent, style == 'original'))
    print(f'{sum(results)}/{len(results)} emoji icons baked')
    return 0 if all(results) else 1


if __name__ == '__main__':
    sys.exit(main())
