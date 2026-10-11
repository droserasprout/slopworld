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


def package(output: Path, revision: str, version: str, architecture: str, binary_dir: Path | None = None) -> Path:
    version_field = debian_version(version)
    if architecture not in ('amd64', 'arm64'):
        raise ValueError(f'Unsupported Debian architecture: {architecture}')
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    destination = output / f'slopworld_{version_field}_{architecture}.deb'
    # A failed input, dependency scan, or archive build preserves existing output.
    with tempfile.TemporaryDirectory(prefix='.debian-', dir=output) as temporary:
        workspace = Path(temporary)
        staging = workspace / 'slopworld'
        shared.stage_system(staging, binary_dir or ROOT / 'slopd/target/release')
        stage_mod(staging / 'usr/share/slopworld/SlopWorld', revision, version)
        copy_file(ROOT / 'packaging/debian/README.Debian', staging / 'usr/share/doc/slopworld/README.Debian')
        shared.stage_notices(staging, 'usr/share/doc/slopworld/copyright')
        # dpkg-shlibdeps requires a source control file even for binary-only staging.
        source_control = workspace / 'debian/control'
        source_control.parent.mkdir()
        source_control.write_text('Source: slopworld\n\nPackage: slopworld\nArchitecture: any\n')
        result = run(
            ['dpkg-shlibdeps', '-O', *(str(staging / 'usr/bin' / binary) for binary in shared.BINARIES)],
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
            (ROOT / 'packaging/debian/control')
            .read_text()
            .format(version=version_field, architecture=architecture, shared_libraries=dependencies)
        )
        shared.normalize_permissions(staging, staging / 'usr/bin')
        archive = workspace / destination.name
        run(['dpkg-deb', '--root-owner-group', '--build', str(staging), str(archive)])
        archive.replace(destination)
    return destination
