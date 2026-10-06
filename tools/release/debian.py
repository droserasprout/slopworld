"""Build a local Debian binary package; Debian owns shared-library dependency discovery.

The release module owns the mod distribution allowlist. This module owns system
paths and Debian metadata, and never changes game directories or user services.
"""

import argparse
import os
import re
import shutil
import tempfile
import tomllib
from pathlib import Path

from tools import ROOT
from tools.release import latest
from tools.utils import log
from tools.utils import run
from tools.utils import run_main
from tools.version.resolve import from_git

BINARIES = ('slopd', 'slopctl', 'slopworld')


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
        for binary in BINARIES:
            target = staging / 'usr/bin' / binary
            latest.copy_file((binary_dir or ROOT / 'slopd/target/release') / binary, target)
            target.chmod(0o755)
        unit = staging / 'usr/lib/systemd/user/slopd.service'
        latest.copy_file(ROOT / 'slopd/slopd.service', unit)
        unit.write_text(unit.read_text().replace('%h/.local/bin/slopd', '/usr/bin/slopd'))
        latest.stage_mod(staging / 'usr/share/slopworld/SlopWorld', revision, version)
        for source, relative_target in (
            ('packaging/slopworld.desktop', 'usr/share/applications/slopworld.desktop'),
            ('mod/Textures/SlopWorld/SlopWorld_icon.png', 'usr/share/icons/hicolor/128x128/apps/slopworld.png'),
            ('packaging/debian/README.Debian', 'usr/share/doc/slopworld/README.Debian'),
            ('LICENSE', 'usr/share/doc/slopworld/copyright'),
        ):
            latest.copy_file(ROOT / source, staging / relative_target)
        shutil.copytree(ROOT / 'licenses', staging / 'usr/share/doc/slopworld/third-party')
        # dpkg-shlibdeps requires a source control file even for binary-only staging.
        source_control = workspace / 'debian/control'
        source_control.parent.mkdir()
        source_control.write_text('Source: slopworld\n\nPackage: slopworld\nArchitecture: any\n')
        result = run(
            ['dpkg-shlibdeps', '-O', *(str(staging / 'usr/bin' / binary) for binary in BINARIES)],
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
        # Distributable data is readable regardless of the developer's umask.
        for path in staging.rglob('*'):
            path.chmod(0o755 if path.is_dir() or path.parent == staging / 'usr/bin' else 0o644)
        staging.chmod(0o755)
        archive = workspace / destination.name
        run(['dpkg-deb', '--root-owner-group', '--build', str(staging), str(archive)])
        archive.replace(destination)
    return destination


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage-only', action='store_true')
    parser.add_argument('--binary-dir', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--revision')
    parser.add_argument('--version')
    args = parser.parse_args()
    if args.stage_only:
        if not all((args.binary_dir, args.output, args.revision, args.version)):
            parser.error('--stage-only requires --binary-dir, --output, --revision, and --version')
        architecture = run(['dpkg', '--print-architecture'], capture_output=True, text=True).stdout.strip()
        log(f'Debian package: {package(args.output, args.revision, args.version, architecture, args.binary_dir)}')
        return
    # Probe packaging tools before spending time compiling. Build on the target
    # Debian/Ubuntu release so its installed library metadata matches the binaries.
    architecture = run(['dpkg', '--print-architecture'], capture_output=True, text=True).stdout.strip()
    if architecture not in ('amd64', 'arm64'):
        raise ValueError(f'Unsupported Debian architecture: {architecture}')
    for tool in ('dpkg-deb', 'dpkg-shlibdeps'):
        run([tool, '--version'], capture_output=True, text=True)
    with (ROOT / 'slopd/Cargo.toml').open('rb') as source:
        fallback = tomllib.load(source)['package']['version']
    version = os.environ.get('VERSION') or from_git(fallback)
    debian_version(version)
    revision = latest.git('rev-parse', 'HEAD')
    run([os.environ['JUST_CMD'], 'BUILD=release', f'VERSION={version}', 'all', 'check-licenses'])
    output = Path(os.environ.get('DEB_DIR', 'dist/debian'))
    if not output.is_absolute():
        output = ROOT / output
    log(f'Debian package: {package(output, revision, version, architecture)}')


if __name__ == '__main__':
    run_main(main)
