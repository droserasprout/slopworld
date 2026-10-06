"""Source measurement regression cases."""

from pathlib import Path

import pytest

from tools.analysis.loc import C_LIKE
from tools.analysis.loc import HASH
from tools.analysis.loc import PY
from tools.analysis.loc import count
from tools.analysis.loc import count_python
from tools.analysis.loc import scan


def test_docstrings_and_assigned_multiline_data() -> None:
    source = '"""Module\ndocs"""\nvalue = f"""generated\n// source\n"""\n# comment\n'
    assert count_python(source) == (6, 0, 3, 3)


def test_escaped_quote_does_not_expose_comment() -> None:
    assert scan(r'let s = "escaped \" // still string"; // real', C_LIKE, None) == (
        True,
        'real',
        None,
    )


def test_lifetime_does_not_hide_trailing_comment() -> None:
    assert scan("fn borrow<'a>(x: &'a str) {} // real", C_LIKE, None, rust=True) == (True, 'real', None)


def test_single_quoted_shell_string_does_not_expose_comment() -> None:
    assert scan("value='abc # string' # real", HASH, None) == (True, 'real', None)


def test_unreadable_file_fails_measurement(tmp_path: Path) -> None:
    with pytest.raises(OSError):
        count(tmp_path / 'missing.py', PY)
