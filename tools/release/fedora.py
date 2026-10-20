"""Own Fedora payload staging and atomic RPM publication.

staging selects mod assets; RPM discovers ELF dependencies.
"""

import os
import re
import tempfile
from pathlib import Path

from tools import ROOT
from tools.release import staging as shared
from tools.release.staging import copy_file
from tools.release.staging import stage_mod
from tools.utils import run


def rpm_version(version: str) -> str:
    # RPM reserves hyphens for Release. Reject macros and field injection before conversion.
    if not re.fullmatch(r'[0-9][A-Za-z0-9.+\-]*', version):
        raise ValueError(f'Invalid RPM upstream version: {version!r}')
    return version.replace('-', '.')


def package(output: Path, revision: str, version: str, architecture: str, binary_dir: Path | None = None) -> Path:
    version_field = rpm_version(version)
    if architecture not in ('x86_64', 'aarch64'):
        raise ValueError(f'Unsupported Fedora architecture: {architecture}')
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    destination = output / f'slopworld-{version_field}-1.{architecture}.rpm'
    # RPM postprocessors split paths on whitespace: build under /tmp, then copy
    # to the output filesystem for atomic publication. Failures preserve old output.
    with (
        tempfile.TemporaryDirectory(prefix='.fedora-', dir=output) as publication,
        tempfile.TemporaryDirectory(prefix='slopworld-rpm-', dir='/tmp') as temporary,
    ):
        workspace = Path(temporary)
        stage_payload(workspace / 'SOURCES/payload', revision, version, binary_dir)
        archive = build_archive(workspace, version_field, architecture, destination.name, source_date_epoch(revision))
        published = Path(publication) / destination.name
        copy_file(archive, published)
        published.replace(destination)
    return destination


def stage_payload(payload: Path, revision: str, version: str, binary_dir: Path | None) -> None:
    shared.stage_system(payload, binary_dir or ROOT / 'slopd/target/release')
    stage_mod(payload / 'usr/share/slopworld/SlopWorld', revision, version)
    copy_file(ROOT / 'packaging/fedora/README.Fedora', payload / 'usr/share/doc/slopworld/README.Fedora')
    shared.stage_notices(payload)
    shared.normalize_permissions(payload, payload / 'usr/bin')


def source_date_epoch(revision: str) -> str:
    """Honor explicit reproducible-build input, otherwise use the packaged commit."""
    epoch = os.environ.get('SOURCE_DATE_EPOCH')
    if epoch is None:
        epoch = run(
            ['git', 'show', '-s', '--format=%ct', '--end-of-options', revision],
            cwd=ROOT,
            capture_output=True,
            text=True,
        ).stdout.strip()
    if not re.fullmatch(r'[0-9]+', epoch):
        raise ValueError('SOURCE_DATE_EPOCH must be a non-negative integer')
    return epoch


def build_archive(workspace: Path, version: str, architecture: str, expected_name: str, epoch: str) -> Path:
    spec = workspace / 'SPECS/slopworld.spec'
    copy_file(ROOT / 'packaging/fedora/slopworld.spec', spec)
    run(
        [
            'rpmbuild',
            '-bb',
            '--target',
            architecture,
            '--define',
            f'_topdir {workspace}',
            '--define',
            f'slopworld_version {version}',
            str(spec),
        ],
        env={**os.environ, 'SOURCE_DATE_EPOCH': epoch},
    )
    archives = list((workspace / 'RPMS').rglob('*.rpm'))
    if len(archives) != 1 or archives[0].name != expected_name:
        raise ValueError('rpmbuild did not produce exactly the expected package')
    # Validate before publication.
    run(['rpm', '-qp', '--requires', str(archives[0])], capture_output=True, text=True)
    return archives[0]
