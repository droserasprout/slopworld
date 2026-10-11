"""Protect README links when its content is included in the book at build time."""

from typing import Any

from tools.docs.introduction import adapt_links
from tools.docs.introduction import render


def test_repository_links_and_images_resolve_in_the_published_book() -> None:
    content = (
        '[Setup](docs/src/installation/linux.md#requirements)\n'
        '<img src="docs/src/images/screenshot.jpg" alt="Workspace">\n'
        '[License](LICENSE) [Notices](licenses/README.md)\n'
        '[External](https://example.com/docs/src/page.md)\n'
        '{{#constant release_version}}\n'
    )
    result = adapt_links(content)
    assert '[Setup](installation/linux.md#requirements)' in result
    assert '<img src="images/screenshot.jpg" alt="Workspace">' in result
    assert '[License](https://github.com/droserasprout/slopworld/blob/main/LICENSE)' in result
    assert '[Notices](https://github.com/droserasprout/slopworld/blob/main/licenses/README.md)' in result
    assert '[External](https://example.com/docs/src/page.md)' in result
    assert '{{#constant release_version}}' in result


def test_only_the_introduction_is_adapted_in_nested_chapters() -> None:
    content = '[Repository link](docs/src/installation/linux.md)'
    introduction = {'path': 'introduction.md', 'content': content, 'sub_items': []}
    other = {
        'path': 'resources/changelog.md',
        'content': content,
        'sub_items': [{'Chapter': introduction}],
    }
    book: dict[str, Any] = {'items': ['Separator', {'PartTitle': 'Resources'}, {'Chapter': other}]}
    render(book)
    assert introduction['content'] == '[Repository link](installation/linux.md)'
    assert other['content'] == content
