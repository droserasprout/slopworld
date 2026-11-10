"""Build a local Debian binary package; Debian owns shared-library dependency discovery.

staging owns shared payload files and permissions. This module owns Debian
metadata and atomic archive publication; native owns local build orchestration.
"""

import re
import tempfile
from pathlib import Path

from tools import ROOT
from tools.release import staging as shared
from tools.release.staging import copy_file
from tools.release.staging import stage_mod
from tools.utils import run


def debian_version(version: str) -> str:
    # Hyphens in binary snapshot versions belong to the upstream version, not
    # the Debian revision. Append our revision explicitly and reject field injection.
    if not re.fullmatch(r'[0-9][A-Za-z0-9.+~\-]*', version):
        raise ValueError(f'Invalid Debian upstream version: {version!r}')
    return version + '-1'


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
    version_field = debian_version(version)
    if architecture not in ('amd64', 'arm64'):
        raise ValueError(f'Unsupported Debian architecture: {architecture}')
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    destination = output / f'{product}_{version_field}_{architecture}.deb'
    # A failed input, dependency scan, or archive build preserves existing output.
    with tempfile.TemporaryDirectory(prefix='.debian-', dir=output) as temporary:
        workspace = Path(temporary)
        staging = workspace / product
        if product == 'slopcar':
            shared.stage_sidecar(staging, binary_dir or ROOT / 'slopcar/target/release', revision, version)
        else:
            shared.stage_system(staging, binary_dir or ROOT / 'slopd/target/release', product)
            if product == 'slopworld':
                stage_mod(staging / 'usr/share/slopworld/SlopWorld', revision, version)
            copy_file(ROOT / 'packaging/debian/README.Debian', staging / f'usr/share/doc/{product}/README.Debian')
            shared.metadata(staging / f'usr/share/doc/{product}', revision, version)
            shared.stage_notices(staging, f'usr/share/doc/{product}/copyright')
        # dpkg-shlibdeps requires a source control file even for binary-only staging.
        source_control = workspace / 'debian/control'
        source_control.parent.mkdir()
        source_control.write_text(f'Source: {product}\n\nPackage: {product}\nArchitecture: any\n')
        binaries = (product,)
        result = run(
            ['dpkg-shlibdeps', '-O', *(str(staging / 'usr/bin' / binary) for binary in binaries)],
            cwd=workspace,
            capture_output=True,
            text=True,
        )
        dependencies = next(
            (
                line.removeprefix('shlibs:Depends=')
                for line in result.stdout.splitlines()
                if line.startswith('shlibs:Depends=')
            ),
            '',
        )
        if not dependencies:
            raise ValueError('dpkg-shlibdeps returned no shared-library dependencies')
        control = staging / 'DEBIAN/control'
        control.parent.mkdir()
        control.write_text(
            (ROOT / ('packaging/debian/control' if product == 'slopworld' else f'packaging/debian/{product}.control'))
            .read_text()
            .format(version=version_field, architecture=architecture, shared_libraries=dependencies)
        )
        shared.normalize_permissions(staging, staging / 'usr/bin')
        archive = workspace / destination.name
        run(['dpkg-deb', '--root-owner-group', '--build', str(staging), str(archive)])
        archive.replace(destination)
    return destination
