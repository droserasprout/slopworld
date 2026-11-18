"""Managed producers overlap while staging waits for all successful outputs."""

import subprocess
import threading
from typing import Any

import pytest

from tools import web_build
from tools.web_timings import Timings


def test_managed_producers_overlap_with_bounded_admission(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv('SLOPWEB_BUILD_JOBS', '2')
    barrier = threading.Barrier(2)
    lock = threading.Lock()
    paths: list[str] = []
    active = peak = 0

    def run(args: list[str], **kwargs: Any) -> None:
        nonlocal active, peak
        assert kwargs['check']
        with lock:
            paths.append(args[2])
            active += 1
            peak = max(peak, active)
        barrier.wait(timeout=10)
        with lock:
            active -= 1

    monkeypatch.setattr(subprocess, 'run', run)
    timings = Timings()
    web_build.build_managed(timings)
    assert peak == 2
    assert len(set(paths)) == len(web_build.PROJECTS)
    assert len(timings.rows) == len(web_build.PROJECTS)
    assert active == 0


def test_failed_producer_joins_other_writers_and_prevents_staging(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv('SLOPWEB_BUILD_JOBS', '6')
    barrier = threading.Barrier(6)
    completed: list[str] = []
    staged = False

    def run(args: list[str], **kwargs: Any) -> None:
        nonlocal staged
        if args[:2] == ['just', 'web-stage']:
            staged = True
        if args[:2] != ['dotnet', 'build']:
            return
        barrier.wait(timeout=10)
        if args[2].endswith('/Probe.csproj'):
            raise subprocess.CalledProcessError(1, args)
        completed.append(args[2])

    monkeypatch.setattr(subprocess, 'run', run)
    with pytest.raises(subprocess.CalledProcessError):
        web_build.main()
    assert len(completed) == 5
    assert not staged


@pytest.mark.parametrize('value', ['0', '-1', 'invalid'])
def test_invalid_job_budget_rejected(monkeypatch: pytest.MonkeyPatch, value: str) -> None:
    monkeypatch.setenv('SLOPWEB_BUILD_JOBS', value)
    with pytest.raises(ValueError):
        web_build.build_managed(Timings())
