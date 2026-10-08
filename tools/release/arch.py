"""Package the native release build with makepkg and the existing Arch metadata.

Source PKGBUILDs own dependencies and user hooks. This module replaces their
build stages with an already validated release payload; it never installs it.
"""

import os
import shutil
from pathlib import Path

from tools import ROOT
from tools.release import staging
from tools.utils import run

ASSET_NAME = 'slopworld-latest-x86_64.pkg.tar.zst'


def package(output: Path, revision: str, version: str, mod: Path) -> Path:
    workspace = output / 'arch-package'
    workspace.mkdir(mode=0o755)
    payload = workspace / 'payload'
    for binary in ('slopd', 'slopctl', 'slopworld'):
        staging.copy_file(ROOT / 'slopd/target/release' / binary, payload / 'usr/bin' / binary)
    unit = payload / 'usr/lib/systemd/user/slopd.service'
    staging.copy_file(ROOT / 'slopd/slopd.service', unit)
    unit.write_text(unit.read_text().replace('%h/.local/bin/slopd', '/usr/bin/slopd'))
    shutil.copytree(mod, payload / 'usr/share/slopworld/SlopWorld')
    for source, destination in (
        ('packaging/slopworld.desktop', 'usr/share/applications/slopworld.desktop'),
        ('packaging/arch/slopworld.rules', 'etc/ananicy.d/slopworld.rules'),
        ('mod/Textures/SlopWorld/SlopWorld_icon.png', 'usr/share/icons/hicolor/128x128/apps/slopworld.png'),
        ('README.md', 'usr/share/doc/slopworld/README.md'),
        ('LICENSE', 'usr/share/licenses/slopworld/LICENSE'),
    ):
        staging.copy_file(ROOT / source, payload / destination)
    shutil.copytree(ROOT / 'licenses', payload / 'usr/share/licenses/slopworld/third-party')
    staging.copy_file(ROOT / 'packaging/arch/slopworld.install', workspace / 'slopworld.install')
    # The metadata and install hook stay owned by PKGBUILD.local. The trusted
    # repository file is sourced by Bash, as it is for ordinary makepkg builds.
    (workspace / 'PKGBUILD').write_text(
        'source "$RELEASE_SOURCE/packaging/arch/PKGBUILD.local"\n'
        'unset -f pkgver prepare build check\n'
        'pkgver="${RELEASE_VERSION//-/.r}"\n'
        'package() { cp -r --preserve=mode,timestamps "$startdir/payload/." "$pkgdir/"; }\n'
    )
    environment = os.environ.copy()
    environment.update(
        RELEASE_SOURCE=str(ROOT),
        RELEASE_VERSION=version,
        PKGDEST=str(workspace),
        PKGEXT='.pkg.tar.zst',
        BUILDDIR=str(workspace),
    )
    arguments = ['makepkg', '--nodeps', '--force']
    # makepkg deliberately refuses root. A user namespace gives it an ordinary
    # UID while keeping repository inputs read-only and writes inside staging.
    output.chmod(0o755)
    for path in (workspace, *workspace.rglob('*')):
        path.chmod(0o755 if path.is_dir() or path.parent == payload / 'usr/bin' else 0o644)
    if os.geteuid() == 0:
        scratch = workspace / 'tmp'
        scratch.mkdir()
        arguments = [
            'bwrap',
            '--unshare-user',
            '--uid',
            '1000',
            '--gid',
            '1000',
            '--ro-bind',
            '/',
            '/',
            '--bind',
            str(scratch),
            '/tmp',
            '--ro-bind',
            str(ROOT),
            str(ROOT),
            '--bind',
            str(workspace),
            str(workspace),
            '--chdir',
            str(workspace),
            '--dev',
            '/dev',
            '--proc',
            '/proc',
            '--',
            *arguments,
        ]
    run(arguments, cwd=workspace, env=environment)
    archives = list(workspace.glob('slopworld-*.pkg.tar.zst'))
    if len(archives) != 1:
        raise ValueError('makepkg did not produce exactly one release package')
    archive_path = output / ASSET_NAME
    archives[0].replace(archive_path)
    # Reading package metadata catches malformed archives before publication.
    # Even file queries initialize libalpm; use an empty staging database so
    # validation also works on hosts without an installed pacman database.
    database = workspace / 'pacman-db'
    database.mkdir()
    run(['pacman', '--dbpath', str(database), '-Qip', str(archive_path)], capture_output=True, text=True)
    return archive_path
