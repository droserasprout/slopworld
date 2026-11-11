"""Protect recovery ownership checks and idempotent cleanup."""

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError

from firefox_cleanup import cleanup
from firefox_cleanup import fields


class CleanupTests(unittest.TestCase):
    def test_metadata_lengths_preserve_following_host_flags(self):
        self.assertEqual(fields(bytes([18, 3, 65, 66, 67, 208, 1, 1, 216, 1, 1])), {26: 1, 27: 1})

    def report(self, directory, name='slopweb-bench-abcdef12'):
        report = Path(directory) / 'report.json'
        report.write_text(json.dumps({'backend': 'Firefox WebDriver', 'fixture': {'created': [name]}}))
        return report

    def test_rejects_unowned_names_before_network_access(self):
        with tempfile.TemporaryDirectory() as directory, patch('firefox_cleanup.urlopen') as opened:
            with self.assertRaisesRegex(ValueError, 'unrecognized'):
                cleanup(self.report(directory, 'existing-agent'), {'url': 'http://localhost', 'token': 'secret'})
            opened.assert_not_called()

    def test_repeated_cleanup_of_absent_fixture_does_not_delete(self):
        with tempfile.TemporaryDirectory() as directory:
            report = self.report(directory)
            missing = HTTPError('http://localhost', 404, 'absent', {}, None)
            with patch('firefox_cleanup.urlopen', side_effect=missing) as opened:
                for _ in range(2):
                    cleanup(report, {'url': 'http://localhost', 'token': 'secret'})
                self.assertEqual([call.args[0].method for call in opened.call_args_list], ['GET', 'GET'])

    def test_refuses_persistent_fixture_even_with_an_owned_name(self):
        class Response:
            status = 200

            def __enter__(self):
                return self

            def __exit__(self, *args):
                pass

            def read(self):
                return bytes([208, 1, 0, 216, 1, 1])

        with (
            tempfile.TemporaryDirectory() as directory,
            patch('firefox_cleanup.urlopen', return_value=Response()) as opened,
        ):
            with self.assertRaisesRegex(ValueError, 'non-temporary'):
                cleanup(self.report(directory), {'url': 'http://localhost', 'token': 'secret'})
            self.assertEqual(opened.call_count, 1)


if __name__ == '__main__':
    unittest.main()
