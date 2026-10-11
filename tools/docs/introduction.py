"""Adapt the included README's repository links for the book after native inclusion."""

import json
import sys
from typing import Any

REPOSITORY = 'https://github.com/droserasprout/slopworld/blob/main/'


def adapt_links(text: str) -> str:
    # Book links and HTML images are relative to docs/src rather than the repo root.
    text = text.replace('](docs/src/', '](').replace('src="docs/src/', 'src="')
    # Repository license files are not published as book pages.
    for path in ('LICENSE', 'licenses/README.md'):
        text = text.replace(f']({path})', f']({REPOSITORY}{path})')
    return text


def render(book: dict[str, Any]) -> None:
    def visit(items: list[Any]) -> None:
        for item in items:
            if not isinstance(item, dict):
                continue
            chapter = item.get('Chapter')
            if chapter is None:
                continue
            if chapter.get('path') == 'introduction.md':
                chapter['content'] = adapt_links(chapter['content'])
            visit(chapter.get('sub_items', []))

    visit(book['items'])


def main() -> None:
    if sys.argv[1:2] == ['supports']:
        return
    _context, book = json.load(sys.stdin)
    render(book)
    json.dump(book, sys.stdout)


if __name__ == '__main__':
    main()
