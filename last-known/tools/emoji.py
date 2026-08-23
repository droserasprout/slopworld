#!/usr/bin/env python3
"""Bakes icon PNGs from emoji glyphs.

Usage: python3 tools/emoji.py --emoji 🏆 --name trophy [--size 32] [--out DIR]
       python3 tools/emoji.py --emoji 🏆 --name trophy --emoji 🎯 --name target
       python3 tools/emoji.py --emoji 📻 --name Jukebox --size 128 --color

Renders each emoji from the Noto Color Emoji face via PangoCairo and keeps only
its alpha - the outline of the glyph - writing <out>/<name>.png. A mono icon is
an alpha mask: RGB is white outright, so the caller tints it, exactly the way the
procedural icons here are drawn (GearIcon, TerminalIcon, TabIcons).

The color is stripped by default on purpose: it is the shape that makes an icon,
and a flat white glyph reads on the column's dark rows the way the other icons
do. `--color` keeps the face's own colors instead, which is what a thing
standing on the map wants - a silhouette there is a box, not a jukebox.
Pango is used rather than PIL's FreeType because NotoColorEmoji is a color
font with CBDT/CBLC tables FreeType cannot load, and because the color glyphs
(Pango draws them) are solid where the monochrome fallback-font glyphs are thin
outlines or tofu boxes.

Rendered at a probe bigger than the final edge and resampled down by LANCZOS,
the same trick tools/fileicons.py and tools/roboface.py play: a glyph's
hairlines survive better than a 32px raster would. The content is boxed and
recentred on the output square, because Pango anchors the glyph at its ink
origin, not its visual centre.
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
DEFAULT_OUT = os.path.join(ROOT, "mod", "Textures", "SlopWorld", "FileIcons")

# The color emoji face, found by name so a machine without it says so rather
# than baking a tofu. Fall back on the system's emoji font if the name differs.
EMOJI_FONTS = ["Noto Color Emoji", "NotoColorEmoji", "EmojiOne Color"]

# Probe edge, and the margin of content box around the glyph's own box.
PROBE = 256
BREATHE = 1.08


def _cairo():
    import cairo  # deferred: only needed when actually baking
    import gi
    gi.require_version("Pango", "1.0")
    gi.require_version("PangoCairo", "1.0")
    from gi.repository import Pango, PangoCairo
    return cairo, Pango, PangoCairo


def render(emoji, size):
    """The emoji at `size` square: premultiplied RGBA, float32, channels first."""
    cairo, Pango, PangoCairo = _cairo()

    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, size, size)
    ctx = cairo.Context(surface)

    layout = PangoCairo.create_layout(ctx)
    layout.set_text(emoji, -1)
    desc = None
    for name in EMOJI_FONTS:
        fd = Pango.FontDescription.from_string(f"{name} {size // 2}")
        layout.set_font_description(fd)
        # Ask Pango whether it found the face; if not, merge in the next guess.
        if Pango.FontDescription.get_family(fd):
            desc = fd
            break
    if desc is None:
        desc = Pango.FontDescription.from_string(f"{EMOJI_FONTS[0]} {size // 2}")
    layout.set_font_description(desc)

    ink, _logical = layout.get_pixel_extents()
    ctx.translate(-ink.x, -ink.y)
    ctx.set_source_rgb(1.0, 1.0, 1.0)
    PangoCairo.show_layout(ctx, layout)

    # Cairo ARGB32 is premultiplied BGRA in memory; the alpha is the shape.
    buf = np.frombuffer(surface.get_data(), dtype=np.uint8).reshape(size, size, 4)
    return buf[..., [2, 1, 0, 3]].astype(np.float32) / 255.0


def bake(emoji, name, size, out, color=False):
    try:
        # Premultiplied throughout: it is what survives a LANCZOS resample without
        # the transparent pixels' black bleeding into the edge of the glyph.
        img = render(emoji, PROBE)
    except Exception as e:
        print(f"  {name}: could not render {emoji!r}: {e}", file=sys.stderr)
        return False

    alpha = img[..., 3]
    ys, xs = np.nonzero(alpha > 10 / 255.0)
    if len(ys) == 0:
        print(f"  {name}: {emoji!r} rendered nothing", file=sys.stderr)
        return False

    # The glyph's own box, grown to a square around its centre, clamped to the
    # probe so we never crop outside the rendered surface.
    cy, cx = (ys.min() + ys.max()) / 2, (xs.min() + xs.max()) / 2
    side = int(max(ys.max() - ys.min(), xs.max() - xs.min()) * BREATHE)
    side = min(side, PROBE)  # cap at probe edge
    y0 = max(0, int(cy - side / 2))
    x0 = max(0, int(cx - side / 2))
    # If the crop would fall short of the probe, shift it so it doesn't
    if y0 + side > PROBE: y0 = PROBE - side
    if x0 + side > PROBE: x0 = PROBE - side
    crop = img[y0:y0 + side, x0:x0 + side]

    # The crop may be a hair off-square; pad to the larger edge before resize.
    m = max(crop.shape[:2])
    padded = np.zeros((m, m, 4), dtype=np.float32)
    py, px = (m - crop.shape[0]) // 2, (m - crop.shape[1]) // 2
    padded[py:py + crop.shape[0], px:px + crop.shape[1]] = crop
    small = np.asarray(Image.fromarray(
        np.clip(padded * 255 + 0.5, 0, 255).astype(np.uint8), "RGBA")
        .resize((size, size), Image.LANCZOS), dtype=np.float32) / 255.0

    out_arr = np.zeros((size, size, 4), dtype=np.float32)
    out_arr[..., 3] = small[..., 3]
    if color:
        # Undo the premultiply the resample was done in. A pixel with no alpha has
        # no color to recover, and stays the black it already is.
        lit = small[..., 3] > 0
        out_arr[..., :3][lit] = np.clip(
            small[..., :3][lit] / small[..., 3][lit, None], 0.0, 1.0)
    else:
        # Mono: RGB is white outright, the alpha is the shape.
        out_arr[..., :3] = 1.0

    os.makedirs(out, exist_ok=True)
    Image.fromarray(np.clip(out_arr * 255 + 0.5, 0, 255).astype(np.uint8), "RGBA") \
        .save(os.path.join(out, f"{name}.png"), optimize=True)
    return True


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--emoji", action="append", required=True,
                    help="an emoji to render (repeatable)")
    ap.add_argument("--name", action="append", required=True,
                    help="output basename, one per --emoji (repeatable)")
    ap.add_argument("--size", type=int, default=32,
                    help="edge of the baked PNG, default 32")
    ap.add_argument("--out", default=DEFAULT_OUT,
                    help="output directory")
    ap.add_argument("--color", action="store_true",
                    help="keep the face's own colors instead of an alpha mask")
    args = ap.parse_args()

    if len(args.emoji) != len(args.name):
        print("--emoji and --name must pair up one-to-one", file=sys.stderr)
        return 2

    done = sum(bake(e, n, args.size, args.out, args.color)
               for e, n in zip(args.emoji, args.name))
    print(f"{done}/{len(args.emoji)} {'color' if args.color else 'mono'} icons "
          f"at {args.size}px -> {os.path.relpath(args.out, ROOT)}")
    return 0 if done == len(args.emoji) else 1


if __name__ == "__main__":
    sys.exit(main())
