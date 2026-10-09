"""Protect container checkout access without building images or running containers."""

import subprocess
from pathlib import Path
from typing import Any

import pytest

from tools.release import container_debian


@pytest.mark.parametrize('linked_worktree', [False, True])
def test_container_trusts_only_mounted_checkout_and_mounts_external_git_metadata(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, linked_worktree: bool
) -> None:
    root = tmp_path / 'checkout with spaces'
    root.mkdir()
    output = root / 'dist/output'
    output.mkdir(parents=True)
    common = tmp_path / 'repository/.git' if linked_worktree else root / '.git'
    monkeypatch.setattr(container_debian, 'ROOT', root)
    monkeypatch.setenv('CONTAINER', 'docker')
    monkeypatch.setenv('CONTAINER_BUILD_ARGS', '')
    monkeypatch.setenv('CONTAINER_RUN_ARGS', '')
    calls: list[list[str]] = []

    def run(arguments: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        calls.append(arguments)
        if arguments[0] == 'git':
            return subprocess.CompletedProcess(arguments, 0, str(common) + '\n')
        if arguments[:2] == ['docker', 'run']:
            (output / 'slopworld_1.0.0-1_amd64.deb').write_bytes(b'fixture archive')
        return subprocess.CompletedProcess(arguments, 0, '')

    monkeypatch.setattr(container_debian, 'run', run)
    archive = container_debian.package(output, 'revision', '1.0.0')
    invocation = calls[-1]
    assert invocation[:2] == ['docker', 'run']
    environments = [invocation[index + 1] for index, value in enumerate(invocation) if value == '--env']
    assert 'GIT_CONFIG_COUNT=1' in environments
    assert 'GIT_CONFIG_KEY_0=safe.directory' in environments
    assert f'GIT_CONFIG_VALUE_0={root}' in environments
    assert f'{root}:{root}:ro' in invocation
    assert (f'{common}:{common}:ro' in invocation) == linked_worktree
    assert archive.name == 'slopworld-1.0.0-amd64.deb'
    assert archive.read_bytes() == b'fixture archive'
