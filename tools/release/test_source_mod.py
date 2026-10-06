"""Protect source-package content without compiling or requiring Git metadata."""

from pathlib import Path

import pytest

from tools.release import latest
from tools.release import source_mod


@pytest.fixture
def inputs(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Path:
    root = tmp_path / 'source snapshot'
    monkeypatch.setattr(source_mod, 'ROOT', root)
    for directory in latest.MOD_DIRECTORIES:
        asset = root / 'mod' / directory / 'asset'
        asset.parent.mkdir(parents=True, exist_ok=True)
        asset.write_text(directory)
    for assembly in latest.MOD_ASSEMBLIES:
        target = root / 'mod/Assemblies' / f'{assembly}.dll'
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(assembly)
    (root / 'LICENSE').write_text('canonical license')
    (root / 'licenses').mkdir()
    (root / 'licenses/NOTICE.txt').write_text('canonical notice')
    return root


@pytest.mark.parametrize('staged_notices', [False, True])
def test_source_mod_includes_themes_and_canonical_notices_and_excludes_stray_assemblies(
    inputs: Path, tmp_path: Path, staged_notices: bool
) -> None:
    for name in ('Assembly-CSharp.dll', 'UnityEngine.CoreModule.dll', 'local.dll'):
        (inputs / 'mod/Assemblies' / name).write_text('must not ship')
    if staged_notices:
        (inputs / 'mod/About/LICENSE').write_text('stale license')
        notices = inputs / 'mod/About/ThirdPartyNotices'
        notices.mkdir()
        (notices / 'stale.txt').write_text('stale notice')
    destination = tmp_path / 'package/mod'
    source_mod.stage(destination)
    assert (destination / 'Themes/asset').read_text() == 'Themes'
    assert {path.name for path in (destination / 'Assemblies').iterdir()} == {
        f'{assembly}.dll' for assembly in latest.MOD_ASSEMBLIES
    }
    assert (destination / 'About/LICENSE').read_text() == 'canonical license'
    assert [path.name for path in (destination / 'About/ThirdPartyNotices').iterdir()] == ['NOTICE.txt']
    assert (destination / 'About/ThirdPartyNotices/NOTICE.txt').read_text() == 'canonical notice'


def test_missing_required_assembly_fails_staging(inputs: Path, tmp_path: Path) -> None:
    (inputs / 'mod/Assemblies/Tomlyn.dll').unlink()
    with pytest.raises(ValueError, match='Tomlyn.dll'):
        source_mod.stage(tmp_path / 'package/mod')
