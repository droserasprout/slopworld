#!/usr/bin/env python3
"""Bake the loading screen's ASCII font into a texture atlas.

The source font is used only by this build-time tool. The game draws the committed PNG
directly, so loading-screen text does not depend on an OS font installation.
"""

import argparse
import os

from PIL import Image
from PIL import ImageDraw
from PIL import ImageFont

from tools import ROOT

DEFAULT_FONT = os.path.join(ROOT, 'assets', 'fonts', 'clacon2.ttf')
DEFAULT_TEXTURE = os.path.join(ROOT, 'mod', 'Textures', 'SlopWorld', 'LoadingFont.png')

FIRST = 32
LAST = 126
SOURCE_SIZE = 64
CELL_WIDTH = 32
CELL_HEIGHT = 64
COLUMNS = 16
ROWS = (LAST - FIRST + COLUMNS) // COLUMNS
GUTTER = 1
PITCH_WIDTH = CELL_WIDTH + GUTTER * 2
PITCH_HEIGHT = CELL_HEIGHT + GUTTER * 2
RUNTIME_LINE_HEIGHT = 60


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--font', default=DEFAULT_FONT)
    parser.add_argument('--texture', default=DEFAULT_TEXTURE)
    args = parser.parse_args()

    font = ImageFont.truetype(args.font, SOURCE_SIZE)
    font_ascent, font_descent = font.getmetrics()
    if any(abs(font.getlength(chr(codepoint)) - CELL_WIDTH) > 0.01 for codepoint in range(FIRST, LAST + 1)):
        raise ValueError('source font must use a fixed half-em advance for printable ASCII')
    bounds = {chr(codepoint): font.getbbox(chr(codepoint), anchor='ls') for codepoint in range(FIRST, LAST + 1)}
    if min(box[0] for box in bounds.values()) < 0 or max(box[2] for box in bounds.values()) > CELL_WIDTH:
        raise ValueError('printable ASCII glyphs exceed their fixed-width cells')
    ascent = max(font_ascent, max(-box[1] for box in bounds.values()))
    descent = max(font_descent, max(box[3] for box in bounds.values()))
    baseline = ascent
    line_height = ascent + descent
    if line_height > CELL_HEIGHT:
        raise ValueError('printable ASCII glyphs do not fit the atlas cell height')
    if line_height != RUNTIME_LINE_HEIGHT:
        raise ValueError(f'font line height is {line_height}; update runtime line height {RUNTIME_LINE_HEIGHT}')
    atlas = Image.new('RGBA', (COLUMNS * PITCH_WIDTH, ROWS * PITCH_HEIGHT), (255, 255, 255, 0))
    draw = ImageDraw.Draw(atlas)

    for slot, codepoint in enumerate(range(FIRST, LAST + 1)):
        column, row = slot % COLUMNS, slot // COLUMNS
        origin = (column * PITCH_WIDTH + GUTTER, row * PITCH_HEIGHT + GUTTER)
        char = chr(codepoint)
        draw.text((origin[0], origin[1] + baseline), char, font=font, fill=(255, 255, 255, 255), anchor='ls')

    os.makedirs(os.path.dirname(args.texture) or '.', exist_ok=True)
    atlas.save(args.texture, optimize=True)
    print(
        f'wrote ASCII {FIRST}-{LAST} PNG atlas ({atlas.width}x{atlas.height}, '
        f'baseline {baseline}, line {line_height}) to {args.texture}'
    )


if __name__ == '__main__':
    main()
