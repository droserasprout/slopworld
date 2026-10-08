"""Regression cases for published links and navigation coverage."""

import tempfile
import unittest
from pathlib import Path

from tools.docs.check_book import check_links
from tools.docs.check_book import check_navigation


class BookChecks(unittest.TestCase):
    def test_links_resolve_rendered_anchors_assets_and_encoded_paths(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            book = Path(directory)
            (book / 'guide').mkdir()
            (book / 'guide/page.html').write_text(
                '<h1 id="explicit"></h1><a name="legacy"></a>'
                '<a href="#explicit">self</a><a href="#legacy">old</a>'
                '<a href="/index.html#start">home</a>'
                '<img src="../a%20b.png"><a href="https://example.com/missing">external</a>'
                '<a href="//example.com/missing">external</a>',
                encoding='utf-8',
            )
            (book / 'index.html').write_text(
                '<h1 id="start"></h1><a href="guide/page.html?query=1#explicit">guide</a>', encoding='utf-8'
            )
            (book / 'a b.png').touch()
            self.assertEqual(check_links(book), [])

    def test_reports_broken_anchors_missing_files_and_repository_escapes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            book = root / 'book'
            book.mkdir()
            (root / 'README.md').write_text('Exists outside the published book.', encoding='utf-8')
            (book / 'index.html').write_text(
                '<a href="#missing">anchor</a><a href="absent.html">file</a><a href="../README.md">repository</a>',
                encoding='utf-8',
            )
            errors = check_links(book)
            self.assertEqual(len(errors), 3)
            self.assertTrue(any('#missing: missing anchor' in error for error in errors))
            self.assertTrue(any('../README.md: missing or out-of-book target' in error for error in errors))

    def test_navigation_detects_orphans_duplicates_and_missing_pages(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory)
            (source / 'SUMMARY.md').write_text(
                '- [Group]()\n  - [Page](./page.md)\n  - [Again](page.md)\n  - [Gone](gone.md)\n',
                encoding='utf-8',
            )
            (source / 'page.md').touch()
            (source / 'orphan.md').touch()
            errors = check_navigation(source)
            self.assertEqual(len(errors), 3)
            self.assertTrue(any('unlisted page: orphan.md' in error for error in errors))
            (source / 'SUMMARY.md').write_text('- [Page](page.md)\n- [Other](orphan.md)\n', encoding='utf-8')
            self.assertEqual(check_navigation(source), [])

    def test_missing_build_fails(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(len(check_links(Path(directory))), 1)


if __name__ == '__main__':
    unittest.main()
