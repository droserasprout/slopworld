"""Adapt Debian versions and filenames to the shared release container."""

from pathlib import Path

from tools import ROOT
from tools.release import container
from tools.release.debian import debian_version


def package(output: Path, revision: str, version: str) -> Path:
    generated_name = f'slopworld_{debian_version(version)}_amd64.deb'
    return container.package(
        ROOT,
        'debian',
        output,
        revision,
        version,
        generated_name=generated_name,
        archive_name=f'slopworld-{version}-amd64.deb',
    )
