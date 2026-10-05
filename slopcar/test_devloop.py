"""Sidecar iterations keep configured environment and continue after command failure."""

import subprocess
from unittest.mock import patch

from slopcar import devloop


def test_iteration_preserves_start_arguments_and_recovers_from_failed_commands(monkeypatch):
    for setting in ('SLOPCAR_CONFIG_DIR', 'SLOPCAR_DATA_DIR', 'SLOPCAR_PORT', 'SLOPCAR_CONTAINER'):
        monkeypatch.setenv(setting, 'configured')
    monkeypatch.setenv('JUST_CMD', '/tmp/just tool')
    with patch.object(devloop, 'run', return_value=subprocess.CompletedProcess([], 23)) as run:
        devloop.iteration(['--workspace', '/tmp/work with spaces'])
    calls = [call.args[0] for call in run.call_args_list]
    assert calls[0] == ['/tmp/just tool', 'sidecar-build']
    assert calls[1] == ['/tmp/just tool', 'install-mod']
    assert calls[2][1:] == ['rm']
    assert calls[3][1:] == ['start', '--workspace', '/tmp/work with spaces']
    assert calls[4] == ['/tmp/just tool', 'sidecar-run']
    assert all(call.kwargs['check'] is False for call in run.call_args_list)
