"""Build local Linux release archives and publish the rolling GitHub latest tag.

Recipes own compiler settings; this module owns staging and publication ordering.
Only tracked mod assets and explicitly named runtime DLLs enter the mod archive.
"""

import argparse
import datetime as dt
import hashlib
import json
import os
import platform
import shutil
import subprocess
import tarfile
import tempfile
import tomllib
import zipfile
from pathlib import Path

from tools import ROOT
from tools.utils import command
from tools.utils import log
from tools.utils import run
from tools.utils import run_main

MOD_DIRECTORIES = ('About', 'Defs', 'Patches', 'Sounds', 'Textures', 'Themes')
MOD_ASSEMBLIES = (
    'SlopWorld',
    '0Harmony',
    'Google.Protobuf',
    'Markdig',
    'Newtonsoft.Json',
    'Tomlyn',
    'System.Buffers',
    'System.Memory',
    'System.Numerics.Vectors',
    'System.Runtime.CompilerServices.Unsafe',
)
DAEMON_NAME = 'slopworld-latest-x86_64-linux'
ASSET_NAMES = (f'{DAEMON_NAME}.tar.gz', 'slopworld-latest-mod.zip', 'SHA256SUMS')


def git(*arguments: str) -> str:
    return run(['git', *arguments], capture_output=True, text=True).stdout.strip()


def validate_checkout(revision: str) -> None:
    if git('status', '--porcelain', '--untracked-files=normal'):
        raise ValueError('Release requires a clean checkout; commit or stash local changes first.')
    if git('rev-parse', 'HEAD') != revision:
        raise ValueError('HEAD changed during the release build; rerun the recipe.')


def build(revision: str) -> str:
    with (ROOT / 'slopd/Cargo.toml').open('rb') as source:
        fallback = tomllib.load(source)['package']['version']
    version = os.environ.get('VERSION') or f'{fallback}-{dt.datetime.now(dt.timezone.utc):%Y%m%d}-{revision[:12]}'
    # Resolve once so daemon and mod agree even across midnight or numeric tags.
    run([os.environ['JUST_CMD'], 'BUILD=release', f'VERSION={version}', 'all', 'check-licenses'])
    validate_checkout(revision)
    return version


def copy_file(source: Path, destination: Path) -> None:
    if not source.is_file() or source.stat().st_size == 0:
        raise ValueError(f'Missing or empty release input: {source}')
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)


def metadata(directory: Path, revision: str, version: str) -> None:
    (directory / 'REVISION').write_text(revision + '\n')
    (directory / 'VERSION').write_text(version + '\n')


def package(output: Path, revision: str, version: str) -> list[Path]:
    output.mkdir(parents=True, exist_ok=True)
    # Finish both archives before replacing any previously prepared outputs.
    with tempfile.TemporaryDirectory(prefix='.release-', dir=output) as temporary:
        staging = Path(temporary)
        daemon = staging / DAEMON_NAME
        for binary in ('slopd', 'slopctl', 'slopworld'):
            copy_file(ROOT / 'slopd/target/release' / binary, daemon / 'bin' / binary)
        for source in ('slopd/slopd.service', 'packaging/slopworld.desktop', 'LICENSE'):
            copy_file(ROOT / source, daemon / Path(source).name)
        shutil.copytree(ROOT / 'licenses', daemon / 'licenses')
        metadata(daemon, revision, version)

        mod = staging / 'SlopWorld'
        tracked = git('ls-files', '-z', '--', 'mod').split('\0')
        for name in filter(None, tracked):
            relative = Path(name).relative_to('mod')
            if relative.parts[0] in MOD_DIRECTORIES:
                copy_file(ROOT / name, mod / relative)
        for assembly in MOD_ASSEMBLIES:
            copy_file(ROOT / 'mod/Assemblies' / f'{assembly}.dll', mod / 'Assemblies' / f'{assembly}.dll')
        copy_file(ROOT / 'mod/About/LICENSE', mod / 'About/LICENSE')
        shutil.copytree(ROOT / 'mod/About/ThirdPartyNotices', mod / 'About/ThirdPartyNotices')
        metadata(mod, revision, version)

        with tarfile.open(staging / ASSET_NAMES[0], 'w:gz') as archive:
            archive.add(daemon, arcname=daemon.name)
        with zipfile.ZipFile(staging / ASSET_NAMES[1], 'w', zipfile.ZIP_DEFLATED) as archive:
            for source in sorted(mod.rglob('*')):
                if source.is_file():
                    archive.write(source, source.relative_to(staging))
        checksums = []
        for name in ASSET_NAMES[:2]:
            with (staging / name).open('rb') as source:
                checksums.append(f'{hashlib.file_digest(source, "sha256").hexdigest()}  {name}\n')
        (staging / 'SHA256SUMS').write_text(''.join(checksums))
        (staging / 'release-notes.md').write_text(
            f'Rolling local build for RimWorld 1.6.\n\nVersion: `{version}`\n\nCommit: `{revision}`\n\n'
            'Download the Linux daemon archive and the mod ZIP. '
            'Verify downloads with `sha256sum -c SHA256SUMS`.\n'
        )
        for name in (*ASSET_NAMES, 'release-notes.md'):
            (staging / name).replace(output / name)
    return [output / name for name in ASSET_NAMES]


def github_json(gh: list[str], endpoint: str) -> dict | None:
    result = run([*gh, 'api', endpoint], capture_output=True, text=True, check=False)
    if result.returncode:
        # Authentication, network and permission failures must not look like a missing release.
        if '(HTTP 404)' in result.stderr:
            return None
        raise subprocess.CalledProcessError(result.returncode, result.args, result.stdout, result.stderr)
    return json.loads(result.stdout)


def publish(gh: list[str], repository: str, output: Path, revision: str, assets: list[Path]) -> None:
    release = github_json(gh, f'repos/{repository}/releases/tags/latest')
    if release and release.get('immutable'):
        raise ValueError('The latest release is immutable; rolling releases require mutable GitHub releases.')
    tag = github_json(gh, f'repos/{repository}/git/ref/tags/latest')
    # Move the remote tag directly, avoiding local tags and Git/gh remote disagreement.
    if tag:
        run(
            [
                *gh,
                'api',
                f'repos/{repository}/git/refs/tags/latest',
                '-X',
                'PATCH',
                '-f',
                f'sha={revision}',
                '-F',
                'force=true',
            ]
        )
    else:
        run(
            [
                *gh,
                'api',
                f'repos/{repository}/git/refs',
                '-X',
                'POST',
                '-f',
                'ref=refs/tags/latest',
                '-f',
                f'sha={revision}',
            ]
        )
    common = ['--repo', repository, '--title', 'Latest', '--notes-file', str(output / 'release-notes.md')]
    if release:
        run([*gh, 'release', 'upload', 'latest', *map(str, assets), '--repo', repository, '--clobber'])
        run([*gh, 'release', 'edit', 'latest', *common, '--draft=false', '--prerelease=false', '--latest'])
    else:
        run([*gh, 'release', 'create', 'latest', *map(str, assets), *common, '--verify-tag', '--latest'])


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('package', 'publish'))
    args = parser.parse_args()
    if platform.system() != 'Linux' or platform.machine() != 'x86_64':
        raise ValueError('Release recipes currently require an x86_64 Linux build host.')
    revision = git('rev-parse', 'HEAD')
    validate_checkout(revision)
    gh = command('GH', 'gh')
    repository = ''
    if args.action == 'publish':
        repository = run(
            [*gh, 'repo', 'view', '--json', 'nameWithOwner', '--jq', '.nameWithOwner'], capture_output=True, text=True
        ).stdout.strip()
    version = build(revision)
    output = Path(os.environ.get('RELEASE_DIR', 'dist/latest'))
    if not output.is_absolute():
        output = ROOT / output
    assets = package(output, revision, version)
    log(f'Release archives: {output}')
    if args.action == 'publish':
        validate_checkout(revision)
        publish(gh, repository, output, revision, assets)


if __name__ == '__main__':
    run_main(main)
