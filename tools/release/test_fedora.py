"""Check RPM contents, dependencies, and atomic publication without the game."""

import platform
import shutil
import subprocess
from pathlib import Path
from typing import Any

import pytest

from tools import ROOT
from tools.release import fedora
from tools.release import staging as release_staging
from tools.utils import run as execute


def test_epoch_uses_packaged_revision(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv('SOURCE_DATE_EPOCH', raising=False)

    def run(arguments: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        assert arguments == ['git', 'show', '-s', '--format=%ct', '--end-of-options', 'release-revision']
        assert kwargs['cwd'] == ROOT
        return subprocess.CompletedProcess(arguments, 0, '1792454400\n')

    monkeypatch.setattr(fedora, 'run', run)
    assert fedora.source_date_epoch('release-revision') == '1792454400'


@pytest.mark.parametrize('epoch', ['', '-1', 'today', '123\n'])
def test_invalid_epoch_is_rejected(monkeypatch: pytest.MonkeyPatch, epoch: str) -> None:
    monkeypatch.setenv('SOURCE_DATE_EPOCH', epoch)
    with pytest.raises(ValueError, match='SOURCE_DATE_EPOCH'):
        fedora.source_date_epoch('unused')


@pytest.fixture
def package_inputs(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Path:
    monkeypatch.setenv('SOURCE_DATE_EPOCH', '1792454400')
    root = tmp_path / 'checkout'
    monkeypatch.setattr(fedora, 'ROOT', root)
    monkeypatch.setattr(release_staging, 'ROOT', root)
    for source in ('slopworld.spec', 'README.Fedora'):
        target = root / 'packaging/fedora' / source
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text((Path(__file__).parents[2] / 'packaging/fedora' / source).read_text())
    files = {
        'slopd/slopd.service': 'ExecStart=%h/.local/bin/slopd\nKillMode=process\n',
        'packaging/slopworld.desktop': '[Desktop Entry]\nExec=slopworld\n',
        'mod/Textures/SlopWorld/SlopWorld_icon.png': 'icon fixture',
        'LICENSE': 'license',
        'licenses/NOTICE.txt': 'notice',
    }
    for binary in release_staging.BINARIES:
        files[f'slopd/target/release/{binary}'] = 'binary fixture'
    for assembly in release_staging.MOD_ASSEMBLIES:
        files[f'mod/Assemblies/{assembly}.dll'] = 'assembly fixture'
    for name, content in files.items():
        target = root / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content)
    monkeypatch.setattr(
        release_staging,
        'run',
        lambda *args, **kwargs: subprocess.CompletedProcess([], 0, 'mod/Textures/SlopWorld/SlopWorld_icon.png\0'),
    )
    return root


@pytest.mark.parametrize('architecture', ['x86_64', 'aarch64'])
def test_system_layout_permissions_and_snapshot_version(
    package_inputs: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, architecture: str
) -> None:
    def run(arguments: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        if arguments[0] == 'rpm':
            assert arguments[:3] == ['rpm', '-qp', '--requires']
            return subprocess.CompletedProcess(arguments, 0, 'libc.so.6()(64bit)\n')
        assert arguments[:4] == ['rpmbuild', '-bb', '--target', architecture]
        assert kwargs['env']['SOURCE_DATE_EPOCH'] == '1792454400'
        workspace = Path(arguments[5].removeprefix('_topdir '))
        assert arguments[7] == 'slopworld_version 0.1.0.20261005.abcdef'
        payload = workspace / 'SOURCES/payload'
        for binary in release_staging.BINARIES:
            assert (payload / 'usr/bin' / binary).stat().st_mode & 0o777 == 0o755
        unit = (payload / 'usr/lib/systemd/user/slopd.service').read_text()
        assert 'ExecStart=/usr/bin/slopd' in unit and 'KillMode=process' in unit
        mod = payload / 'usr/share/slopworld/SlopWorld'
        assert (mod / 'VERSION').read_text() == '0.1.0-20261005-abcdef\n'
        assert (mod / 'REVISION').read_text() == 'revision\n'
        assert {p.stem for p in (mod / 'Assemblies').iterdir()} == set(release_staging.MOD_ASSEMBLIES)
        for name in (
            'usr/share/licenses/slopworld/LICENSE',
            'usr/share/licenses/slopworld/third-party/NOTICE.txt',
            'usr/share/doc/slopworld/README.Fedora',
            'usr/share/applications/slopworld.desktop',
            'usr/share/icons/hicolor/128x128/apps/slopworld.png',
            'usr/share/slopworld/SlopWorld/About/ThirdPartyNotices/NOTICE.txt',
        ):
            assert (payload / name).is_file()
        assert all(p.stat().st_mode & 0o777 == 0o644 for p in mod.rglob('*') if p.is_file())
        archive = workspace / f'RPMS/{architecture}/slopworld-0.1.0.20261005.abcdef-1.{architecture}.rpm'
        archive.parent.mkdir(parents=True)
        archive.write_bytes(b'rpm fixture')
        return subprocess.CompletedProcess(arguments, 0, '')

    monkeypatch.setattr(fedora, 'run', run)
    result = fedora.package(tmp_path / 'output with spaces', 'revision', '0.1.0-20261005-abcdef', architecture)
    assert result.name == f'slopworld-0.1.0.20261005.abcdef-1.{architecture}.rpm'
    assert result.read_bytes() == b'rpm fixture'


@pytest.mark.parametrize('failure', ['input', 'build', 'missing-archive', 'extra-archive', 'query'])
def test_failed_package_preserves_previous_output(
    package_inputs: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch, failure: str
) -> None:
    output = tmp_path / 'output'
    output.mkdir()
    previous = output / 'slopworld-1.0.0-1.x86_64.rpm'
    previous.write_bytes(b'previous package')
    if failure == 'input':
        (package_inputs / 'mod/Assemblies/Tomlyn.dll').unlink()

    def run(arguments: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        if failure == 'build' or arguments[0] == 'rpm':
            raise subprocess.CalledProcessError(1, arguments)
        if failure != 'missing-archive':
            directory = Path(arguments[5].removeprefix('_topdir ')) / 'RPMS/x86_64'
            directory.mkdir(parents=True)
            (directory / previous.name).write_bytes(b'new archive')
            if failure == 'extra-archive':
                (directory / 'unexpected.rpm').write_bytes(b'extra archive')
        return subprocess.CompletedProcess(arguments, 0, '')

    monkeypatch.setattr(fedora, 'run', run)
    with pytest.raises((ValueError, subprocess.CalledProcessError)):
        fedora.package(output, 'revision', '1.0.0', 'x86_64')
    assert previous.read_bytes() == b'previous package'
    assert list(output.iterdir()) == [previous]


@pytest.mark.parametrize('version', ['snapshot', '1.0\nRequires: injected', '1:2.0', '', '../1', '1%{lua:evil}', '1~2'])
def test_invalid_upstream_versions_are_rejected(version: str) -> None:
    with pytest.raises(ValueError, match='version'):
        fedora.rpm_version(version)


def test_unsupported_architecture_is_rejected(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match='architecture'):
        fedora.package(tmp_path, 'revision', '1.0.0', 'noarch')
    assert not list(tmp_path.iterdir())


def test_real_rpm_archive_round_trip(package_inputs: Path, tmp_path: Path) -> None:
    if not all(shutil.which(tool) for tool in ('rpm', 'rpmbuild', 'rpm2cpio', 'cpio')):
        pytest.skip('Fedora packaging tools are required for archive integration')
    architecture = platform.machine()
    if architecture not in ('x86_64', 'aarch64'):
        pytest.skip('Unsupported native Fedora architecture')
    # Native ELF fixtures exercise library discovery without compiling Rust.
    for binary in release_staging.BINARIES:
        shutil.copyfile('/bin/true', package_inputs / 'slopd/target/release' / binary)
    archive = fedora.package(tmp_path / 'output with spaces', 'revision', '1.0.0', architecture)
    metadata = execute(
        ['rpm', '-qp', '--queryformat', '%{VERSION} %{RELEASE} %{ARCH}', str(archive)],
        capture_output=True,
        text=True,
    ).stdout
    assert metadata == f'1.0.0 1 {architecture}'
    build_time = execute(
        ['rpm', '-qp', '--queryformat', '%{BUILDTIME}', str(archive)], capture_output=True, text=True
    ).stdout
    assert build_time == '1792454400'
    requires = execute(['rpm', '-qp', '--requires', str(archive)], capture_output=True, text=True).stdout
    assert 'libc.so.6' in requires
    assert all(dependency in requires.splitlines() for dependency in ('bubblewrap', 'passt', 'tmux'))
    assert 'mono' not in requires.lower()
    assert not execute(['rpm', '-qp', '--scripts', str(archive)], capture_output=True, text=True).stdout.strip()
    owners = execute(
        ['rpm', '-qp', '--queryformat', '[%{FILEUSERNAME}:%{FILEGROUPNAME}\n]', str(archive)],
        capture_output=True,
        text=True,
    ).stdout.splitlines()
    assert owners and set(owners) == {'root:root'}
    extracted = tmp_path / 'extracted'
    extracted.mkdir()
    # Separate commands expose conversion and extraction failures.
    payload = execute(['rpm2cpio', str(archive)], capture_output=True).stdout
    execute(['cpio', '-idm', '--quiet'], cwd=extracted, input=payload, capture_output=True)
    mod = extracted / 'usr/share/slopworld/SlopWorld'
    assert (mod / 'VERSION').read_text() == '1.0.0\n'
    assert (mod / 'About/ThirdPartyNotices/NOTICE.txt').is_file()
    assert (extracted / 'usr/bin/slopd').stat().st_mode & 0o777 == 0o755
    assert 'ExecStart=/usr/bin/slopd' in (extracted / 'usr/lib/systemd/user/slopd.service').read_text()
