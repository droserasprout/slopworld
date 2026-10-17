"""Own straight-alpha artwork fitting, silhouettes, and PNG publication.

Renderers must convert their native alpha representation before handing images
here. Pillow RGBA resizing handles premultiplication internally. Catalogs own
sizing policy; font families with a common scale must retain that scale.
"""

import io
from pathlib import Path

import numpy as np
from numpy.typing import NDArray
from PIL import Image

from tools.generated_files import write_if_changed


def from_premultiplied(pixels: NDArray[np.uint8]) -> Image.Image:
    """Convert byte RGBA pixels from a premultiplied renderer exactly once."""
    return Image.fromarray(pixels, 'RGBa').convert('RGBA')


def crop_ink(image: Image.Image, threshold: int = 0) -> Image.Image:
    """Crop alpha bounds; reject empty artwork rather than publishing a blank slot."""
    alpha = image.getchannel('A')
    bounds = alpha.point(lambda value: 255 if value > threshold else 0).getbbox()
    if bounds is None:
        raise ValueError('artwork rendered no visible ink')
    return image.crop(bounds)


def fit(image: Image.Image, size: int, *, margin: int = 0, breathe: float = 1.0) -> Image.Image:
    """Center ink within a square, with symmetric padding even at probe edges."""
    if size <= 0 or margin < 0 or 2 * margin >= size or breathe < 1:
        raise ValueError('size, margin, and breathe must leave a positive drawing area')
    ink = crop_ink(image, threshold=10)
    scale = (size - 2 * margin) / (max(ink.size) * breathe)
    dimensions = (max(1, round(ink.width * scale)), max(1, round(ink.height * scale)))
    resized = ink.resize(dimensions, Image.Resampling.LANCZOS)
    canvas = Image.new('RGBA', (size, size))
    canvas.paste(resized, ((size - resized.width) // 2, (size - resized.height) // 2))
    return canvas


def silhouette(image: Image.Image) -> Image.Image:
    """Replace color with white while preserving every alpha value and placement."""
    result = Image.new('RGBA', image.size, 'white')
    result.putalpha(image.getchannel('A'))
    return result


def png_bytes(image: Image.Image) -> bytes:
    output = io.BytesIO()
    image.save(output, format='PNG', optimize=True)
    return output.getvalue()


def save_png(image: Image.Image, path: Path) -> None:
    write_if_changed(path, png_bytes(image))
