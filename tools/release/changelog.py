"""Read release descriptions from the human-maintained Keep a Changelog document."""

import re
from datetime import date
from pathlib import Path

from markdown_it import MarkdownIt


def description(path: Path, version: str) -> str:
    source = path.read_text()
    lines = source.splitlines(keepends=True)
    tokens = MarkdownIt().parse(source)
    sections = [token for token in tokens if token.type == 'heading_open' and token.tag == 'h2' and token.map]
    matches: list[str] = []
    for index, section in enumerate(sections):
        assert section.map is not None
        heading = lines[section.map[0]].strip()
        if not re.match(rf'^##\s+\[?{re.escape(version)}\]?(?:\s|$)', heading):
            continue
        dated = re.fullmatch(rf'##\s+\[?{re.escape(version)}\]?\s+-\s+(\d{{4}}-\d{{2}}-\d{{2}})', heading)
        if dated is None:
            raise ValueError(f'CHANGELOG.md entry for {version} requires a release date (YYYY-MM-DD).')
        date.fromisoformat(dated[1])
        end = len(lines)
        if index + 1 < len(sections):
            following = sections[index + 1].map
            assert following is not None
            end = following[0]
        body = ''.join(lines[section.map[1] : end])
        # The canonical document keeps global link definitions after these
        # footer markers; they are not release prose.
        body = re.split(r'<!--\s*(?:Links|Versions)\s*-->', body, maxsplit=1)[0].strip()
        if not body:
            raise ValueError(f'CHANGELOG.md entry for {version} is empty.')
        matches.append(f'## {version} - {dated[1]}\n\n{body}')
    if len(matches) != 1:
        raise ValueError(f'CHANGELOG.md must contain one nonempty entry for {version}.')
    return matches[0] + '\n'
