"""Protect alpha conversion and geometry using synthetic pixels, without font backends."""

from pathlib import Path

import numpy as np
import pytest
from PIL import Image

from tools.assets import emoji
from tools.assets import emoji_atlas
from tools.assets.artwork import fit
from tools.assets.artwork import from_premultiplied
from tools.assets.artwork import save_png
from tools.assets.artwork import silhouette


def test_premultiplied_color_is_converted_before_resampling() -> None:
    # This is Cairo's representation of half-transparent red. Treating it as
    # straight RGBA before resize incorrectly darkens the atlas edges.
    pixels = np.zeros((8, 8, 4), dtype=np.uint8)
    pixels[2:6, 2:6] = [128, 0, 0, 128]
    rendered = from_premultiplied(pixels)
    cell = np.asarray(emoji_atlas.bake_one(rendered))
    visible = cell[..., 3] > 16
    assert np.all(cell[..., 0][visible] >= 250)
    assert np.all(cell[..., 1:3][visible] == 0)
    assert cell[..., 3].max() < 255


def test_original_and_silhouette_have_identical_alpha_and_geometry() -> None:
    pixels = np.zeros((12, 8, 4), dtype=np.uint8)
    pixels[0:10, 0:3] = [20, 80, 200, 180]
    original = fit(Image.fromarray(pixels), 32, margin=2, breathe=1.08)
    mono = np.asarray(silhouette(original))
    np.testing.assert_array_equal(mono[..., 3], np.asarray(original)[..., 3])
    assert np.all(mono[..., :3] == 255)
    bounds = original.getchannel('A').getbbox()
    assert bounds is not None
    left, top, right, bottom = bounds
    assert abs(left - (32 - right)) <= 1
    assert abs(top - (32 - bottom)) <= 1
    assert top >= 2 and bottom <= 30


def test_single_pixel_ink_survives_and_empty_artwork_fails() -> None:
    image = Image.new('RGBA', (3, 3))
    image.putpixel((0, 0), (255, 255, 255, 255))
    assert fit(image, 16).getchannel('A').getbbox() == (0, 0, 16, 16)
    with pytest.raises(ValueError, match='no visible ink'):
        fit(Image.new('RGBA', (3, 3)), 16)


def test_standalone_and_atlas_use_identical_fitting(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    rendered = Image.new('RGBA', (8, 8), (255, 20, 80, 128))
    monkeypatch.setattr(emoji, 'render', lambda _key: rendered)
    assert emoji.bake('x', 'color', 32, tmp_path, color=True)
    assert emoji.bake('x', 'mono', 32, tmp_path)
    with Image.open(tmp_path / 'color.png') as original, Image.open(tmp_path / 'mono.png') as mono:
        np.testing.assert_array_equal(original, emoji_atlas.bake_one(rendered))
        np.testing.assert_array_equal(original.getchannel('A'), mono.getchannel('A'))


def test_identical_png_publication_preserves_mtime(tmp_path: Path) -> None:
    path = tmp_path / 'icon.png'
    image = Image.new('RGBA', (2, 2), 'white')
    save_png(image, path)
    before = path.stat().st_mtime_ns
    save_png(image, path)
    assert path.stat().st_mtime_ns == before
