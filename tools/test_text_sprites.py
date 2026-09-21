#!/usr/bin/env python3
"""Check sprite catalog generation without loading fonts or image assets."""
import pathlib
import re
import tempfile
import unittest

import emoji_atlas


class TextSpriteMetadataTests(unittest.TestCase):
    def test_packaged_metadata_matches_generator(self):
        source = pathlib.Path(emoji_atlas.DEFAULT_DATA).read_text(encoding="utf-8")
        literals = re.findall(r'"((?:\\U[0-9A-F]{8})+)"', source)
        keys = [literal.encode("ascii").decode("unicode_escape") for literal in literals]
        self.assertTrue(keys)
        self.assertEqual(len(keys), len(set(keys)))
        rows = int(re.search(r"AtlasRows = (\d+)", source).group(1))
        self.assertLessEqual(len(keys), rows * emoji_atlas.COLUMNS)
        with tempfile.TemporaryDirectory() as directory:
            output = pathlib.Path(directory) / "TextSpriteData.cs"
            emoji_atlas.write_data(str(output), keys, rows)
            self.assertEqual(source, output.read_text(encoding="utf-8"))

    def test_sequences_and_bmp_keys_keep_slot_order(self):
        with tempfile.TemporaryDirectory() as directory:
            output = pathlib.Path(directory) / "TextSpriteData.cs"
            emoji_atlas.write_data(str(output), ["👩‍💻", "❤️", "😀"], 1)
            source = output.read_text(encoding="utf-8")
            self.assertIn(r'"\U0001F469\U0000200D\U0001F4BB", "\U00002764\U0000FE0F", "\U0001F600"', source)


if __name__ == "__main__":
    unittest.main()
