"""Protect exact version selection and human-authored release descriptions."""

from pathlib import Path

import pytest

from tools.release.changelog import description


def test_extracts_matching_date_and_changes_without_other_releases_or_footer(tmp_path: Path) -> None:
    path = tmp_path / 'CHANGELOG.md'
    source = """# Changelog

## [Unreleased]

Future changes.

## [0.0.2] - 2026-10-10

Newer release.

## [0.0.1] - 2026-10-09

### Added

- Human **release notes** with [a link](https://example.com).

```markdown
## [9.9.9] - 2026-10-09
```

<!-- Links -->
[0.0.1]: https://example.com/release
"""
    path.write_text(source)
    result = description(path, '0.0.1')
    assert result.startswith('## 0.0.1 - 2026-10-09\n\n### Added\n')
    assert 'Human **release notes**' in result
    assert '## [9.9.9]' in result
    assert 'Future' not in result and 'Newer' not in result and '<!-- Links -->' not in result
    assert path.read_text() == source


@pytest.mark.parametrize(
    'source',
    [
        '## [v0.0.1] - 2026-10-09\n\nGit tag is not a changelog version.\n',
        '## [0.1.0] - 2026-10-09\n\nOther version.\n',
        '## [0.0.1] - ????-??-??\n\nUndated.\n',
        '## [0.0.1] - 2026-02-30\n\nInvalid date.\n',
        '## [0.0.1] - 2026-10-09\n\n## [0.0.2] - 2026-10-10\nLater.\n',
        '## [0.0.1] - 2026-10-09\nFirst.\n## [0.0.1] - 2026-10-09\nDuplicate.\n',
    ],
)
def test_missing_undated_empty_or_duplicate_entry_is_rejected(tmp_path: Path, source: str) -> None:
    path = tmp_path / 'CHANGELOG.md'
    path.write_text(source)
    with pytest.raises(ValueError):
        description(path, '0.0.1')
