"""Protect running executable comparison, managed unit rendering, and restart decisions."""

import filecmp
import subprocess
from pathlib import Path
from typing import Any
from unittest.mock import patch

import pytest

from tools.host import install_daemon as installer
from tools.utils import run as execute


def test_diagnostic_override_is_validated_and_materialized(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    source = tmp_path / 'unit'
    source.write_bytes(b'[Service]\nExecStart=slopd\n')
    monkeypatch.delenv('SLOPWORLD_DEBUG', raising=False)
    assert installer.service_contents(source) == source.read_bytes()
    for value in ('', '0', '1', 'TRUE', 'false'):
        monkeypatch.setenv('SLOPWORLD_DEBUG', value)
        assert installer.service_contents(source).endswith(f'Environment=SLOPWORLD_DEBUG={value}\n'.encode())
    monkeypatch.setenv('SLOPWORLD_DEBUG', 'true\nExecStart=other')
    with pytest.raises(ValueError, match='SLOPWORLD_DEBUG'):
        installer.service_contents(source)


@pytest.mark.parametrize('pid', ['0', '-1', 'invalid'])
def test_unusable_pid_requires_restart(tmp_path: Path, pid: str) -> None:
    results = [subprocess.CompletedProcess([], 0), subprocess.CompletedProcess([], 0, stdout=pid)]
    with patch.object(installer, 'run', side_effect=results):
        assert installer.needs_restart(tmp_path / 'binary', tmp_path / 'unit', b'unit')


def test_restart_compares_running_executable_contents_and_managed_unit(tmp_path: Path) -> None:
    unit = tmp_path / 'unit'
    unit.write_bytes(b'unit')
    results = [subprocess.CompletedProcess([], 0), subprocess.CompletedProcess([], 0, stdout='123\n')]
    with (
        patch.object(installer, 'run', side_effect=results),
        patch.object(filecmp, 'cmp', return_value=True) as compare,
    ):
        assert not installer.needs_restart(tmp_path / 'binary', unit, b'unit')
        compare.assert_called_once_with(tmp_path / 'binary', '/proc/123/exe', shallow=False)
    for matching, contents in [(False, b'unit'), (True, b'changed unit')]:
        with (
            patch.object(installer, 'run', side_effect=results),
            patch.object(filecmp, 'cmp', return_value=matching),
        ):
            assert installer.needs_restart(tmp_path / 'binary', unit, contents)


def test_inactive_service_requires_restart_without_pid_query(tmp_path: Path) -> None:
    with patch.object(installer, 'run', return_value=subprocess.CompletedProcess([], 3)) as run:
        assert installer.needs_restart(tmp_path / 'binary', tmp_path / 'unit', b'unit')
        assert run.call_count == 1


@pytest.mark.parametrize('restart', [False, True])
def test_install_restarts_only_when_needed(tmp_path: Path, monkeypatch: pytest.MonkeyPatch, restart: bool) -> None:
    source = tmp_path / 'build'
    source.mkdir()
    for name in ('slopd', 'slopctl'):
        (source / name).write_bytes(name.encode())
    monkeypatch.setenv('TARGET', str(source))
    monkeypatch.setenv('BIN', str(tmp_path / 'bin'))
    monkeypatch.setenv('UNITS', str(tmp_path / 'units'))
    monkeypatch.setenv('BUILD', 'debug')
    monkeypatch.delenv('SLOPWORLD_DEBUG', raising=False)

    def install_only(arguments: list[str], **kwargs: Any) -> subprocess.CompletedProcess[Any]:
        if arguments[0] == 'install':
            return execute(arguments, **kwargs)
        return subprocess.CompletedProcess(arguments, 0, stdout='status')

    with (
        patch.object(installer, 'needs_restart', return_value=restart),
        patch.object(installer, 'run', side_effect=install_only) as run,
    ):
        installer.main()
    calls = [call.args[0] for call in run.call_args_list]
    assert (['systemctl', '--user', 'restart', 'slopd.service'] in calls) == restart
    assert (tmp_path / 'bin/slopd').read_bytes() == b'slopd'
    unit = (tmp_path / 'units/slopd.service').read_text()
    assert f'"{tmp_path}/bin/slopd"' in unit
    assert ['systemctl', '--user', 'enable', 'slopd.service'] in calls
    assert ['systemctl', '--user', 'enable', '--now', 'slopd.service'] not in calls
