from pathlib import Path

import pytest

from tools.licenses.sidecar_licenses import find_notice


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
    with pytest.raises(ValueError, match='Missing full MPL-2.0'):
        find_notice(tmp_path, 'MPL-2.0')
