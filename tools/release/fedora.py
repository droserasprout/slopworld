"""Own Fedora payload staging and atomic RPM publication.

staging selects mod assets; RPM discovers ELF dependencies.
"""

import argparse
import os
import platform
import re
import shutil
import tempfile
from pathlib import Path

from tools import ROOT
from tools.release.staging import copy_file
from tools.release.staging import stage_mod
from tools.utils import log
from tools.utils import run
from tools.utils import run_main
from tools.version.metadata import package_version
from tools.version.resolve import from_git

BINARIES = ('slopd', 'slopctl', 'slopworld')


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
        archive = build_archive(workspace, version_field, architecture, destination.name)
        published = Path(publication) / destination.name
        copy_file(archive, published)
        published.replace(destination)
    return destination


def stage_payload(payload: Path, revision: str, version: str, binary_dir: Path | None) -> None:
    for binary in BINARIES:
        target = payload / 'usr/bin' / binary
        copy_file((binary_dir or ROOT / 'slopd/target/release') / binary, target)
        target.chmod(0o755)
    unit = payload / 'usr/lib/systemd/user/slopd.service'
    copy_file(ROOT / 'slopd/slopd.service', unit)
    unit.write_text(unit.read_text().replace('%h/.local/bin/slopd', '/usr/bin/slopd'))
    stage_mod(payload / 'usr/share/slopworld/SlopWorld', revision, version)
    for source, relative_target in (
        ('packaging/slopworld.desktop', 'usr/share/applications/slopworld.desktop'),
        ('mod/Textures/SlopWorld/SlopWorld_icon.png', 'usr/share/icons/hicolor/128x128/apps/slopworld.png'),
        ('packaging/fedora/README.Fedora', 'usr/share/doc/slopworld/README.Fedora'),
        ('LICENSE', 'usr/share/licenses/slopworld/LICENSE'),
    ):
        copy_file(ROOT / source, payload / relative_target)
    shutil.copytree(ROOT / 'licenses', payload / 'usr/share/licenses/slopworld/third-party')
    for path in (payload, *payload.rglob('*')):
        path.chmod(0o755 if path.is_dir() or path.parent == payload / 'usr/bin' else 0o644)


def build_archive(workspace: Path, version: str, architecture: str, expected_name: str) -> Path:
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
        ]
    )
    archives = list((workspace / 'RPMS').rglob('*.rpm'))
    if len(archives) != 1 or archives[0].name != expected_name:
        raise ValueError('rpmbuild did not produce exactly the expected package')
    # Validate before publication.
    run(['rpm', '-qp', '--requires', str(archives[0])], capture_output=True, text=True)
    return archives[0]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage-only', action='store_true')
    parser.add_argument('--binary-dir', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--revision')
    parser.add_argument('--version')
    args = parser.parse_args()
    architecture = platform.machine()
    if architecture not in ('x86_64', 'aarch64'):
        raise ValueError(f'Unsupported Fedora architecture: {architecture}')
    if args.stage_only:
        if not all((args.binary_dir, args.output, args.revision, args.version)):
            parser.error('--stage-only requires --binary-dir, --output, --revision, and --version')
        log(f'Fedora package: {package(args.output, args.revision, args.version, architecture, args.binary_dir)}')
        return
    for tool in ('rpmbuild', 'rpm'):
        run([tool, '--version'], capture_output=True, text=True)
    version = os.environ.get('VERSION') or from_git(package_version(ROOT))
    rpm_version(version)
    revision = run(['git', 'rev-parse', 'HEAD'], capture_output=True, text=True).stdout.strip()
    run([os.environ['JUST_CMD'], 'BUILD=release', f'VERSION={version}', 'all'])
    output = Path(os.environ.get('RPM_DIR', 'dist/fedora'))
    if not output.is_absolute():
        output = ROOT / output
    log(f'Fedora package: {package(output, revision, version, architecture)}')


if __name__ == '__main__':
    run_main(main)
