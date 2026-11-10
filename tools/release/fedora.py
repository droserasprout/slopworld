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


def package(
    output: Path,
    revision: str,
    version: str,
    architecture: str,
    binary_dir: Path | None = None,
    *,
    product: str = 'slopworld',
) -> Path:
    if product not in shared.PRODUCTS:
        raise ValueError(f'Unsupported product: {product}')
    version_field = rpm_version(version)
    if architecture not in ('x86_64', 'aarch64'):
        raise ValueError(f'Unsupported Fedora architecture: {architecture}')
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    destination = output / f'{product}-{version_field}-1.{architecture}.rpm'
    # RPM postprocessors split paths on whitespace: build under /tmp, then copy
    # to the output filesystem for atomic publication. Failures preserve old output.
    with (
        tempfile.TemporaryDirectory(prefix='.fedora-', dir=output) as publication,
        tempfile.TemporaryDirectory(prefix='slopworld-rpm-', dir='/tmp') as temporary,
    ):
        workspace = Path(temporary)
        if product == 'slopcar':
            shared.stage_sidecar(
                workspace / 'SOURCES/payload', binary_dir or ROOT / 'slopcar/target/release', revision, version
            )
        elif product == 'slopweb':
            shared.stage_slopweb(workspace / 'SOURCES/payload', revision, version, binary_dir)
        else:
            stage_payload(workspace / 'SOURCES/payload', revision, version, binary_dir, product)
        archive = build_archive(
            workspace, version_field, architecture, destination.name, source_date_epoch(revision), product=product
        )
        published = Path(publication) / destination.name
        copy_file(archive, published)
        published.replace(destination)
    return destination


def stage_payload(
    payload: Path, revision: str, version: str, binary_dir: Path | None, product: str = 'slopworld'
) -> None:
    shared.stage_system(payload, binary_dir or ROOT / 'slopd/target/release', product)
    if product == 'slopworld':
        stage_mod(payload / 'usr/share/slopworld/SlopWorld', revision, version)
    copy_file(ROOT / 'packaging/fedora/README.Fedora', payload / f'usr/share/doc/{product}/README.Fedora')
    shared.metadata(payload / f'usr/share/doc/{product}', revision, version)
    shared.stage_notices(payload, f'usr/share/licenses/{product}/LICENSE')
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


def build_archive(
    workspace: Path, version: str, architecture: str, expected_name: str, epoch: str, *, product: str = 'slopworld'
) -> Path:
    spec = workspace / f'SPECS/{product}.spec'
    copy_file(ROOT / f'packaging/fedora/{product}.spec', spec)
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
