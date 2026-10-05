"""Protect suite isolation, build failure ordering, and IPC scratch ownership."""

import subprocess
from unittest.mock import patch

import pytest

from bench import runner
from tools import ROOT


def test_release_build_preserves_command_flags_and_stops_on_failure(monkeypatch):
    monkeypatch.setenv('BUILD', 'release')
    monkeypatch.setenv('CARGO', '"/tmp/cargo tool" --offline')
    monkeypatch.setenv('CARGOFLAGS', '--release --frozen')
    monkeypatch.setenv('TEST_PROJECT', 'tests/project.csproj')
    with (
        patch.object(runner, 'run', side_effect=subprocess.CalledProcessError(23, ['cargo'])) as run,
        patch.object(runner, 'build_ipc') as ipc,
    ):
        with pytest.raises(subprocess.CalledProcessError):
            runner.build()
        assert run.call_args.args[0] == [
            '/tmp/cargo tool',
            '--offline',
            'build',
            '--quiet',
            '--bin',
            'slopd',
            '--release',
            '--frozen',
        ]
        ipc.assert_not_called()


def test_daemon_suite_uses_custom_target_and_skips_other_lanes(tmp_path, monkeypatch):
    monkeypatch.setenv('BUILD', 'debug')
    monkeypatch.setenv('CARGO_TARGET_DIR', str(tmp_path / 'target with spaces'))
    with patch.object(runner, 'run') as run, patch.object(runner, 'run_ipc') as ipc:
        runner.run_suite('daemon')
    assert run.call_args.args[0] == [str(tmp_path / 'target with spaces/debug/slopd'), '--perf-bench']
    ipc.assert_not_called()


def test_ipc_fixtures_and_roundtrips_stay_in_scratch(tmp_path, monkeypatch):
    monkeypatch.setenv('BUILD', 'release')
    monkeypatch.setenv('BENCH_IPC_OUTPUT', str(tmp_path / 'output'))
    monkeypatch.setenv('TMPDIR', str(tmp_path / 'scratch'))
    before = list((ROOT / 'bench/ipc/fixtures').iterdir())
    calls = []

    def execute(arguments, **kwargs):
        calls.append(arguments)
        if arguments[0] == 'protoc':
            kwargs['stdout'].write(b'fixture')
            assert kwargs['stdin'].name.endswith('.textproto')
        elif 'stdout' in kwargs:
            kwargs['stdout'].write('csv')
        return subprocess.CompletedProcess(arguments, 0)

    with patch.object(runner, 'run', side_effect=execute):
        runner.run_ipc()
    scratch = tmp_path / 'scratch/ipc-fixtures'
    assert {path.name for path in scratch.iterdir()} == {'plain.pb', 'ansi.pb', 'unicode.pb', 'large.pb'}
    assert {path.name for path in (tmp_path / 'output').iterdir()} == {'mono.csv', 'net8.csv', 'rust.csv'}
    assert calls[-1][-2:] == ['--verify', str(scratch)]
    assert list((ROOT / 'bench/ipc/fixtures').iterdir()) == before
