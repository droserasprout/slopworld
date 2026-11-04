"""Protect text-symbol coverage and pinned build inputs."""

import hashlib
import re
import tomllib

import numpy as np
from PIL import Image
from PIL import ImageFont

from tools.assets import emoji_atlas
from tools.assets import symbol_atlas
from tools.assets.artwork import packed_ink_bounds


def test_all_text_default_symbols_have_monochrome_glyphs() -> None:
    literals = re.findall(r'"((?:\\U[0-9A-F]{8})+)"', emoji_atlas.DEFAULT_DATA.read_text())
    keys = {value.encode('ascii').decode('unicode_escape') for value in literals}
    expected = {ord(key) for key in keys if len(key) == 1 and key + '\ufe0f' in keys}
    assert len(expected) == 207
    assert set(symbol_atlas.codepoints()) == expected
    choices = symbol_atlas.font_choices()
    fonts = [ImageFont.truetype(str(path), symbol_atlas.RASTER_SIZE) for path in symbol_atlas.FONTS]
    assert [list(choices.values()).count(i) for i in range(3)] == [148, 43, 16]
    assert all(choices[ord(c)] == 0 for c in '⚠☠❤☘✔✍🖥')
    assert choices[ord('☮')] == 1
    assert choices[ord('©')] == 2
    for cp in expected:
        font = fonts[choices[cp]]
        missing = bytes(font.getmask('\u0378'))
        mask = font.getmask(chr(cp))
        assert mask.getbbox() is not None, hex(cp)
        assert bytes(mask) != missing, hex(cp)


def test_packaged_symbol_atlas_matches_metadata_and_has_ink_in_every_slot() -> None:
    points = symbol_atlas.codepoints()
    ink_bounds = []
    with Image.open(symbol_atlas.TEXTURE) as atlas:
        assert atlas.size == (512, 416)
        for slot, _cp in enumerate(points):
            x = slot % symbol_atlas.COLUMNS * symbol_atlas.SIZE
            y = slot // symbol_atlas.COLUMNS * symbol_atlas.SIZE
            cell = atlas.crop((x, y, x + symbol_atlas.SIZE, y + symbol_atlas.SIZE))
            ink_bounds.append(packed_ink_bounds(cell))
            assert cell.getchannel('A').getbbox() is not None
            pixels = np.asarray(cell)
            assert np.all(pixels[:, :, :3][pixels[:, :, 3] > 0] == 255)
    fonts = [ImageFont.truetype(str(path), symbol_atlas.RASTER_SIZE) for path in symbol_atlas.FONTS]
    choices = symbol_atlas.font_choices()
    font_bounds = [symbol_atlas.glyph(fonts[choices[cp]], cp)[1] for cp in points]
    sources = [choices[cp] for cp in points]
    assert symbol_atlas.DATA.read_text() == symbol_atlas.data_source(points, ink_bounds, font_bounds, sources)


def test_font_matches_recorded_source_checksum() -> None:
    source = tomllib.loads(symbol_atlas.FONT.with_name('source.toml').read_text())
    assert hashlib.sha256(symbol_atlas.FONT.read_bytes()).hexdigest() == source['font_sha256']
    directory = symbol_atlas.FONTS[0].parent
    for entry in tomllib.loads((directory / 'source.toml').read_text())['fonts']:
        assert hashlib.sha256((directory / entry['font_file']).read_bytes()).hexdigest() == entry['font_sha256']
