"""Adapt Fedora versions and filenames to the shared release container."""

from pathlib import Path

from tools import ROOT
from tools.release import container
from tools.release.fedora import rpm_version


def package(output: Path, revision: str, version: str) -> Path:
    generated_name = f'slopworld-{rpm_version(version)}-1.x86_64.rpm'
    return container.package(
        ROOT,
        'fedora',
        output,
        revision,
        version,
        generated_name=generated_name,
        archive_name=f'slopworld-{version}-x86_64.rpm',
    )
