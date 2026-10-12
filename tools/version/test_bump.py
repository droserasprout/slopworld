"""Protect release preparation from partial edits and dependency version changes."""

import tomllib
from pathlib import Path

import pytest

from tools import ROOT
from tools.version import product
from tools.version.bump import bump


@pytest.fixture
def repository(tmp_path: Path) -> Path:
    for entry in product.FILES:
        path = tmp_path / entry.path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes((ROOT / entry.path).read_bytes())
    for path, (_, updated) in product.changes('0.0.1', tmp_path).items():
        path.write_text(updated)
    (tmp_path / 'CHANGELOG.md').write_text('## [0.0.2] - 2026-10-12\n\nHuman release notes.\n')
    return tmp_path


def snapshot(repository: Path) -> dict[Path, bytes]:
    return {path: path.read_bytes() for path in repository.rglob('*') if path.is_file()}


def test_bump_preserves_dependencies_and_changelog_and_is_repeatable(repository: Path) -> None:
    before = snapshot(repository)
    locks = {name: tomllib.loads((repository / name / 'Cargo.lock').read_text()) for name in ('slopd', 'slopcar')}
    assert len(bump('0.0.2', repository)) == len(product.FILES)
    product.check('0.0.2', repository)
    assert (repository / 'CHANGELOG.md').read_bytes() == before[repository / 'CHANGELOG.md']
    for name, lock in locks.items():
        for package in lock['package']:
            if package['name'] == name:
                package['version'] = '0.0.2'
        assert tomllib.loads((repository / name / 'Cargo.lock').read_text()) == lock
    after = snapshot(repository)
    assert bump('0.0.2', repository) == []
    assert snapshot(repository) == after


@pytest.mark.parametrize('version', ['v0.0.2', '0.00.2', '0.0', '0.0.2-rc1', '0.0.2\n'])
def test_invalid_version_does_not_write(repository: Path, version: str) -> None:
    before = snapshot(repository)
    with pytest.raises(ValueError, match=r'MAJOR\.MINOR\.PATCH'):
        bump(version, repository)
    assert snapshot(repository) == before


@pytest.mark.parametrize('source', ['', '## [0.0.2]\n\nUndated.\n', '## [0.0.2] - 2026-10-12\n'])
def test_missing_dated_release_notes_do_not_write(repository: Path, source: str) -> None:
    (repository / 'CHANGELOG.md').write_text(source)
    before = snapshot(repository)
    with pytest.raises(ValueError, match=r'CHANGELOG\.md'):
        bump('0.0.2', repository)
    assert snapshot(repository) == before


@pytest.mark.parametrize('duplicate', [False, True])
def test_malformed_final_entry_does_not_partially_bump(repository: Path, duplicate: bool) -> None:
    entry = product.FILES[-1]
    path = repository / entry.path
    source = path.read_text()
    path.write_text(source + '\npkgver=0.0.1\n' if duplicate else source.replace('pkgver=', 'missing='))
    before = snapshot(repository)
    with pytest.raises(ValueError, match='exactly one'):
        bump('0.0.2', repository)
    assert snapshot(repository) == before


def test_failed_write_rolls_back_prior_and_partial_writes(repository: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    before = snapshot(repository)
    write_text = Path.write_text
    failed = False

    def write(path: Path, source: str) -> int:
        nonlocal failed
        if path == repository / product.FILES[2].path and not failed:
            failed = True
            write_text(path, 'partial')
            raise OSError('write failed')
        return write_text(path, source)

    monkeypatch.setattr(Path, 'write_text', write)
    with pytest.raises(OSError, match='write failed'):
        bump('0.0.2', repository)
    assert snapshot(repository) == before
