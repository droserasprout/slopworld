"""Exercise native package construction and its metadata without game access."""

import shutil
from pathlib import Path

import pytest

from tools.release import arch
from tools.release import staging
from tools.utils import run as execute


def test_local_source_snapshot_excludes_build_output_and_keeps_sources(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = tmp_path / 'checkout'
    start = root / 'packaging/arch'
    staging.copy_file(Path(__file__).parents[2] / 'packaging/arch/PKGBUILD.local', start / 'PKGBUILD.local')
    excluded = (
        'slopd/target',
        'slopcar/target',
        'mod/Source/SlopWorld/bin',
        'mod/Source/SlopWorld/obj',
        'mod/Dependencies/bin',
        'mod/Dependencies/obj',
        'mod/Tests/bin',
        'mod/Tests/obj',
        'bench/ipc/rust/target',
        'bench/ipc/csharp/bin',
        'bench/ipc/csharp/obj',
        'bench/terminal-input/tests/http_transport/bin',
        'bench/terminal-input/tests/http_transport/obj',
        'coverage',
    )
    kept = ('slopd/src/bin/slopctl.rs', 'slopcar/src/main.rs', 'mod/Tests/Program.cs', 'bench/ipc/rust/src/main.rs')
    for name in (*[f'{directory}/cached-output' for directory in excluded], *kept):
        target = root / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text('fixture')
    game = tmp_path / 'game'
    managed = game
    managed.mkdir(parents=True)
    (managed / 'Assembly-CSharp.dll').touch()
    # Just exports MANAGED; the snapshot fixture must not depend on a host install.
    monkeypatch.setenv('MANAGED', str(tmp_path / 'missing-host-game'))
    execute(
        [
            'bash',
            '-euc',
            (
                'startdir="$1/packaging/arch"; srcdir="$startdir/src"; pkgdir="$startdir/pkg"; '
                'RIMWORLD="$2"; MANAGED="$2"; source "$startdir/PKGBUILD.local"; '
                'python() { printf "1.0.0\\n"; }; cargo() { :; }; uv() { :; }; prepare'
            ),
            'snapshot-test',
            str(root),
            str(game),
        ]
    )
    snapshot = start / 'src/slopworld'
    assert all(not (snapshot / directory).exists() for directory in excluded)
    assert all((snapshot / name).read_text() == 'fixture' for name in kept)


def test_real_arch_package_metadata_and_safe_payload(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    if not all(shutil.which(tool) for tool in ('makepkg', 'pacman', 'fakeroot', 'bwrap')):
        pytest.skip('Arch packaging tools are required')
    root = tmp_path / 'checkout'
    root.mkdir()
    monkeypatch.setattr(arch, 'ROOT', root)
    monkeypatch.setattr(staging, 'ROOT', root)
    for relative in (
        'packaging/arch/PKGBUILD.local',
        'packaging/arch/slopworld.install',
        'packaging/arch/slopworld.rules',
        'packaging/slopworld.desktop',
        'README.md',
        'LICENSE',
    ):
        staging.copy_file(Path(__file__).parents[2] / relative, root / relative)
    for name in ('slopd', 'slopctl', 'slopworld'):
        staging.copy_file(Path('/bin/true'), root / 'slopd/target/release' / name)
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
    archive = arch.package(output, 'revision', '1.0.0', 'x86_64', mod=mod)
    database = tmp_path / 'pacman-db'
    database.mkdir()
    result = execute(['pacman', '--dbpath', str(database), '-Qip', str(archive)], capture_output=True, text=True)
    assert '1.0.0-1' in result.stdout
    result = execute(['bsdtar', '-tf', str(archive)], capture_output=True, text=True)
    assert '.BUILDINFO' in result.stdout and '.MTREE' in result.stdout
    assert 'usr/share/slopworld/SlopWorld/VERSION' in result.stdout
    assert 'usr/bin/slopd' in result.stdout


def test_repeated_builds_and_failed_validation_preserve_published_package(
    tmp_path: Path,
) -> None:
    from unittest.mock import patch

    output = tmp_path / 'output'
    binary_dir = tmp_path / 'binaries'
    mod = tmp_path / 'mod'
    mod.mkdir()

    def build(workspace: Path, version: str, binaries: Path, staged_mod: Path) -> Path:
        assert binaries == binary_dir and staged_mod == mod
        archive = workspace / 'slopworld-1.0.0-x86_64.pkg.tar.zst'
        archive.write_bytes(b'package')
        return archive

    with patch.object(arch, 'build_archive', side_effect=build):
        for _ in range(2):
            result = arch.package(output, 'revision', '1.0.0', 'x86_64', binary_dir, mod=mod)
            assert result.read_bytes() == b'package'
            assert list(output.iterdir()) == [result]
    with (
        patch.object(arch, 'build_archive', side_effect=ValueError('invalid archive')),
        pytest.raises(ValueError, match='invalid archive'),
    ):
        arch.package(output, 'revision', '1.0.0', 'x86_64', binary_dir, mod=mod)
    assert result.read_bytes() == b'package'
    assert list(output.iterdir()) == [result]
