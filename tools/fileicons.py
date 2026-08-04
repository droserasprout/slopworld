#!/usr/bin/env python3
"""Bakes the files view's icons from the vendored SVGs.

Usage: python3 tools/fileicons.py [--fetch] [--size N]
  --fetch   re-download the manifest's icons from upstream into tools/fileicons/svg
  --size    edge of the baked PNG, default 32

Reads tools/fileicons/manifest.toml, rasterizes tools/fileicons/svg/<name>.svg and
writes mod/Textures/SlopWorld/FileIcons/<name>.png. Needs rsvg-convert (librsvg) and
pillow; cairosvg stands in for rsvg-convert if that is what the machine has.

The SVGs are vendored rather than fetched at bake time, so a build is offline and a
bake is the same on every machine whatever upstream did last week. --fetch is how the
set is refreshed on purpose, which is also when the LICENSE beside them wants a look.

Rendered at four times the final edge and box-filtered down rather than rasterized at
32 directly: these are flat vector shapes with hairline detail (a keyhole, a chevron,
lettering inside a page), and librsvg's own antialiasing at 32px drops it. The filter
is the same trick tools/roboface.py plays for the same reason.
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

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SRC = os.path.join(HERE, "fileicons", "svg")
MANIFEST = os.path.join(HERE, "fileicons", "manifest.toml")
OUT = os.path.join(ROOT, "mod", "Textures", "SlopWorld", "FileIcons")

UPSTREAM = (
    "https://raw.githubusercontent.com/material-extensions/"
    "vscode-material-icon-theme/main/icons/{}.svg"
)

# Four samples an edge. Past this the filter is averaging noise librsvg already
# resolved, and the bake takes a minute for nothing.
SUPER = 4


def names():
    with open(MANIFEST, "rb") as f:
        return list(tomllib.load(f).keys())


def fetch(icons):
    os.makedirs(SRC, exist_ok=True)
    for name in icons:
        url = UPSTREAM.format(name)
        try:
            with urllib.request.urlopen(url, timeout=30) as r:
                body = r.read()
        except Exception as e:
            print(f"  {name}: {e}", file=sys.stderr)
            continue
        with open(os.path.join(SRC, f"{name}.svg"), "wb") as f:
            f.write(body)
        print(f"  {name}")


def render(path, edge):
    """One SVG at `edge` pixels square, as RGBA."""
    try:
        png = subprocess.run(
            ["rsvg-convert", "-w", str(edge), "-h", str(edge), "-a", path],
            check=True,
            capture_output=True,
        ).stdout
    except FileNotFoundError:
        import cairosvg  # only reached where rsvg-convert is not

        png = cairosvg.svg2png(url=path, output_width=edge, output_height=edge)
    return Image.open(io.BytesIO(png)).convert("RGBA")


def bake(name, size):
    src = os.path.join(SRC, f"{name}.svg")
    if not os.path.exists(src):
        print(f"  {name}: no svg; run with --fetch", file=sys.stderr)
        return False

    big = render(src, size * SUPER)

    # Premultiply before the filter. Averaging straight RGBA drags the colour of a
    # fully transparent pixel into its neighbours, which on these icons is black,
    # and every edge comes out with a dark rind.
    a = np.asarray(big, dtype=np.float64) / 255.0
    a[..., :3] *= a[..., 3:4]

    box = a.reshape(size, SUPER, size, SUPER, 4).mean(axis=(1, 3))

    alpha = box[..., 3:4]
    rgb = np.divide(box[..., :3], alpha, out=np.zeros_like(box[..., :3]), where=alpha > 0)
    out = np.concatenate([rgb, alpha], axis=-1)

    img = Image.fromarray(np.clip(out * 255.0 + 0.5, 0, 255).astype(np.uint8), "RGBA")
    img.save(os.path.join(OUT, f"{name}.png"), optimize=True)
    return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--fetch", action="store_true")
    ap.add_argument("--size", type=int, default=32)
    args = ap.parse_args()

    icons = names()
    if args.fetch:
        print(f"fetching {len(icons)} icons")
        fetch(icons)

    os.makedirs(OUT, exist_ok=True)
    done = sum(bake(n, args.size) for n in icons)
    print(f"{done}/{len(icons)} icons at {args.size}px -> {os.path.relpath(OUT, ROOT)}")
    return 0 if done == len(icons) else 1


if __name__ == "__main__":
    sys.exit(main())
