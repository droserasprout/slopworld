"""Check text asset metadata and loading-tip glyph coverage without image assets."""

import pathlib
import re
import tempfile
import unittest

from PIL import Image

from tools import ROOT
from tools.assets import emoji_atlas
from tools.assets.artwork import packed_ink_bounds


class TextSpriteMetadataTests(unittest.TestCase):
    def test_packaged_metadata_round_trips_through_writer(self) -> None:
        source = pathlib.Path(emoji_atlas.DEFAULT_DATA).read_text(encoding='utf-8')
        literals = re.findall(r'"((?:\\U[0-9A-F]{8})+)"', source)
        keys = [literal.encode('ascii').decode('unicode_escape') for literal in literals]
        self.assertTrue(keys)
        self.assertIn('✨', keys)
        self.assertIn('🍰', keys)
        self.assertIn('👩‍💻', keys)
        self.assertIn('👍🏽', keys)
        self.assertIn('🇺🇾', keys)
        self.assertIn('1️⃣', keys)
        self.assertNotIn('#', keys)
        self.assertNotIn('\u200d', keys)
        self.assertEqual(len(keys), len(set(keys)))
        row_match = re.search(r'AtlasRows = (\d+)', source)
        assert row_match is not None
        rows = int(row_match.group(1))
        self.assertLessEqual(len(keys), rows * emoji_atlas.COLUMNS)
        ink_bounds = []
        with Image.open(emoji_atlas.DEFAULT_TEXTURE) as atlas:
            for slot in range(len(keys)):
                x = slot % emoji_atlas.COLUMNS * emoji_atlas.SIZE
                y = slot // emoji_atlas.COLUMNS * emoji_atlas.SIZE
                ink_bounds.append(packed_ink_bounds(atlas.crop((x, y, x + emoji_atlas.SIZE, y + emoji_atlas.SIZE))))
        with tempfile.TemporaryDirectory() as directory:
            output = pathlib.Path(directory) / 'TextSpriteData.cs'
            emoji_atlas.write_data(str(output), keys, rows, ink_bounds)
            self.assertEqual(source, output.read_text(encoding='utf-8'))

    def test_font_charset_excludes_invisible_sequence_components(self) -> None:
        from unittest.mock import patch

        with patch(
            'tools.assets.emoji_atlas.subprocess.check_output', return_value='23 200d 20e3 2728 e0030-e0039 e007f'
        ):
            self.assertEqual(emoji_atlas.font_codepoints(), [0x2728])

    def test_sequences_and_bmp_keys_keep_slot_order(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = pathlib.Path(directory) / 'TextSpriteData.cs'
            emoji_atlas.write_data(str(output), ['👩‍💻', '❤️', '😀'], 1, [0x20200000] * 3)
            source = output.read_text(encoding='utf-8')
            self.assertIn(r'"\U0001F469\U0000200D\U0001F4BB", "\U00002764\U0000FE0F", "\U0001F600"', source)

    def test_pinned_unicode_data_contains_representative_sequences(self) -> None:
        keys = set(emoji_atlas.unicode_sequences(emoji_atlas.DEFAULT_SEQUENCES))
        self.assertIn('👩‍💻', keys)
        self.assertIn('🏳️‍🌈', keys)
        self.assertIn('1️⃣', keys)

    def test_loading_tips_use_ascii_glyphs(self) -> None:
        path = ROOT / 'shared/loading-tips.txt'
        tips = [line for line in path.read_text(encoding='utf-8').splitlines() if line and not line.startswith('#')]
        self.assertTrue(tips)
        for line in tips:
            unsupported = {char for char in line if ord(char) > 127}
            self.assertFalse(unsupported, f'non-ASCII loading tip: {line!r}')


if __name__ == '__main__':
    unittest.main()
