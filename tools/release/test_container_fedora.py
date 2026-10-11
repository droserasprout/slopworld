"""Check Fedora container mounts and Git trust without containers."""

import subprocess
from pathlib import Path
from typing import Any

import pytest

from tools.release import container
from tools.release import container_fedora


@pytest.mark.parametrize('linked_worktree', [False, True])
@pytest.mark.parametrize('version', ['1.0.0', '1.0.0-20261011-abcdef'])
def test_container_trusts_only_mounted_checkout_and_mounts_external_git_metadata(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, linked_worktree: bool, version: str
) -> None:
    root = tmp_path / 'checkout with spaces'
    root.mkdir()
    output = root / 'dist/output'
    output.mkdir(parents=True)
    common = tmp_path / 'repository/.git' if linked_worktree else root / '.git'
    monkeypatch.setattr(container_fedora, 'ROOT', root)
    monkeypatch.setenv('CONTAINER', 'docker')
    monkeypatch.setenv('CONTAINER_BUILD_ARGS', '')
    monkeypatch.setenv('CONTAINER_RUN_ARGS', '')
    calls: list[list[str]] = []

    def run(arguments: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        calls.append(arguments)
        if arguments[0] == 'git':
            return subprocess.CompletedProcess(arguments, 0, str(common) + '\n')
        if arguments[:2] == ['docker', 'run']:
            mounts = [arguments[index + 1] for index, value in enumerate(arguments) if value == '--volume']
            workspace = Path(next(mount.removesuffix(':/output') for mount in mounts if mount.endswith(':/output')))
            assert workspace != output
            (workspace / f'slopworld-{version.replace("-", ".")}-1.x86_64.rpm').write_bytes(b'fixture archive')
        return subprocess.CompletedProcess(arguments, 0, '')

    monkeypatch.setattr(container, 'run', run)
    archive = container_fedora.package(output, 'revision', version)
    invocation = calls[-1]
    assert invocation[:4] == ['docker', 'run', '--platform', 'linux/amd64']
    assert invocation[invocation.index('--security-opt') + 1] == 'label=disable'
    environments = [invocation[index + 1] for index, value in enumerate(invocation) if value == '--env']
    assert 'GIT_CONFIG_COUNT=1' in environments
    assert 'GIT_CONFIG_KEY_0=safe.directory' in environments
    assert f'GIT_CONFIG_VALUE_0={root}' in environments
    assert f'{root}:{root}:ro' in invocation
    assert (f'{common}:{common}:ro' in invocation) == linked_worktree
    assert archive.name == f'slopworld-{version}-x86_64.rpm'
    assert archive.read_bytes() == b'fixture archive'
