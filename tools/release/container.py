"""Own release container execution and atomic artifact handoff.

Adapters own versions and filenames; native packagers own payload staging.
"""

import os
import shlex
import tempfile
from pathlib import Path

from tools.utils import command
from tools.utils import run


def package(
    root: Path,
    distribution: str,
    output: Path,
    revision: str,
    version: str,
    *,
    generated_name: str,
    archive_name: str,
) -> Path:
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    # Fresh output prevents stale package reuse; the same filesystem allows atomic rename.
    with tempfile.TemporaryDirectory(prefix=f'.{distribution}-container-', dir=output) as temporary:
        workspace = Path(temporary)
        compile_package(root, distribution, workspace, revision, version)
        generated = workspace / generated_name
        if not generated.is_file() or generated.stat().st_size == 0:
            raise ValueError(f'Missing or empty container package: {generated_name}')
        archive = output / archive_name
        generated.replace(archive)
    return archive


def compile_package(root: Path, distribution: str, output: Path, revision: str, version: str) -> None:
    container = command('CONTAINER', 'podman')
    run(
        [
            *container,
            'build',
            '--platform',
            'linux/amd64',
            *shlex.split(os.environ.get('CONTAINER_BUILD_ARGS', '')),
            '-t',
            f'slopworld-{distribution}-release',
            '-f',
            str(root / f'packaging/{distribution}/Dockerfile'),
            str(root / f'packaging/{distribution}'),
        ]
    )
    cache = root / f'dist/{distribution}-build'
    mounts = []
    for name, destination in (
        ('target', '/build/target'),
        ('registry', '/usr/local/cargo/registry'),
        ('git', '/usr/local/cargo/git'),
    ):
        directory = cache / name
        directory.mkdir(parents=True, exist_ok=True)
        mounts.extend(['--volume', f'{directory}:{destination}'])
    common = Path(
        run(['git', 'rev-parse', '--git-common-dir'], cwd=root, capture_output=True, text=True).stdout.strip()
    )
    if not common.is_absolute():
        common = (root / common).resolve()
    if not common.is_relative_to(root):
        mounts.extend(['--volume', f'{common}:{common}:ro'])
    run(
        [
            *container,
            'run',
            '--platform',
            'linux/amd64',
            *shlex.split(os.environ.get('CONTAINER_RUN_ARGS', '')),
            '--rm',
            # Allow SELinux access without relabeling repositories or worktree metadata.
            '--security-opt',
            'label=disable',
            '--volume',
            f'{root}:{root}:ro',
            '--volume',
            f'{output}:/output',
            *mounts,
            '--workdir',
            str(root),
            '--env',
            f'SLOPWORLD_BUILD_VERSION={version}',
            '--env',
            'CARGO_TARGET_DIR=/build/target',
            # Container and checkout owners may differ. Trust only this checkout.
            '--env',
            'GIT_CONFIG_COUNT=1',
            '--env',
            'GIT_CONFIG_KEY_0=safe.directory',
            '--env',
            f'GIT_CONFIG_VALUE_0={root}',
            f'slopworld-{distribution}-release',
            'bash',
            '-euc',
            'cargo build --manifest-path slopd/Cargo.toml --release --locked; '
            f'python3 -m pytest tools/release/test_{distribution}.py -q -p no:cacheprovider; '
            f'python3 -m tools.release.{distribution} --stage-only --binary-dir /build/target/release '
            '--output /output --revision "$1" --version "$2"',
            f'release-{distribution}',
            revision,
            version,
        ]
    )
