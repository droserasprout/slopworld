"""Measure pixel differences without resizing or normalizing either reference."""

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image


def dominant_colors(pixels):
    colors, counts = np.unique(pixels.reshape(-1, 4).astype(np.uint8), axis=0, return_counts=True)
    order = np.argsort(counts)[-5:][::-1]
    return [{'rgba': colors[index].tolist(), 'pixels': int(counts[index])} for index in order]


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('native', type=Path)
parser.add_argument('browser', type=Path)
parser.add_argument('output', type=Path)
parser.add_argument('--region', nargs=4, type=int, metavar=('X', 'Y', 'WIDTH', 'HEIGHT'))
args = parser.parse_args()
with Image.open(args.native) as native, Image.open(args.browser) as browser:
    if native.size != browser.size:
        raise ValueError(f'Reference sizes differ: {native.size} versus {browser.size}')
    x, y, width, height = args.region or (0, 0, *native.size)
    if min(x, y) < 0 or min(width, height) <= 0 or x + width > native.width or y + height > native.height:
        raise ValueError('Comparison region is outside the reference')
    box = (x, y, x + width, y + height)
    a = np.asarray(native.convert('RGBA').crop(box), dtype=np.float32)
    b = np.asarray(browser.convert('RGBA').crop(box), dtype=np.float32)
    difference = np.abs(a - b)
    changed = np.any(difference != 0, axis=2)
    report = {
        'native': str(args.native),
        'browser': str(args.browser),
        'imageSize': native.size,
        'region': [x, y, width, height],
        'pixels': int(changed.size),
        'differentPixels': int(changed.sum()),
        'differentFraction': float(changed.mean()),
        'exactMatch': not bool(changed.any()),
        'meanAbsoluteChannelError': float(difference.mean()),
        'rootMeanSquareChannelError': float(np.sqrt(np.mean(difference**2))),
        'maxChannelError': float(difference.max()),
        'nativeDominantColors': dominant_colors(a),
        'browserDominantColors': dominant_colors(b),
        'comparison': 'Unmodified RGBA pixels; matching configuration and content require separate evidence.',
    }
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report, indent=2))
