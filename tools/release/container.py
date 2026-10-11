"""Own release container execution and atomic artifact handoff.

Native packagers own version conversion, payload staging, and archive formats.
"""

import os
import shlex
import tempfile
from pathlib import Path

from tools import ROOT
from tools.release.arch import arch_version
from tools.release.debian import debian_version
from tools.release.fedora import rpm_version
from tools.utils import command
from tools.utils import run


def package(distribution: str, output: Path, revision: str, version: str) -> Path:
    if distribution == 'arch':
        arch_version(version)
        generated_name = archive_name = f'slopworld-{version}-x86_64.pkg.tar.zst'
    elif distribution == 'debian':
        generated_name = f'slopworld_{debian_version(version)}_amd64.deb'
        archive_name = f'slopworld-{version}-amd64.deb'
    elif distribution == 'fedora':
        generated_name = f'slopworld-{rpm_version(version)}-1.x86_64.rpm'
        archive_name = f'slopworld-{version}-x86_64.rpm'
    else:
        raise ValueError(f'Unsupported distribution: {distribution}')
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    # Fresh output prevents stale package reuse; the same filesystem allows atomic rename.
    with tempfile.TemporaryDirectory(prefix=f'.{distribution}-container-', dir=output) as temporary:
        workspace = Path(temporary)
        compile_package(ROOT, distribution, workspace, revision, version)
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
            # Arch's root makepkg wrapper needs nested Bubblewrap namespaces.
            *(['--privileged'] if distribution == 'arch' else []),
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
            (
                'cargo build --manifest-path slopd/Cargo.toml --release --locked; '
                f'python3 -m pytest tools/release/test_{distribution}.py -q -p no:cacheprovider; '
                f'python3 -m tools.release.native {distribution} --stage-only --binary-dir /build/target/release '
                '--output /output --revision "$1" --version "$2"'
            ),
            f'release-{distribution}',
            revision,
            version,
        ]
    )
