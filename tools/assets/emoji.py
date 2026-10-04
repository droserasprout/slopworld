#!/usr/bin/env python3
"""Bake icon PNG files from emoji glyphs.

Usage: uv run --locked --extra assets python -m tools.assets.emoji --emoji 🏆 --name trophy [--size 32] [--out DIR]
       uv run --locked --extra assets python -m tools.assets.emoji --emoji 🏆 --name trophy --emoji 🎯 --name target
       uv run --locked --extra assets python -m tools.assets.emoji --emoji 📻 --name Jukebox --size 128 --color

The tool renders each emoji from bundled Noto Color Emoji through PangoCairo.
It writes each result to <out>/<name>.png.
For monochrome output, the tool keeps the glyph alpha and sets each RGB channel to white.
The caller can then tint the icon like other procedural icons.

By default, the tool removes color so the icon matches other sidebar icons.
Use `--color` to preserve the glyph colors for map objects.
Pillow FreeType cannot load the CBDT and CBLC tables in Noto Color Emoji.
Pango renders the color glyphs; fallback fonts may produce thin outlines or missing-glyph boxes.

The tool renders above the output size and then applies a LANCZOS filter.
This process preserves thin strokes better than direct 32-pixel rendering.
The tool centers the content because Pango positions a glyph from its ink origin.
"""

import argparse
import os
import sys
from functools import lru_cache

import numpy as np
from PIL import Image

from tools import ROOT
from tools.assets import emoji_font

DEFAULT_OUT = os.path.join(ROOT, 'mod', 'Textures', 'SlopWorld', 'FileIcons')

# Set the probe edge and the margin around the glyph bounds.
PROBE = 256
BREATHE = 1.08


@lru_cache(maxsize=1)
def _cairo():
    import cairo  # deferred: only needed when actually baking
    import gi

    gi.require_version('Pango', '1.0')
    gi.require_version('PangoCairo', '1.0')
    from gi.repository import Pango
    from gi.repository import PangoCairo

    emoji_font.configure_font()
    # A fresh map avoids any fonts cached before the private configuration.
    PangoCairo.FontMap.set_default(PangoCairo.FontMap.new())
    return cairo, Pango, PangoCairo


def render(emoji, size):
    """The emoji at `size` square: premultiplied RGBA, float32, channels first."""
    cairo, Pango, PangoCairo = _cairo()

    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, size, size)
    ctx = cairo.Context(surface)

    layout = PangoCairo.create_layout(ctx)
    layout.set_text(emoji, -1)
    desc = Pango.FontDescription.from_string(f'{emoji_font.FAMILY} {size // 2}')
    layout.set_font_description(desc)

    ink, _logical = layout.get_pixel_extents()
    ctx.translate(-ink.x, -ink.y)
    ctx.set_source_rgb(1.0, 1.0, 1.0)
    PangoCairo.show_layout(ctx, layout)

    # Cairo stores premultiplied ARGB32 data as BGRA bytes. The alpha channel defines the shape.
    buf = np.frombuffer(surface.get_data(), dtype=np.uint8).reshape(size, size, 4)
    return buf[..., [2, 1, 0, 3]].astype(np.float32) / 255.0


def bake(emoji, name, size, out, color=False):
    try:
        # Keep premultiplied color during the LANCZOS filter.
        # This prevents transparent black pixels from darkening the glyph edge.
        img = render(emoji, PROBE)
    except Exception as e:
        print(f'  {name}: could not render {emoji!r}: {e}', file=sys.stderr)
        return False

    alpha = img[..., 3]
    ys, xs = np.nonzero(alpha > 10 / 255.0)
    if len(ys) == 0:
        print(f'  {name}: {emoji!r} rendered nothing', file=sys.stderr)
        return False

    # Expand the glyph bounds to a centered square.
    # Limit the square to the probe surface.
    cy, cx = (ys.min() + ys.max()) / 2, (xs.min() + xs.max()) / 2
    side = int(max(ys.max() - ys.min(), xs.max() - xs.min()) * BREATHE)
    side = min(side, PROBE)  # cap at probe edge
    y0 = max(0, int(cy - side / 2))
    x0 = max(0, int(cx - side / 2))
    # If the crop would fall short of the probe, shift it so it doesn't
    if y0 + side > PROBE:
        y0 = PROBE - side
    if x0 + side > PROBE:
        x0 = PROBE - side
    crop = img[y0 : y0 + side, x0 : x0 + side]

    # The crop may be a hair off-square. Pad to the larger edge before resize.
    m = max(crop.shape[:2])
    padded = np.zeros((m, m, 4), dtype=np.float32)
    py, px = (m - crop.shape[0]) // 2, (m - crop.shape[1]) // 2
    padded[py : py + crop.shape[0], px : px + crop.shape[1]] = crop
    small = (
        np.asarray(
            Image.fromarray(np.clip(padded * 255 + 0.5, 0, 255).astype(np.uint8), 'RGBA').resize(
                (size, size), Image.LANCZOS
            ),
            dtype=np.float32,
        )
        / 255.0
    )

    out_arr = np.zeros((size, size, 4), dtype=np.float32)
    out_arr[..., 3] = small[..., 3]
    if color:
        # Undo the premultiply the resample was done in. A pixel with no alpha has
        # no color to recover, and stays the black it already is.
        lit = small[..., 3] > 0
        out_arr[..., :3][lit] = np.clip(small[..., :3][lit] / small[..., 3][lit, None], 0.0, 1.0)
    else:
        # Mono: RGB is white outright, the alpha is the shape.
        out_arr[..., :3] = 1.0

    os.makedirs(out, exist_ok=True)
    Image.fromarray(np.clip(out_arr * 255 + 0.5, 0, 255).astype(np.uint8), 'RGBA').save(
        os.path.join(out, f'{name}.png'), optimize=True
    )
    return True


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--emoji', action='append', required=True, help='an emoji to render (repeatable)')
    ap.add_argument('--name', action='append', required=True, help='output basename, one per --emoji (repeatable)')
    ap.add_argument('--size', type=int, default=32, help='edge of the baked PNG, default 32')
    ap.add_argument('--out', default=DEFAULT_OUT, help='output directory')
    ap.add_argument('--color', action='store_true', help="keep the face's own colors instead of an alpha mask")
    args = ap.parse_args()

    if len(args.emoji) != len(args.name):
        print('--emoji and --name must pair up one-to-one', file=sys.stderr)
        return 2

    done = sum(bake(e, n, args.size, args.out, args.color) for e, n in zip(args.emoji, args.name))
    print(
        f'{done}/{len(args.emoji)} {"color" if args.color else "mono"} icons '
        f'at {args.size}px -> {os.path.relpath(args.out, ROOT)}'
    )
    return 0 if done == len(args.emoji) else 1


if __name__ == '__main__':
    sys.exit(main())
