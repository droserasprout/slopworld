"""License checks must reject stale distributions without repairing them."""

import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from tools.licenses import stage_licenses


class LicenseTests(unittest.TestCase):
    def test_check_rejects_missing_changed_and_extra_files_without_writing(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'licenses').mkdir()
            (root / 'mod/About').mkdir(parents=True)
            (root / 'LICENSE').write_text('project')
            (root / 'licenses/example').write_text('third party')
            with patch.object(stage_licenses, 'ROOT', root):
                with patch('sys.argv', ['stage-licenses', '--check']):
                    with self.assertRaises(SystemExit):
                        stage_licenses.main()
                self.assertFalse((root / 'mod/About/LICENSE').exists())
                with patch('sys.argv', ['stage-licenses']):
                    stage_licenses.main()
                with patch('sys.argv', ['stage-licenses', '--check']):
                    stage_licenses.main()
                    staged = root / 'mod/About/ThirdPartyNotices/example'
                    staged.write_text('stale')
                    with self.assertRaises(SystemExit):
                        stage_licenses.main()
                    self.assertEqual(staged.read_text(), 'stale')
                    staged.write_text('third party')
                    extra = staged.with_name('extra')
                    extra.write_text('obsolete')
                    with self.assertRaises(SystemExit):
                        stage_licenses.main()
                    self.assertTrue(extra.exists())


if __name__ == '__main__':
    unittest.main()
