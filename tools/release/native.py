"""Build and package native Arch/Debian/Fedora releases; format modules own archives."""

import argparse
import os
import platform
from pathlib import Path

from tools import ROOT
from tools.release import arch
from tools.release import debian
from tools.release import fedora
from tools.utils import log
from tools.utils import run
from tools.utils import run_main
from tools.version.metadata import package_version
from tools.version.resolve import from_git


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('distribution', choices=('arch', 'debian', 'fedora'))
    parser.add_argument('--stage-only', action='store_true')
    parser.add_argument('--binary-dir', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--revision')
    parser.add_argument('--version')
    args = parser.parse_args()
    packager = {'arch': arch, 'debian': debian, 'fedora': fedora}[args.distribution]
    if args.stage_only and not all((args.binary_dir, args.output, args.revision, args.version)):
        parser.error('--stage-only requires --binary-dir, --output, --revision, and --version')
    if args.distribution == 'debian':
        architecture = run(['dpkg', '--print-architecture'], capture_output=True, text=True).stdout.strip()
        supported: tuple[str, ...] = ('amd64', 'arm64')
        tools: tuple[str, ...] = ('dpkg-deb', 'dpkg-shlibdeps')
        output_setting = 'DEB_DIR'
    elif args.distribution == 'arch':
        architecture = platform.machine()
        supported = ('x86_64',)
        tools = ('makepkg', 'pacman', 'fakeroot', 'bwrap')
        output_setting = 'ARCH_DIR'
    else:
        architecture = platform.machine()
        supported = ('x86_64', 'aarch64')
        tools = ('rpmbuild', 'rpm')
        output_setting = 'RPM_DIR'
    if architecture not in supported:
        raise ValueError(f'Unsupported {args.distribution} architecture: {architecture}')
    if args.stage_only:
        result = packager.package(args.output, args.revision, args.version, architecture, args.binary_dir)
    else:
        # Probe native tools before compiling against this distribution's libraries.
        for tool in tools:
            run([tool, '--version'], capture_output=True, text=True)
        version = os.environ.get('VERSION') or from_git(package_version(ROOT))
        if args.distribution == 'arch':
            arch.arch_version(version)
        elif args.distribution == 'debian':
            debian.debian_version(version)
        elif args.distribution == 'fedora':
            fedora.rpm_version(version)
        revision = run(['git', 'rev-parse', 'HEAD'], capture_output=True, text=True).stdout.strip()
        run([os.environ['JUST_CMD'], 'BUILD=release', f'VERSION={version}', 'all'])
        output = Path(os.environ.get(output_setting, f'dist/{args.distribution}'))
        if not output.is_absolute():
            output = ROOT / output
        result = packager.package(output, revision, version, architecture)
    log(f'{args.distribution} package: {result}')


if __name__ == '__main__':
    run_main(main)
