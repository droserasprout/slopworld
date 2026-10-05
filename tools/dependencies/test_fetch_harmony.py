"""Protect verified installation and recovery without accessing GitHub."""

import hashlib
import io
import json
import tempfile
import unittest
import urllib.error
import zipfile
from pathlib import Path
from unittest.mock import patch

from tools.dependencies import fetch_harmony as harmony


def archive_bytes(entry=harmony.ARCHIVE_ENTRY, assembly=b'new assembly'):
    output = io.BytesIO()
    with zipfile.ZipFile(output, 'w') as archive:
        archive.writestr(entry, assembly)
        archive.writestr('../unexpected', b'must not be extracted')
    return output.getvalue()


class FetchHarmonyTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.destination = Path(self.directory.name) / 'assemblies with spaces'
        self.destination.mkdir()
        self.target = self.destination / '0Harmony.dll'
        self.target.write_bytes(b'old assembly')

    def fetch(self, archive, digest=None, assets=True, download_error=None):
        asset = {
            'name': harmony.ASSET_NAME,
            'browser_download_url': 'https://example.test/harmony.zip',
            'digest': digest if digest is not None else 'sha256:' + hashlib.sha256(archive).hexdigest(),
        }
        release = {'tag_name': 'v1.2.3', 'assets': [asset] if assets else []}
        responses = [io.BytesIO(json.dumps(release).encode()), download_error or io.BytesIO(archive)]
        with patch.object(harmony.urllib.request, 'urlopen', side_effect=responses) as download:
            harmony.fetch_harmony(self.destination)
        return download

    def assert_untouched(self):
        self.assertEqual(self.target.read_bytes(), b'old assembly')
        self.assertEqual(list(self.destination.iterdir()), [self.target])

    def test_verified_assembly_replaces_existing_file_and_can_be_installed_repeatedly(self):
        for _ in range(2):
            download = self.fetch(archive_bytes())
            self.assertEqual(self.target.read_bytes(), b'new assembly')
            self.assertEqual(self.target.stat().st_mode & 0o777, 0o644)
            self.assertEqual(list(self.destination.iterdir()), [self.target])
            self.assertEqual(download.call_args_list[0].args[0].full_url, harmony.API_URL)
            self.assertEqual(download.call_args_list[1].args[0], 'https://example.test/harmony.zip')
        self.assertFalse((self.destination.parent / 'unexpected').exists())

    def test_install_creates_missing_destination(self):
        self.target.unlink()
        self.destination.rmdir()
        self.fetch(archive_bytes())
        self.assertEqual(self.target.read_bytes(), b'new assembly')

    def test_missing_asset_and_invalid_digests_preserve_existing_installation(self):
        with self.assertRaisesRegex(ValueError, 'has no HarmonyMod.zip asset'):
            self.fetch(archive_bytes(), assets=False)
        for digest in ('', 'sha256:abc', 'sha512:' + '0' * 64):
            with self.subTest(digest=digest), self.assertRaisesRegex(ValueError, 'no valid SHA-256 digest'):
                self.fetch(archive_bytes(), digest=digest)
        self.assert_untouched()

    def test_checksum_mismatch_preserves_existing_installation(self):
        with self.assertRaisesRegex(ValueError, 'failed SHA-256 verification'):
            self.fetch(archive_bytes(), digest='sha256:' + '0' * 64)
        self.assert_untouched()

    def test_missing_empty_and_invalid_archives_preserve_existing_installation(self):
        for archive in (archive_bytes(entry='other.dll'), archive_bytes(assembly=b''), b'invalid zip'):
            with self.subTest(archive=archive), self.assertRaises((ValueError, zipfile.BadZipFile)):
                self.fetch(archive)
            self.assert_untouched()

    def test_download_failure_preserves_existing_installation(self):
        with self.assertRaises(urllib.error.URLError):
            self.fetch(archive_bytes(), download_error=urllib.error.URLError('connection lost'))
        self.assert_untouched()

    def test_failed_publish_cleans_staging_file_and_next_attempt_recovers(self):
        with patch.object(harmony.os, 'replace', side_effect=OSError('cannot publish')):
            with self.assertRaisesRegex(OSError, 'cannot publish'):
                self.fetch(archive_bytes())
        self.assert_untouched()
        self.fetch(archive_bytes())
        self.assertEqual(self.target.read_bytes(), b'new assembly')


if __name__ == '__main__':
    unittest.main()
