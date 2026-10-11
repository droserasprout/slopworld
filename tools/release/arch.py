"""Package the native release build with makepkg and the existing Arch metadata.

Source PKGBUILDs own dependencies and user hooks. This module replaces their
build stages with an already validated release payload; it never installs it.
"""

import os
import re
import shutil
import tempfile
from pathlib import Path

from tools import ROOT
from tools.release import staging
from tools.utils import run


def arch_version(version: str) -> str:
    if not re.fullmatch(r'[0-9][A-Za-z0-9.+\-]*', version):
        raise ValueError(f'Invalid Arch upstream version: {version!r}')
    return version.replace('-', '.r')


def package(
    output: Path,
    revision: str,
    version: str,
    architecture: str,
    binary_dir: Path | None = None,
    *,
    mod: Path | None = None,
) -> Path:
    arch_version(version)
    if architecture != 'x86_64':
        raise ValueError(f'Unsupported Arch architecture: {architecture}')
    # Fresh staging permits repeated builds and preserves the old package on failure.
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.arch-', dir=output) as temporary:
        workspace = Path(temporary)
        if mod is None:
            mod = workspace / 'mod'
            staging.stage_mod(mod, revision, version)
        archive = build_archive(workspace, version, binary_dir or ROOT / 'slopd/target/release', mod)
        destination = output / archive.name
        archive.replace(destination)
    return destination


def build_archive(workspace: Path, version: str, binary_dir: Path, mod: Path) -> Path:
    payload = workspace / 'payload'
    staging.stage_system(payload, binary_dir)
    shutil.copytree(mod, payload / 'usr/share/slopworld/SlopWorld')
    for source, destination in (
        ('packaging/arch/slopworld.rules', 'etc/ananicy.d/slopworld.rules'),
        ('README.md', 'usr/share/doc/slopworld/README.md'),
    ):
        staging.copy_file(ROOT / source, payload / destination)
    staging.stage_notices(payload)
    staging.copy_file(ROOT / 'packaging/arch/slopworld.install', workspace / 'slopworld.install')
    # The metadata and install hook stay owned by PKGBUILD.local. The trusted
    # repository file is sourced by Bash, as it is for ordinary makepkg builds.
    (workspace / 'PKGBUILD').write_text(
        'source "$RELEASE_SOURCE/packaging/arch/PKGBUILD.local"\n'
        'unset -f pkgver prepare build check\n'
        'pkgver="$RELEASE_VERSION"\n'
        'package() { cp -r --preserve=mode,timestamps "$startdir/payload/." "$pkgdir/"; }\n'
    )
    environment = os.environ.copy()
    environment.update(
        RELEASE_SOURCE=str(ROOT),
        RELEASE_VERSION=arch_version(version),
        PKGDEST=str(workspace),
        PKGEXT='.pkg.tar.zst',
        BUILDDIR=str(workspace),
    )
    arguments = ['makepkg', '--nodeps', '--force']
    # makepkg deliberately refuses root. A user namespace gives it an ordinary
    # UID while keeping repository inputs read-only and writes inside staging.
    staging.normalize_permissions(workspace, payload / 'usr/bin')
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
    archive_path = workspace / f'slopworld-{version}-x86_64.pkg.tar.zst'
    archives[0].replace(archive_path)
    # Reading package metadata catches malformed archives before publication.
    # Even file queries initialize libalpm; use an empty staging database so
    # validation also works on hosts without an installed pacman database.
    database = workspace / 'pacman-db'
    database.mkdir()
    run(['pacman', '--dbpath', str(database), '-Qip', str(archive_path)], capture_output=True, text=True)
    return archive_path
