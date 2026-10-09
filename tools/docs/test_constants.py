"""Protect shared metadata substitution in prose, commands, and nested book chapters."""

from pathlib import Path
from typing import Any

import pytest

from tools.docs.constants import expand
from tools.docs.constants import render
from tools.docs.constants import values


def test_version_bump_updates_prose_commands_and_links(tmp_path: Path) -> None:
    manifest = tmp_path / 'slopd/Cargo.toml'
    manifest.parent.mkdir()
    template = (
        'Version {{#constant release_version}} uses {{#constant release_tag_format}}.\n'
        '```sh\nsudo apt install ./slopworld-{{#constant release_version}}-amd64.deb\n```\n'
        '[Release](https://example.com/releases/tag/{{#constant release_tag}})\n'
    )
    for version in ('0.0.1', '2.3.4'):
        manifest.write_text(f'[package]\nversion = "{version}"\n')
        result = expand(template, values(tmp_path))
        assert f'Version {version} uses vMAJOR.MINOR.PATCH.' in result
        assert f'slopworld-{version}-amd64.deb' in result
        assert f'/tag/v{version})' in result
        assert '{{#constant' not in result


def test_nested_chapters_are_expanded_without_changing_other_mdbook_directives() -> None:
    chapter: dict[str, Any] = {
        'Chapter': {
            'content': '{{#constant release_tag}} {{#include example.md}}',
            'sub_items': [{'Chapter': {'content': '{{#constant release_version}}'}}],
        }
    }
    book = {'items': [chapter]}
    render(book, {'release_version': '1.2.3', 'release_tag': 'v1.2.3'})
    assert chapter['Chapter']['content'] == 'v1.2.3 {{#include example.md}}'
    assert chapter['Chapter']['sub_items'][0]['Chapter']['content'] == '1.2.3'


def test_unknown_constant_fails_the_build() -> None:
    with pytest.raises(ValueError, match='Unknown documentation constant'):
        expand('{{#constant typo}}', {})
