"""Regression cases for published links and navigation coverage."""

import tempfile
import unittest
from pathlib import Path

from tools.docs.check_book import check_links
from tools.docs.check_book import check_navigation
from tools.docs.check_book import check_repository_links
from tools.docs.check_book import repository_documents
from tools.utils import run


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


class RepositoryChecks(unittest.TestCase):
    def setUp(self) -> None:
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name).resolve()
        run(['git', 'init', '--quiet'], cwd=self.root, capture_output=True)

    def write(self, name: str, text: str = '') -> None:
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding='utf-8')

    def test_checks_new_and_tracked_docs_but_not_ignored_output(self) -> None:
        self.write('README.md')
        self.write('notes/owner.md')
        run(['git', 'add', 'README.md', 'notes/owner.md'], cwd=self.root, capture_output=True)
        self.write('slopcar/README.md', '[Migration](../docs/src/guides/storage-migration.md)')
        self.write('notes/new.md', '[Missing](missing.md)')
        self.write('.gitignore', 'build/\n')
        self.write('build/README.md', '[Ignored](missing.md)')
        self.write('docs/src/guide.md', '[Book owns this](missing.md)')
        self.assertEqual(
            {path.relative_to(self.root).as_posix() for path in repository_documents(self.root)},
            {'README.md', 'notes/owner.md', 'slopcar/README.md', 'notes/new.md'},
        )
        errors = check_repository_links(self.root)
        self.assertEqual(len(errors), 2)
        self.assertTrue(any('slopcar/README.md: ../docs/src/guides/storage-migration.md:' in e for e in errors))
        self.assertTrue(any('notes/new.md: missing.md:' in e for e in errors))

    def test_parses_references_html_images_and_escaped_destinations_but_not_code(self) -> None:
        self.write('docs/a b.md')
        self.write('docs/guide(v2).md')
        self.write('image.png')
        self.write(
            'README.md',
            """
[Reference][guide]
[guide]: <docs/a b.md> "Title"
[Encoded](docs/a%20b.md?view=1#heading)
[Parentheses](docs/guide(v2).md)
[Directory](docs/)
[Root](/docs/a%20b.md)
![Asset](image.png)
<img src="image.png"><a href="docs/a%20b.md">HTML</a>
[External](https://example.com/missing)
[External](//example.com/missing)
[Anchor](#unchecked-github-anchor)
`[Inline example](missing.md)`
```
[Fenced example](missing.md)
```

    [Indented example](missing.md)
""",
        )
        self.assertEqual(check_repository_links(self.root), [])
        (self.root / 'image.png').unlink()
        self.assertEqual(
            check_repository_links(self.root),
            [
                'README.md: image.png: missing or out-of-repository target',
            ],
        )

    def test_rejects_escaping_targets_even_when_they_exist(self) -> None:
        self.write('README.md', '[Outside](../)')
        self.assertEqual(
            check_repository_links(self.root),
            [
                'README.md: ../: missing or out-of-repository target',
            ],
        )


if __name__ == '__main__':
    unittest.main()
