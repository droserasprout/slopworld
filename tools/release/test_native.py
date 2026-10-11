"""Native CLI validates inputs and stops before packaging on build failures."""

import platform
import subprocess
from pathlib import Path
from unittest.mock import patch

import pytest

from tools.release import arch
from tools.release import debian
from tools.release import fedora
from tools.release import native


@pytest.mark.parametrize('distribution', ['arch', 'debian', 'fedora'])
@pytest.mark.parametrize('failure', ['probe', 'build', None])
def test_local_build_precedes_packaging(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path, distribution: str, failure: str | None
) -> None:
    monkeypatch.setattr('sys.argv', ['native', distribution])
    monkeypatch.setenv('JUST_CMD', 'just')
    monkeypatch.setenv('VERSION', '1.0.0')
    monkeypatch.setenv('ARCH_DIR', str(tmp_path))
    monkeypatch.setenv('DEB_DIR', str(tmp_path))
    monkeypatch.setenv('RPM_DIR', str(tmp_path))
    monkeypatch.setattr(platform, 'machine', lambda: 'x86_64')
    commands = []

    def run(arguments: list[str], **kwargs: object) -> subprocess.CompletedProcess[str]:
        commands.append(arguments)
        if (failure == 'probe' and '--version' in arguments) or (failure == 'build' and arguments[0] == 'just'):
            raise subprocess.CalledProcessError(1, arguments)
        return subprocess.CompletedProcess(arguments, 0, 'amd64\n' if arguments[0] == 'dpkg' else 'revision\n')

    packager = {'arch': arch, 'debian': debian, 'fedora': fedora}[distribution]
    with patch.object(native, 'run', side_effect=run), patch.object(packager, 'package') as package:
        if failure:
            with pytest.raises(subprocess.CalledProcessError):
                native.main()
            package.assert_not_called()
        else:
            native.main()
            assert commands[-1] == ['just', 'BUILD=release', 'VERSION=1.0.0', 'all']
            package.assert_called_once_with(
                tmp_path, 'revision', '1.0.0', 'amd64' if distribution == 'debian' else 'x86_64'
            )


@pytest.mark.parametrize('distribution', ['arch', 'debian', 'fedora'])
def test_stage_only_uses_supplied_inputs_without_building(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path, distribution: str
) -> None:
    monkeypatch.setattr(
        'sys.argv',
        [
            'native',
            distribution,
            '--stage-only',
            '--binary-dir',
            str(tmp_path),
            '--output',
            str(tmp_path),
            '--revision',
            'revision',
            '--version',
            '1.0.0',
        ],
    )
    monkeypatch.setattr(platform, 'machine', lambda: 'x86_64')
    packager = {'arch': arch, 'debian': debian, 'fedora': fedora}[distribution]
    with (
        patch.object(native, 'run', return_value=subprocess.CompletedProcess([], 0, 'amd64\n')) as run,
        patch.object(packager, 'package') as package,
    ):
        native.main()
        assert all(call.args[0] == ['dpkg', '--print-architecture'] for call in run.call_args_list)
        package.assert_called_once_with(
            tmp_path, 'revision', '1.0.0', 'amd64' if distribution == 'debian' else 'x86_64', tmp_path
        )
