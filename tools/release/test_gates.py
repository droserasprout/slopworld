"""Protect release eligibility and fail-closed ordering without builds or publication."""

import subprocess
from collections.abc import Callable
from pathlib import Path
from unittest.mock import patch

import pytest

from tools import ROOT
from tools.release import gates
from tools.utils import run
from tools.version import product


@pytest.fixture
def repository(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Callable[..., str]:
    def git(*args: str) -> str:
        return run(['git', *args], cwd=tmp_path, capture_output=True, text=True).stdout.strip()

    git('init', '-b', 'main')
    git('config', 'user.name', 'Release test')
    git('config', 'user.email', 'release@example.test')
    for entry in product.FILES:
        path = tmp_path / entry.path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text((ROOT / entry.path).read_text())
    for path, (_, updated) in product.changes('0.0.1', tmp_path).items():
        path.write_text(updated)
    git('add', '.')
    git('commit', '-m', 'fixture')
    monkeypatch.setattr(gates, 'git', git)
    monkeypatch.setattr(gates, 'ROOT', tmp_path)
    monkeypatch.delenv('VERSION', raising=False)
    monkeypatch.delenv('RELEASE_MAIN_REF', raising=False)
    return git


@pytest.mark.parametrize('tag, annotated', [('v0.0.1', False), ('v0.0.1', True)])
def test_clean_main_accepts_lightweight_and_annotated_tags(
    repository: Callable[..., str], tag: str, annotated: bool
) -> None:
    repository('tag', *(['-a', '-m', 'Release'] if annotated else []), tag)
    assert gates.prepare() == gates.Release(repository('rev-parse', 'HEAD'), tag, '0.0.1')


def test_tagged_feature_checkout_is_rejected(repository: Callable[..., str]) -> None:
    repository('tag', 'v0.0.1')
    repository('checkout', '-b', 'feature')
    with pytest.raises(ValueError, match='requires main'):
        gates.prepare()


@pytest.mark.parametrize('tags', [[], ['0.0.1'], ['latest'], ['v01.0.0'], ['v0.0.1', 'v0.0.2']])
def test_missing_invalid_or_ambiguous_version_is_rejected(repository: Callable[..., str], tags: list[str]) -> None:
    for tag in tags:
        repository('tag', tag)
    with pytest.raises(ValueError, match='unambiguous'):
        gates.prepare()


@pytest.mark.parametrize('staged', [False, True])
def test_dirty_checkout_is_rejected(repository: Callable[..., str], tmp_path: Path, staged: bool) -> None:
    repository('tag', 'v0.0.1')
    (tmp_path / 'change').write_text('unreviewed')
    if staged:
        repository('add', 'change')
    with pytest.raises(ValueError, match='clean checkout'):
        gates.prepare()


def test_override_cannot_replace_tag(repository: Callable[..., str], monkeypatch: pytest.MonkeyPatch) -> None:
    repository('tag', 'v0.0.1')
    monkeypatch.setenv('VERSION', '0.0.2')
    with pytest.raises(ValueError, match='VERSION must match'):
        gates.prepare()
    monkeypatch.setenv('VERSION', '0.0.1')
    assert gates.prepare().version == '0.0.1'


@pytest.mark.parametrize('change', ['commit', 'tag'])
def test_delayed_checkout_changes_are_rejected(repository: Callable[..., str], change: str) -> None:
    repository('tag', 'v0.0.1')
    release = gates.prepare()
    if change == 'commit':
        repository('commit', '--allow-empty', '-m', 'new commit')
        repository('tag', '-f', 'v0.0.1')
    else:
        repository('tag', '-d', 'v0.0.1')
        repository('tag', 'v0.0.2')
    with pytest.raises(ValueError, match=r'changed|must match'):
        gates.validate(release)


@pytest.mark.parametrize('entry', product.FILES, ids=lambda entry: entry.path)
def test_committed_version_mismatch_is_rejected(
    repository: Callable[..., str], tmp_path: Path, entry: product.VersionFile
) -> None:
    source, match = entry.read(tmp_path)
    start, end = match.span('version')
    (tmp_path / entry.path).write_text(source[:start] + '0.0.2' + entry.suffix + source[end:])
    repository('add', entry.path)
    repository('commit', '-m', 'mismatched version')
    repository('tag', 'v0.0.1')
    with pytest.raises(ValueError, match='must match'):
        gates.prepare()


@pytest.mark.parametrize('failed_stage', [None, 'generate-all', 'lint', 'test', 'generate-check-protocol', 'build'])
def test_build_stops_at_failed_stage(monkeypatch: pytest.MonkeyPatch, failed_stage: str | None) -> None:
    monkeypatch.setenv('JUST_CMD', '/path with spaces/just')
    release = gates.Release('commit', 'v0.0.1', '0.0.1')
    calls: list[list[str]] = []

    def invoke(args: list[str]) -> None:
        calls.append(args)
        if args[4] == failed_stage:
            raise subprocess.CalledProcessError(1, args)

    with patch.object(gates, 'run', side_effect=invoke), patch.object(gates, 'validate') as validate:
        if failed_stage:
            with pytest.raises(subprocess.CalledProcessError):
                gates.build(release)
        else:
            gates.build(release)
        stages = ['generate-all', 'lint', 'test', 'generate-check-protocol', 'build']
        count = stages.index(failed_stage) + 1 if failed_stage else len(stages)
        assert [call[4] for call in calls] == stages[:count]
        assert all(call[:3] == ['/path with spaces/just', 'BUILD=release', 'VERSION=0.0.1'] for call in calls)
        assert validate.call_count == count + (0 if failed_stage else 1)


@pytest.mark.parametrize('stage', [0, 1, 2, 3, 4])
def test_sources_changed_by_stage_stop_release(monkeypatch: pytest.MonkeyPatch, stage: int) -> None:
    monkeypatch.setenv('JUST_CMD', 'just')
    with (
        patch.object(gates, 'validate', side_effect=[*[None] * (stage + 1), ValueError('dirty')]),
        patch.object(gates, 'run') as invoke,
    ):
        with pytest.raises(ValueError, match='dirty'):
            gates.build(gates.Release('commit', 'v0.0.1', '0.0.1'))
        assert invoke.call_count == stage + 1


@pytest.mark.parametrize('on_main', [False, True])
def test_detached_release_requires_main_ancestry(repository: Callable[..., str], on_main: bool) -> None:
    if not on_main:
        repository('checkout', '-b', 'feature')
        repository('commit', '--allow-empty', '-m', 'not on main')
    repository('tag', 'v0.0.1')
    repository('checkout', '--detach')
    if on_main:
        assert gates.prepare().tag == 'v0.0.1'
    else:
        with pytest.raises(ValueError, match='belong to main'):
            gates.prepare()
