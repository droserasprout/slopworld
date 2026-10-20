"""Own hosted release build identity and the verified handoff to publication.

Gates validate tagged source; publish constructs archives and talks to GitHub.
Preview builds use the same packaging path but can never publish a release.
"""

import argparse
import hashlib
import json
import os
from dataclasses import asdict
from pathlib import Path

from tools import ROOT
from tools.release import changelog
from tools.release import gates
from tools.release import publish
from tools.utils import command
from tools.utils import log
from tools.utils import run
from tools.utils import run_main
from tools.version import product
from tools.version.metadata import package_version


def identity(preview: bool) -> gates.Release:
    if preview:
        if gates.git('status', '--porcelain', '--untracked-files=normal'):
            raise ValueError('Preview requires a clean checkout.')
        revision = gates.git('rev-parse', 'HEAD')
        version = package_version()
        product.check(version, ROOT)
        release = gates.Release(revision, '', f'{version}-preview-{revision[:12]}')
    else:
        release = gates.prepare()
    # These values come from the coordinator, not a mutable branch ref.
    for setting, actual in (('RELEASE_REVISION', release.revision), ('RELEASE_VERSION', release.version)):
        expected = os.environ.get(setting)
        if expected and actual != expected:
            raise ValueError(f'{setting} does not match this checkout.')
    return release


def validate(release: gates.Release) -> None:
    if identity(preview=not release.tag) != release:
        raise ValueError('Release identity changed during the build.')


def asset_names(version: str) -> list[str]:
    return [
        f'slopworld-{version}-x86_64-linux.tar.gz',
        f'slopworld-{version}-mod.zip',
        f'slopworld-{version}-x86_64.pkg.tar.zst',
        f'slopworld-{version}-amd64.deb',
        f'slopworld-{version}-x86_64.rpm',
        'SHA256SUMS',
        'release-notes.md',
    ]


def digest(path: Path) -> str:
    if path.is_symlink() or not path.is_file() or path.stat().st_size == 0:
        raise ValueError(f'Missing, empty, or symlinked release file: {path.name}')
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def manifest(output: Path, release: gates.Release) -> dict[str, object]:
    return {
        'schema': 1,
        **asdict(release),
        'files': {name: digest(output / name) for name in asset_names(release.version)},
    }


def verify(output: Path, release: gates.Release) -> list[Path]:
    expected = set(asset_names(release.version)) | {'release.json'}
    if {path.name for path in output.iterdir()} != expected:
        raise ValueError('Release bundle has missing or unexpected files.')
    digest(output / 'release.json')
    if json.loads((output / 'release.json').read_text()) != manifest(output, release):
        raise ValueError('Release manifest identity or file checksums do not match.')
    names = asset_names(release.version)
    checksums = ''.join(f'{digest(output / name)}  {name}\n' for name in names[:-2])
    if (output / 'SHA256SUMS').read_text() != checksums:
        raise ValueError('SHA256SUMS does not describe the release payload.')
    if release.tag and (output / 'release-notes.md').read_text() != changelog.description(
        ROOT / 'CHANGELOG.md', release.version
    ):
        raise ValueError('Release notes do not match the tagged changelog.')
    return [output / name for name in (*names, 'release.json')]


def build(output: Path, release: gates.Release) -> None:
    # A fresh bundle prevents an old package or attestation from entering a new run.
    if output.exists() and any(output.iterdir()):
        raise ValueError('Release output must be empty; use a fresh directory.')
    just = [os.environ['JUST_CMD'], 'BUILD=release', f'VERSION={release.version}', 'MANAGED=rimref']
    validate(release)
    for recipes in (
        ('generate-check-protocol', 'generate-licenses', 'generate-check-licenses', 'docs-build'),
        ('build',),
    ):
        run([*just, *recipes])
        validate(release)
    notes = (
        changelog.description(ROOT / 'CHANGELOG.md', release.version)
        if release.tag
        else f'Preview of {release.revision}; not a published release.\n'
    )
    publish.package(output, release.revision, release.version, notes=notes, native_packages=True)
    validate(release)
    (output / 'release.json').write_text(json.dumps(manifest(output, release), indent=2) + '\n')
    verify(output, release)


def upload(output: Path, release: gates.Release) -> None:
    if not release.tag:
        raise ValueError('Preview bundles cannot be published.')
    repository = os.environ['GITHUB_REPOSITORY']
    gh = command('GH', 'gh')
    assets = verify(output, release)
    # The attestation covers the exact bytes, source commit, tag and workflow.
    # Verification precedes every remote mutation, including release creation.
    for asset in assets:
        run(
            [
                *gh,
                'attestation',
                'verify',
                str(asset),
                '--repo',
                repository,
                '--signer-workflow',
                f'{repository}/.github/workflows/release.yml',
                '--source-digest',
                release.revision,
                '--source-ref',
                f'refs/tags/{release.tag}',
                '--deny-self-hosted-runners',
            ]
        )
    validate(release)
    publish.publish(gh, repository, output, release.revision, release.tag, assets, require_tag=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('prepare', 'package', 'verify', 'publish'))
    parser.add_argument('--preview', action='store_true')
    args = parser.parse_args()
    release = identity(args.preview)
    if args.action == 'prepare':
        if release.tag:
            changelog.description(ROOT / 'CHANGELOG.md', release.version)
        values = f'commit={release.revision}\nversion={release.version}\ntag={release.tag}\n'
        if github_output := os.environ.get('GITHUB_OUTPUT'):
            with Path(github_output).open('a') as stream:
                stream.write(values)
        log(values.rstrip())
        return
    output = Path(os.environ.get('RELEASE_DIR') or 'dist/release').resolve()
    if args.action == 'package':
        build(output, release)
    elif args.action == 'verify':
        verify(output, release)
    else:
        upload(output, release)


if __name__ == '__main__':
    run_main(main)
