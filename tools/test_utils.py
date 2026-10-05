"""Process helpers preserve arguments, failure status, and secret boundaries."""

import subprocess
import sys
from unittest.mock import patch

import pytest

from tools import ROOT
from tools import utils


def test_command_overrides_preserve_quoting_without_shell_execution(monkeypatch):
    monkeypatch.setenv('TEST_TOOL', '"/tmp/tools with spaces/python" -X dev "$(false)"')
    assert utils.command('TEST_TOOL', 'python') == ['/tmp/tools with spaces/python', '-X', 'dev', '$(false)']
    monkeypatch.setenv('TEST_TOOL', '')
    with pytest.raises(ValueError, match='must name a command'):
        utils.command('TEST_TOOL', 'python')


def test_process_helpers_default_to_checkout_and_propagate_failure(tmp_path):
    with patch.object(utils.subprocess, 'run') as run:
        utils.run(['tool', 'one argument'])
        run.assert_called_once_with(['tool', 'one argument'], cwd=ROOT, check=True)
    result = utils.run([sys.executable, '-c', 'raise SystemExit(23)'], cwd=tmp_path, check=False)
    assert result.returncode == 23
    with pytest.raises(subprocess.CalledProcessError):
        utils.run([sys.executable, '-c', 'raise SystemExit(23)'])
    child = utils.spawn([sys.executable, '-c', 'print("ready")'], cwd=tmp_path, stdout=subprocess.PIPE, text=True)
    output, _ = child.communicate(timeout=10)
    assert child.returncode == 0
    assert output.strip() == 'ready'


@pytest.mark.parametrize('status,expected', [(23, 23), (-15, 143)])
def test_cli_failure_does_not_print_secret_arguments(status, expected, capsys):
    def fail():
        raise subprocess.CalledProcessError(status, ['gogdl', '--code', 'secret'])

    with pytest.raises(SystemExit) as error:
        utils.run_main(fail)
    assert error.value.code == expected
    assert 'secret' not in capsys.readouterr().err


def test_cli_interrupt_and_missing_setting_are_reported(capsys):
    for exception, status in [
        (KeyboardInterrupt(), 130),
        (KeyError('REQUIRED_SETTING'), 1),
        (OSError('unavailable'), 1),
    ]:

        def fail():
            raise exception

        with pytest.raises(SystemExit) as error:
            utils.run_main(fail)
        assert error.value.code == status
    assert 'missing setting REQUIRED_SETTING' in capsys.readouterr().err
