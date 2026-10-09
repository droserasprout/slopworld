"""Build local Linux release archives and publish versioned GitHub releases.

Recipes own compiler settings; this module owns staging and publication ordering.
Only tracked mod assets and explicitly named runtime DLLs enter the mod archive.
"""

import argparse
import hashlib
import json
import os
import platform
import shutil
import subprocess
import tarfile
import tempfile
import zipfile
from pathlib import Path
from typing import Any

from tools import ROOT
from tools.release import arch
from tools.release import changelog
from tools.release import container_debian
from tools.release import gates
from tools.release import staging
from tools.utils import command
from tools.utils import log
from tools.utils import run
from tools.utils import run_main


def package(output: Path, revision: str, version: str, *, notes: str, native_packages: bool = False) -> list[Path]:
    daemon_name = f'slopworld-{version}-x86_64-linux'
    asset_names = (f'{daemon_name}.tar.gz', f'slopworld-{version}-mod.zip')
    output.mkdir(parents=True, exist_ok=True)
    # Finish every archive and native package before replacing prepared outputs.
    with tempfile.TemporaryDirectory(prefix='.release-', dir=output) as temporary:
        workspace = Path(temporary)
        daemon = workspace / daemon_name
        for binary in ('slopd', 'slopctl', 'slopworld'):
            staging.copy_file(ROOT / 'slopd/target/release' / binary, daemon / 'bin' / binary)
        for source_name in ('slopd/slopd.service', 'packaging/slopworld.desktop', 'LICENSE'):
            staging.copy_file(ROOT / source_name, daemon / Path(source_name).name)
        shutil.copytree(ROOT / 'licenses', daemon / 'licenses')
        staging.metadata(daemon, revision, version)

        mod = workspace / 'SlopWorld'
        staging.stage_mod(mod, revision, version)

        with tarfile.open(workspace / asset_names[0], 'w:gz') as archive:
            archive.add(daemon, arcname=daemon.name)
        with zipfile.ZipFile(workspace / asset_names[1], 'w', zipfile.ZIP_DEFLATED) as archive:
            for source in sorted(mod.rglob('*')):
                if source.is_file():
                    archive.write(source, source.relative_to(workspace))
        names = list(asset_names[:2])
        if native_packages:
            names.append(arch.package(workspace, revision, version, mod).name)
            names.append(container_debian.package(workspace, revision, version).name)
        checksums = []
        for name in names:
            with (workspace / name).open('rb') as stream:
                checksums.append(f'{hashlib.file_digest(stream, "sha256").hexdigest()}  {name}\n')
        (workspace / 'SHA256SUMS').write_text(''.join(checksums))
        (workspace / 'release-notes.md').write_text(notes)
        names.append('SHA256SUMS')
        for name in (*names, 'release-notes.md'):
            (workspace / name).replace(output / name)
    return [output / name for name in names]


def github_json(gh: list[str], endpoint: str) -> dict[str, Any] | None:
    result = run([*gh, 'api', endpoint], capture_output=True, text=True, check=False)
    if result.returncode:
        # Authentication, network and permission failures must not look like a missing release.
        if '(HTTP 404)' in result.stderr:
            return None
        raise subprocess.CalledProcessError(result.returncode, result.args, result.stdout, result.stderr)
    document: dict[str, Any] = json.loads(result.stdout)
    return document


def publish(gh: list[str], repository: str, output: Path, revision: str, tag: str, assets: list[Path]) -> None:
    if github_json(gh, f'repos/{repository}/releases/tags/{tag}'):
        raise ValueError(f'Release {tag} already exists; published versions are not replaced.')
    # The commits endpoint dereferences both lightweight and annotated tags.
    remote = github_json(gh, f'repos/{repository}/commits/{tag}')
    if remote and remote.get('sha') != revision:
        raise ValueError(f'Remote tag {tag} does not point to the built commit.')
    if not remote:
        run(
            [
                *gh,
                'api',
                f'repos/{repository}/git/refs',
                '-X',
                'POST',
                '-f',
                f'ref=refs/tags/{tag}',
                '-f',
                f'sha={revision}',
            ]
        )
    run(
        [
            *gh,
            'release',
            'create',
            tag,
            *map(str, assets),
            '--repo',
            repository,
            '--title',
            tag,
            '--notes-file',
            str(output / 'release-notes.md'),
            '--verify-tag',
        ]
    )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('package', 'publish'))
    args = parser.parse_args()
    if platform.system() != 'Linux' or platform.machine() != 'x86_64':
        raise ValueError('Release recipes currently require an x86_64 Linux build host.')
    release = gates.prepare()
    notes = changelog.description(ROOT / 'CHANGELOG.md', release.version)
    gh = command('GH', 'gh')
    repository = ''
    if args.action == 'publish':
        repository = run(
            [*gh, 'repo', 'view', '--json', 'nameWithOwner', '--jq', '.nameWithOwner'], capture_output=True, text=True
        ).stdout.strip()
    gates.build(release)
    version = release.version
    output = Path(os.environ.get('RELEASE_DIR') or f'dist/{version}')
    if not output.is_absolute():
        output = ROOT / output
    assets = package(output, release.revision, version, notes=notes, native_packages=True)
    gates.validate(release)
    log(f'Release archives: {output}')
    if args.action == 'publish':
        publish(gh, repository, output, release.revision, release.tag, assets)


if __name__ == '__main__':
    run_main(main)
