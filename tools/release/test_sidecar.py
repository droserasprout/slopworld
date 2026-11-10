"""Protect separate sidecar packages, dependencies, and release handoff."""

import subprocess
from pathlib import Path
from typing import Any

import pytest

from tools import ROOT
from tools.release import sidecar
from tools.release import staging


@pytest.fixture
def binary(tmp_path: Path) -> Path:
    path = tmp_path / 'bin/slopcar'
    path.parent.mkdir()
    path.write_bytes(b'fixture binary, never executed\n')
    return path


def test_package_payload_excludes_daemon_and_game(tmp_path: Path, binary: Path) -> None:
    payload = tmp_path / 'payload'
    staging.stage_sidecar(payload, binary.parent, 'revision', '1.2.3')
    assert [path.name for path in (payload / 'usr/bin').iterdir()] == ['slopcar']
    assert not (payload / 'usr/lib').exists()
    assert not (payload / 'usr/share/slopworld').exists()
    assert (payload / 'usr/share/licenses/slopcar/THIRD-PARTY-NOTICES.txt').is_file()
    assert (payload / 'usr/bin/slopcar').stat().st_mode & 0o777 == 0o755


@pytest.mark.parametrize(
    'distribution,architecture',
    [('arch', 'x86_64'), ('debian', 'amd64'), ('debian', 'arm64'), ('fedora', 'x86_64'), ('fedora', 'aarch64')],
)
def test_distro_packages_have_only_sidecar_payload_and_docker_dependency(
    tmp_path: Path,
    binary: Path,
    monkeypatch: pytest.MonkeyPatch,
    distribution: str,
    architecture: str,
) -> None:
    from tools.release import arch
    from tools.release import debian
    from tools.release import fedora

    module = {'arch': arch, 'debian': debian, 'fedora': fedora}[distribution]
    monkeypatch.setenv('SOURCE_DATE_EPOCH', '1234567890')
    metadata = []

    def run(arguments: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        if arguments[0] == 'dpkg-shlibdeps':
            assert [Path(arg).name for arg in arguments[2:]] == ['slopcar']
            return subprocess.CompletedProcess(arguments, 0, 'shlibs:Depends=libc6 (>= 2.34)\n')
        if arguments[0] == 'dpkg-deb':
            payload = Path(arguments[-2])
            metadata.append((payload / 'DEBIAN/control').read_text())
            output = Path(arguments[-1])
        elif arguments[0] == 'rpmbuild':
            spec = Path(arguments[-1])
            metadata.append(spec.read_text())
            workspace = spec.parent.parent
            payload = workspace / 'SOURCES/payload'
            output = workspace / f'RPMS/slopcar-1.2.3-1.{architecture}.rpm'
        elif 'makepkg' in arguments:
            workspace = Path(kwargs['cwd'])
            payload = workspace / 'payload'
            assert 'PKGBUILD.slopcar' in (workspace / 'PKGBUILD').read_text()
            assert not (workspace / 'slopworld.install').exists()
            metadata.append((ROOT / 'packaging/arch/PKGBUILD.slopcar').read_text())
            output = workspace / 'slopcar-1.2.3-1-x86_64.pkg.tar.zst'
        else:
            return subprocess.CompletedProcess(arguments, 0, '')
        assert sorted(path.name for path in (payload / 'usr/bin').iterdir()) == ['slopcar']
        assert not (payload / 'usr/share/slopworld').exists()
        assert not (payload / 'usr/lib/systemd').exists()
        assert (payload / 'usr/share/doc/slopcar/VERSION').read_text() == '1.2.3\n'
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_bytes(b'fixture package')
        return subprocess.CompletedProcess(arguments, 0, '')

    monkeypatch.setattr(module, 'run', run)
    result = module.package(tmp_path / 'output', 'revision', '1.2.3', architecture, binary.parent, product='slopcar')
    assert result.read_bytes() == b'fixture package'
    assert metadata and 'docker' in metadata[0]
    assert not any(dependency in metadata[0] for dependency in ('bubblewrap', 'alsa-lib', 'tmux', 'dotnet'))


@pytest.mark.parametrize('architecture', ['amd64', 'arm64'])
def test_container_builds_only_sidecar_for_requested_architecture(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    architecture: str,
) -> None:
    from tools.release import container

    monkeypatch.setenv('CONTAINER', 'docker')
    monkeypatch.setenv('CONTAINER_BUILD_ARGS', '')
    monkeypatch.setenv('CONTAINER_RUN_ARGS', '')
    calls = []

    def run(arguments: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        calls.append(arguments)
        return subprocess.CompletedProcess(arguments, 0, '.git\n')

    monkeypatch.setattr(container, 'run', run)
    container.compile_package(
        tmp_path, 'debian', tmp_path / 'output', 'revision', '1.2.3', product='slopcar', architecture=architecture
    )
    invocation = calls[-1]
    assert invocation[:4] == ['docker', 'run', '--platform', f'linux/{architecture}']
    script = invocation[invocation.index('-euc') + 1]
    assert 'cargo build --manifest-path slopcar/Cargo.toml' in script
    assert '--product slopcar' in script
    assert 'cargo build --manifest-path slopd/' not in script


def test_sidecar_release_failure_preserves_previous_outputs(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, binary: Path
) -> None:
    from tools.release import container

    output = tmp_path / 'output'
    output.mkdir()
    for name in sidecar.asset_names('1.2.3', 'amd64'):
        (output / name).write_bytes(b'previous')

    def fail(*args: object, **kwargs: object) -> None:
        raise ValueError('package failed')

    monkeypatch.setattr(container, 'package', fail)
    with pytest.raises(ValueError, match='package failed'):
        sidecar.build(output, 'revision', '1.2.3', 'amd64')
    assert {path.read_bytes() for path in output.iterdir()} == {b'previous'}


@pytest.mark.parametrize('distribution', ['arch', 'debian', 'fedora'])
def test_real_sidecar_package_round_trip(
    tmp_path: Path,
    binary: Path,
    monkeypatch: pytest.MonkeyPatch,
    distribution: str,
) -> None:
    import platform
    import shutil

    from tools.release import arch
    from tools.release import debian
    from tools.release import fedora

    required = {
        'arch': ('makepkg', 'pacman', 'bsdtar', 'bwrap'),
        'debian': ('dpkg', 'dpkg-deb', 'dpkg-shlibdeps'),
        'fedora': ('rpm', 'rpmbuild'),
    }[distribution]
    if not all(shutil.which(tool) for tool in required):
        pytest.skip(f'{distribution} packaging tools are required')
    machine = platform.machine()
    if machine not in ('x86_64', 'aarch64') or (distribution == 'arch' and machine != 'x86_64'):
        pytest.skip('unsupported architecture')
    architecture = {'x86_64': 'amd64', 'aarch64': 'arm64'}[machine] if distribution == 'debian' else machine
    shutil.copyfile('/bin/true', binary)
    monkeypatch.setenv('SOURCE_DATE_EPOCH', '1234567890')
    module = {'arch': arch, 'debian': debian, 'fedora': fedora}[distribution]
    archive = module.package(tmp_path / 'output', 'revision', '1.2.3', architecture, binary.parent, product='slopcar')

    def output(arguments: list[str]) -> str:
        return subprocess.run(arguments, capture_output=True, text=True, check=True).stdout

    if distribution == 'debian':
        requires = output(['dpkg-deb', '--field', str(archive), 'Depends'])
        assert 'libc6' in requires and 'docker' in requires
        files = output(['dpkg-deb', '--contents', str(archive)])
    elif distribution == 'fedora':
        requires = output(['rpm', '-qp', '--requires', str(archive)])
        assert 'libc.so.6' in requires and 'docker' in requires
        files = output(['rpm', '-qpl', str(archive)])
    else:
        requires = output(['bsdtar', '-xOf', str(archive), '.PKGINFO'])
        assert 'depend = docker' in requires
        files = output(['bsdtar', '-tf', str(archive)])
    assert all(dependency not in requires for dependency in ('bubblewrap', 'alsa-lib', 'tmux', 'dotnet'))
    assert 'usr/bin/slopcar' in files and 'site-functions/_slopcar' in files
    assert 'licenses/slopcar' in files
    assert 'usr/bin/slopd' not in files and 'systemd/' not in files and 'usr/share/slopworld/' not in files
