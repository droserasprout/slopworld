"""Compile and package Debian releases in an isolated Debian toolchain container."""

import os
import shlex
from pathlib import Path

from tools import ROOT
from tools.utils import command
from tools.utils import run

ASSET_NAME = 'slopworld-latest-amd64.deb'


def package(output: Path, revision: str, version: str) -> Path:
    container = command('CONTAINER', 'podman')
    run(
        [
            *container,
            'build',
            *shlex.split(os.environ.get('CONTAINER_BUILD_ARGS', '')),
            '-t',
            'slopworld-debian-release',
            '-f',
            str(ROOT / 'packaging/debian/Dockerfile'),
            str(ROOT / 'packaging/debian'),
        ]
    )
    cache = ROOT / 'dist/debian-build'
    mounts = []
    for name, destination in (
        ('target', '/build/target'),
        ('registry', '/usr/local/cargo/registry'),
        ('git', '/usr/local/cargo/git'),
    ):
        directory = cache / name
        directory.mkdir(parents=True, exist_ok=True)
        mounts.extend(['--volume', f'{directory}:{destination}'])
    common = Path(run(['git', 'rev-parse', '--git-common-dir'], capture_output=True, text=True).stdout.strip())
    if not common.is_absolute():
        common = (ROOT / common).resolve()
    if not common.is_relative_to(ROOT):
        mounts.extend(['--volume', f'{common}:{common}:ro'])
    run(
        [
            *container,
            'run',
            *shlex.split(os.environ.get('CONTAINER_RUN_ARGS', '')),
            '--rm',
            '--volume',
            f'{ROOT}:{ROOT}:ro',
            '--volume',
            f'{output}:/output',
            *mounts,
            '--workdir',
            str(ROOT),
            '--env',
            f'SLOPWORLD_BUILD_VERSION={version}',
            '--env',
            'CARGO_TARGET_DIR=/build/target',
            'slopworld-debian-release',
            'bash',
            '-euc',
            'cargo build --manifest-path slopd/Cargo.toml --release --locked; '
            'python3 -m pytest tools/release/test_debian.py -q -p no:cacheprovider; '
            'python3 -m tools.release.debian --stage-only --binary-dir /build/target/release '
            '--output /output --revision "$1" --version "$2"',
            'release-debian',
            revision,
            version,
        ]
    )
    # Stable asset names replace the previous rolling package rather than leaving
    # stale versioned downloads on GitHub. Full versions stay in package metadata.
    generated = output / f'slopworld_{version}-1_amd64.deb'
    destination = output / ASSET_NAME
    generated.replace(destination)
    return destination
