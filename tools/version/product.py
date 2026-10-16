"""Own editable product version locations; metadata owns release tag syntax."""

import re
from dataclasses import dataclass
from pathlib import Path

from tools import ROOT
from tools.version.metadata import release_version


@dataclass(frozen=True)
class VersionFile:
    path: str
    pattern: str
    suffix: str = ''

    def read(self, repository: Path) -> tuple[str, re.Match[str]]:
        source = (repository / self.path).read_text()
        matches = list(re.finditer(self.pattern, source, re.MULTILINE))
        if len(matches) != 1:
            raise ValueError(f'{self.path}: expected exactly one product version entry.')
        return source, matches[0]


FILES = (
    VersionFile('slopd/Cargo.toml', r'^version = "(?P<version>[^"\n]+)"$'),
    VersionFile('slopcar/Cargo.toml', r'^version = "(?P<version>[^"\n]+)"$'),
    VersionFile('slopd/Cargo.lock', r'^name = "slopd"\nversion = "(?P<version>[^"\n]+)"$'),
    VersionFile('slopcar/Cargo.lock', r'^name = "slopcar"\nversion = "(?P<version>[^"\n]+)"$'),
    VersionFile('mod/Source/SlopWorld/Bootstrap/ModEntry.cs', r'^            return "(?P<version>[^"\n]+)";$'),
    VersionFile('packaging/arch/PKGBUILD.git', r'^pkgver=(?P<version>[^\n]+)$', '.r0.gHEAD'),
    VersionFile('packaging/arch/PKGBUILD.local', r'^pkgver=(?P<version>[^\n]+)$'),
)


def validate_version(version: str) -> None:
    if release_version('v' + version) is None:
        raise ValueError('Version must use MAJOR.MINOR.PATCH without a v prefix.')


def check(version: str, repository: Path = ROOT) -> None:
    validate_version(version)
    for entry in FILES:
        _, match = entry.read(repository)
        expected = version + entry.suffix
        if match['version'] != expected:
            raise ValueError(
                f'{entry.path}: version {match["version"]!r} must match {expected!r}; '
                f'run just release-bump-version {version} before committing and tagging.'
            )


def changes(version: str, repository: Path = ROOT) -> dict[Path, tuple[str, str]]:
    """Prepare every replacement before writing, preserving unrelated bytes."""
    validate_version(version)
    result: dict[Path, tuple[str, str]] = {}
    for entry in FILES:
        source, match = entry.read(repository)
        start, end = match.span('version')
        updated = source[:start] + version + entry.suffix + source[end:]
        if updated != source:
            result[repository / entry.path] = source, updated
    return result
