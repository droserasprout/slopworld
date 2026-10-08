"""Source installs stage canonical notices without refreshing the checkout."""

import subprocess
from pathlib import Path
from unittest.mock import patch

import pytest

from tools.host import install_mod
from tools.release import source_mod
from tools.release import staging


@pytest.fixture
def checkout(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Path:
    root = tmp_path / 'checkout'
    monkeypatch.setattr(source_mod, 'ROOT', root)
    for directory in staging.MOD_DIRECTORIES:
        (root / 'mod' / directory).mkdir(parents=True)
    (root / 'mod/Assemblies').mkdir()
    for assembly in staging.MOD_ASSEMBLIES:
        (root / 'mod/Assemblies' / f'{assembly}.dll').write_text(assembly)
    (root / 'LICENSE').write_text('canonical license')
    (root / 'licenses').mkdir()
    (root / 'licenses/NOTICE.txt').write_text('canonical notice')
    return root


@pytest.mark.parametrize('staged_notices', [False, True])
@pytest.mark.parametrize('install_fails', [False, True])
def test_install_uses_canonical_notices_and_cleans_temporary_source(
    checkout: Path, staged_notices: bool, install_fails: bool
) -> None:
    if staged_notices:
        (checkout / 'mod/About/LICENSE').write_text('stale license')
        notices = checkout / 'mod/About/ThirdPartyNotices'
        notices.mkdir()
        (notices / 'stale.txt').write_text('stale notice')
    before = {p.relative_to(checkout): p.read_bytes() for p in checkout.rglob('*') if p.is_file()}
    sources: list[Path] = []

    def run(arguments: list[str]) -> None:
        assert arguments[:4] == ['/runner with spaces/slopworld', 'mod', 'install', '--source']
        assert arguments[5:] == ['--game', '/game with spaces']
        source = Path(arguments[4])
        sources.append(source)
        assert (source / 'About/LICENSE').read_text() == 'canonical license'
        assert [p.name for p in (source / 'About/ThirdPartyNotices').iterdir()] == ['NOTICE.txt']
        assert (source / 'About/ThirdPartyNotices/NOTICE.txt').read_text() == 'canonical notice'
        if install_fails:
            raise subprocess.CalledProcessError(1, arguments)

    with patch.object(install_mod, 'run', side_effect=run):
        if install_fails:
            with pytest.raises(subprocess.CalledProcessError):
                install_mod.install('/runner with spaces/slopworld', '/game with spaces')
        else:
            install_mod.install('/runner with spaces/slopworld', '/game with spaces')
    assert len(sources) == 1
    assert not sources[0].parent.exists()
    assert {p.relative_to(checkout): p.read_bytes() for p in checkout.rglob('*') if p.is_file()} == before


def test_staging_failure_does_not_invoke_installer(checkout: Path) -> None:
    (checkout / 'mod/Assemblies/Tomlyn.dll').unlink()
    with patch.object(install_mod, 'run') as run:
        with pytest.raises(ValueError, match='Tomlyn.dll'):
            install_mod.install('/runner', '/game')
    run.assert_not_called()
