"""Regression tests for the shipped theme build validator."""

import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import validate_themes as themes


class ThemeValidationTests(unittest.TestCase):
    def test_contrast_faces_require_opacity_but_panels_allow_alpha(self):
        source = (themes.THEMES / "UI" / "slopworld-warm.toml").read_text()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / "slopworld-warm.toml"
            with patch.object(themes, "ROOT", root):
                for role in ("accent", "destructive", "checkFace"):
                    original = next(line for line in source.splitlines() if line.startswith(role + " ="))
                    with self.subTest(role=role):
                        path.write_text(source.replace(original, f'{role} = "#ABCDEF80"'))
                        with self.assertRaisesRegex(ValueError, role + " must be opaque"):
                            themes.read_file(path, themes.UI_KEYS, themes.UI_COLORS)
                        path.write_text(source.replace(original, f'{role} = "#ABCDEFFF"'))
                        themes.read_file(path, themes.UI_KEYS, themes.UI_COLORS)

    def test_boolean_integer_fields_are_rejected(self):
        source = (themes.THEMES / "UI" / "slopworld-warm.toml").read_text()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / "slopworld-warm.toml"
            with patch.object(themes, "ROOT", root):
                for original, replacement, message in (
                    ("version = 1", "version = true", "version must be integer 1"),
                    ("order = 0", "order = false", "order must be a non-negative integer"),
                    ("order = 0", "order = true", "order must be a non-negative integer"),
                ):
                    with self.subTest(replacement=replacement):
                        path.write_text(source.replace(original, replacement))
                        with self.assertRaisesRegex(ValueError, message):
                            themes.read_file(path, themes.UI_KEYS, themes.UI_COLORS)


if __name__ == "__main__":
    unittest.main()
