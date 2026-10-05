"""Protect Debian system layout and publication when staging or tools fail."""

import subprocess
from pathlib import Path

import pytest

from tools.release import debian
from tools.release import latest


@pytest.fixture
def package_inputs(tmp_path, monkeypatch):
    root = tmp_path / 'checkout'
    monkeypatch.setattr(debian, 'ROOT', root)
    monkeypatch.setattr(latest, 'ROOT', root)
    for source in ('control', 'README.Debian'):
        target = root / 'packaging/debian' / source
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text((Path(__file__).parents[2] / 'packaging/debian' / source).read_text())
    files = {
        'slopd/slopd.service': 'ExecStart=%h/.local/bin/slopd\nKillMode=process\n',
        'packaging/slopworld.desktop': '[Desktop Entry]\nExec=slopworld\n',
        'mod/Textures/SlopWorld/SlopWorld_icon.png': 'icon fixture',
        'mod/About/LICENSE': 'license',
        'mod/About/ThirdPartyNotices/NOTICE.txt': 'notice',
        'LICENSE': 'license',
        'licenses/NOTICE.txt': 'notice',
    }
    for binary in debian.BINARIES:
        files[f'slopd/target/release/{binary}'] = 'binary fixture'
    for assembly in latest.MOD_ASSEMBLIES:
        files[f'mod/Assemblies/{assembly}.dll'] = 'assembly fixture'
    for name, content in files.items():
        target = root / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content)
    monkeypatch.setattr(latest, 'git', lambda *args: 'mod/Textures/SlopWorld/SlopWorld_icon.png\0')
    return root


@pytest.mark.parametrize('architecture', ['amd64', 'arm64'])
def test_package_system_paths_permissions_dependencies_and_metadata(
    package_inputs, tmp_path, monkeypatch, architecture
):
    def run(arguments, **kwargs):
        if arguments[0] == 'dpkg-shlibdeps':
            assert (kwargs['cwd'] / 'debian/control').is_file()
            assert all(Path(binary).is_file() for binary in arguments[2:])
            return subprocess.CompletedProcess(arguments, 0, 'shlibs:Depends=libasound2t64 (>= 1.0), libc6 (>= 2.38)\n')
        assert arguments[:3] == ['dpkg-deb', '--root-owner-group', '--build']
        staging = Path(arguments[3])
        control = (staging / 'DEBIAN/control').read_text()
        assert f'Architecture: {architecture}\n' in control
        assert 'Version: 0.1.0-20261005-abcdef-1\n' in control
        assert 'libasound2t64 (>= 1.0), libc6 (>= 2.38)' in control
        assert 'passt' in control and 'tmux' in control
        for binary in debian.BINARIES:
            assert (staging / 'usr/bin' / binary).stat().st_mode & 0o777 == 0o755
        unit = (staging / 'usr/lib/systemd/user/slopd.service').read_text()
        assert 'ExecStart=/usr/bin/slopd' in unit and 'KillMode=process' in unit
        mod = staging / 'usr/share/slopworld/SlopWorld'
        assert (mod / 'VERSION').read_text() == '0.1.0-20261005-abcdef\n'
        assert (mod / 'REVISION').read_text() == 'revision\n'
        assert (mod / 'About/ThirdPartyNotices/NOTICE.txt').is_file()
        assert {p.stem for p in (mod / 'Assemblies').iterdir()} == set(latest.MOD_ASSEMBLIES)
        assert (staging / 'usr/share/doc/slopworld/copyright').is_file()
        assert (staging / 'usr/share/doc/slopworld/third-party/NOTICE.txt').is_file()
        assert (staging / 'usr/share/applications/slopworld.desktop').is_file()
        assert (staging / 'usr/share/icons/hicolor/128x128/apps/slopworld.png').is_file()
        assert all(p.stat().st_mode & 0o777 == 0o644 for p in mod.rglob('*') if p.is_file())
        Path(arguments[4]).write_bytes(b'deb archive fixture')

    monkeypatch.setattr(debian, 'run', run)
    result = debian.package(tmp_path / 'output with spaces', 'revision', '0.1.0-20261005-abcdef', architecture)
    assert result.name == f'slopworld_0.1.0-20261005-abcdef-1_{architecture}.deb'
    assert result.read_bytes() == b'deb archive fixture'


@pytest.mark.parametrize('failure', ['input', 'scan', 'build', 'empty-dependencies'])
def test_failed_package_preserves_previous_output(package_inputs, tmp_path, monkeypatch, failure):
    output = tmp_path / 'output'
    output.mkdir()
    previous = output / 'slopworld_1.0.0-1_amd64.deb'
    previous.write_bytes(b'previous package')
    if failure == 'input':
        (package_inputs / 'mod/Assemblies/Tomlyn.dll').unlink()

    def run(arguments, **kwargs):
        if arguments[0] == 'dpkg-shlibdeps' and failure != 'scan':
            return subprocess.CompletedProcess(
                arguments, 0, '' if failure == 'empty-dependencies' else 'shlibs:Depends=libc6\n'
            )
        raise subprocess.CalledProcessError(1, arguments)

    monkeypatch.setattr(debian, 'run', run)
    with pytest.raises((ValueError, subprocess.CalledProcessError)):
        debian.package(output, 'revision', '1.0.0', 'amd64')
    assert previous.read_bytes() == b'previous package'
    assert list(output.iterdir()) == [previous]


@pytest.mark.parametrize('version', ['snapshot', '1.0\nDepends: injected', '1:2.0', '', '../1'])
def test_invalid_upstream_versions_are_rejected(version):
    with pytest.raises(ValueError, match='version'):
        debian.debian_version(version)


@pytest.mark.parametrize('failing_tool', ['dpkg', 'dpkg-deb', 'dpkg-shlibdeps', 'just'])
def test_tool_or_build_failure_stops_before_packaging(monkeypatch, failing_tool):
    from unittest.mock import patch

    monkeypatch.setattr('sys.argv', ['debian'])
    monkeypatch.setenv('JUST_CMD', 'just')
    monkeypatch.setenv('VERSION', '1.0.0')
    monkeypatch.setattr(latest, 'git', lambda *args: 'revision')

    def run(arguments, **kwargs):
        if arguments[0] == failing_tool:
            raise subprocess.CalledProcessError(1, arguments)
        return subprocess.CompletedProcess(arguments, 0, 'amd64\n')

    with patch.object(debian, 'run', side_effect=run), patch.object(debian, 'package') as package:
        with pytest.raises(subprocess.CalledProcessError):
            debian.main()
        package.assert_not_called()


def test_real_debian_archive_round_trip(package_inputs, tmp_path):
    import shutil

    if not all(shutil.which(tool) for tool in ('dpkg', 'dpkg-deb', 'dpkg-shlibdeps')):
        pytest.skip('Debian packaging tools are required for archive integration')
    architecture = debian.run(['dpkg', '--print-architecture'], capture_output=True, text=True).stdout.strip()
    if architecture not in ('amd64', 'arm64'):
        pytest.skip('Unsupported native Debian architecture')
    # Native ELF fixtures exercise the installed library database and archive tools
    # without compiling Rust or depending on proprietary game assemblies.
    for binary in debian.BINARIES:
        shutil.copyfile('/bin/true', package_inputs / 'slopd/target/release' / binary)
    archive = debian.package(tmp_path / 'output', 'revision', '1.0.0', architecture)
    result = debian.run(['dpkg-deb', '--field', str(archive), 'Depends'], capture_output=True, text=True)
    assert 'libc6' in result.stdout
    extracted = tmp_path / 'extracted'
    debian.run(['dpkg-deb', '--extract', str(archive), str(extracted)])
    assert (extracted / 'usr/share/slopworld/SlopWorld/VERSION').read_text() == '1.0.0\n'
    assert (extracted / 'usr/bin/slopd').stat().st_mode & 0o111
    result = debian.run(['dpkg-deb', '--fsys-tarfile', str(archive)], capture_output=True)
    import io
    import tarfile

    with tarfile.open(fileobj=io.BytesIO(result.stdout)) as contents:
        assert all(member.uid == 0 and member.gid == 0 for member in contents.getmembers())
