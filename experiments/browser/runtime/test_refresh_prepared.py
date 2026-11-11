"""Retained prototype assets survive code rebuilds without retaining stale code."""

import tempfile
import unittest
from pathlib import Path

from refresh_prepared import refresh


class RefreshTests(unittest.TestCase):
    def test_refresh_preserves_presentation_and_replaces_code(self):
        with tempfile.TemporaryDirectory() as temporary:
            source, destination = (Path(temporary) / name for name in ('core', 'eco'))
            source.mkdir()
            destination.mkdir()
            (source / 'app.js').write_text('new')
            (destination / 'old.js').write_text('stale')
            for name in ('fonts', 'background', 'portraits'):
                directory = destination / 'assets' / name
                directory.mkdir(parents=True)
                (directory / 'manifest.json').write_text(name)
            (destination / 'assets/reference-appearance.json').write_text('preferences')
            refresh(source, destination)
            self.assertEqual((destination / 'app.js').read_text(), 'new')
            self.assertFalse((destination / 'old.js').exists())
            for name in ('fonts', 'background', 'portraits'):
                self.assertEqual((destination / 'assets' / name / 'manifest.json').read_text(), name)
            self.assertEqual((destination / 'assets/reference-appearance.json').read_text(), 'preferences')
            with self.assertRaises(FileNotFoundError):
                refresh(source / 'missing', destination)
            self.assertEqual((destination / 'app.js').read_text(), 'new')
            self.assertFalse(list(Path(temporary).glob('.refresh-*')))


if __name__ == '__main__':
    unittest.main()
