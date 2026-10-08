"""Exercise native package construction and its metadata without game access."""

import shutil
from pathlib import Path

import pytest

from tools.release import arch
from tools.release import latest
from tools.utils import run as execute


def test_real_arch_package_metadata_and_safe_payload(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    if not all(shutil.which(tool) for tool in ('makepkg', 'pacman', 'fakeroot', 'bwrap')):
        pytest.skip('Arch packaging tools are required')
    root = tmp_path / 'checkout'
    root.mkdir()
    monkeypatch.setattr(arch, 'ROOT', root)
    for relative in (
        'packaging/arch/PKGBUILD.local',
        'packaging/arch/slopworld.install',
        'packaging/arch/slopworld.rules',
        'packaging/slopworld.desktop',
        'README.md',
        'LICENSE',
    ):
        latest.copy_file(Path(__file__).parents[2] / relative, root / relative)
    for name in ('slopd', 'slopctl', 'slopworld'):
        latest.copy_file(Path('/bin/true'), root / 'slopd/target/release' / name)
    (root / 'slopd/slopd.service').write_text('ExecStart=%h/.local/bin/slopd\nKillMode=process\n')
    notices = root / 'licenses'
    notices.mkdir()
    (notices / 'NOTICE.txt').write_text('fixture notice')
    icon = root / 'mod/Textures/SlopWorld/SlopWorld_icon.png'
    icon.parent.mkdir(parents=True)
    icon.write_bytes(b'icon fixture')
    mod = tmp_path / 'mod'
    mod.mkdir()
    (mod / 'VERSION').write_text('1.0.0\n')
    output = root / 'dist/output'
    output.mkdir(parents=True)
    archive = arch.package(output, 'revision', '1.0.0', mod)
    database = tmp_path / 'pacman-db'
    database.mkdir()
    result = execute(['pacman', '--dbpath', str(database), '-Qip', str(archive)], capture_output=True, text=True)
    assert '1.0.0-1' in result.stdout
    result = execute(['bsdtar', '-tf', str(archive)], capture_output=True, text=True)
    assert '.BUILDINFO' in result.stdout and '.MTREE' in result.stdout
    assert 'usr/share/slopworld/SlopWorld/VERSION' in result.stdout
    assert 'usr/bin/slopd' in result.stdout
