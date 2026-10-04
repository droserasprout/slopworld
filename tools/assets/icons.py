#!/usr/bin/env python3
"""Bake the mod action icons from a Nerd Font Codicons set.

Usage: python3 tools/assets/icons.py [--size N] [--margin N] [--font PATH] [--report]
  --size    Set the PNG edge length. The default is 64 pixels.
  --margin  Set the clear margin. The default is 2 pixels.
  --font    Override the bundled Symbols Nerd Font TTF file.
  --report  Print the size and ink coverage of each glyph.

Reads assets/icons/manifest.toml and writes mod/Textures/SlopWorld/Icons/<slot>.png.
The tool requires Pillow and NumPy. The default font is bundled under
assets/fonts/nerd-symbols/, with its pinned source and license notices.
Pillow uses its included FreeType library to render the font outlines.

The repository includes the generated PNG files, so normal builds do not run the baker.

The Codicons set uses a 16-pixel editor grid with a consistent stroke weight.
The glyph sizes differ by design.
For example, `circle-filled` is a small status dot, but `terminal` fills its cell.
The tool applies one scale factor to all glyphs.
It selects the factor that fits the largest glyph.
This preserves the designed size differences between glyphs.
Use --report to inspect the output sizes.

The tool renders above the output size and then applies a box filter.
This process preserves thin strokes that direct 64-pixel rendering can remove.

The output is an alpha mask with white RGB channels.
The caller uses GUI.color to apply the required color.
"""

import argparse
import os
import sys
import tomllib

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
MANIFEST = os.path.join(ROOT, "assets", "icons", "manifest.toml")
OUT = os.path.join(ROOT, "mod", "Textures", "SlopWorld", "Icons")

DEFAULT_FONT = os.path.join(ROOT, "assets", "fonts", "nerd-symbols",
                            "SymbolsNerdFont-Regular.ttf")

# Render each glyph at this size before fitting and downsampling it.
# This size lets the tool reduce every manifest glyph instead of enlarging it.
EM = 256


def ink(font, code):
    """Return the cropped glyph pixels, or None if the glyph draws nothing."""
    # Use a three-em canvas and put the pen one em from each leading edge.
    # This prevents clipping for glyphs that extend beyond their advance width.
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
    ap.add_argument("--font", default=DEFAULT_FONT)
    ap.add_argument("--report", action="store_true")
    args = ap.parse_args()

    with open(MANIFEST, "rb") as f:
        table = tomllib.load(f)

    path = args.font
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
