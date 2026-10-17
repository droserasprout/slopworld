"""Own bundled emoji shaping and Cairo-to-Pillow alpha conversion.

Catalogs choose sequences, geometry, and output style. All emoji consumers use
this renderer, including tintable action icons and application artwork.
"""

from collections.abc import Iterable
from collections.abc import Iterator
from functools import lru_cache
from types import ModuleType
from typing import Any

import numpy as np
from PIL import Image

from tools.assets import emoji_font
from tools.assets.artwork import from_premultiplied

PROBE = 256


@lru_cache(maxsize=1)
def cairo_backend() -> tuple[ModuleType, ModuleType, ModuleType]:
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


def _layout(context: Any, size: int) -> Any:
    _, Pango, PangoCairo = cairo_backend()
    layout = PangoCairo.create_layout(context)
    layout.set_font_description(Pango.FontDescription.from_string(f'{emoji_font.FAMILY} {size // 2}'))
    return layout


def supported_sequences(keys: Iterable[str]) -> Iterator[str]:
    """Yield sequences that shape to one bundled-font glyph, preserving input order."""
    cairo, _, _ = cairo_backend()
    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, PROBE, PROBE)
    layout = _layout(cairo.Context(surface), PROBE)
    for key in keys:
        if _sequence_is_supported(layout, key):
            yield key


def _sequence_is_supported(layout: Any, key: str) -> bool:
    """A Noto ligature must shape to one glyph; fallback/component runs are not artwork."""
    layout.set_text(key, -1)
    runs = [run for line in layout.get_lines() for run in line.runs]
    return (
        len(runs) == 1
        and len(runs[0].glyphs.glyphs) == 1
        and bool(runs[0].item.analysis.font.describe().get_family() == emoji_font.FAMILY)
    )


def render(emoji: str, size: int = PROBE) -> Image.Image:
    """Render into a straight-alpha RGBA probe, ready for Pillow operations."""
    cairo, _, PangoCairo = cairo_backend()

    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, size, size)
    ctx = cairo.Context(surface)

    layout = _layout(ctx, size)
    layout.set_text(emoji, -1)

    ink, _logical = layout.get_pixel_extents()
    ctx.translate(-ink.x, -ink.y)
    ctx.set_source_rgb(1.0, 1.0, 1.0)
    PangoCairo.show_layout(ctx, layout)

    # Cairo stores premultiplied ARGB32 data as BGRA bytes. The alpha channel defines the shape.
    surface.flush()
    buf = np.frombuffer(surface.get_data(), dtype=np.uint8).reshape(size, size, 4)
    # RGBa explicitly marks Cairo pixels as premultiplied. Convert exactly once;
    # RGBA resize will premultiply internally before filtering.
    return from_premultiplied(buf[..., [2, 1, 0, 3]])
