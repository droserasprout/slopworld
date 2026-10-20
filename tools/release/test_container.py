"""Protect artifact handoff from container failures and stale output."""

import subprocess
from pathlib import Path
from typing import Any

import pytest

from tools.release import container


@pytest.mark.parametrize('distribution', ['arch', 'debian', 'fedora'])
@pytest.mark.parametrize('failure', ['build', 'run', 'missing', 'empty'])
def test_failed_container_preserves_previous_artifacts(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, distribution: str, failure: str
) -> None:
    monkeypatch.setattr(container, 'ROOT', tmp_path)
    monkeypatch.setenv('CONTAINER', 'docker')
    monkeypatch.setenv('CONTAINER_BUILD_ARGS', '')
    monkeypatch.setenv('CONTAINER_RUN_ARGS', '')
    output = tmp_path / 'output'
    output.mkdir()
    generated = 'slopworld_1.0.0-1_amd64.deb' if distribution == 'debian' else 'slopworld-1.0.0-1.x86_64.rpm'
    published = 'slopworld-1.0.0-amd64.deb' if distribution == 'debian' else 'slopworld-1.0.0-x86_64.rpm'
    if distribution == 'arch':
        generated = published = 'slopworld-1.0.0-x86_64.pkg.tar.zst'
    previous = {generated: b'stale package', published: b'previous release'}
    for name, content in previous.items():
        (output / name).write_bytes(content)

    def run(arguments: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        if arguments[:2] == ['docker', failure]:
            raise subprocess.CalledProcessError(1, arguments)
        if arguments[0] == 'git':
            return subprocess.CompletedProcess(arguments, 0, '.git\n')
        if arguments[:2] == ['docker', 'run'] and failure == 'empty':
            mounts = [arguments[index + 1] for index, value in enumerate(arguments) if value == '--volume']
            workspace = Path(next(mount.removesuffix(':/output') for mount in mounts if mount.endswith(':/output')))
            (workspace / generated).touch()
        return subprocess.CompletedProcess(arguments, 0, '')

    monkeypatch.setattr(container, 'run', run)
    with pytest.raises((subprocess.CalledProcessError, ValueError)):
        container.package(distribution, output, 'revision', '1.0.0')
    assert {path.name: path.read_bytes() for path in output.iterdir()} == previous


@pytest.mark.parametrize('distribution', ['arch', 'debian', 'fedora'])
def test_invalid_version_is_rejected_before_container_io(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, distribution: str
) -> None:
    def unexpected(*args: Any, **kwargs: Any) -> None:
        pytest.fail('Invalid versions must be rejected before container work')

    monkeypatch.setattr(container, 'run', unexpected)
    with pytest.raises(ValueError, match='version'):
        container.package(distribution, tmp_path / 'output', 'revision', '../1')
    assert not (tmp_path / 'output').exists()


@pytest.mark.parametrize('distribution', ['arch', 'debian', 'fedora'])
@pytest.mark.parametrize('linked_worktree', [False, True])
@pytest.mark.parametrize('version', ['1.0.0', '1.0.0-20261011-abcdef'])
def test_container_trusts_only_mounted_checkout_and_mounts_external_git_metadata(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, linked_worktree: bool, version: str, distribution: str
) -> None:
    root = tmp_path / 'checkout with spaces'
    root.mkdir()
    output = root / 'dist/output'
    output.mkdir(parents=True)
    common = tmp_path / 'repository/.git' if linked_worktree else root / '.git'
    monkeypatch.setattr(container, 'ROOT', root)
    monkeypatch.setenv('CONTAINER', 'docker')
    monkeypatch.setenv('CONTAINER_BUILD_ARGS', '')
    monkeypatch.setenv('CONTAINER_RUN_ARGS', '')
    generated = (
        f'slopworld_{version}-1_amd64.deb'
        if distribution == 'debian'
        else f'slopworld-{version.replace("-", ".")}-1.x86_64.rpm'
    )
    published = f'slopworld-{version}-' + ('amd64.deb' if distribution == 'debian' else 'x86_64.rpm')
    if distribution == 'arch':
        generated = published = f'slopworld-{version}-x86_64.pkg.tar.zst'
    calls: list[list[str]] = []

    def run(arguments: list[str], **kwargs: Any) -> subprocess.CompletedProcess[str]:
        calls.append(arguments)
        if arguments[0] == 'git':
            return subprocess.CompletedProcess(arguments, 0, str(common) + '\n')
        if arguments[:2] == ['docker', 'run']:
            mounts = [arguments[index + 1] for index, value in enumerate(arguments) if value == '--volume']
            workspace = Path(next(mount.removesuffix(':/output') for mount in mounts if mount.endswith(':/output')))
            assert workspace != output
            assert workspace.stat().st_mode & 0o777 == 0o755
            (workspace / generated).write_bytes(b'fixture archive')
        return subprocess.CompletedProcess(arguments, 0, '')

    monkeypatch.setattr(container, 'run', run)
    archive = container.package(distribution, output, 'revision', version)
    invocation = calls[-1]
    assert ('--privileged' in invocation) == (distribution == 'arch')
    assert invocation[:4] == ['docker', 'run', '--platform', 'linux/amd64']
    assert invocation[invocation.index('--security-opt') + 1] == 'label=disable'
    environments = [invocation[index + 1] for index, value in enumerate(invocation) if value == '--env']
    assert 'GIT_CONFIG_COUNT=1' in environments
    assert 'GIT_CONFIG_KEY_0=safe.directory' in environments
    assert f'GIT_CONFIG_VALUE_0={root}' in environments
    assert f'{root}:{root}:ro' in invocation
    assert (f'{common}:{common}:ro' in invocation) == linked_worktree
    assert archive.name == published
    assert archive.read_bytes() == b'fixture archive'
