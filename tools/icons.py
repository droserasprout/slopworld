#!/usr/bin/env python3
"""Bakes the mod's action icons out of a Nerd Font's Codicons.

Usage: python3 tools/icons.py [--size N] [--margin N] [--font PATH] [--report]
  --size    edge of the baked PNG, default 64
  --margin  pixels of clear space around the largest glyph, default 2
  --font    a Nerd Font .ttf, default the first of FONTS that exists
  --report  print each glyph's drawn size and ink coverage instead of staying quiet

Reads tools/icons/manifest.toml and writes mod/Textures/SlopWorld/Icons/<slot>.png.
Needs pillow and a Nerd Font installed - on Arch that is one of the ttf-*-nerd
packages. Unlike tools/fileicons.py this wants no rsvg-convert: the glyphs are
outlines in a font and FreeType, which pillow already carries, is the rasterizer.

The font is NOT vendored the way tools/fileicons.py vendors its SVGs - it is four
megabytes to hold twenty glyphs. The PNGs are committed instead, so a build never
needs the font and only a rebake does.

Codicons is VS Code's set, drawn on a 16px editor grid with consistent weight. The fixed
set provides a common scale, while glyph sizes still vary by design. The
glyphs are NOT all the same size and are not meant to be - `circle-filled` is an inline
status dot and `terminal` fills its cell - so this scales every glyph by ONE factor,
the one that fits the largest of them, rather than fitting each into the box
separately. Fitting each would flatten exactly the relative sizing the set was drawn
with, and is also the hand-tuning the nine procedural icon classes this replaced had to
do by eye. --report prints the sizes so that agreement can be checked.

Rendered several times larger than the final edge and box-filtered down rather than
rasterized at 64 directly, the trick tools/fileicons.py explains: FreeType's own
antialiasing at 64px drops the thin strokes these are mostly made of.

The output is an alpha mask: RGB is white outright and the alpha is the shape, the way
every icon in this mod is drawn, because the caller tints it with GUI.color.
"""

import argparse
import os
import sys
import tomllib

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
MANIFEST = os.path.join(HERE, "icons", "manifest.toml")
OUT = os.path.join(ROOT, "mod", "Textures", "SlopWorld", "Icons")

# Any Nerd Font carries the same private use area, so this is only about finding one.
# The Mono and Propo cuts of a face hold the same glyphs at the same outlines.
FONTS = [
    "/usr/share/fonts/TTF/FiraCodeNerdFont-Regular.ttf",
    "/usr/share/fonts/TTF/JetBrainsMonoNerdFont-Regular.ttf",
    "/usr/share/fonts/nerd-fonts/SymbolsNerdFont-Regular.ttf",
    os.path.expanduser("~/.local/share/fonts/SymbolsNerdFont-Regular.ttf"),
]

# The em to render a glyph at before it is fitted and downsampled. Large enough that the
# fit is a shrink for every glyph in the manifest, so nothing is ever scaled up.
EM = 256


def find_font(explicit):
    if explicit:
        return explicit
    for p in FONTS:
        if os.path.exists(p):
            return p
    print("no Nerd Font found; pass --font, or install one (Arch: ttf-firacode-nerd)",
          file=sys.stderr)
    sys.exit(2)


def ink(font, code):
    """The glyph's pixels, cropped to what it actually draws. None if it draws nothing."""
    # Three ems of canvas with the pen an em in, so a glyph that reaches outside its
    # advance width - Codicons has several - is not clipped before it is measured.
    img = Image.new("L", (EM * 3, EM * 3), 0)
    ImageDraw.Draw(img).text((EM, EM), chr(code), font=font, fill=255)
    a = np.asarray(img)
    ys, xs = np.nonzero(a)
    if len(xs) == 0:
        return None
    return a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", type=int, default=64)
    ap.add_argument("--margin", type=int, default=2)
    ap.add_argument("--font")
    ap.add_argument("--report", action="store_true")
    args = ap.parse_args()

    with open(MANIFEST, "rb") as f:
        table = tomllib.load(f)

    path = find_font(args.font)
    font = ImageFont.truetype(path, EM)

    drawn = {}
    for slot, entry in sorted(table.items()):
        a = ink(font, int(entry["code"], 16))
        if a is None:
            print(f"  {slot}: {entry['glyph']} (U+{entry['code'].upper()}) "
                  f"is not in {os.path.basename(path)}", file=sys.stderr)
            continue
        drawn[slot] = a

    if not drawn:
        return 1

    # One scale for the whole set, off the glyph that needs the most room.
    box = args.size - 2 * args.margin
    span = max(max(a.shape) for a in drawn.values())

    os.makedirs(OUT, exist_ok=True)
    report = {}
    for slot, a in drawn.items():
        h, w = a.shape
        s = box / span
        # Resized at 4x the final edge and box-filtered down, not straight to size.
        big_w, big_h = max(1, round(w * s)) * 4, max(1, round(h * s)) * 4
        small = Image.fromarray(a).resize((big_w, big_h), Image.LANCZOS)
        small = np.asarray(small, dtype=np.float64).reshape(
            big_h // 4, 4, big_w // 4, 4).mean(axis=(1, 3)) / 255.0

        # Centred in the square, rounding the odd pixel to the top-left the way the rest
        # of the chrome does.
        out = np.ones((args.size, args.size, 4), dtype=np.float64)
        out[..., 3] = 0.0
        oh, ow = small.shape
        y, x = (args.size - oh) // 2, (args.size - ow) // 2
        out[y:y + oh, x:x + ow, 3] = np.clip(small, 0.0, 1.0)

        Image.fromarray(np.clip(out * 255.0 + 0.5, 0, 255).astype(np.uint8), "RGBA") \
            .save(os.path.join(OUT, f"{slot}.png"), optimize=True)
        report[slot] = (ow, oh, float(out[..., 3].sum() / (args.size ** 2)))

    if args.report:
        print(f"{os.path.basename(path)}, {span}/{EM} em fitted to {box}/{args.size}px")
        for slot, (w, h, cover) in sorted(report.items(), key=lambda kv: -kv[1][2]):
            print(f"  {slot:<10} {w:>2}x{h:<2}  {cover * 100:5.1f}%  "
                  f"{table[slot]['glyph']}")

    print(f"{len(drawn)}/{len(table)} icons at {args.size}px -> "
          f"{os.path.relpath(OUT, ROOT)}")
    return 0 if len(drawn) == len(table) else 1


if __name__ == "__main__":
    sys.exit(main())
