#!/usr/bin/env python3
"""Check text asset metadata and loading-tip glyph coverage without image assets."""

import pathlib
import re
import tempfile
import unittest

from tools import ROOT
from tools.assets import emoji_atlas


class TextSpriteMetadataTests(unittest.TestCase):
    def test_packaged_metadata_round_trips_through_writer(self):
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
        rows = int(re.search(r'AtlasRows = (\d+)', source).group(1))
        self.assertLessEqual(len(keys), rows * emoji_atlas.COLUMNS)
        with tempfile.TemporaryDirectory() as directory:
            output = pathlib.Path(directory) / 'TextSpriteData.cs'
            emoji_atlas.write_data(str(output), keys, rows)
            self.assertEqual(source, output.read_text(encoding='utf-8'))

    def test_sequences_and_bmp_keys_keep_slot_order(self):
        with tempfile.TemporaryDirectory() as directory:
            output = pathlib.Path(directory) / 'TextSpriteData.cs'
            emoji_atlas.write_data(str(output), ['👩‍💻', '❤️', '😀'], 1)
            source = output.read_text(encoding='utf-8')
            self.assertIn(r'"\U0001F469\U0000200D\U0001F4BB", "\U00002764\U0000FE0F", "\U0001F600"', source)

    def test_pinned_unicode_data_contains_representative_sequences(self):
        keys = set(emoji_atlas.unicode_sequences(emoji_atlas.DEFAULT_SEQUENCES))
        self.assertIn('👩‍💻', keys)
        self.assertIn('🏳️‍🌈', keys)
        self.assertIn('1️⃣', keys)

    def test_loading_tips_use_ascii_glyphs(self):
        path = ROOT / 'mod/Source/SlopWorld/Patches/LoadingScreen/LoadingScreen.Tips.cs'
        source = path.read_text(encoding='utf-8')
        lines = source.splitlines()
        start = next(i for i, line in enumerate(lines) if 'static readonly List<string> Tips' in line)
        for offset, line in enumerate(lines[start + 1 :], start + 2):
            if line.strip() == '};':
                break
            stripped = line.strip()
            if not stripped or stripped == '{' or stripped.startswith('//'):
                continue
            match = re.match(r'\s*"((?:\\.|[^"\\])*)"\s*,?\s*(?://.*)?$', line)
            self.assertIsNotNone(match, f'unrecognized loading tip literal at {path}:{offset}')
            literal = match.group(1)
            unsupported = {char for char in literal if ord(char) > 127}
            escaped_values = r'(?:u([0-9a-fA-F]{4})|U([0-9a-fA-F]{8})|x([0-9a-fA-F]{1,4}))'
            for escaped in re.finditer(r'(\\+)' + escaped_values, literal):
                if len(escaped.group(1)) % 2 == 0:
                    continue
                codepoint = int(next(value for value in escaped.groups()[1:] if value), 16)
                if codepoint > 127:
                    unsupported.add(f'\\u{codepoint:04X}')
            self.assertFalse(unsupported, f'non-ASCII loading tip at {path}:{offset}: {sorted(unsupported)!r}')


if __name__ == '__main__':
    unittest.main()
