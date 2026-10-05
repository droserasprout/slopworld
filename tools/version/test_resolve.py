"""Protect Rust tag-rule parity and checkout-local Git resolution."""

import pytest

from tools.utils import run
from tools.version import resolve


@pytest.mark.parametrize('tag,expected', [('v1.2.3', '1.2.3'), ('0.0.0', '0.0.0'), ('1.2.3', '1.2.3')])
def test_release_tag_wins_over_commit(tag, expected):
    assert resolve.resolve('9.9.9', tag, 'abcd', '20261004') == expected


@pytest.mark.parametrize('tag', [None, '', 'v01.2.3', '1.02.3', '1.2.03', '1.2', 'v1.2.3-rc1', '١.2.3', '1.2.3\n'])
def test_other_tags_use_utc_date_and_commit(tag):
    assert resolve.resolve('9.9.9', tag, 'abcd', '20261004') == '9.9.9-20261004-abcd'


def test_missing_git_or_empty_hash_uses_fallback(monkeypatch, tmp_path):
    assert resolve.resolve('1.2.3', None, ' ', '20261004') == '1.2.3'
    assert resolve.from_git('1.2.3', tmp_path) == '1.2.3'

    def unavailable(*a, **kw):
        raise FileNotFoundError()

    monkeypatch.setattr(resolve, 'run', unavailable)
    assert resolve.from_git('1.2.3', tmp_path) == '1.2.3'


def test_version_uses_selected_repository_instead_of_parent(tmp_path):
    run(['git', 'init', '-q', str(tmp_path)])
    run(
        [
            'git',
            '-c',
            'user.name=Tool test',
            '-c',
            'user.email=test@example.test',
            'commit',
            '-q',
            '--allow-empty',
            '-m',
            'fixture',
        ],
        cwd=tmp_path,
    )
    run(['git', 'tag', 'v7.8.9'], cwd=tmp_path)
    assert resolve.from_git('1.2.3', tmp_path) == '7.8.9'
