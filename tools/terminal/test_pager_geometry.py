"""Styled tmux captures must treat hyperlink metadata as zero-width controls."""

import pytest

from tools.terminal.pager_geometry import Cell
from tools.terminal.pager_geometry import parse_capture


@pytest.mark.parametrize('terminator', ['\x07', '\x1b\\'])
def test_hyperlinks_preserve_text_and_surrounding_background(terminator: str) -> None:
    output = f'\x1b[48;2;0;40;0m\x1b]8;;https://example.invalid/path{terminator}link\x1b]8;;{terminator} \nnext\x1b[0m!'
    rows = parse_capture(output)
    assert rows[0] == [Cell(char, (0, 40, 0), None) for char in 'link ']
    assert rows[1] == [Cell(char, (0, 40, 0), None) for char in 'next'] + [Cell('!', None, None)]
