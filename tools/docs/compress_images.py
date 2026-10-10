"""Generate heavily compressed JPEG copies of book PNGs, preserving originals."""

import argparse
from io import BytesIO
from pathlib import Path

from PIL import Image

from tools import ROOT
from tools.generated_files import write_if_changed


def compress_image(source: Path, quality: int) -> None:
    with Image.open(source) as image:
        rgba = image.convert('RGBA')
        # JPEG has no alpha channel; flatten transparent artwork onto white.
        background = Image.new('RGBA', rgba.size, 'white')
        rgb = Image.alpha_composite(background, rgba).convert('RGB')
        output = BytesIO()
        rgb.save(output, format='JPEG', quality=quality, subsampling=2, optimize=True, progressive=True)
    destination = source.with_suffix('.jpg')
    content = output.getvalue()
    write_if_changed(destination, content)
    print(f'{source.relative_to(ROOT)} -> {destination.relative_to(ROOT)} ({len(content):,} bytes)')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--quality', type=int, default=15, help='JPEG quality from 1 to 95 (default: 15)')
    args = parser.parse_args()
    if not 1 <= args.quality <= 95:
        parser.error('--quality must be between 1 and 95')
    sources = sorted(path for path in (ROOT / 'docs/src').rglob('*') if path.suffix.lower() == '.png')
    for source in sources:
        compress_image(source, args.quality)
    print(f'Converted {len(sources)} PNG(s); originals and document links are unchanged.')


if __name__ == '__main__':
    main()
