#!/usr/bin/env python3
"""Bake the files view icons from vendored SVG files.

Usage: uv run --locked --extra assets python -m tools.assets.fileicons [--fetch] [--size N]
  --fetch   Download the manifest icons into assets/fileicons/svg again.
  --size    Set the PNG edge length. The default is 32 pixels.

The tool reads assets/fileicons/manifest.toml.
It converts each assets/fileicons/svg/<name>.svg file to a PNG file.
It writes the PNG files to mod/Textures/SlopWorld/FileIcons.
The tool requires Pillow and either rsvg-convert or CairoSVG.

The build uses vendored SVG files and does not require network access.
Use --fetch to update the SVG files from upstream.
Review licenses/material-icon-theme/LICENSE.txt when you update the SVG files.

The tool renders at four times the output size and then applies a box filter.
This process preserves thin details that direct 32-pixel rendering can remove.
tools/assets/roboface.py uses the same process.
"""

import argparse
import io
import os
import subprocess
import sys
import tomllib
import urllib.request

import numpy as np
from PIL import Image

from tools import ROOT

SRC = os.path.join(ROOT, 'assets', 'fileicons', 'svg')
MANIFEST = os.path.join(ROOT, 'assets', 'fileicons', 'manifest.toml')
OUT = os.path.join(ROOT, 'mod', 'Textures', 'SlopWorld', 'FileIcons')

UPSTREAM = 'https://raw.githubusercontent.com/material-extensions/vscode-material-icon-theme/main/icons/{}.svg'

# Use four samples for each output pixel.
# Larger values increase processing time without improving these SVG files.
SUPER = 4


def names():
    with open(MANIFEST, 'rb') as f:
        return list(tomllib.load(f).keys())


def fetch(icons):
    os.makedirs(SRC, exist_ok=True)
    for name in icons:
        url = UPSTREAM.format(name)
        try:
            with urllib.request.urlopen(url, timeout=30) as r:
                body = r.read()
        except Exception as e:
            print(f'  {name}: {e}', file=sys.stderr)
            continue
        with open(os.path.join(SRC, f'{name}.svg'), 'wb') as f:
            f.write(body)
        print(f'  {name}')


def render(path, edge):
    """One SVG at `edge` pixels square, as RGBA."""
    try:
        png = subprocess.run(
            ['rsvg-convert', '-w', str(edge), '-h', str(edge), '-a', path],
            check=True,
            capture_output=True,
        ).stdout
    except FileNotFoundError:
        import cairosvg  # only reached where rsvg-convert is not

        png = cairosvg.svg2png(url=path, output_width=edge, output_height=edge)
    return Image.open(io.BytesIO(png)).convert('RGBA')


def bake(name, size):
    src = os.path.join(SRC, f'{name}.svg')
    if not os.path.exists(src):
        print(f'  {name}: no SVG. Run with --fetch.', file=sys.stderr)
        return False

    big = render(src, size * SUPER)

    # Premultiply alpha before filtering.
    # Direct RGBA averaging can add black edges from transparent pixels.
    a = np.asarray(big, dtype=np.float64) / 255.0
    a[..., :3] *= a[..., 3:4]

    box = a.reshape(size, SUPER, size, SUPER, 4).mean(axis=(1, 3))

    alpha = box[..., 3:4]
    rgb = np.divide(box[..., :3], alpha, out=np.zeros_like(box[..., :3]), where=alpha > 0)
    out = np.concatenate([rgb, alpha], axis=-1)

    img = Image.fromarray(np.clip(out * 255.0 + 0.5, 0, 255).astype(np.uint8), 'RGBA')
    img.save(os.path.join(OUT, f'{name}.png'), optimize=True)
    return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--fetch', action='store_true')
    ap.add_argument('--size', type=int, default=32)
    args = ap.parse_args()

    icons = names()
    if args.fetch:
        print(f'fetching {len(icons)} icons')
        fetch(icons)

    os.makedirs(OUT, exist_ok=True)
    done = sum(bake(n, args.size) for n in icons)
    print(f'{done}/{len(icons)} icons at {args.size}px -> {os.path.relpath(OUT, ROOT)}')
    return 0 if done == len(icons) else 1


if __name__ == '__main__':
    sys.exit(main())
