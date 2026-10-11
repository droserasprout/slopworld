"""Protect artifact handoff from container failures and stale output."""

import subprocess
from pathlib import Path
from typing import Any

import pytest

from tools.release import container
from tools.release import container_debian
from tools.release import container_fedora


@pytest.mark.parametrize('distribution', ['debian', 'fedora'])
@pytest.mark.parametrize('failure', ['build', 'run', 'missing', 'empty'])
def test_failed_container_preserves_previous_artifacts(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, distribution: str, failure: str
) -> None:
    adapter = container_debian if distribution == 'debian' else container_fedora
    monkeypatch.setattr(adapter, 'ROOT', tmp_path)
    monkeypatch.setenv('CONTAINER', 'docker')
    monkeypatch.setenv('CONTAINER_BUILD_ARGS', '')
    monkeypatch.setenv('CONTAINER_RUN_ARGS', '')
    output = tmp_path / 'output'
    output.mkdir()
    generated = 'slopworld_1.0.0-1_amd64.deb' if distribution == 'debian' else 'slopworld-1.0.0-1.x86_64.rpm'
    published = 'slopworld-1.0.0-amd64.deb' if distribution == 'debian' else 'slopworld-1.0.0-x86_64.rpm'
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
        adapter.package(output, 'revision', '1.0.0')
    assert {path.name: path.read_bytes() for path in output.iterdir()} == previous


@pytest.mark.parametrize('adapter', [container_debian, container_fedora])
def test_invalid_version_is_rejected_before_container_io(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, adapter: Any
) -> None:
    def unexpected(*args: Any, **kwargs: Any) -> None:
        pytest.fail('Invalid versions must be rejected before container work')

    monkeypatch.setattr(container, 'run', unexpected)
    with pytest.raises(ValueError, match='version'):
        adapter.package(tmp_path / 'output', 'revision', '../1')
    assert not (tmp_path / 'output').exists()
