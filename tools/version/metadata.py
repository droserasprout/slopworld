"""Own canonical release tag syntax and the Cargo-backed product version."""

import argparse
import re
import tomllib
from pathlib import Path

from tools import ROOT

TAG_PREFIX = 'v'
TAG_FORMAT = f'{TAG_PREFIX}MAJOR.MINOR.PATCH'
TAG_GLOB = f'{TAG_PREFIX}[0-9]*.[0-9]*.[0-9]*'
VERSION_PATTERN = r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)'


def release_version(tag: str) -> str | None:
    if re.fullmatch(TAG_PREFIX + VERSION_PATTERN, tag):
        return tag[len(TAG_PREFIX) :]
    return None


def package_version(repository: Path = ROOT) -> str:
    with (repository / 'slopd/Cargo.toml').open('rb') as manifest:
        version: str = tomllib.load(manifest)['package']['version']
    return version


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('tag')
    args = parser.parse_args()
    version = release_version(args.tag)
    if version is None:
        parser.error(f'Release tag must use {TAG_FORMAT}.')
    print(version)


if __name__ == '__main__':
    main()
