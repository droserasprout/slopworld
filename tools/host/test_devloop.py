"""Worktree selection supports unusual names and rejects ambiguous input."""

import os
from pathlib import Path

import pytest

from tools.host import devloop


def test_idle_prompt_accepts_current_checkout() -> None:
    reader, writer = os.pipe()
    with os.fdopen(reader) as terminal, os.fdopen(writer, 'w'):
        assert devloop.read_choice(terminal, timeout=0) == '\n'


@pytest.mark.parametrize('line', ['2\n', 'q\n', '\n', ''])
def test_prompt_preserves_selection_quit_and_eof(line: str) -> None:
    reader, writer = os.pipe()
    with os.fdopen(reader) as terminal:
        with os.fdopen(writer, 'w') as source:
            source.write(line)
        assert devloop.read_choice(terminal, timeout=0) == line


def test_nul_records_preserve_spaces_newlines_and_detached_heads() -> None:
    records = b'worktree /tmp/a space\0HEAD abc\0branch refs/heads/feature/a\0\0worktree /tmp/line\nbreak\0HEAD def\0detached\0\0'
    worktrees = devloop.parse_worktrees(records)
    assert worktrees == [
        devloop.Worktree(Path('/tmp/a space'), 'feature/a'),
        devloop.Worktree(Path('/tmp/line\nbreak')),
    ]
    assert devloop.select('', worktrees, Path('/current')) == Path('/current')
    assert devloop.select('2', worktrees, Path('/current')) == Path('/tmp/line\nbreak')


@pytest.mark.parametrize('choice', ['0', '-1', '01', '3', '1;false', '100000', ' 1', '\u0661'])
def test_invalid_choice_does_not_select_a_checkout(choice: str) -> None:
    with pytest.raises(ValueError, match='Choose a listed number'):
        devloop.select(choice, [devloop.Worktree(Path('/tmp/one'))], Path('/current'))
