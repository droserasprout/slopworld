"""Protect stage failure boundaries and retry behavior without touching containers."""

import subprocess
from unittest.mock import patch

import pytest

from slopcar import devloop


@pytest.mark.parametrize('failed_stage, expected_count', [(None, 5), (0, 1), (1, 2), (3, 4)])
def test_iteration_stops_at_failed_required_stage(monkeypatch, failed_stage, expected_count):
    monkeypatch.setenv('JUST_CMD', '/tmp/just tool')
    monkeypatch.setenv('SLOPCAR', '/tmp/sidecar binary')
    expected = [
        ['/tmp/just tool', 'sidecar', 'image'],
        ['/tmp/just tool', 'install', 'mod'],
        ['/tmp/sidecar binary', 'rm'],
        ['/tmp/sidecar binary', 'start', '--workspace', '/tmp/work with spaces'],
        ['/tmp/just tool', 'sidecar', 'run'],
    ]
    outcomes = [subprocess.CompletedProcess([], 23 if i == failed_stage else 0) for i in range(5)]
    with patch.object(devloop, 'run', side_effect=outcomes) as run:
        devloop.iteration(['--workspace', '/tmp/work with spaces'])
    assert [call.args[0] for call in run.call_args_list] == expected[:expected_count]


def test_iteration_tolerates_absent_container_and_can_retry_after_failure(monkeypatch):
    monkeypatch.setenv('JUST_CMD', 'just')
    monkeypatch.setenv('SLOPCAR', 'sidecar')
    outcomes = [subprocess.CompletedProcess([], status) for status in (23, 0, 0, 23, 0, 0)]
    with patch.object(devloop, 'run', side_effect=outcomes) as run:
        devloop.iteration([])
        devloop.iteration([])
    assert run.call_count == 6
    assert run.call_args.args[0] == ['just', 'sidecar', 'run']
