from pathlib import Path

import pytest

from tools.licenses.sidecar_licenses import find_notice
from tools.licenses.sidecar_licenses import find_notices


def test_notices_choose_mit_and_include_mpl_dependencies(tmp_path: Path) -> None:
    mit = tmp_path / 'LICENSE-MIT'
    mit.write_text('Permission is hereby granted')
    mpl = tmp_path / 'LICENSE'
    mpl.write_text('Mozilla Public License Version 2.0')
    assert find_notice(tmp_path, 'MIT OR Apache-2.0') == ('MIT', mit)
    assert find_notice(tmp_path, 'MPL-2.0') == ('MPL-2.0', mpl)
    with pytest.raises(ValueError, match='Review license'):
        find_notice(tmp_path, 'Unknown')
    mpl.unlink()
    with pytest.raises(ValueError, match=r'Missing full MPL-2\.0'):
        find_notice(tmp_path, 'MPL-2.0')


def test_unicode_conjunction_requires_both_full_notices(tmp_path: Path) -> None:
    expression = '(MIT OR Apache-2.0) AND Unicode-3.0'
    mit = tmp_path / 'LICENSE-MIT'
    mit.write_text('Permission is hereby granted')
    with pytest.raises(ValueError, match=r'Missing full Unicode-3\.0'):
        find_notices(tmp_path, expression)
    unicode = tmp_path / 'LICENSE-UNICODE'
    unicode.write_text('UNICODE LICENSE V3')
    assert find_notices(tmp_path, expression) == [('MIT', mit), ('Unicode-3.0', unicode)]
    mit.unlink()
    with pytest.raises(ValueError, match='Missing full MIT'):
        find_notices(tmp_path, expression)
