"""Startup configures accounts before handing commands to an unprivileged process."""

import json
import os
import subprocess
from pathlib import Path

import pytest


@pytest.fixture
def startup(tmp_path):
    # Simulate privileged tools without touching the host's accounts or ownership.
    helper = tmp_path / 'helper'
    helper.write_text(
        '#!/usr/bin/env python3\n'
        'import json, os, pathlib, sys\n'
        'name = pathlib.Path(sys.argv[0]).name\n'
        'if name == "id":\n'
        '    print(os.environ.get("TEST_UID", "0"))\n'
        '    sys.exit(0)\n'
        'with open(os.environ["TEST_LOG"], "a") as stream:\n'
        '    stream.write(json.dumps([name, *sys.argv[1:]]) + "\\n")\n'
        'if name == os.environ.get("TEST_FAIL"):\n'
        '    sys.exit(19)\n'
        'if name == "setpriv":\n'
        '    command = sys.argv[sys.argv.index("--") + 1:]\n'
        '    os.environ["TEST_UID"] = os.environ.get("PUID", "1000")\n'
        '    os.execv("/bin/sh", ["sh", *command])\n'
    )
    helper.chmod(0o755)
    for name in ('id', 'groupmod', 'usermod', 'chown', 'setpriv', 'slopcar-doctor'):
        (tmp_path / name).symlink_to(helper)
    # Replace only the absolute privileged tool paths with isolated test doubles.
    script = tmp_path / 'entrypoint.sh'
    source = Path(__file__).with_name('entrypoint.sh').read_text()
    for name in ('id', 'groupmod', 'usermod', 'chown', 'setpriv'):
        directory = '/usr/sbin' if name in ('groupmod', 'usermod') else '/usr/bin'
        source = source.replace(f'{directory}/{name}', str(tmp_path / name))
    script.write_text(source)
    log = tmp_path / 'calls'

    def run(**settings):
        env = {key: value for key, value in os.environ.items() if key not in ('PUID', 'PGID')}
        env.update(PATH=f'{tmp_path}:{env["PATH"]}', TEST_LOG=str(log), **settings)
        log.write_text('')
        result = subprocess.run(
            ['sh', str(script), 'slopcar-doctor', 'argument with spaces'],
            env=env,
            text=True,
            capture_output=True,
            check=False,
        )
        return result, [json.loads(line) for line in log.read_text().splitlines()]

    return run


@pytest.mark.parametrize('settings', [{}, {'PUID': '1001', 'PGID': '1002'}, {'PUID': '12345', 'PGID': '0'}])
def test_startup_and_restart_drop_privileges_before_doctor(startup, settings):
    uid, gid = settings.get('PUID', '1000'), settings.get('PGID', '1000')
    for _ in range(2):
        result, calls = startup(**settings)
        assert result.returncode == 0, result.stderr
        assert calls[:4] == [
            ['groupmod', '-o', '-g', gid, 'slop'],
            ['usermod', '-o', '-u', uid, '-g', gid, '-d', '/nonexistent', 'slop'],
            ['usermod', '-d', '/home/slop', 'slop'],
            ['chown', f'{uid}:{gid}', '/home/slop'],
        ]
        assert calls[4][0] == 'setpriv'
        for flag in (
            '--reuid=slop',
            '--regid=slop',
            '--clear-groups',
            '--bounding-set=-all',
            '--inh-caps=-all',
            '--ambient-caps=-all',
        ):
            assert flag in calls[4]
        assert calls[5:] == [['slopcar-doctor', 'argument with spaces']]


@pytest.mark.parametrize('settings', [{'PUID': '0'}, {'PUID': '000'}, {'PUID': '-1'}, {'PGID': 'bad'}])
def test_invalid_identity_stops_before_account_changes(startup, settings):
    result, calls = startup(**settings)
    assert result.returncode != 0
    assert 'slopcar:' in result.stderr
    assert calls == []


@pytest.mark.parametrize('command', ['groupmod', 'usermod', 'chown', 'setpriv'])
def test_failed_initialization_never_launches_application(startup, command):
    result, calls = startup(TEST_FAIL=command)
    assert result.returncode == 19
    assert calls[-1][0] == command
    assert not any(call[0] == 'slopcar-doctor' for call in calls)
