"""Check book navigation and local links against mdBook's rendered IDs.

mdBook owns Markdown parsing and anchor generation. This check reads its HTML so
explicit anchors, generated heading suffixes, and theme links use the same rules.
External URLs are left to their owners; no network or game access is needed.
"""

import re
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote
from urllib.parse import urlsplit

from tools import ROOT


class Page(HTMLParser):
    def __init__(self, text: str) -> None:
        super().__init__(convert_charrefs=True)
        self.ids: set[str] = set()
        self.links: list[str] = []
        self.feed(text)

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        anchor = values.get('id') or (values.get('name') if tag == 'a' else None)
        if anchor:
            self.ids.add(anchor)
        for attribute in ('href', 'src'):
            value = values.get(attribute)
            if value:
                self.links.append(value)


def check_navigation(source: Path) -> list[str]:
    summary = source / 'SUMMARY.md'
    errors: list[str] = []
    listed: set[Path] = set()
    for link in re.findall(r'\]\(([^)]*)\)', summary.read_text(encoding='utf-8')):
        url = urlsplit(link)
        if not url.path or url.scheme or url.netloc:
            continue
        page = (source / unquote(url.path)).resolve()
        if not page.is_relative_to(source.resolve()) or not page.is_file():
            errors.append(f'SUMMARY.md: missing or out-of-book page: {link}')
        if page in listed:
            errors.append(f'SUMMARY.md: duplicate page: {link}')
        listed.add(page)
    for page in sorted(source.rglob('*.md')):
        if page != summary and page.resolve() not in listed:
            errors.append(f'SUMMARY.md: unlisted page: {page.relative_to(source)}')
    return errors


def check_links(book: Path) -> list[str]:
    book = book.resolve()
    pages = {path: Page(path.read_text(encoding='utf-8')) for path in sorted(book.rglob('*.html'))}
    if not pages:
        return [f'No rendered HTML in {book}; build the book first.']
    errors: list[str] = []
    for path, page in pages.items():
        for link in sorted(set(page.links)):
            url = urlsplit(link)
            if url.scheme or url.netloc:
                continue
            decoded = unquote(url.path)
            target = (book / decoded.lstrip('/')) if decoded.startswith('/') else path.parent / decoded
            target = target.resolve() if decoded else path
            if target.is_dir():
                target /= 'index.html'
            label = f'{path.relative_to(book)}: {link}'
            if not target.is_relative_to(book) or not target.is_file():
                errors.append(f'{label}: missing or out-of-book target')
            elif url.fragment and target in pages and unquote(url.fragment) not in pages[target].ids:
                errors.append(f'{label}: missing anchor')
    return errors


def main() -> None:
    errors = check_navigation(ROOT / 'docs/src') + check_links(ROOT / 'docs/book')
    if errors:
        raise SystemExit('\n'.join(errors))
    print('Book navigation, local links, and anchors passed.')


if __name__ == '__main__':
    main()
